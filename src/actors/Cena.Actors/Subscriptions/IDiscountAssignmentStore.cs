// =============================================================================
// Cena Platform — IDiscountAssignmentStore + InMemory impl
// (per-user discount-codes feature)
//
// Persistence seam for the DiscountAssignment aggregate. Two responsibilities:
//
//   1. Per-stream load + append (mirrors ISubscriptionAggregateStore shape).
//   2. Cross-stream lookups by normalized email — needed because the
//      one-active-per-email rule and the student-side
//      /api/me/applicable-discount endpoint both need "given an email,
//      what's the active assignment?".
//
// The InMemory impl is production-grade for single-host installs (matches
// the ADR-0042 migration pattern). The Marten variant is in
// MartenDiscountAssignmentStore.cs.
// =============================================================================

using System.Collections.Concurrent;
using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>Lightweight read-side row for cross-stream lookups.</summary>
/// <param name="AssignmentId">Stable assignment id.</param>
/// <param name="TargetEmailNormalized">Canonical email this assignment was issued for.</param>
/// <param name="Status">Lifecycle state.</param>
/// <param name="Kind">PercentOff or AmountOff.</param>
/// <param name="Value">Basis points or agorot.</param>
/// <param name="DurationMonths">Repeating duration in months.</param>
/// <param name="StripeCouponId">External coupon id.</param>
/// <param name="StripePromotionCodeId">External promotion code id.</param>
/// <param name="IssuedByAdminSubjectIdEncrypted">Encrypted issuer subject id.</param>
/// <param name="Reason">Free-text issuance reason (audit).</param>
/// <param name="IssuedAt">Issuance timestamp.</param>
/// <param name="RedeemedAt">Redemption timestamp (null until Redeemed).</param>
/// <param name="RevokedAt">Revocation timestamp (null until Revoked).</param>
public sealed record DiscountAssignmentSummary(
    string AssignmentId,
    string TargetEmailNormalized,
    DiscountStatus Status,
    DiscountKind Kind,
    int Value,
    int DurationMonths,
    string StripeCouponId,
    string StripePromotionCodeId,
    string IssuedByAdminSubjectIdEncrypted,
    string Reason,
    DateTimeOffset IssuedAt,
    DateTimeOffset? RedeemedAt,
    DateTimeOffset? RevokedAt);

/// <summary>Persistence seam for DiscountAssignment aggregates.</summary>
public interface IDiscountAssignmentStore
{
    /// <summary>Load the aggregate for the given assignment id (fresh if no stream).</summary>
    Task<DiscountAssignment> LoadAsync(string assignmentId, CancellationToken ct);

    /// <summary>Append an event to the stream and commit.</summary>
    Task AppendAsync(string assignmentId, object @event, CancellationToken ct);

    /// <summary>
    /// Find the single active (Issued) assignment for a normalized email,
    /// if any. Used at issue-time (one-active-per-email check) and at the
    /// student-side /api/me/applicable-discount endpoint.
    /// </summary>
    Task<DiscountAssignmentSummary?> FindActiveByEmailAsync(
        string targetEmailNormalized, CancellationToken ct);

    /// <summary>
    /// List all assignments for a normalized email, all statuses. Used by
    /// the admin search UI.
    /// </summary>
    Task<IReadOnlyList<DiscountAssignmentSummary>> ListByEmailAsync(
        string targetEmailNormalized, CancellationToken ct);

    /// <summary>List the most recent <paramref name="limit"/> assignments across all emails.</summary>
    Task<IReadOnlyList<DiscountAssignmentSummary>> ListRecentAsync(
        int limit, CancellationToken ct);
}

/// <summary>
/// In-memory <see cref="IDiscountAssignmentStore"/>. Production-grade for
/// single-host installs; Marten variant for multi-replica deployments.
/// </summary>
public sealed class InMemoryDiscountAssignmentStore : IDiscountAssignmentStore
{
    // Per-stream event lists keyed by assignment id (NOT stream key — caller
    // doesn't need to know the prefix).
    private readonly ConcurrentDictionary<string, List<object>> _eventsById = new();

    /// <inheritdoc/>
    public Task<DiscountAssignment> LoadAsync(string assignmentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        }
        var aggregate = new DiscountAssignment();
        if (_eventsById.TryGetValue(assignmentId, out var events))
        {
            // Snapshot so concurrent Append doesn't mutate while we replay.
            object[] snapshot;
            lock (events)
            {
                snapshot = events.ToArray();
            }
            foreach (var e in snapshot) aggregate.Apply(e);
        }
        return Task.FromResult(aggregate);
    }

    /// <inheritdoc/>
    public Task AppendAsync(string assignmentId, object @event, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        }
        ArgumentNullException.ThrowIfNull(@event);
        var list = _eventsById.GetOrAdd(assignmentId, _ => new List<object>());
        lock (list)
        {
            list.Add(@event);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<DiscountAssignmentSummary?> FindActiveByEmailAsync(
        string targetEmailNormalized, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetEmailNormalized))
        {
            return Task.FromResult<DiscountAssignmentSummary?>(null);
        }
        var hit = AllSummaries()
            .Where(s => s.Status == DiscountStatus.Issued
                     && string.Equals(s.TargetEmailNormalized, targetEmailNormalized,
                                      StringComparison.Ordinal))
            .OrderByDescending(s => s.IssuedAt)
            .FirstOrDefault();
        return Task.FromResult<DiscountAssignmentSummary?>(hit);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DiscountAssignmentSummary>> ListByEmailAsync(
        string targetEmailNormalized, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetEmailNormalized))
        {
            return Task.FromResult<IReadOnlyList<DiscountAssignmentSummary>>(Array.Empty<DiscountAssignmentSummary>());
        }
        var list = AllSummaries()
            .Where(s => string.Equals(s.TargetEmailNormalized, targetEmailNormalized,
                                      StringComparison.Ordinal))
            .OrderByDescending(s => s.IssuedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<DiscountAssignmentSummary>>(list);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DiscountAssignmentSummary>> ListRecentAsync(
        int limit, CancellationToken ct)
    {
        if (limit <= 0) limit = 100;
        var list = AllSummaries()
            .OrderByDescending(s => s.IssuedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<DiscountAssignmentSummary>>(list);
    }

    private IEnumerable<DiscountAssignmentSummary> AllSummaries()
    {
        foreach (var (_, events) in _eventsById)
        {
            object[] snapshot;
            lock (events)
            {
                snapshot = events.ToArray();
            }
            var aggregate = DiscountAssignment.ReplayFrom(snapshot);
            if (aggregate.State.Status == DiscountStatus.None) continue;
            yield return DiscountAssignmentSummaryBuilder.From(aggregate.State);
        }
    }
}

/// <summary>Helper to project an aggregate state into the read-side summary.</summary>
internal static class DiscountAssignmentSummaryBuilder
{
    public static DiscountAssignmentSummary From(DiscountAssignmentState s) => new(
        AssignmentId: s.AssignmentId,
        TargetEmailNormalized: s.TargetEmailNormalized,
        Status: s.Status,
        Kind: s.Kind,
        Value: s.Value,
        DurationMonths: s.DurationMonths,
        StripeCouponId: s.StripeCouponId,
        StripePromotionCodeId: s.StripePromotionCodeId,
        IssuedByAdminSubjectIdEncrypted: s.IssuedByAdminSubjectIdEncrypted,
        Reason: s.Reason,
        IssuedAt: s.IssuedAt ?? DateTimeOffset.MinValue,
        RedeemedAt: s.RedeemedAt,
        RevokedAt: s.RevokedAt);
}
