// =============================================================================
// PRR-322f-error-paths (t_45c183ba9fef) — server-side fail-fast tests
//
// Pins Layer-2 of the cm audit 2026-04-30: when BatchGenerateAsync returns
// Success=false (no API key, circuit-breaker open, model unreachable),
// GenerateVariantsJobStrategy MUST throw with batch.Error so the runner
// marks the job Failed (not Completed). Pre-audit, the strategy continued
// through an empty Results list and reported 'Generated 0 · 0 passed · 0
// persisted' as a Completed job — silently masking the real reason.
//
// Same family as the AsyncLocal write-back trap (also in cm audit
// 2026-04-30): pure-function-tested code paths can silently produce zero
// on the wider call chain. Integration tests against the real call chain
// catch what unit tests miss.
// =============================================================================

using Cena.Admin.Api;
using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.QualityGate;
using Cena.Api.Contracts.Admin.QualityGate;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class GenerateVariantsErrorPathsTests
{
    private const string DraftId = "draft-math-5u-2026-035582-p3";

    private static IServiceProvider BuildScope(
        IAiGenerationService ai,
        IDocumentStore store,
        IQuestionBankService qbs,
        IQualityGateService qg,
        bool legalGateEnabled = true)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cena:Variants:BagrutSeedToLlmEnabled"] = legalGateEnabled.ToString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSingleton(store);
        services.AddSingleton(ai);
        services.AddSingleton(qbs);
        services.AddSingleton(qg);
        return services.BuildServiceProvider();
    }

    private static IngestionJobDocument MakeJob(string draftId = DraftId, int count = 3) =>
        new()
        {
            Id          = "job-test-1",
            Type        = IngestionJobType.GenerateVariants,
            Status      = IngestionJobStatus.Running,
            CreatedBy   = "test-curator",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                draftId,
                count,
                subject       = "math",
                topic         = (string?)null,
                grade         = "12",
                bloomsLevel   = 3,
                minDifficulty = 0.4f,
                maxDifficulty = 0.7f,
                language      = "he",
            }),
        };

    private static BagrutDraftPayloadDocument MakeDraft() => new()
    {
        Id              = DraftId,
        ExamCode        = "math-5u-2026-035582",
        SourcePdfId     = "pdf-test",
        SourcePage      = 3,
        Prompt          = "Compute the integral of x dx",
        LatexContent    = @"\int x \,dx",
        ExtractionConfidence = 0.9,
        CreatedAt       = DateTimeOffset.UtcNow,
    };

    private static IDocumentStore MakeStoreReturningDraft(BagrutDraftPayloadDocument draft)
    {
        var store = Substitute.For<IDocumentStore>();
        var query = Substitute.For<IQuerySession>();
        store.QuerySession().Returns(query);
        query.LoadAsync<BagrutDraftPayloadDocument>(draft.Id, Arg.Any<CancellationToken>())
            .Returns(draft);
        return store;
    }

    private sealed class NoopProgressReporter : IJobProgressReporter
    {
        public List<(string level, string message)> Logs { get; } = new();
        public bool CancelRequested => false;
        public Task ReportAsync(int pct, string? message, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogAsync(string level, string message, CancellationToken ct = default)
        {
            Logs.Add((level, message));
            return Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    // Layer 2 — fail-fast on batch.Success==false
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Throws_With_Batch_Error_When_Llm_Call_Fails()
    {
        // Reproduces the exact production scenario cm caught: Anthropic
        // API key not configured → GenerateQuestionsAsync returns
        // Success=false, "No API key configured for Anthropic..." →
        // BatchGenerateAsync forwards Success=false → strategy MUST
        // throw, not silent-Complete.
        var ai = Substitute.For<IAiGenerationService>();
        ai.BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>())
            .Returns(new BatchGenerateResponse(
                Success:           false,
                Results:           Array.Empty<BatchGenerateResult>(),
                TotalGenerated:    0,
                PassedQualityGate: 0,
                NeedsReview:       0,
                AutoRejected:      0,
                ModelUsed:         "claude-sonnet-4-6-20260215",
                Error:             "No API key configured for Anthropic. Set Anthropic:ApiKey in configuration or go to Settings > AI Providers."));

        var store = MakeStoreReturningDraft(MakeDraft());
        var qbs = Substitute.For<IQuestionBankService>();
        var qg  = Substitute.For<IQualityGateService>();
        var sp  = BuildScope(ai, store, qbs, qg);

        var strategy = new GenerateVariantsJobStrategy();
        var reporter = new NoopProgressReporter();
        var job = MakeJob();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            strategy.ExecuteAsync(job, sp, reporter, CancellationToken.None));

        Assert.Contains("AI batch generation failed", ex.Message);
        Assert.Contains("No API key configured for Anthropic", ex.Message);

        // Curator-facing log entry MUST also surface the error so the
        // drawer can render it. Pinning this ensures a future refactor
        // doesn't drop the log call while keeping the throw.
        Assert.Contains(reporter.Logs,
            entry => entry.level == "error"
                && entry.message.Contains("AI batch generation failed", StringComparison.Ordinal)
                && entry.message.Contains("No API key configured for Anthropic", StringComparison.Ordinal));

        // QuestionBankService MUST NOT be called when the LLM call
        // failed — no candidates to persist. Pre-audit the empty-Results
        // loop ran and called nothing, but the SUCCESS-shaped result doc
        // would have written. Pin: the persistence path is never even
        // entered.
        await qbs.DidNotReceiveWithAnyArgs().CreateQuestionAsync(default!, default!);
    }

    [Fact]
    public async Task ExecuteAsync_Throws_With_Generic_Message_When_Batch_Error_Is_Empty()
    {
        // Defensive guard: if a future provider returns Success=false
        // with a null/empty Error, the strategy still throws with a
        // clear-but-generic message rather than NRE / blank string.
        var ai = Substitute.For<IAiGenerationService>();
        ai.BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>())
            .Returns(new BatchGenerateResponse(
                Success:           false,
                Results:           Array.Empty<BatchGenerateResult>(),
                TotalGenerated:    0,
                PassedQualityGate: 0,
                NeedsReview:       0,
                AutoRejected:      0,
                ModelUsed:         "claude-sonnet-4-6-20260215",
                Error:             null));

        var store = MakeStoreReturningDraft(MakeDraft());
        var sp = BuildScope(ai, store,
            Substitute.For<IQuestionBankService>(),
            Substitute.For<IQualityGateService>());

        var strategy = new GenerateVariantsJobStrategy();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            strategy.ExecuteAsync(MakeJob(), sp, new NoopProgressReporter(), CancellationToken.None));

        Assert.Contains("AI batch generation failed", ex.Message);
        Assert.Contains("without an error message", ex.Message);
    }

    // ------------------------------------------------------------------
    // Pre-existing legal-gate behaviour preserved (regression guard)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Throws_When_Legal_Gate_Disabled_Even_When_Llm_Available()
    {
        // PRR-249 legal gate predates the no-API-key check. When the
        // gate is OFF, the strategy MUST refuse before touching any LLM
        // — pinned so the new fail-fast doesn't accidentally re-order
        // checks and let a disabled-by-legal job slip through.
        var ai = Substitute.For<IAiGenerationService>();
        var store = Substitute.For<IDocumentStore>();
        var sp = BuildScope(ai, store,
            Substitute.For<IQuestionBankService>(),
            Substitute.For<IQualityGateService>(),
            legalGateEnabled: false);

        var strategy = new GenerateVariantsJobStrategy();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            strategy.ExecuteAsync(MakeJob(), sp, new NoopProgressReporter(), CancellationToken.None));

        Assert.Contains("SOURCE_ANCHORED_VARIANTS_DISABLED", ex.Message);
        // LLM never touched.
        await ai.DidNotReceiveWithAnyArgs().BatchGenerateAsync(default!, default!);
    }
}
