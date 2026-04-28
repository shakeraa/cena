// =============================================================================
// Cena Platform — IngestionJobRunnerHostedService
//
// Channel-driven worker that executes queued IngestionJobDocument rows.
// One job at a time per process (sufficient for dev; bound queue
// concurrency via WorkerCount field if scaling). On host start:
//
//   1. Rehydrate Marten — any Queued/Running rows from a previous boot
//      are flipped back to Queued and re-injected into the channel.
//   2. Loop: read next id, load doc, dispatch to strategy, mark terminal.
//
// Strategies are pluggable (`IIngestionJobStrategy` keyed by job type).
// Cooperative cancellation: each tick checks `CancelRequested` from the
// doc; the linked CancellationToken is honoured at strategy checkpoints.
// =============================================================================

using System.Text.Json;
using System.Threading.Channels;
using Cena.Admin.Api.QualityGate;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api.Ingestion;

public interface IIngestionJobStrategy
{
    IngestionJobType Type { get; }

    Task<object?> ExecuteAsync(
        IngestionJobDocument job,
        IServiceProvider scoped,
        IJobProgressReporter progress,
        CancellationToken ct);
}

public interface IJobProgressReporter
{
    Task ReportAsync(int pct, string? message, CancellationToken ct = default);
    Task LogAsync(string level, string message, CancellationToken ct = default);
    bool CancelRequested { get; }
}

internal sealed class IngestionJobRunnerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _channel;
    private readonly ILogger<IngestionJobRunnerHostedService> _logger;

    // Cancellation cadence: how often we re-read the doc to honour
    // CancelRequested while a strategy is mid-flight.
    private static readonly TimeSpan CancelPollInterval = TimeSpan.FromSeconds(2);

    public IngestionJobRunnerHostedService(
        IServiceScopeFactory scopeFactory,
        Channel<string> channel,
        ILogger<IngestionJobRunnerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionJobRunner started");

        // Stagger boot: let the rest of the host finish coming up before
        // we hammer Marten with a rehydrate query.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        // Pull leftover Queued/Running rows back into the channel.
        await using (var bootScope = _scopeFactory.CreateAsyncScope())
        {
            try
            {
                var jobs = bootScope.ServiceProvider.GetRequiredService<IIngestionJobService>();
                var pending = await jobs.RehydrateAsync(stoppingToken);
                foreach (var id in pending)
                {
                    if (!_channel.Writer.TryWrite(id))
                    {
                        _logger.LogWarning("Rehydrate channel full; lost {JobId}", id);
                    }
                }
                if (pending.Count > 0)
                {
                    _logger.LogInformation("Rehydrated {Count} pending ingestion jobs", pending.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion job rehydrate failed");
            }
        }

        await foreach (var jobId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ExecuteJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion job {JobId} runner crashed", jobId);
            }
        }

        _logger.LogInformation("IngestionJobRunner stopped");
    }

    private async Task ExecuteJobAsync(string jobId, CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var jobs = sp.GetRequiredService<IIngestionJobService>();
        var strategies = sp.GetServices<IIngestionJobStrategy>().ToList();

        var doc = await jobs.GetAsync(jobId, stoppingToken);
        if (doc is null)
        {
            _logger.LogWarning("Ingestion job {JobId} not found in Marten; skipped", jobId);
            return;
        }

        // Pre-start cancel check (cancel-while-queued).
        if (doc.CancelRequested
            || doc.Status is IngestionJobStatus.Cancelled
                            or IngestionJobStatus.Completed
                            or IngestionJobStatus.Failed)
        {
            return;
        }

        var strategy = strategies.FirstOrDefault(s => s.Type == doc.Type);
        if (strategy is null)
        {
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Failed,
                $"No strategy registered for job type '{doc.Type}'", null, stoppingToken);
            return;
        }

        await jobs.MarkStartedAsync(jobId, stoppingToken);

        // Linked cancellation: stoppingToken (host shutdown) + a watcher
        // that flips when CancelRequested goes true.
        using var cancelCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var cancelWatcher = WatchCancelAsync(jobId, cancelCts, stoppingToken);

        var reporter = new ProgressReporter(jobId, jobs);
        try
        {
            var result = await strategy.ExecuteAsync(doc, sp, reporter, cancelCts.Token);
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Completed, null, result, stoppingToken);
            _logger.LogInformation("Ingestion job {JobId} completed", jobId);
        }
        catch (OperationCanceledException) when (cancelCts.IsCancellationRequested
                                                  && !stoppingToken.IsCancellationRequested)
        {
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Cancelled,
                "Cancelled by user", null, stoppingToken);
            _logger.LogInformation("Ingestion job {JobId} cancelled", jobId);
        }
        catch (Exception ex)
        {
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Failed,
                ex.Message, null, stoppingToken);
            _logger.LogError(ex, "Ingestion job {JobId} failed", jobId);
        }
        finally
        {
            try { cancelCts.Cancel(); } catch { /* idempotent cancel */ }
            try { await cancelWatcher; } catch { /* watcher exit is best-effort */ }
        }
    }

    private async Task WatchCancelAsync(
        string jobId, CancellationTokenSource cts, CancellationToken stop)
    {
        while (!cts.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            try { await Task.Delay(CancelPollInterval, cts.Token); }
            catch (OperationCanceledException) { return; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IIngestionJobService>();
                var doc = await jobs.GetAsync(jobId, stop);
                if (doc?.CancelRequested == true)
                {
                    cts.Cancel();
                    return;
                }
            }
            catch { /* transient — try again next tick */ }
        }
    }

    private sealed class ProgressReporter : IJobProgressReporter
    {
        private readonly string _jobId;
        private readonly IIngestionJobService _jobs;

        public ProgressReporter(string jobId, IIngestionJobService jobs)
        {
            _jobId = jobId;
            _jobs = jobs;
        }

        public bool CancelRequested
        {
            get
            {
                var doc = _jobs.GetAsync(_jobId).GetAwaiter().GetResult();
                return doc?.CancelRequested == true;
            }
        }

        public async Task ReportAsync(int pct, string? message, CancellationToken ct = default)
        {
            await _jobs.UpdateProgressAsync(_jobId, pct, message, ct);
            // Also log the progress message so the per-job log stream
            // forms a complete narrative (otherwise the SPA only sees
            // the latest progressMessage, not the history).
            if (!string.IsNullOrWhiteSpace(message))
                await _jobs.AppendLogAsync(_jobId, "info", $"[{pct}%] {message}", ct);
        }

        public Task LogAsync(string level, string message, CancellationToken ct = default) =>
            _jobs.AppendLogAsync(_jobId, level, message, ct);
    }
}

// ----- Bagrut strategy -----

internal sealed class BagrutIngestionJobStrategy : IIngestionJobStrategy
{
    public IngestionJobType Type => IngestionJobType.Bagrut;

    public async Task<object?> ExecuteAsync(
        IngestionJobDocument job,
        IServiceProvider scoped,
        IJobProgressReporter progress,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<BagrutJobPayload>(job.PayloadJson ?? "{}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Bagrut job payload missing");

        await progress.ReportAsync(5, "Reading PDF…", ct);

        var service = scoped.GetRequiredService<IBagrutPdfIngestionService>();
        var persistence = scoped.GetRequiredService<IBagrutDraftPersistence>();

        await progress.ReportAsync(15, "Running OCR cascade…", ct);

        var result = await service.IngestAsync(
            payload.FileBytes, payload.ExamCode, payload.UploadedBy, ct);

        await progress.ReportAsync(80, $"Persisting {result.Drafts.Count} drafts…", ct);

        var ids = await persistence.PersistAsync(
            examCode: payload.ExamCode,
            sourcePdfId: result.PdfId,
            sourceFilename: payload.SourceFilename,
            submittedBy: payload.UploadedBy,
            drafts: result.Drafts,
            ct: ct);

        // ADR-0043 §runtime-gate: write BagrutCorpusItemDocument rows so the
        // similarity rejector has Ministry text to compare AI variants
        // against. Mirrors the corpus side-effect of the original
        // BagrutIngestHandler.HandleAsync (lines 167-189). Skipped silently
        // when the curator didn't supply Ministry subject+paper codes —
        // matches the original "missing-metadata-warning" behaviour.
        var corpusWritten = 0;
        if (!string.IsNullOrWhiteSpace(payload.MinistrySubjectCode)
            && !string.IsNullOrWhiteSpace(payload.MinistryQuestionPaperCode)
            && result.Drafts.Count > 0)
        {
            await progress.ReportAsync(90, "Writing Bagrut reference corpus…", ct);
            var corpus = scoped.GetRequiredService<IBagrutCorpusService>();
            var ctx = new BagrutCorpusIngestContext(
                ExamCode: payload.ExamCode,
                MinistrySubjectCode: payload.MinistrySubjectCode!.Trim(),
                MinistryQuestionPaperCode: payload.MinistryQuestionPaperCode!.Trim(),
                Units: payload.Units,
                Year: payload.Year,
                Season: null,
                Moed: null,
                Stream: null,
                DefaultTopicId: payload.TopicId,
                SourceFilename: payload.SourceFilename,
                SourcePdfId: result.PdfId,
                UploadedBy: payload.UploadedBy,
                IngestedAt: DateTimeOffset.UtcNow);
            var corpusItems = BagrutCorpusExtractor.Extract(result.Drafts, ctx);
            if (corpusItems.Count > 0)
            {
                await corpus.UpsertManyAsync(corpusItems, ct);
                corpusWritten = corpusItems.Count;
            }
        }

        await progress.ReportAsync(100,
            $"Completed: {result.Drafts.Count} drafts, {corpusWritten} corpus items", ct);

        return new
        {
            pdfId = result.PdfId,
            examCode = result.ExamCode,
            totalPages = result.TotalPages,
            questionsExtracted = result.QuestionsExtracted,
            figuresExtracted = result.FiguresExtracted,
            persistedDraftIds = ids,
            corpusItemsWritten = corpusWritten,
            warnings = result.Warnings,
        };
    }
}

// Payload for bagrut jobs. Captured by the endpoint and stored as JSON
// inside IngestionJobDocument.PayloadJson; deserialized by the strategy.
// MinistrySubjectCode + MinistryQuestionPaperCode (when both present) trigger
// the BagrutCorpusItemDocument write — feeds the ADR-0043 isomorph rejector.
public sealed record BagrutJobPayload(
    string ExamCode,
    string UploadedBy,
    string? SourceFilename,
    byte[] FileBytes,
    string? MinistrySubjectCode = null,
    string? MinistryQuestionPaperCode = null,
    int? Units = null,
    int? Year = null,
    string? TopicId = null);

// ----- Cloud-dir strategy -----

internal sealed class CloudDirIngestionJobStrategy : IIngestionJobStrategy
{
    public IngestionJobType Type => IngestionJobType.CloudDir;

    public async Task<object?> ExecuteAsync(
        IngestionJobDocument job,
        IServiceProvider scoped,
        IJobProgressReporter progress,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CloudDirJobPayload>(job.PayloadJson ?? "{}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Cloud-dir job payload missing");

        await progress.ReportAsync(10, $"Listing {payload.Provider}:{payload.BucketOrPath}…", ct);

        var pipeline = scoped.GetRequiredService<IIngestionPipelineService>();

        var ingestRequest = new Cena.Api.Contracts.Admin.Ingestion.CloudDirIngestRequest(
            Provider: payload.Provider,
            BucketOrPath: payload.BucketOrPath,
            FileKeys: payload.FileKeys ?? Array.Empty<string>(),
            Prefix: payload.Prefix);

        await progress.ReportAsync(30, "Queueing files for ingestion…", ct);

        var resp = await pipeline.IngestCloudDirectoryAsync(ingestRequest);

        await progress.ReportAsync(100,
            $"Queued {resp.FilesQueued}, skipped {resp.FilesSkipped}", ct);

        return new
        {
            filesQueued = resp.FilesQueued,
            filesSkipped = resp.FilesSkipped,
            batchId = resp.BatchId,
        };
    }
}

public sealed record CloudDirJobPayload(
    string Provider,
    string BucketOrPath,
    IReadOnlyList<string>? FileKeys,
    string? Prefix);

// ----- GenerateVariants strategy (option 2) -----

internal sealed class GenerateVariantsJobStrategy : IIngestionJobStrategy
{
    public IngestionJobType Type => IngestionJobType.GenerateVariants;

    public async Task<object?> ExecuteAsync(
        IngestionJobDocument job,
        IServiceProvider scoped,
        IJobProgressReporter progress,
        CancellationToken ct)
    {
        // ADR-0059 §15.5 + PRR-249: source-anchored variants pass Ministry-
        // derived text to the LLM as a creative seed. Implementation-
        // complete, BUT gated until counsel signs the legal-delta memo
        // (PRR-249 §6). Curator-readable error code so the SPA can
        // distinguish this from a general failure and render the
        // 'Disabled pending legal sign-off' banner.
        var config = scoped.GetRequiredService<IConfiguration>();
        var enabled = config.GetValue<bool>("Cena:Variants:BagrutSeedToLlmEnabled");
        if (!enabled)
        {
            await progress.LogAsync("error",
                "SOURCE_ANCHORED_VARIANTS_DISABLED — Cena:Variants:BagrutSeedToLlmEnabled is false. " +
                "Implementation is production-grade but gated on PRR-249 legal-delta memo sign-off. " +
                "See docs/engineering/feature-flags.md.",
                ct);
            throw new InvalidOperationException(
                "SOURCE_ANCHORED_VARIANTS_DISABLED: source-anchored variant generation is gated " +
                "on PRR-249 legal-delta memo sign-off. Set Cena:Variants:BagrutSeedToLlmEnabled=true " +
                "in appsettings (or the OCR_VARIANTS_BAGRUT_SEED_ENABLED env var, see docs/engineering/feature-flags.md) " +
                "after the memo lands.");
        }

        var payload = JsonSerializer.Deserialize<GenerateVariantsJobPayload>(
            job.PayloadJson ?? "{}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("GenerateVariants job payload missing");

        await progress.ReportAsync(5, $"Loading draft {payload.DraftId}…", ct);

        var store = scoped.GetRequiredService<Marten.IDocumentStore>();
        Cena.Infrastructure.Documents.BagrutDraftPayloadDocument? draft;
        await using (var session = store.QuerySession())
        {
            draft = await session
                .LoadAsync<Cena.Infrastructure.Documents.BagrutDraftPayloadDocument>(
                    payload.DraftId, ct);
        }
        if (draft is null)
            throw new InvalidOperationException(
                $"Bagrut draft payload {payload.DraftId} not found. " +
                "Re-run the Bagrut ingest to repopulate.");

        await progress.ReportAsync(20,
            $"Asking AI for {payload.Count} variants of '{Truncate(draft.Prompt, 60)}'…", ct);

        // Build a BatchGenerateRequest. This is generation by *category*,
        // not literal prompt-cloning — the AI gets subject/topic/grade/
        // bloom/difficulty/language and writes new questions in that
        // domain. ADR-0043 §runtime-gate: every candidate is then
        // similarity-checked against BagrutCorpusItemDocument so it
        // can't accidentally clone Ministry text.
        // ADR-0059 §15.5 structural-variant: pass the Bagrut draft prompt
        // + LaTeX to the LLM as a creative seed. AiGenerationService.BuildPrompt
        // detects the [SOURCE-AS-CREATIVE-SEED] marker and emits do-not-copy
        // guardrails so the output is competency-equivalent rather than a
        // near-clone of the Ministry text.
        var batchRequest = new BatchGenerateRequest(
            Count: Math.Clamp(payload.Count, 1, 20),
            Subject: payload.Subject,
            Topic: payload.Topic,
            Grade: payload.Grade,
            BloomsLevel: payload.BloomsLevel,
            MinDifficulty: payload.MinDifficulty,
            MaxDifficulty: payload.MaxDifficulty,
            Language: payload.Language,
            SourceContext: draft.Prompt,
            SourceLatex: draft.LatexContent);

        await progress.LogAsync("info",
            $"Seeding LLM with source ({draft.Prompt.Length} chars) — competency-equivalent variants requested",
            ct);

        var ai = scoped.GetRequiredService<IAiGenerationService>();
        var qualityGate = scoped.GetRequiredService<
            QualityGateServices.IQualityGateService>();

        await progress.ReportAsync(35, "Running AI batch generation…", ct);

        var batch = await ai.BatchGenerateAsync(batchRequest, qualityGate);

        var passedQg = batch.Results.Count(r => r.PassedQualityGate);
        await progress.LogAsync("info",
            $"LLM returned {batch.Results.Count} candidates · {passedQg} passed quality gate · model={batch.ModelUsed}",
            ct);

        // ADR-0059 §15.5 + ADR-0032 / ADR-0002: persist passing variants
        // through the canonical IQuestionBankService.CreateQuestionAsync,
        // which routes through CasGatedQuestionPersister so each candidate
        // hits the CAS gate, quality-gate event log, and question-stream
        // append in one atomic write. Failed candidates surface in the
        // job log; result also includes the produced QuestionIds for
        // curator follow-up.
        var qbs = scoped.GetRequiredService<IQuestionBankService>();
        var persistedIds = new List<string>();
        var persistFailures = new List<string>();

        var idx = 0;
        foreach (var r in batch.Results)
        {
            idx++;
            ct.ThrowIfCancellationRequested();

            if (!r.PassedQualityGate)
            {
                await progress.LogAsync("warn",
                    $"[v{idx}] dropped — quality gate {r.QualityGate.Decision}",
                    ct);
                continue;
            }
            if (string.Equals(r.CasOutcome, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                await progress.LogAsync("warn",
                    $"[v{idx}] dropped — CAS gate failed: {r.CasFailureReason}",
                    ct);
                continue;
            }

            var difficulty = (float)Math.Clamp((decimal)r.Question.Difficulty, 0m, 1m);
            var createReq = new CreateQuestionRequest(
                SourceType: "ai-generated",
                Stem: r.Question.Stem,
                StemHtml: null,
                Options: r.Question.Options
                    .Select(o => new CreateOptionRequest(
                        Label: o.Label,
                        Text: o.Text,
                        TextHtml: null,
                        IsCorrect: o.IsCorrect,
                        DistractorRationale: o.DistractorRationale))
                    .ToList(),
                Subject: payload.Subject,
                Topic: r.Question.Topic ?? payload.Topic,
                Grade: payload.Grade,
                BloomsLevel: r.Question.BloomsLevel,
                Difficulty: difficulty,
                ConceptIds: null,
                Language: payload.Language,
                SourceDocId: null,
                SourceUrl: null,
                SourceFilename: null,
                OriginalText: null,
                // Provenance lineage: link variant back to the Bagrut
                // draft + pdf so curators can trace the seed.
                PromptText: $"variant-of:{payload.DraftId} · pdf:{draft.SourcePdfId} · exam:{draft.ExamCode}",
                ModelId: batch.ModelUsed,
                ModelTemperature: null,
                RawModelOutput: null,
                Explanation: r.Question.Explanation,
                LearningObjectiveId: null);

            try
            {
                var detail = await qbs.CreateQuestionAsync(createReq, job.CreatedBy ?? "system");
                if (detail is null)
                {
                    persistFailures.Add($"v{idx}: persistence returned null");
                    await progress.LogAsync("error",
                        $"[v{idx}] persistence returned null — see admin-api logs",
                        ct);
                    continue;
                }
                persistedIds.Add(detail.Id);
                await progress.LogAsync("info",
                    $"[v{idx}] persisted as {detail.Id} (Bloom={r.Question.BloomsLevel}, diff={difficulty:F2})",
                    ct);
            }
            catch (Exception ex)
            {
                persistFailures.Add($"v{idx}: {ex.GetType().Name}: {ex.Message}");
                await progress.LogAsync("error",
                    $"[v{idx}] persistence failed: {ex.Message}",
                    ct);
            }
        }

        await progress.ReportAsync(100,
            $"Generated {batch.Results.Count} · {passedQg} passed · {persistedIds.Count} persisted",
            ct);

        return new
        {
            sourceDraftId = payload.DraftId,
            sourcePdfId = draft.SourcePdfId,
            requested = batchRequest.Count,
            generated = batch.Results.Count,
            passedQualityGate = passedQg,
            persistedQuestionIds = persistedIds,
            persistFailures,
            sample = batch.Results.Take(5)
                .Select(r => new
                {
                    stem = Truncate(r.Question.Stem ?? "", 200),
                    topic = r.Question.Topic,
                    bloomsLevel = r.Question.BloomsLevel,
                    difficulty = r.Question.Difficulty,
                    passedQualityGate = r.PassedQualityGate,
                    casOutcome = r.CasOutcome,
                })
                .ToList(),
        };
    }

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : s.Substring(0, n) + "…";
}

public sealed record GenerateVariantsJobPayload(
    string DraftId,
    int Count,
    string Subject,
    string? Topic,
    string Grade,
    int BloomsLevel,
    float MinDifficulty,
    float MaxDifficulty,
    string Language);
