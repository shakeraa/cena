// =============================================================================
// Cena Platform — ChurnReasonCapture (EPIC-PRR-I PRR-331)
//
// Structured capture of cancel/downgrade reasons. The existing
// SubscriptionCancelled_V1 event carries a free-form string that is
// fine for per-event audit but useless for tier-mix analysis — free
// text doesn't aggregate. PRR-331 adds a categorical dropdown so we
// can surface:
//   - "how many cancels cite 'too expensive' vs 'kid doesn't use'"
//   - "feature-gap distribution across tier"
//   - signal into PRR-332 pricing-ab-testing + PRR-330 unit-economics
//
// Session-scoped retention per ADR-0003 — the survey response is
// non-sensitive (no student content, no PII beyond the already-
// encrypted parent id) but we still honour the 30-day default
// retention via the Marten doc's implicit lifecycle. The repository
// exposes Delete so a parent who revokes data access sees the
// survey record purged alongside their subscription trail.
//
// Free-text comment is OPTIONAL + bounded (MaxFreeTextLength). Empty
// comment is the expected common case.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Categorical churn-reason taxonomy (PRR-331).</summary>
public enum ChurnReasonCategory
{
    /// <summary>Tier or sibling-add price exceeded budget.</summary>
    TooExpensive = 0,

    /// <summary>Student (kid) stopped using the product meaningfully.</summary>
    KidDoesNotUse = 1,

    /// <summary>Feature / content gap — product did not do X the parent needs.</summary>
    MissingFeature = 2,

    /// <summary>Switched to a different exam-prep service.</summary>
    SwitchedCompetitor = 3,

    /// <summary>Student finished the exam cycle (e.g., Bagrut 4U done).</summary>
    ExamCycleEnded = 4,

    /// <summary>Anything else; free-text comment is a signal here.</summary>
    Other = 5,
}

/// <summary>
/// A structured churn-reason survey response. Written at cancel /
/// downgrade time. The Marten doc carries the parent id as a stable
/// key so a later revocation or anonymization run can locate and
/// purge it.
/// </summary>
public sealed record ChurnReasonReport
{
    /// <summary>Max free-text length. Enforced server-side.</summary>
    public const int MaxFreeTextLength = 500;

    /// <summary>Compound id: <c>{parentSubjectIdEncrypted}|{collectedAt.ToUnixTimeSeconds()}</c>.</summary>
    public string Id { get; init; } = "";

    /// <summary>Encrypted parent id — matches the subscription stream key.</summary>
    public string ParentSubjectIdEncrypted { get; init; } = "";

    /// <summary>Which dropdown option the parent picked.</summary>
    public ChurnReasonCategory Category { get; init; }

    /// <summary>Optional free-text elaboration. Bounded at <see cref="MaxFreeTextLength"/>.</summary>
    public string? FreeText { get; init; }

    /// <summary>Wall-clock of the survey submission.</summary>
    public DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// Whether the cancel was followed by a refund (per PRR-306). Null
    /// when unknown at capture time — the refund worker can update later.
    /// </summary>
    public bool? FollowedByRefund { get; init; }

    /// <summary>Build the stable compound id given the two natural keys.</summary>
    public static string BuildId(string parentSubjectIdEncrypted, DateTimeOffset collectedAt)
        => parentSubjectIdEncrypted + "|" + collectedAt.ToUnixTimeSeconds();
}

/// <summary>Abstraction over the churn-reason store.</summary>
public interface IChurnReasonRepository
{
    /// <summary>Write a survey response. Idempotent by Id.</summary>
    Task RecordAsync(ChurnReasonReport report, CancellationToken ct = default);

    /// <summary>
    /// Enumerate reports in <paramref name="windowStartUtc"/> → now (inclusive).
    /// The dashboard aggregates these server-side via
    /// <see cref="ChurnReasonAggregator.Aggregate"/>.
    /// </summary>
    Task<IReadOnlyList<ChurnReasonReport>> ListSinceAsync(
        DateTimeOffset windowStartUtc, CancellationToken ct = default);

    /// <summary>
    /// Purge every report for the given parent. Used on account-deletion
    /// requests to honour data-minimisation obligations.
    /// </summary>
    Task<int> PurgeForParentAsync(string parentSubjectIdEncrypted, CancellationToken ct = default);
}

/// <summary>
/// In-memory repository for dev/test. Not a stub — a real
/// ConcurrentDictionary-backed store that honours every interface
/// method with production-grade semantics (mirrors the
/// SandboxPaymentGateway idiom). Marten-backed variant is an
/// upcoming follow-up; DI lets hosts swap the binding without touching
/// callers.
/// </summary>
public sealed class InMemoryChurnReasonRepository : IChurnReasonRepository
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChurnReasonReport> _rows
        = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task RecordAsync(ChurnReasonReport report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        _rows[report.Id] = report;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChurnReasonReport>> ListSinceAsync(
        DateTimeOffset windowStartUtc, CancellationToken ct = default)
    {
        var snapshot = _rows.Values
            .Where(r => r.CollectedAt >= windowStartUtc)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ChurnReasonReport>>(snapshot);
    }

    /// <inheritdoc />
    public Task<int> PurgeForParentAsync(
        string parentSubjectIdEncrypted, CancellationToken ct = default)
    {
        var purged = 0;
        foreach (var key in _rows.Keys)
        {
            if (_rows.TryGetValue(key, out var r) &&
                string.Equals(r.ParentSubjectIdEncrypted, parentSubjectIdEncrypted, StringComparison.Ordinal))
            {
                if (_rows.TryRemove(key, out _)) purged++;
            }
        }
        return Task.FromResult(purged);
    }
}

/// <summary>
/// Pure dashboard aggregator. Given a flat list of reports + a
/// window, return the per-category counts the unit-economics
/// dashboard consumes.
/// </summary>
public static class ChurnReasonAggregator
{
    /// <summary>
    /// Count reports per category within the window. Input doesn't
    /// need to be pre-filtered; this helper re-filters by
    /// <paramref name="windowStartUtc"/> and <paramref name="windowEndUtc"/>.
    /// Categories with zero count ARE still present in the output
    /// (count=0) so the dashboard can draw a stable axis.
    /// </summary>
    public static IReadOnlyDictionary<ChurnReasonCategory, int> CountByCategory(
        IEnumerable<ChurnReasonReport> reports,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var map = Enum.GetValues<ChurnReasonCategory>()
            .ToDictionary(c => c, _ => 0);
        foreach (var r in reports)
        {
            if (r.CollectedAt < windowStartUtc) continue;
            if (r.CollectedAt > windowEndUtc) continue;
            map[r.Category]++;
        }
        return map;
    }

    /// <summary>Total count in the window, for dashboard header.</summary>
    public static int TotalInWindow(
        IEnumerable<ChurnReasonReport> reports,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return reports.Count(r =>
            r.CollectedAt >= windowStartUtc && r.CollectedAt <= windowEndUtc);
    }
}
