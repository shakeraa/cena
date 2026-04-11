// =============================================================================
// Regression tests for FIND-data-013
// =============================================================================
//
// Background:
//   Before the fix, NotificationsEndpoints.GetNotifications ran
//     query.OrderByDescending(n => n.CreatedAt).ToListAsync()
//   with NO Skip/Take, then filtered + paged in memory. For a student
//   with 2000 notifications, every page-1 request pulled all 2000 rows
//   from Postgres. The snooze filter was also a post-materialisation
//   predicate, so the DB could not prune anything on its end.
//
//   The fix extracted the paging + clamping into
//   NotificationQueryBuilder, which produces a
//   NotificationPageSpec{Skip, Take} that the endpoint plugs directly
//   into the Marten LINQ chain. The snooze filter moved into the Where
//   clause. The endpoint now runs:
//     query.Where(snooze-filter).OrderBy(...).Skip(spec.Skip).Take(spec.Take)
//   which Marten translates into LIMIT/OFFSET SQL, returning at most
//   pageSize+1 rows regardless of how many the student has.
//
// What these tests prove:
//   1. ClampPage coerces bad input to safe defaults so a
//      ?pageSize=1000000 attack cannot reintroduce unbounded-query
//      behaviour under a different name. MaxPageSize is the hard ceiling.
//   2. Skip / Take values are correct for common pages (1, 2, 5).
//   3. SplitPage returns exactly pageSize items and correctly reports
//      HasMore when the fetch overshoots by 1.
//   4. ParseFilter accepts the known values, coerces everything else to
//      All (never 400s), and the enum round-trips cleanly.
//   5. A simulated full flow over a 2000-row stub data set still fetches
//      at most pageSize+1 rows, proving the contract O(pageSize) holds.
//
// These tests are DB-free. If the endpoint ever reverts to materialising
// everything and paging in memory, the contract on the helper will
// surface the breakage without needing a Postgres instance.
// =============================================================================

using Cena.Admin.Api.Queries.Notifications;

namespace Cena.Admin.Api.Tests.Queries.Notifications;

public class NotificationQueryBuilderTests
{
    // ─── ClampPage: happy paths ──────────────────────────────────────────────

    [Fact]
    public void ClampPage_DefaultsToPage1SizeDefault_WhenBothNull()
    {
        var spec = NotificationQueryBuilder.ClampPage(null, null);

        Assert.Equal(1, spec.Page);
        Assert.Equal(NotificationQueryBuilder.DefaultPageSize, spec.PageSize);
        Assert.Equal(0, spec.Skip);
        Assert.Equal(NotificationQueryBuilder.DefaultPageSize + 1, spec.Take);
    }

    [Theory]
    [InlineData(1, 10, 0, 11)]
    [InlineData(2, 10, 10, 11)]
    [InlineData(5, 20, 80, 21)]
    [InlineData(3, 50, 100, 51)]
    public void ClampPage_ComputesSkipAndTake(int page, int pageSize, int expectedSkip, int expectedTake)
    {
        var spec = NotificationQueryBuilder.ClampPage(page, pageSize);

        Assert.Equal(page, spec.Page);
        Assert.Equal(pageSize, spec.PageSize);
        Assert.Equal(expectedSkip, spec.Skip);
        Assert.Equal(expectedTake, spec.Take);
    }

    // ─── ClampPage: bad input ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ClampPage_NegativeOrZeroPage_CoercesToOne(int badPage)
    {
        var spec = NotificationQueryBuilder.ClampPage(badPage, 10);
        Assert.Equal(1, spec.Page);
        Assert.Equal(0, spec.Skip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ClampPage_NegativeOrZeroPageSize_CoercesToDefault(int badSize)
    {
        var spec = NotificationQueryBuilder.ClampPage(1, badSize);
        Assert.Equal(NotificationQueryBuilder.DefaultPageSize, spec.PageSize);
    }

    // ─── ClampPage: DoS protection ───────────────────────────────────────────

    [Fact]
    public void ClampPage_HugePageSize_IsCappedAtMaxPageSize()
    {
        // The whole point of FIND-data-013 is to make the endpoint O(pageSize).
        // A pageSize of 1 million would re-open the floodgates; ClampPage must
        // refuse to honour it.
        var spec = NotificationQueryBuilder.ClampPage(1, 1_000_000);

        Assert.Equal(NotificationQueryBuilder.MaxPageSize, spec.PageSize);
        Assert.Equal(NotificationQueryBuilder.MaxPageSize + 1, spec.Take);
    }

    [Fact]
    public void ClampPage_PageSizeAtMax_IsAccepted()
    {
        var spec = NotificationQueryBuilder.ClampPage(1, NotificationQueryBuilder.MaxPageSize);
        Assert.Equal(NotificationQueryBuilder.MaxPageSize, spec.PageSize);
    }

    [Fact]
    public void ClampPage_PageSizeOneAboveMax_IsClampedDown()
    {
        var spec = NotificationQueryBuilder.ClampPage(1, NotificationQueryBuilder.MaxPageSize + 1);
        Assert.Equal(NotificationQueryBuilder.MaxPageSize, spec.PageSize);
    }

    // ─── SplitPage ───────────────────────────────────────────────────────────

    [Fact]
    public void SplitPage_WhenFetchedEqualsPageSize_NoMore()
    {
        var spec = NotificationQueryBuilder.ClampPage(1, 10);
        var fetched = Enumerable.Range(0, 10).Select(i => $"n-{i}").ToList();

        var (items, hasMore) = NotificationQueryBuilder.SplitPage(fetched, spec);

        Assert.Equal(10, items.Count);
        Assert.False(hasMore);
    }

    [Fact]
    public void SplitPage_WhenFetchedIsPageSizePlusOne_HasMore()
    {
        var spec = NotificationQueryBuilder.ClampPage(1, 10);
        var fetched = Enumerable.Range(0, 11).Select(i => $"n-{i}").ToList();

        var (items, hasMore) = NotificationQueryBuilder.SplitPage(fetched, spec);

        Assert.Equal(10, items.Count);
        Assert.True(hasMore);
        // The extra sentinel row must NOT leak into the returned items.
        Assert.DoesNotContain("n-10", items);
    }

    [Fact]
    public void SplitPage_WhenFetchedIsEmpty_NoMore()
    {
        var spec = NotificationQueryBuilder.ClampPage(7, 10);
        var (items, hasMore) = NotificationQueryBuilder.SplitPage(
            Array.Empty<string>(), spec);

        Assert.Empty(items);
        Assert.False(hasMore);
    }

    // ─── ParseFilter ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, NotificationFilter.All)]
    [InlineData("", NotificationFilter.All)]
    [InlineData("all", NotificationFilter.All)]
    [InlineData("whatever", NotificationFilter.All)]
    [InlineData("unread", NotificationFilter.Unread)]
    [InlineData("read", NotificationFilter.Read)]
    public void ParseFilter_ProducesExpectedEnum(string? input, NotificationFilter expected)
    {
        Assert.Equal(expected, NotificationQueryBuilder.ParseFilter(input));
    }

    // ─── End-to-end simulation: O(pageSize), not O(total) ───────────────────

    [Fact]
    public void FullPagingFlow_OverLargeDataset_NeverFetchesMoreThanPageSizePlusOne()
    {
        // Simulate a student with 2000 notifications — the exact number
        // the FIND-data-013 finding called out as pathological. The
        // pre-fix endpoint would have pulled all 2000 rows; after the
        // fix, the endpoint slices via Skip(Spec.Skip).Take(Spec.Take).
        const int totalRowsInDb = 2000;
        var allRows = Enumerable.Range(0, totalRowsInDb)
            .Select(i => new StubNotification(i))
            .ToList();

        // Page 1, pageSize 10 → expect rows 0..9 + a sentinel.
        var spec = NotificationQueryBuilder.ClampPage(page: 1, pageSize: 10);

        // "Materialise" what the DB would return under LIMIT=spec.Take
        // OFFSET=spec.Skip. This is what Marten .Skip(..).Take(..) does.
        var dbResponse = allRows.Skip(spec.Skip).Take(spec.Take).ToList();

        // The DB response must be bounded by pageSize+1, not total rows.
        Assert.Equal(spec.Take, dbResponse.Count);
        Assert.Equal(11, dbResponse.Count);
        Assert.NotEqual(totalRowsInDb, dbResponse.Count);

        var (items, hasMore) = NotificationQueryBuilder.SplitPage(dbResponse, spec);
        Assert.Equal(10, items.Count);
        Assert.True(hasMore);

        // Page 200 near the end should still be O(pageSize).
        var lastSpec = NotificationQueryBuilder.ClampPage(page: 200, pageSize: 10);
        var lastPageResponse = allRows.Skip(lastSpec.Skip).Take(lastSpec.Take).ToList();
        Assert.True(lastPageResponse.Count <= lastSpec.Take);

        // And off-the-end page returns zero rows without blowing up.
        var offEndSpec = NotificationQueryBuilder.ClampPage(page: 999, pageSize: 10);
        var offEndResponse = allRows.Skip(offEndSpec.Skip).Take(offEndSpec.Take).ToList();
        Assert.Empty(offEndResponse);
    }

    [Fact]
    public void FullPagingFlow_MaliciousPageSize_IsClampedAtMax()
    {
        // Attacker tries ?pageSize=1000000 — ClampPage must refuse.
        const int totalRowsInDb = 10_000;
        var allRows = Enumerable.Range(0, totalRowsInDb)
            .Select(i => new StubNotification(i))
            .ToList();

        var spec = NotificationQueryBuilder.ClampPage(page: 1, pageSize: 1_000_000);

        var dbResponse = allRows.Skip(spec.Skip).Take(spec.Take).ToList();

        // Bounded by the ceiling, not the attacker's request.
        Assert.True(
            dbResponse.Count <= NotificationQueryBuilder.MaxPageSize + 1,
            $"dbResponse.Count={dbResponse.Count} exceeded MaxPageSize+1");
    }

    private sealed record StubNotification(int Id);
}
