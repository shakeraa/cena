// =============================================================================
// Cena Platform — IngestionJobGenerateVariantsStrategy (extracted from IngestionJobRunnerHostedService for the LOC ratchet).
// =============================================================================

using System.Text.Json;
using Cena.Admin.Api.QualityGate;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api.Ingestion;


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

        // Fail-fast on LLM-call failure (no API key, circuit-breaker open,
        // model not reachable). Pre-audit, the strategy continued through
        // an empty Results list and reported 'Generated 0 · 0 passed · 0
        // persisted' as a Completed job — silent failure that masked the
        // real reason ("No API key configured for Anthropic" was on
        // batch.Error but never logged). Throwing here lets the runner
        // mark the job Failed with the actual error message; the drawer
        // renders it in the existing red-error path and the curator can
        // act. Same family as the AsyncLocal write-back trap (cm audit
        // 2026-04-30): don't let pure-function-tested code paths
        // silently produce zero on the wider call chain.
        if (!batch.Success)
        {
            var reason = string.IsNullOrWhiteSpace(batch.Error)
                ? "AI batch generation failed without an error message — see admin-api logs for details."
                : batch.Error;
            await progress.LogAsync("error",
                $"AI batch generation failed: {reason}",
                ct);
            throw new InvalidOperationException(
                $"AI batch generation failed: {reason}");
        }

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

            var createReq = BuildVariantCreateRequest(r, batch, draft, payload);

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
                    $"[v{idx}] persisted as {detail.Id} (Bloom={r.Question.BloomsLevel}, diff={r.Question.Difficulty:F2})",
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

        // Update the source PipelineItemDocument so the kanban
        // "N variants" label and the sidebar `extractedQuestions`
        // list reflect the variants actually persisted.
        //
        // Race window: this is a LoadAsync → mutate → SaveChangesAsync
        // pair, which TOCTOU-races if two GenerateVariants jobs run
        // against the same draft concurrently (curator double-click
        // across tabs, or two curators on one draft). Mitigation: this
        // is a single-curator-clicked path on a low-frequency surface,
        // and the worst case is "second job's variant IDs clobber the
        // first" — the underlying QuestionReadModel rows persist either
        // way; only the kanban list view is affected. A Postgres advisory
        // lock would close the window if usage ever justifies it (see
        // feedback_marten_concurrency_patterns).
        if (persistedIds.Count > 0)
        {
            await using var writeSession = store.LightweightSession();
            var pipelineDoc = await writeSession.LoadAsync<Cena.Actors.Ingest.PipelineItemDocument>(
                payload.DraftId, ct);
            if (pipelineDoc is not null)
            {
                pipelineDoc.ExtractedQuestionIds.AddRange(persistedIds);
                pipelineDoc.ExtractedQuestionCount =
                    pipelineDoc.ExtractedQuestionIds.Count;
                pipelineDoc.UpdatedAt = DateTimeOffset.UtcNow;
                writeSession.Store(pipelineDoc);
                await writeSession.SaveChangesAsync(ct);

                await progress.LogAsync("info",
                    $"Linked {persistedIds.Count} variants to source pipeline doc {payload.DraftId}",
                    ct);
            }
            else
            {
                // Draft row vanished between job-start and writeback —
                // rare (only if curator rejected/deleted the source mid-
                // job). Variants still persisted; just don't appear in
                // the kanban view of the source.
                await progress.LogAsync("warn",
                    $"Source pipeline doc {payload.DraftId} not found at variant-writeback; {persistedIds.Count} variants persisted but not linked to kanban view",
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

    /// <summary>
    /// PRR-322f-audit / 2026-04-30: pure construction of the
    /// CreateQuestionRequest a single batch-result becomes when persisted
    /// as a Bagrut variant. Extracted from the inline code so the
    /// provenance contract can be unit-tested without mocking the entire
    /// job-runner pipeline (Marten + IAiGenerationService + IQuestionBankService
    /// + IJobProgressReporter). Keep the field assignments aligned with
    /// QuestionState.QuestionProvenanceState (source) + AiGenerationState
    /// (LLM) — the two distinct provenance states the question aggregate
    /// records.
    /// </summary>
    internal static CreateQuestionRequest BuildVariantCreateRequest(
        BatchGenerateResult result,
        BatchGenerateResponse batch,
        Cena.Infrastructure.Documents.BagrutDraftPayloadDocument draft,
        GenerateVariantsJobPayload payload)
    {
        var difficulty = (float)Math.Clamp((decimal)result.Question.Difficulty, 0m, 1m);

        return new CreateQuestionRequest(
            SourceType: "ai-generated",
            Stem: result.Question.Stem,
            StemHtml: null,
            Options: result.Question.Options
                .Select(o => new CreateOptionRequest(
                    Label: o.Label,
                    Text: o.Text,
                    TextHtml: null,
                    IsCorrect: o.IsCorrect,
                    DistractorRationale: o.DistractorRationale))
                .ToList(),
            Subject: payload.Subject,
            Topic: result.Question.Topic ?? payload.Topic,
            Grade: payload.Grade,
            BloomsLevel: result.Question.BloomsLevel,
            Difficulty: difficulty,
            ConceptIds: null,
            Language: payload.Language,
            // Source-provenance: populates QuestionProvenanceState. The
            // Bagrut draft IS the source for these variants. Was null /
            // null / null / null pre-audit; curators couldn't trace any
            // variant back to its seed without parsing the synthetic
            // string previously dumped into PromptText.
            SourceDocId:    payload.DraftId,
            SourceUrl:      $"bagrut-pdf:{draft.SourcePdfId}",
            SourceFilename: $"{draft.ExamCode}-page{draft.SourcePage}.pdf",
            OriginalText:   draft.Prompt,
            // AI-generation provenance: populates AiGenerationState. Real
            // values now (was null / synthetic-breadcrumb / null pre-audit).
            // PromptText holds the actual LLM prompt used (shared across
            // the N variants in this batch — one GenerateQuestionsAsync
            // call). ModelTemperature + RawModelOutput likewise.
            PromptText:        batch.PromptUsed ?? "",
            ModelId:           batch.ModelUsed,
            ModelTemperature:  batch.TemperatureUsed,
            RawModelOutput:    batch.RawOutput,
            Explanation:       result.Question.Explanation,
            LearningObjectiveId: null);
    }
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
