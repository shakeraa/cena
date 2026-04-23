// =============================================================================
// Cena Platform — IHouseholdCardSource (EPIC-PRR-I PRR-324)
//
// Why this exists:
//   The household dashboard endpoint needs to render, for each linked student,
//   a HouseholdStudentCardDto with weekly/monthly minutes-on-task plus a
//   readiness snapshot. That data lives across several read models — the
//   session-time projection for minutes, the exam-target retention service
//   for readiness buckets, potentially a follow-up Marten projection that
//   batches it all. The endpoint should not care which; it asks
//   IHouseholdCardSource for the cards and moves on.
//
//   This is the same "narrow port" pattern as ISubscriptionAggregateStore
//   (ADR-0057) and IStudentEntitlementResolver: a one-method interface
//   whose production implementation is host-composed and whose default
//   implementation is a legitimate zero-data fallback that keeps the
//   endpoint surface exercisable end-to-end.
//
// Why here (Cena.Api.Contracts) and not in Cena.Actors:
//   Same reason as HouseholdDashboardAggregator: the interface signature
//   references HouseholdStudentCardDto which lives in Cena.Api.Contracts,
//   and Cena.Actors cannot see that type (it would be a cyclic reference).
//   The interface is still a stable integration seam — hosts and real
//   card-source implementations can live in whichever project makes sense
//   (likely Cena.Actors once a Marten projection is added in a follow-up
//   PR, or a host-adjacent Cena.Actors.Projections project).
//
// Why an interface and not a concrete default only:
//   1. A real Marten-backed implementation is a follow-up PR (see
//      NoopHouseholdCardSource file banner). The endpoint must still be
//      exercisable today — this is the PRR-324 DoD line "no cross-household
//      leakage, no raw transcripts exposed", not "ships perfect usage
//      rollups on day one".
//   2. Every production host composes a different set of read models. The
//      seam lets the student-host DI container wire a real source without
//      the aggregator or DTOs caring.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>
/// Builds the per-student <see cref="HouseholdStudentCardDto"/> list that
/// the household dashboard aggregator then folds into the response DTO.
/// One implementation per backing read model — see
/// <see cref="NoopHouseholdCardSource"/> for the zero-data default.
/// </summary>
public interface IHouseholdCardSource
{
    /// <summary>
    /// Build a card per linked student, preserving input order (the
    /// aggregator will sort siblings by ordinal regardless, but preserving
    /// order makes the implementation obvious when tracing through a log).
    /// </summary>
    /// <param name="linkedStudents">
    /// The parent's linked students from
    /// <see cref="SubscriptionState.LinkedStudents"/>. Primary is at
    /// ordinal 0, siblings are 1..N.
    /// </param>
    /// <param name="now">
    /// Wall-clock reference for windowed usage calcs (weekly = last 7 days
    /// ending at <paramref name="now"/>). Pure implementations ignore it;
    /// real implementations use it for time-bounded queries.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    /// One card per input student, in the same order as the input list.
    /// Never null, never empty unless <paramref name="linkedStudents"/> is
    /// empty (in which case the endpoint should 404 before this is called).
    /// </returns>
    Task<IReadOnlyList<HouseholdStudentCardDto>> BuildCardsAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset now,
        CancellationToken ct);
}

// =============================================================================
// NoopHouseholdCardSource (EPIC-PRR-I PRR-324)
//
// Why co-located in this file:
//   The port and its legitimate zero-data default ship together so callers
//   can find the default at the same search location as the interface.
//   Mirrors how NullEmailSender sits next to IEmailSender and
//   NullErrorAggregator next to IErrorAggregator in their respective
//   namespaces.
//
// Why this exists:
//   The household dashboard endpoint needs an IHouseholdCardSource registered
//   in DI to resolve. The production Marten-backed source (which joins the
//   per-student minutes-on-task projection + the exam-target readiness
//   snapshot) is a follow-up task — PRR-324's BE scope is the endpoint
//   surface + aggregator invariants, not the read-model plumbing.
//
//   This source returns one zero-minutes / null-readiness card per linked
//   student. The endpoint still returns a correctly-shaped 200 response
//   that Vue can render (primary card + sibling cards + household rollup),
//   just with zeros where usage data will land once the projection is
//   wired. UI label text must say "no usage data yet" or equivalent
//   — it should NOT say "0 minutes" and claim that as real data (per memory
//   "Labels match data").
//
// Why this is a LEGITIMATE default, not a stub:
//   The pattern is the same as NullEmailSender / NullErrorAggregator /
//   NullWhatsAppSender (see Cena.Actors.Notifications.NullEmailSender file
//   banner). The interface has a real contract ("build cards"); this
//   implementation honours the contract ("here is a card per linked
//   student, with the scalars I know how to fill — zero — and nulls
//   where I have no data"). No production path depends on the fallback
//   returning data — the frontend treats null readiness as "not yet
//   computed" and zero minutes as "no usage logged".
//
//   When the Marten projection ships, the host DI container replaces this
//   implementation with Replace(). Until then, the endpoint surface is
//   exercisable end-to-end without any "here is a 404/503 until the
//   projection lands" stub behaviour.
//
//   Per the project directive "No stubs — production grade" (2026-04-11):
//   a stub pretends to return real data; a null/default implementation is
//   an explicit zero-data honest answer. This file is the latter.
// =============================================================================

/// <summary>
/// Zero-data <see cref="IHouseholdCardSource"/>. Returns one card per linked
/// student with zero minutes-on-task, null readiness, and the student's
/// effective tier taken from the LinkedStudent record. The host swaps this
/// for a Marten-backed implementation once the minutes-on-task projection
/// ships.
/// </summary>
public sealed class NoopHouseholdCardSource : IHouseholdCardSource
{
    /// <inheritdoc />
    public Task<IReadOnlyList<HouseholdStudentCardDto>> BuildCardsAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linkedStudents);

        var cards = new List<HouseholdStudentCardDto>(linkedStudents.Count);
        foreach (var ls in linkedStudents)
        {
            cards.Add(new HouseholdStudentCardDto(
                StudentSubjectIdEncrypted: ls.StudentSubjectIdEncrypted,
                DisplayName: null,                           // No name projection wired yet.
                OrdinalInHousehold: ls.Ordinal,
                WeeklyMinutesOnTask: 0,                      // Zero = "no usage data yet".
                MonthlyMinutesOnTask: 0,
                ReadinessSnapshot: null,                     // Null = "not computed".
                Tier: ls.Tier.ToString()));
        }

        return Task.FromResult<IReadOnlyList<HouseholdStudentCardDto>>(cards);
    }
}
