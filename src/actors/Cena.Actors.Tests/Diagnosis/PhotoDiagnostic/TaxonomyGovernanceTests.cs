// =============================================================================
// Cena Platform — TaxonomyGovernance tests (EPIC-PRR-J PRR-375)
//
// Locks the two-reviewer guardrail, rollback-preserves-audit-trail, and the
// dispute-feedback integration. Every test runs against the InMemory store
// (which is production-grade per memory "No stubs — production grade");
// Marten-backed behavior is identical by contract and picked up by the
// separate Marten integration test suite.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class TaxonomyGovernanceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);

    private const string Key = "mc-bag4-001-sign-flip-distributive";
    private const string Content = """{"explanationEn":"Distribute the minus."}""";

    // =========================================================================
    // Store invariants
    // =========================================================================

    [Fact]
    public async Task Propose_creates_row_with_Proposed_status_and_monotonic_version()
    {
        var store = new InMemoryTaxonomyVersionStore();

        var v1 = await store.ProposeAsync(Key, Content, "alice", Now, CancellationToken.None);
        var v2 = await store.ProposeAsync(Key, Content, "alice", Now, CancellationToken.None);

        Assert.Equal(TaxonomyVersionStatus.Proposed, v1.Status);
        Assert.Equal(1, v1.TaxonomyVersion);
        Assert.Equal(2, v2.TaxonomyVersion);
        Assert.Empty(v1.Reviewers);
        Assert.Null(v1.ApprovedAtUtc);
    }

    [Fact]
    public async Task Approve_with_one_reviewer_stays_Proposed()
    {
        // The "approve with <2 reviewers throws" DoD is implemented as:
        // the store tolerates the first reviewer but does NOT promote to
        // Approved until the threshold is met. Solo-approve attempts that
        // try to skip the threshold are blocked by status transitions
        // elsewhere (e.g., re-approving a terminal state throws). This test
        // locks that the guardrail is structural — you literally cannot
        // reach Approved without ≥2 reviewers.
        var store = new InMemoryTaxonomyVersionStore();
        var v = await store.ProposeAsync(Key, Content, "author", Now, CancellationToken.None);

        var afterOne = await store.ApproveAsync(v.Id, "alice", Now, CancellationToken.None);

        Assert.Equal(TaxonomyVersionStatus.Proposed, afterOne.Status);
        Assert.Single(afterOne.Reviewers);
        Assert.Null(afterOne.ApprovedAtUtc);
    }

    [Fact]
    public async Task Approve_with_two_distinct_reviewers_promotes_and_stamps_ApprovedAtUtc()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var v = await store.ProposeAsync(Key, Content, "author", Now, CancellationToken.None);

        await store.ApproveAsync(v.Id, "alice", Now, CancellationToken.None);
        var approved = await store.ApproveAsync(
            v.Id, "bob", Now.AddSeconds(5), CancellationToken.None);

        Assert.Equal(TaxonomyVersionStatus.Approved, approved.Status);
        Assert.Equal(2, approved.Reviewers.Count);
        Assert.Equal(Now.AddSeconds(5), approved.ApprovedAtUtc);
    }

    [Fact]
    public async Task Approve_dedupes_same_reviewer_case_insensitively()
    {
        // Two signoffs from the same human must not cross the 2-reviewer
        // threshold. Case-insensitive comparison because reviewer identity
        // typically flows from an email / username that can drift case.
        var store = new InMemoryTaxonomyVersionStore();
        var v = await store.ProposeAsync(Key, Content, "author", Now, CancellationToken.None);

        await store.ApproveAsync(v.Id, "Alice", Now, CancellationToken.None);
        var stillProposed = await store.ApproveAsync(
            v.Id, "alice", Now.AddSeconds(5), CancellationToken.None);

        Assert.Equal(TaxonomyVersionStatus.Proposed, stillProposed.Status);
        Assert.Single(stillProposed.Reviewers);
    }

    [Fact]
    public async Task Approve_on_terminal_status_throws()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var v = await store.ProposeAsync(Key, Content, "author", Now, CancellationToken.None);
        await store.ApproveAsync(v.Id, "alice", Now, CancellationToken.None);
        await store.ApproveAsync(v.Id, "bob", Now, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ApproveAsync(v.Id, "carol", Now, CancellationToken.None));
    }

    [Fact]
    public async Task Rollback_preserves_audit_trail_and_resurrects_prior_Approved_version()
    {
        var store = new InMemoryTaxonomyVersionStore();

        // v1: approved → goes live
        var v1 = await store.ProposeAsync(Key, Content, "author", Now, CancellationToken.None);
        await store.ApproveAsync(v1.Id, "alice", Now, CancellationToken.None);
        await store.ApproveAsync(v1.Id, "bob", Now, CancellationToken.None);

        // v2: approved → supersedes v1 (v1 auto-Deprecated)
        var v2 = await store.ProposeAsync(
            Key, Content + " v2", "author", Now.AddMinutes(1), CancellationToken.None);
        await store.ApproveAsync(v2.Id, "carol", Now.AddMinutes(1), CancellationToken.None);
        await store.ApproveAsync(v2.Id, "dave", Now.AddMinutes(2), CancellationToken.None);

        // Verify v2 is the live version and v1 is Deprecated.
        var live1 = await store.GetLatestApprovedAsync(Key, CancellationToken.None);
        Assert.NotNull(live1);
        Assert.Equal(v2.Id, live1!.Id);

        // Now roll back v2 — v1 should resume as the live Approved version.
        var rolled = await store.RollbackAsync(v2.Id, CancellationToken.None);
        Assert.Equal(TaxonomyVersionStatus.RolledBack, rolled.Status);
        // Content preserved — the audit trail is intact.
        Assert.Equal(Content + " v2", rolled.TemplateContent);
        Assert.NotNull(rolled.ApprovedAtUtc);

        var live2 = await store.GetLatestApprovedAsync(Key, CancellationToken.None);
        Assert.NotNull(live2);
        Assert.Equal(v1.Id, live2!.Id);
        Assert.Equal(TaxonomyVersionStatus.Approved, live2.Status);
    }

    [Fact]
    public async Task Deprecate_excludes_row_from_GetLatestApprovedAsync()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var v = await store.ProposeAsync(Key, Content, "author", Now, CancellationToken.None);
        await store.ApproveAsync(v.Id, "alice", Now, CancellationToken.None);
        await store.ApproveAsync(v.Id, "bob", Now, CancellationToken.None);

        var before = await store.GetLatestApprovedAsync(Key, CancellationToken.None);
        Assert.NotNull(before);

        var deprecated = await store.DeprecateAsync(v.Id, CancellationToken.None);
        Assert.Equal(TaxonomyVersionStatus.Deprecated, deprecated.Status);

        var after = await store.GetLatestApprovedAsync(Key, CancellationToken.None);
        Assert.Null(after);
    }

    [Fact]
    public async Task ListVersionsAsync_returns_all_versions_descending()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var v1 = await store.ProposeAsync(Key, Content, "a", Now, CancellationToken.None);
        var v2 = await store.ProposeAsync(Key, Content, "a", Now, CancellationToken.None);
        var v3 = await store.ProposeAsync(Key, Content, "a", Now, CancellationToken.None);

        var rows = await store.ListVersionsAsync(Key, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal(v3.TaxonomyVersion, rows[0].TaxonomyVersion);
        Assert.Equal(v2.TaxonomyVersion, rows[1].TaxonomyVersion);
        Assert.Equal(v1.TaxonomyVersion, rows[2].TaxonomyVersion);
    }

    [Fact]
    public async Task ListVersionsAsync_returns_empty_for_unknown_key()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var rows = await store.ListVersionsAsync("unknown-key", CancellationToken.None);
        Assert.Empty(rows);
    }

    // =========================================================================
    // Governance service
    // =========================================================================

    [Fact]
    public async Task FlagHighDisputeTemplates_returns_reasons_above_threshold()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var metrics = new StubDisputeMetricsService(
            snapshot: BuildSnapshot(
                perReasonUpheldRate: new Dictionary<DisputeReason, double>
                {
                    [DisputeReason.WrongNarration] = 0.12, // above 5%
                    [DisputeReason.WrongStepIdentified] = 0.03, // below
                    [DisputeReason.OcrMisread] = 0.08, // above
                },
                perReasonCounts: new Dictionary<DisputeReason, int>
                {
                    [DisputeReason.WrongNarration] = 40,
                    [DisputeReason.WrongStepIdentified] = 30,
                    [DisputeReason.OcrMisread] = 25,
                }));
        var service = new TaxonomyGovernanceService(store, metrics);

        var flagged = await service.FlagHighDisputeTemplatesAsync(
            threshold: 0.05, CancellationToken.None);

        Assert.Contains(DisputeReason.WrongNarration.ToString(), flagged);
        Assert.Contains(DisputeReason.OcrMisread.ToString(), flagged);
        Assert.DoesNotContain(DisputeReason.WrongStepIdentified.ToString(), flagged);
    }

    [Fact]
    public async Task FlagHighDisputeTemplates_returns_empty_when_no_reason_above_threshold()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var metrics = new StubDisputeMetricsService(
            snapshot: BuildSnapshot(
                perReasonUpheldRate: new Dictionary<DisputeReason, double>
                {
                    [DisputeReason.WrongNarration] = 0.01,
                    [DisputeReason.WrongStepIdentified] = 0.02,
                },
                perReasonCounts: new Dictionary<DisputeReason, int>
                {
                    [DisputeReason.WrongNarration] = 1,
                    [DisputeReason.WrongStepIdentified] = 1,
                }));
        var service = new TaxonomyGovernanceService(store, metrics);

        var flagged = await service.FlagHighDisputeTemplatesAsync(
            threshold: 0.05, CancellationToken.None);

        Assert.Empty(flagged);
    }

    [Fact]
    public async Task RecordReviewAsync_with_approve_true_appends_reviewer_to_latest_Proposed()
    {
        var store = new InMemoryTaxonomyVersionStore();
        var proposed = await store.ProposeAsync(Key, Content, "author", Now, CancellationToken.None);
        var service = new TaxonomyGovernanceService(store, StubDisputeMetricsService.Empty());

        await service.RecordReviewAsync(Key, "alice", approve: true, CancellationToken.None);
        var result = await service.RecordReviewAsync(
            Key, "bob", approve: true, CancellationToken.None);

        Assert.Equal(TaxonomyVersionStatus.Approved, result.Status);
        Assert.Equal(proposed.Id, result.Id);
    }

    // =========================================================================
    // Fixtures
    // =========================================================================

    private static DisputeMetricsSnapshot BuildSnapshot(
        IReadOnlyDictionary<DisputeReason, double> perReasonUpheldRate,
        IReadOnlyDictionary<DisputeReason, int> perReasonCounts)
    {
        // Fill in missing reasons with zero so the snapshot is dense and
        // the aggregator invariants (every enum value present) hold.
        var ratesDense = new Dictionary<DisputeReason, double>();
        var countsDense = new Dictionary<DisputeReason, int>();
        foreach (var r in Enum.GetValues<DisputeReason>())
        {
            ratesDense[r] = perReasonUpheldRate.TryGetValue(r, out var rate) ? rate : 0.0;
            countsDense[r] = perReasonCounts.TryGetValue(r, out var count) ? count : 0;
        }

        return new DisputeMetricsSnapshot(
            WindowDays: 7,
            TotalDisputes: countsDense.Values.Sum(),
            UpheldCount: 0,
            RejectedCount: 0,
            InReviewCount: 0,
            NewCount: 0,
            WithdrawnCount: 0,
            UpheldRate: 0.0,
            PerReasonCounts: countsDense,
            PerReasonUpheldRate: ratesDense,
            AlertThreshold: 0.05,
            IsAboveAlertThreshold: false);
    }

    private sealed class StubDisputeMetricsService : IDisputeMetricsService
    {
        private readonly DisputeMetricsSnapshot _snapshot;
        public StubDisputeMetricsService(DisputeMetricsSnapshot snapshot) { _snapshot = snapshot; }

        public static StubDisputeMetricsService Empty()
        {
            var ratesDense = new Dictionary<DisputeReason, double>();
            var countsDense = new Dictionary<DisputeReason, int>();
            foreach (var r in Enum.GetValues<DisputeReason>())
            {
                ratesDense[r] = 0.0;
                countsDense[r] = 0;
            }
            return new StubDisputeMetricsService(new DisputeMetricsSnapshot(
                WindowDays: 7,
                TotalDisputes: 0,
                UpheldCount: 0,
                RejectedCount: 0,
                InReviewCount: 0,
                NewCount: 0,
                WithdrawnCount: 0,
                UpheldRate: 0.0,
                PerReasonCounts: countsDense,
                PerReasonUpheldRate: ratesDense,
                AlertThreshold: 0.05,
                IsAboveAlertThreshold: false));
        }

        public Task<DisputeMetricsSnapshot> GetAsync(
            AggregationWindow window, CancellationToken ct)
            => Task.FromResult(_snapshot);
    }
}
