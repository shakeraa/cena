// =============================================================================
// Cena Platform — Trial cohort reader (Phase 4).
// Queries the Marten event store for Trial* events in [from, to) and feeds
// them into TrialCohortMetricsCalculator. Lifetime counts come from total
// QueryRawEventDataOnly counts.
//
// Design note: we read events directly rather than materializing a
// projection. The trial funnel is small (thousands of trials/year, not
// millions), Marten event-store reads are indexed on event type, and
// keeping it materialization-free means there's no projection-replay
// gap to worry about per feedback_event_sourcing_replay_check.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;

namespace Cena.Admin.Api.Features.TrialCohort;

public interface ITrialCohortReader
{
    Task<TrialCohortMetricsDto> GetMetricsAsync(
        DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct);
}

public sealed class MartenTrialCohortReader : ITrialCohortReader
{
    private readonly IDocumentStore _store;

    public MartenTrialCohortReader(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<TrialCohortMetricsDto> GetMetricsAsync(
        DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        if (windowEnd <= windowStart)
        {
            throw new ArgumentException(
                "windowEnd must be strictly after windowStart.", nameof(windowEnd));
        }

        await using var session = _store.QuerySession();

        // In-window events. Each event carries its own canonical timestamp
        // (TrialStartedAt / ConvertedAt / TrialEndedAt) — that's the
        // cohort-membership timestamp, NOT the Marten emit time.
        var startedInWindow = await session.Events
            .QueryRawEventDataOnly<TrialStarted_V1>()
            .Where(e => e.TrialStartedAt >= windowStart && e.TrialStartedAt < windowEnd)
            .ToListAsync(ct);

        var convertedInWindow = await session.Events
            .QueryRawEventDataOnly<TrialConverted_V1>()
            .Where(e => e.ConvertedAt >= windowStart && e.ConvertedAt < windowEnd)
            .ToListAsync(ct);

        var expiredInWindow = await session.Events
            .QueryRawEventDataOnly<TrialExpired_V1>()
            .Where(e => e.TrialEndedAt >= windowStart && e.TrialEndedAt < windowEnd)
            .ToListAsync(ct);

        // Lifetime counts for the active-count derivation. Three small
        // round-trips; could be folded into one query later if profiling
        // shows it matters, but at expected trial volumes (thousands/year)
        // this is well below the noise floor.
        var lifetimeStarted = await session.Events
            .QueryRawEventDataOnly<TrialStarted_V1>().CountAsync(ct);
        var lifetimeConverted = await session.Events
            .QueryRawEventDataOnly<TrialConverted_V1>().CountAsync(ct);
        var lifetimeExpired = await session.Events
            .QueryRawEventDataOnly<TrialExpired_V1>().CountAsync(ct);

        return TrialCohortMetricsCalculator.Compute(
            startedInWindow: startedInWindow,
            convertedInWindow: convertedInWindow,
            expiredInWindow: expiredInWindow,
            lifetimeStarted: lifetimeStarted,
            lifetimeConverted: lifetimeConverted,
            lifetimeExpired: lifetimeExpired,
            windowStart: windowStart,
            windowEnd: windowEnd);
    }
}
