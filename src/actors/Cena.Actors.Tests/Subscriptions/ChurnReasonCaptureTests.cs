// =============================================================================
// Cena Platform — Churn-reason capture tests (EPIC-PRR-I PRR-331)
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class ChurnReasonCaptureTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Repository_records_and_lists_by_window()
    {
        var repo = new InMemoryChurnReasonRepository();
        await repo.RecordAsync(BuildReport("enc::p1", ChurnReasonCategory.TooExpensive, Now.AddDays(-1)));
        await repo.RecordAsync(BuildReport("enc::p2", ChurnReasonCategory.KidDoesNotUse, Now.AddDays(-10)));

        var window = await repo.ListSinceAsync(Now.AddDays(-7), CancellationToken.None);

        Assert.Single(window);
        Assert.Equal("enc::p1", window[0].ParentSubjectIdEncrypted);
    }

    [Fact]
    public async Task Record_is_idempotent_on_duplicate_id()
    {
        var repo = new InMemoryChurnReasonRepository();
        var r1 = BuildReport("enc::dup", ChurnReasonCategory.TooExpensive, Now);
        await repo.RecordAsync(r1);
        await repo.RecordAsync(r1);   // same Id

        var window = await repo.ListSinceAsync(Now.AddDays(-1), CancellationToken.None);
        Assert.Single(window);
    }

    [Fact]
    public async Task Purge_removes_only_matching_parents_reports()
    {
        var repo = new InMemoryChurnReasonRepository();
        await repo.RecordAsync(BuildReport("enc::p1", ChurnReasonCategory.Other, Now));
        await repo.RecordAsync(BuildReport("enc::p1", ChurnReasonCategory.Other, Now.AddDays(-30)));
        await repo.RecordAsync(BuildReport("enc::p2", ChurnReasonCategory.Other, Now));

        var purged = await repo.PurgeForParentAsync("enc::p1", CancellationToken.None);
        Assert.Equal(2, purged);

        var remaining = await repo.ListSinceAsync(Now.AddDays(-365), CancellationToken.None);
        Assert.Single(remaining);
        Assert.Equal("enc::p2", remaining[0].ParentSubjectIdEncrypted);
    }

    [Fact]
    public void Aggregator_counts_include_zero_categories_for_stable_axis()
    {
        // Dashboards draw a fixed x-axis of categories; zero-count entries
        // must still appear so bars don't disappear on sparse weeks.
        var reports = new[]
        {
            BuildReport("p1", ChurnReasonCategory.TooExpensive, Now),
            BuildReport("p2", ChurnReasonCategory.TooExpensive, Now),
            BuildReport("p3", ChurnReasonCategory.KidDoesNotUse, Now),
        };

        var counts = ChurnReasonAggregator.CountByCategory(
            reports, Now.AddDays(-7), Now);

        Assert.Equal(2, counts[ChurnReasonCategory.TooExpensive]);
        Assert.Equal(1, counts[ChurnReasonCategory.KidDoesNotUse]);
        Assert.Equal(0, counts[ChurnReasonCategory.SwitchedCompetitor]);
        Assert.Equal(0, counts[ChurnReasonCategory.Other]);
        // Every category represented:
        Assert.Equal(Enum.GetValues<ChurnReasonCategory>().Length, counts.Count);
    }

    [Fact]
    public void Aggregator_respects_window_bounds()
    {
        var reports = new[]
        {
            BuildReport("p1", ChurnReasonCategory.Other, Now.AddDays(-10)),   // outside 7-day window
            BuildReport("p2", ChurnReasonCategory.Other, Now.AddDays(-3)),    // inside
            BuildReport("p3", ChurnReasonCategory.Other, Now.AddDays(1)),     // future, outside
        };

        var counts = ChurnReasonAggregator.CountByCategory(
            reports, Now.AddDays(-7), Now);

        Assert.Equal(1, counts[ChurnReasonCategory.Other]);
    }

    [Fact]
    public void TotalInWindow_matches_per_category_sum()
    {
        var reports = new[]
        {
            BuildReport("p1", ChurnReasonCategory.TooExpensive, Now),
            BuildReport("p2", ChurnReasonCategory.MissingFeature, Now),
            BuildReport("p3", ChurnReasonCategory.MissingFeature, Now),
        };

        var total = ChurnReasonAggregator.TotalInWindow(
            reports, Now.AddDays(-1), Now);
        Assert.Equal(3, total);

        var counts = ChurnReasonAggregator.CountByCategory(
            reports, Now.AddDays(-1), Now);
        Assert.Equal(total, counts.Values.Sum());
    }

    [Fact]
    public void Aggregator_null_reports_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ChurnReasonAggregator.CountByCategory(null!, Now, Now));
    }

    [Fact]
    public void Report_BuildId_is_deterministic_and_stable()
    {
        var id1 = ChurnReasonReport.BuildId("enc::p", Now);
        var id2 = ChurnReasonReport.BuildId("enc::p", Now);
        Assert.Equal(id1, id2);
        Assert.Contains("enc::p", id1);
        Assert.Contains(Now.ToUnixTimeSeconds().ToString(), id1);
    }

    [Fact]
    public async Task Record_null_throws()
    {
        var repo = new InMemoryChurnReasonRepository();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repo.RecordAsync(null!, CancellationToken.None));
    }

    private static ChurnReasonReport BuildReport(
        string parentId, ChurnReasonCategory cat, DateTimeOffset at) =>
        new()
        {
            Id = ChurnReasonReport.BuildId(parentId, at),
            ParentSubjectIdEncrypted = parentId,
            Category = cat,
            FreeText = null,
            CollectedAt = at,
            FollowedByRefund = null,
        };
}
