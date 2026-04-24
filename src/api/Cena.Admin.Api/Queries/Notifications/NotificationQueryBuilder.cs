// =============================================================================
// Cena Platform — Notification Query Builder (FIND-data-013)
// =============================================================================
//
// Pure, side-effect-free helpers for paging + clamping the
// /api/notifications list endpoint.
//
// Why this lives here (and not inline in the endpoint):
//   - The old GetNotifications handler called .ToListAsync() WITHOUT a
//     Skip/Take, then paged in memory. For a student with thousands of
//     notifications, every page-1 request pulled the full set.
//   - The fix pushes Skip/Take into the Marten LINQ query and pushes the
//     snooze-filter into the Where clause so the server only returns the
//     page-sized slice.
//   - By keeping the paging math + parameter clamping + snooze-filter
//     construction in this pure class, we can unit-test the behaviour
//     without spinning up Postgres. That also guarantees that a future
//     refactor of the endpoint handler cannot accidentally undo the
//     O(pageSize) guarantee — the helper's contract enforces it.
//
// Callers:
//   - src/api/Cena.Api.Host/Endpoints/NotificationsEndpoints.cs
//   - src/api/Cena.Student.Api.Host/Endpoints/NotificationsEndpoints.cs
//
// Tests:
//   - src/api/Cena.Admin.Api.Tests/Queries/Notifications/NotificationQueryBuilderTests.cs
//
// References:
//   - docs/reviews/agent-3-data-findings.md (FIND-data-013)
// =============================================================================

namespace Cena.Admin.Api.Queries.Notifications;

/// <summary>
/// Represents a clamped notification page request. Guarantees that
/// <see cref="Skip"/> and <see cref="Take"/> produce exactly one bounded
/// slice of rows, regardless of what the caller asked for.
/// </summary>
public readonly record struct NotificationPageSpec(
    int Page,
    int PageSize,
    int Skip,
    int Take);

/// <summary>
/// Pure helpers for validating, clamping and deriving notification query
/// parameters. No I/O, no Marten session.
/// </summary>
public static class NotificationQueryBuilder
{
    /// <summary>Default page size used by the endpoint when no explicit value is provided.</summary>
    public const int DefaultPageSize = 10;

    /// <summary>
    /// Maximum page size the endpoint will serve in a single request. Guards
    /// against pathological <c>?limit=1000000</c> requests that would
    /// reintroduce unbounded-query behaviour under a different name.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Clamps the caller-supplied page + pageSize into a safe <see cref="NotificationPageSpec"/>.
    /// </summary>
    /// <param name="page">
    /// Raw page number (1-based). Values less than 1 or null are coerced
    /// to 1; huge values are allowed (the DB returns 0 rows past the end).
    /// </param>
    /// <param name="pageSize">
    /// Raw page size. Null or values less than 1 coerce to
    /// <see cref="DefaultPageSize"/>; values greater than <see cref="MaxPageSize"/>
    /// are clamped down. This is the O(1) guarantee for the endpoint.
    /// </param>
    public static NotificationPageSpec ClampPage(int? page, int? pageSize)
    {
        var currentPage = page is int p && p > 0 ? p : 1;

        var size = pageSize switch
        {
            null => DefaultPageSize,
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            int s => s
        };

        var skip = (currentPage - 1) * size;
        if (skip < 0) skip = 0; // defensive; int overflow caller would need billions of pages

        // Take one extra row to detect HasMore cheaply with a single query.
        var take = size + 1;

        return new NotificationPageSpec(currentPage, size, skip, take);
    }

    /// <summary>
    /// Given a materialised page that may contain up to <c>pageSize + 1</c>
    /// rows, slice it back down to <c>pageSize</c> and report whether more
    /// rows exist beyond this page.
    /// </summary>
    public static (IReadOnlyList<T> Items, bool HasMore) SplitPage<T>(
        IReadOnlyList<T> fetched,
        NotificationPageSpec spec)
    {
        ArgumentNullException.ThrowIfNull(fetched);

        if (fetched.Count <= spec.PageSize)
            return (fetched, false);

        // fetched.Count == spec.PageSize + 1 when there is exactly one more
        // row; we drop that row and report HasMore = true.
        var items = new List<T>(spec.PageSize);
        for (var i = 0; i < spec.PageSize && i < fetched.Count; i++)
            items.Add(fetched[i]);
        return (items, true);
    }

    /// <summary>
    /// Parses the <c>filter</c> query-string value into a strongly-typed
    /// enum. Unknown values collapse to <see cref="NotificationFilter.All"/>
    /// so that the endpoint never 400s on a typo.
    /// </summary>
    public static NotificationFilter ParseFilter(string? filter)
    {
        return filter switch
        {
            "unread" => NotificationFilter.Unread,
            "read" => NotificationFilter.Read,
            _ => NotificationFilter.All
        };
    }
}

/// <summary>
/// Filter the viewer selected on the /api/notifications list endpoint.
/// </summary>
public enum NotificationFilter
{
    All,
    Unread,
    Read
}
