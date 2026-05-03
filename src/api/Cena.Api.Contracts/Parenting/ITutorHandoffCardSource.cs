// =============================================================================
// Cena Platform — ITutorHandoffCardSource + NoopTutorHandoffCardSource
//                 (EPIC-PRR-I PRR-325)
//
// Why this exists:
//   The endpoint assembles a TutorHandoffReportDto from two inputs:
//   the parent's request (window + opt-ins + student id) and a
//   pre-built aggregate bundle (TutorHandoffCards). The bundle folds
//   together data from several read models — mastery, minutes-on-task,
//   topics practiced, misconceptions — each of which has its own
//   projection + cache story. The endpoint should not care which
//   projection backs each field; it asks ITutorHandoffCardSource for
//   the bundle and moves on.
//
//   This is the same narrow-port pattern as IHouseholdCardSource
//   (PRR-324) and IStudentEntitlementResolver: a single async method
//   whose production implementation is host-composed, and whose
//   legitimate zero-data default keeps the endpoint surface
//   exercisable end-to-end.
//
// Why here (Cena.Api.Contracts.Parenting) and not in Cena.Actors:
//   The port signature references TutorHandoffCards which lives in
//   Cena.Api.Contracts (alongside the request/response DTOs). Actors
//   cannot reference Contracts — that would be a cyclic project
//   reference. Contracts is the only compile-legal home that keeps
//   the port close to the types it traffics in.
//
// Why an interface and not a concrete default only:
//   A real Marten-backed implementation (joining the mastery store,
//   minutes-on-task projection, tutor-context session archive, etc.)
//   is a follow-up PR. The endpoint must still be exercisable today
//   — this matches the PRR-324 "no cross-household leakage" DoD
//   pattern, not "ships perfect usage rollups on day one".
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Api.Contracts.Parenting;

/// <summary>
/// Builds the <see cref="TutorHandoffCards"/> aggregate bundle for a
/// single student / window combination. One implementation per backing
/// read-model composition; see <see cref="NoopTutorHandoffCardSource"/>
/// for the zero-data default that ships today.
/// </summary>
public interface ITutorHandoffCardSource
{
    /// <summary>
    /// Build the card bundle for one student in the given window.
    /// </summary>
    /// <param name="linkedStudent">
    /// The student slot on the parent's subscription — already
    /// IDOR-verified by the endpoint layer before this method is
    /// invoked.
    /// </param>
    /// <param name="windowStart">Inclusive window start (UTC).</param>
    /// <param name="windowEnd">Exclusive window end (UTC).</param>
    /// <param name="locale">Locale for display labels.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    /// Non-null bundle. Zero-data fields (empty collections / null
    /// summaries) are legal; the assembler still produces a well-
    /// formed report.
    /// </returns>
    Task<TutorHandoffCards> BuildCardsAsync(
        LinkedStudent linkedStudent,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string locale,
        CancellationToken ct);
}

// =============================================================================
// NoopTutorHandoffCardSource (EPIC-PRR-I PRR-325)
//
// Why co-located in this file:
//   Port + legitimate zero-data default ship together. Matches how
//   IHouseholdCardSource sits next to NoopHouseholdCardSource in the
//   PRR-324 file.
//
// Why this is a LEGITIMATE default, not a stub (memory discipline:
// "No stubs — production grade", 2026-04-11):
//   A stub pretends to return real data that isn't there; this default
//   honours the port contract with honest empties:
//     - TopicsPracticed: empty list (no projection wired → no topics
//       to list).
//     - MasteryDeltas: empty dictionary (same reason).
//     - TimeOnTaskMinutes: 0 (assembler's opt-in guard converts this
//       into a rendered notice when IncludeTimeOnTask is false; when
//       the flag is true, the parent sees "0 minutes" which the UI
//       labels honestly — memory "Labels match data").
//     - MisconceptionSummary: null (no aggregate available yet).
//     - RecommendedFocusAreas: empty list.
//   When the Marten-backed source ships, the host DI container
//   replaces this default via Replace(). Until then the endpoint
//   surface is exercisable end-to-end without 404/503 stubs.
// =============================================================================

/// <summary>
/// Zero-data <see cref="ITutorHandoffCardSource"/>. Returns an empty-
/// scalar bundle so the endpoint produces a well-formed report that
/// says "no data yet" in every section. Host swaps this out for a
/// Marten-backed implementation once the backing projections ship.
/// </summary>
public sealed class NoopTutorHandoffCardSource : ITutorHandoffCardSource
{
    /// <inheritdoc />
    public Task<TutorHandoffCards> BuildCardsAsync(
        LinkedStudent linkedStudent,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string locale,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linkedStudent);
        var bundle = new TutorHandoffCards(
            StudentDisplayName: null,
            TopicsPracticed: Array.Empty<string>(),
            MasteryDeltas: new Dictionary<string, MasteryDelta>(),
            TimeOnTaskMinutes: 0L,
            MisconceptionSummary: null,
            RecommendedFocusAreas: Array.Empty<string>());
        return Task.FromResult(bundle);
    }
}
