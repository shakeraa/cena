// =============================================================================
// Cena Platform — IParentDashboardCardSource + NoopParentDashboardCardSource
//                 (EPIC-PRR-I PRR-320)
//
// Why this exists:
//   The /api/me/parent-dashboard endpoint previously hard-coded zero values
//   for WeeklyMinutes / MonthlyMinutes / TopicsPracticed / LastActiveAt /
//   ReadinessScore with a "PRR-323 will fill these values" TODO comment.
//   The comment is now closed out by this port + a real Marten-backed
//   implementation (MartenParentDashboardCardSource).
//
//   The endpoint assembles ParentDashboardStudentDto from two inputs:
//   the parent's subscription aggregate (linked students + tier) and a
//   pre-built aggregate bundle (ParentDashboardCards). The bundle folds
//   together data from read models that may be backed by different
//   projections (event-log scan today, dedicated projection tomorrow).
//   The endpoint should not care which backing store supplies each field;
//   it asks IParentDashboardCardSource for the bundle and moves on.
//
//   This is the same narrow-port pattern as:
//     • IHouseholdCardSource (PRR-324)
//     • ITutorHandoffCardSource (PRR-325)
//     • IStudentEntitlementResolver (ADR-0057)
//     • IRefundUsageProbe (PRR-306)
//   A single async method, host-composed, with a legitimate zero-data
//   default that keeps the endpoint surface exercisable end-to-end.
//
// Why here (Cena.Api.Contracts.Parenting) and NOT in Cena.Actors:
//   The port signature references ParentDashboardCards which is a
//   wire-adjacent aggregation type. Cena.Actors cannot reference
//   Cena.Api.Contracts (that would be cyclic — Contracts already
//   depends on Actors for enum / state types). Contracts is the only
//   compile-legal home that keeps the port close to the types it
//   traffics in. See TutorHandoffReportAssembler file banner for the
//   same rationale applied to the PRR-325 pair.
//
// Why an interface and not a concrete default only:
//   A real Marten-backed implementation
//   (MartenParentDashboardCardSource) ships alongside this file and is
//   wired by the student API host when an IDocumentStore is available.
//   The Noop default stays in DI as a TryAdd guard so hosts without a
//   Marten store (tests, sandbox composers) still resolve the
//   endpoint's dependency graph.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Api.Contracts.Parenting;

/// <summary>
/// Builds a per-student aggregate bundle for the parent dashboard
/// response. One implementation per backing read-model composition; see
/// <see cref="NoopParentDashboardCardSource"/> for the zero-data default
/// that ships with every host.
/// </summary>
public interface IParentDashboardCardSource
{
    /// <summary>
    /// Build the dashboard card bundle for every linked student on the
    /// parent's subscription. The endpoint is responsible for feature-
    /// fencing + tier-gating BEFORE reaching this method; implementations
    /// trust their input.
    /// </summary>
    /// <param name="linkedStudents">
    /// The parent's linked students from
    /// <see cref="SubscriptionState.LinkedStudents"/>. Empty list is legal
    /// (returns an empty bundle).
    /// </param>
    /// <param name="now">
    /// Wall-clock reference for windowed usage calcs (weekly = last 7
    /// days, monthly + topics = last 30 days, ending at
    /// <paramref name="now"/>).
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    /// Non-null bundle. The PerStudent dictionary is keyed by
    /// StudentSubjectIdEncrypted; students absent from the bundle are
    /// treated by the endpoint as zero-scalar cards (see
    /// <see cref="ParentDashboardCards.GetOrZero"/>).
    /// </returns>
    Task<ParentDashboardCards> BuildAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset now,
        CancellationToken ct);
}

// =============================================================================
// ParentDashboardStudentCard (EPIC-PRR-I PRR-320)
//
// Why this exists:
//   An internal bundle type — not returned over the wire. The wire-format
//   DTO is ParentDashboardStudentDto (see ParentDashboardDtos.cs); this
//   card carries only the fields that an IParentDashboardCardSource
//   computes (the endpoint supplies StudentId / DisplayName / ActiveTier
//   from the subscription aggregate, so the card does not re-supply
//   those).
//
// Why not reuse ParentDashboardStudentDto directly:
//   The DTO is a wire-format record tied to the response schema;
//   changing field names there breaks Arabic / Hebrew / English clients.
//   The card is an internal contract between the card source and the
//   endpoint, free to evolve without a wire-format migration.
// =============================================================================

/// <summary>
/// Internal per-student bundle returned by
/// <see cref="IParentDashboardCardSource"/>. Carries only the fields the
/// card source owns — the endpoint layers on identity + tier from the
/// subscription aggregate.
/// </summary>
/// <param name="WeeklyMinutes">Practice minutes in the last 7 days.</param>
/// <param name="MonthlyMinutes">Practice minutes in the last 30 days.</param>
/// <param name="TopicsPracticed">Count of distinct concept ids in the last 30 days.</param>
/// <param name="LastActiveAt">Most-recent engagement timestamp, or null if none.</param>
/// <param name="ReadinessScore">
/// 0-100 readiness score for the student's primary exam target. Null
/// when no readiness model is wired (current default). A follow-up task
/// (separate from PRR-320) will wire a real readiness projection here.
/// </param>
public sealed record ParentDashboardStudentCard(
    int WeeklyMinutes,
    int MonthlyMinutes,
    int TopicsPracticed,
    DateTimeOffset? LastActiveAt,
    int? ReadinessScore);

// =============================================================================
// ParentDashboardCards (EPIC-PRR-I PRR-320)
//
// Bundle returned by IParentDashboardCardSource. Keyed by
// StudentSubjectIdEncrypted so the endpoint can iterate LinkedStudents
// and look up each card without order-dependence.
// =============================================================================

/// <summary>
/// Aggregate bundle returned by
/// <see cref="IParentDashboardCardSource.BuildAsync"/>. The dictionary
/// is keyed by <see cref="LinkedStudent.StudentSubjectIdEncrypted"/>;
/// lookups for students not in the dictionary MUST return a zero-scalar
/// card (use <see cref="GetOrZero"/>).
/// </summary>
/// <param name="PerStudent">Per-student computed fields.</param>
/// <param name="ComputedAtUtc">Wall-clock stamp at bundle build time.</param>
public sealed record ParentDashboardCards(
    IReadOnlyDictionary<string, ParentDashboardStudentCard> PerStudent,
    DateTimeOffset ComputedAtUtc)
{
    /// <summary>
    /// Empty singleton bundle. Used by <see cref="NoopParentDashboardCardSource"/>
    /// and by the endpoint when <paramref name="linkedStudents"/> is empty.
    /// </summary>
    public static ParentDashboardCards Empty(DateTimeOffset computedAtUtc) =>
        new(new Dictionary<string, ParentDashboardStudentCard>(StringComparer.Ordinal),
            computedAtUtc);

    /// <summary>
    /// Look up a student's card, returning a zero-scalar card if the
    /// student is absent. Never throws.
    /// </summary>
    public ParentDashboardStudentCard GetOrZero(string studentSubjectIdEncrypted)
    {
        if (!string.IsNullOrEmpty(studentSubjectIdEncrypted)
            && PerStudent.TryGetValue(studentSubjectIdEncrypted, out var card))
        {
            return card;
        }
        return new ParentDashboardStudentCard(
            WeeklyMinutes: 0,
            MonthlyMinutes: 0,
            TopicsPracticed: 0,
            LastActiveAt: null,
            ReadinessScore: null);
    }
}

// =============================================================================
// NoopParentDashboardCardSource (EPIC-PRR-I PRR-320)
//
// Why co-located in this file:
//   Port + legitimate zero-data default ship together. Matches how
//   NoopHouseholdCardSource sits next to IHouseholdCardSource
//   (PRR-324) and NoopTutorHandoffCardSource next to
//   ITutorHandoffCardSource (PRR-325).
//
// Why this is a LEGITIMATE default, not a stub (memory discipline:
// "No stubs — production grade", 2026-04-11):
//   A stub pretends to return real data that isn't there; this default
//   honours the port contract with an HONEST empty answer. The empty
//   dictionary means "this composer has no projection wired to compute
//   data yet" — the endpoint then falls back to zero-scalar cards which
//   the frontend labels accurately (memory "Labels match data": the
//   UI says "no activity yet" / "0 minutes" rather than claiming usage
//   that does not exist).
//
//   A production host (student API host) wires
//   MartenParentDashboardCardSource ahead of this Noop via TryAdd so
//   the real source wins. Tests and sandbox composers that omit the
//   Marten wiring still resolve the endpoint's dependency graph and
//   return honest empties — no 503/NotImplemented stub behaviour.
//
//   The exact same pattern is used by NullEmailSender,
//   NoopHouseholdCardSource, NoopTutorHandoffCardSource, and
//   NullErrorAggregator across the codebase.
// =============================================================================

/// <summary>
/// Zero-data <see cref="IParentDashboardCardSource"/>. Returns an empty
/// bundle so every student falls through to a zero-scalar card via
/// <see cref="ParentDashboardCards.GetOrZero"/>. Host swaps this out
/// for <c>MartenParentDashboardCardSource</c> when an
/// <see cref="Marten.IDocumentStore"/> is in the container.
/// </summary>
public sealed class NoopParentDashboardCardSource : IParentDashboardCardSource
{
    /// <inheritdoc />
    public Task<ParentDashboardCards> BuildAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linkedStudents);
        return Task.FromResult(ParentDashboardCards.Empty(now));
    }
}
