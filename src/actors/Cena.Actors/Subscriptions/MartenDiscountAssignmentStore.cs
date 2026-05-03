// =============================================================================
// Cena Platform — MartenDiscountAssignmentStore (per-user discount-codes feature)
//
// Marten-backed implementation of IDiscountAssignmentStore. Mirrors the
// MartenSubscriptionAggregateStore shape:
//   - LoadAsync : FetchStreamAsync on the aggregate stream key
//   - AppendAsync : StartStream on first event, Append on subsequent events
//   - FindActive / ListByEmail / ListRecent : QueryAllRawEvents projected
//     into DiscountAssignmentSummary on the fly (no separate projection
//     document — keeps the schema thin until cross-replica volume justifies)
//
// Stream identity: string-typed, keyed by `discount-{assignmentId}`. Per
// MartenConfiguration StreamIdentity = AsString.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>Marten-backed event store for <see cref="DiscountAssignment"/>.</summary>
public sealed class MartenDiscountAssignmentStore : IDiscountAssignmentStore
{
    private readonly IDocumentStore _store;

    public MartenDiscountAssignmentStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async Task<DiscountAssignment> LoadAsync(string assignmentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        }
        var streamKey = DiscountAssignment.StreamKey(assignmentId);
        await using var session = _store.LightweightSession();
        var events = await session.Events.FetchStreamAsync(streamKey, token: ct);
        var aggregate = new DiscountAssignment();
        foreach (var e in events) aggregate.Apply(e.Data);
        return aggregate;
    }

    /// <inheritdoc/>
    public async Task AppendAsync(string assignmentId, object @event, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        }
        ArgumentNullException.ThrowIfNull(@event);
        var streamKey = DiscountAssignment.StreamKey(assignmentId);
        await using var session = _store.LightweightSession();
        var existing = await session.Events.FetchStreamStateAsync(streamKey, token: ct);
        if (existing is null)
        {
            session.Events.StartStream<DiscountAssignment>(streamKey, @event);
        }
        else
        {
            session.Events.Append(streamKey, @event);
        }
        await session.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<DiscountAssignmentSummary?> FindActiveByEmailAsync(
        string targetEmailNormalized, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetEmailNormalized)) return null;
        var summaries = await ProjectAllAsync(ct);
        return summaries
            .Where(s => s.Status == DiscountStatus.Issued
                     && string.Equals(s.TargetEmailNormalized, targetEmailNormalized,
                                      StringComparison.Ordinal))
            .OrderByDescending(s => s.IssuedAt)
            .FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiscountAssignmentSummary>> ListByEmailAsync(
        string targetEmailNormalized, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetEmailNormalized))
        {
            return Array.Empty<DiscountAssignmentSummary>();
        }
        var summaries = await ProjectAllAsync(ct);
        return summaries
            .Where(s => string.Equals(s.TargetEmailNormalized, targetEmailNormalized,
                                      StringComparison.Ordinal))
            .OrderByDescending(s => s.IssuedAt)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiscountAssignmentSummary>> ListRecentAsync(
        int limit, CancellationToken ct)
    {
        if (limit <= 0) limit = 100;
        var summaries = await ProjectAllAsync(ct);
        return summaries
            .OrderByDescending(s => s.IssuedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Read every discount-* stream event and replay into per-stream
    /// summaries. Acceptable for the launch volume (admin-only feature,
    /// expected on the order of dozens-to-hundreds of issuances per year);
    /// when volume warrants we can swap this for a Marten-projected
    /// DiscountAssignmentSummary document with an inline projection.
    /// </summary>
    private async Task<List<DiscountAssignmentSummary>> ProjectAllAsync(CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var rawEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey != null
                     && e.StreamKey.StartsWith(DiscountAssignment.StreamKeyPrefix))
            .ToListAsync(ct);

        var byStream = rawEvents
            .Where(e => e.StreamKey != null && e.Data != null)
            .GroupBy(e => e.StreamKey!);

        var summaries = new List<DiscountAssignmentSummary>();
        foreach (var group in byStream)
        {
            var ordered = group.OrderBy(e => e.Sequence).Select(e => e.Data!);
            var aggregate = DiscountAssignment.ReplayFrom(ordered);
            if (aggregate.State.Status == DiscountStatus.None) continue;
            summaries.Add(DiscountAssignmentSummaryBuilder.From(aggregate.State));
        }
        return summaries;
    }
}
