// =============================================================================
// Cena Platform — MartenQuestionPool policy guard tests (PRR-246 + ADR-0043)
//
// Pins the new QuestionPoolPolicy filter behaviour:
//
//   1. ADR-0043 — default policy excludes BagrutReference items. This is
//      the P0 ship-gate hold the original 4-arg LoadAsync silently leaked.
//   2. PRR-246 — when QuestionPaperCodes is non-empty, only items whose
//      QuestionPaperCodes intersects the policy's set surface in the pool.
//   3. PRR-246 — empty/null QuestionPaperCodes = no exam-target restriction
//      (Freestyle path stays unchanged).
//   4. Admin opt-in — AllowReferenceItems=true returns reference items
//      alongside recreations (curator surface).
// =============================================================================

using Cena.Actors.Questions;
using Cena.Actors.Serving;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Serving;

public sealed class MartenQuestionPoolPolicyTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = "pool_policy_test_" + Guid.NewGuid().ToString("N")[..8];
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Schema.For<QuestionReadModel>().Identity(d => d.Id);
        });

        await SeedAsync();
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DefaultPolicy_ExcludesBagrutReferenceItems_ClosesADR0043Gap()
    {
        var pool = await MartenQuestionPool.LoadAsync(
            _store, new[] { "math" }, NullLogger.Instance);

        // Reference item q-ref must NOT surface; only recreation items q-rec-* do.
        var ids = pool.GetForConcept("algebra").Select(p => p.ItemId).ToList();
        Assert.DoesNotContain("q-ref-leaks-into-pool", ids);
        Assert.Contains("q-rec-deliverable", ids);
    }

    [Fact]
    public async Task ExplicitStrictPolicy_ExcludesBagrutReferenceItems()
    {
        var pool = await MartenQuestionPool.LoadAsync(
            _store, new[] { "math" }, QuestionPoolPolicy.Strict, NullLogger.Instance);

        var ids = pool.GetForConcept("algebra").Select(p => p.ItemId).ToList();
        Assert.DoesNotContain("q-ref-leaks-into-pool", ids);
    }

    [Fact]
    public async Task AllowReferenceItemsPolicy_IncludesBagrutReference_AdminOptIn()
    {
        // Curator/admin surface needs to inspect reference corpus directly.
        var policy = new QuestionPoolPolicy(AllowReferenceItems: true);
        var pool = await MartenQuestionPool.LoadAsync(
            _store, new[] { "math" }, policy, NullLogger.Instance);

        var ids = pool.GetForConcept("algebra").Select(p => p.ItemId).ToList();
        Assert.Contains("q-ref-leaks-into-pool", ids);
        Assert.Contains("q-rec-deliverable", ids);
    }

    [Fact]
    public async Task PaperCodesFilter_RestrictsToAlignedItems()
    {
        // Student with ExamTarget {035582} should only see items aligned to 035582.
        var policy = new QuestionPoolPolicy(
            AllowReferenceItems: false,
            QuestionPaperCodes: new[] { "035582" });

        var pool = await MartenQuestionPool.LoadAsync(
            _store, new[] { "math" }, policy, NullLogger.Instance);

        var ids = pool.GetForConcept("algebra").Select(p => p.ItemId).ToList();
        Assert.Contains("q-rec-035582-only", ids);   // aligned to 035582
        Assert.DoesNotContain("q-rec-035581-only", ids); // aligned to 035581 only
        Assert.Contains("q-rec-multi-paper", ids);   // aligned to BOTH papers
    }

    [Fact]
    public async Task EmptyPaperCodes_NoRestriction_FreestyleBehaviour()
    {
        // Freestyle path: no exam-target binding. All recreated items
        // surface (still excludes reference items by default).
        var policy = new QuestionPoolPolicy(
            AllowReferenceItems: false,
            QuestionPaperCodes: Array.Empty<string>());

        var pool = await MartenQuestionPool.LoadAsync(
            _store, new[] { "math" }, policy, NullLogger.Instance);

        var ids = pool.GetForConcept("algebra").Select(p => p.ItemId).ToList();
        Assert.Contains("q-rec-035581-only", ids);
        Assert.Contains("q-rec-035582-only", ids);
        Assert.Contains("q-rec-multi-paper", ids);
        Assert.DoesNotContain("q-ref-leaks-into-pool", ids);
    }

    private async Task SeedAsync()
    {
        await using var session = _store.LightweightSession();

        // 1. BagrutReference item — MUST NEVER surface under default policy.
        session.Store(new QuestionReadModel
        {
            Id = "q-ref-leaks-into-pool",
            Subject = "math",
            Status = "Published",
            BloomsLevel = 3,
            Difficulty = 0.5f,
            Concepts = new List<string> { "algebra" },
            Language = "he",
            SourceType = "BagrutReference",
            QuestionPaperCodes = new List<string> { "035582" },
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // 2. Plain recreation — surfaces under default; no paper-code restriction.
        session.Store(new QuestionReadModel
        {
            Id = "q-rec-deliverable",
            Subject = "math",
            Status = "Published",
            BloomsLevel = 3,
            Difficulty = 0.5f,
            Concepts = new List<string> { "algebra" },
            Language = "he",
            SourceType = "ai-generated",
            QuestionPaperCodes = new List<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // 3. Recreation aligned only to 035581.
        session.Store(new QuestionReadModel
        {
            Id = "q-rec-035581-only",
            Subject = "math",
            Status = "Published",
            BloomsLevel = 3,
            Difficulty = 0.5f,
            Concepts = new List<string> { "algebra" },
            Language = "he",
            SourceType = "authored",
            QuestionPaperCodes = new List<string> { "035581" },
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // 4. Recreation aligned only to 035582.
        session.Store(new QuestionReadModel
        {
            Id = "q-rec-035582-only",
            Subject = "math",
            Status = "Published",
            BloomsLevel = 3,
            Difficulty = 0.5f,
            Concepts = new List<string> { "algebra" },
            Language = "he",
            SourceType = "authored",
            QuestionPaperCodes = new List<string> { "035582" },
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // 5. Recreation aligned to BOTH 035581 + 035582 (shared skill family).
        session.Store(new QuestionReadModel
        {
            Id = "q-rec-multi-paper",
            Subject = "math",
            Status = "Published",
            BloomsLevel = 3,
            Difficulty = 0.5f,
            Concepts = new List<string> { "algebra" },
            Language = "he",
            SourceType = "authored",
            QuestionPaperCodes = new List<string> { "035581", "035582" },
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // 6. Draft item — never surfaces regardless of policy.
        session.Store(new QuestionReadModel
        {
            Id = "q-draft-not-published",
            Subject = "math",
            Status = "Draft",
            BloomsLevel = 3,
            Difficulty = 0.5f,
            Concepts = new List<string> { "algebra" },
            Language = "he",
            SourceType = "authored",
            QuestionPaperCodes = new List<string> { "035582" },
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await session.SaveChangesAsync();
    }
}
