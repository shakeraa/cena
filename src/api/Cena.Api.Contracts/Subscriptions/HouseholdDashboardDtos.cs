// =============================================================================
// Cena Platform — Household dashboard DTOs (EPIC-PRR-I PRR-324)
//
// Why this exists:
//   PRR-320 shipped a per-student parent dashboard. PRR-324's household view
//   is a superset — a parent with 2+ linked students needs to see the primary
//   + siblings side by side, plus household-wide rollups (total weekly /
//   monthly minutes on task, readiness summary).
//
//   The wire shape is deliberately separate from the existing
//   ParentDashboardResponseDto because:
//     1. It has a distinct domain: "household" vs "list of students". The
//        primary student has structural privilege (ordinal 0, retail rate);
//        siblings carry discount depth. The shape surfaces that asymmetry
//        rather than flattening it into a ranked list.
//     2. Privacy line (ADR-0003): only SUMMARY SCALARS ship — weekly /
//        monthly minutes and a single-string readiness snapshot per card.
//        No session ids, no misconception tags, no raw transcripts cross
//        this boundary. The "no session ids" rule is enforced structurally:
//        there is no field on any DTO in this file that could hold one.
//     3. TierFeature.ParentDashboard gates access (see
//        HouseholdDashboardEndpoints). Retail-B2B overlap is prevented by
//        the SKU fence (persona #8 pricing-leak guard, PRR-343).
//
// What the DTOs are NOT:
//   - Not a read model. Cards are assembled at request time by an
//     IHouseholdCardSource; no Marten projection exists yet.
//   - Not a superset of ParentDashboardStudentDto. The two shapes overlap
//     (both show minutes on task) but household adds ordinal + tier labels,
//     and the sibling view omits per-student fields that duplicate across
//     all household members.
// =============================================================================

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>
/// One student card as rendered on the household dashboard. Structurally
/// identical across primary and siblings — the primary is distinguished
/// by <see cref="OrdinalInHousehold"/> == 0, not by a separate shape. Only
/// summary scalars; no session ids, no transcripts.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">
/// Wire-format encrypted student id (ADR-0038). Safe to return over the wire
/// because decryption requires the Key Store — a client cannot reverse it.
/// The frontend treats this as an opaque deep-link key.
/// </param>
/// <param name="DisplayName">
/// Optional display name. Null/empty when the student has opted out of
/// showing a name on the parent surface (ADR-0041 §visibility).
/// </param>
/// <param name="OrdinalInHousehold">
/// 0 = primary student, 1..N = siblings. Stable across re-ordering
/// (matches LinkedStudent.Ordinal billing convention — see
/// SubscriptionState file-header comment).
/// </param>
/// <param name="WeeklyMinutesOnTask">Total practice minutes in the last 7 days.</param>
/// <param name="MonthlyMinutesOnTask">Total practice minutes in the last 30 days.</param>
/// <param name="ReadinessSnapshot">
/// Short human-readable summary ("On track for 4-unit", "Needs more review",
/// or null when no exam target is configured). Never a raw score, never
/// numeric Bagrut prediction (ADR-0050 §bucket-only), never PII.
/// </param>
/// <param name="Tier">Effective tier string (e.g. "Premium"). Human-readable label.</param>
public sealed record HouseholdStudentCardDto(
    string StudentSubjectIdEncrypted,
    string? DisplayName,
    int OrdinalInHousehold,
    int WeeklyMinutesOnTask,
    int MonthlyMinutesOnTask,
    string? ReadinessSnapshot,
    string Tier);

/// <summary>
/// Household-wide rollup of all linked students. Sums are computed by the
/// pure <see cref="HouseholdDashboardAggregator"/> from the individual cards
/// — the aggregator is the single source of truth for these totals so they
/// stay aligned with the per-card minutes on display.
/// </summary>
/// <param name="TotalLinkedStudents">Count of cards in the response (primary + siblings).</param>
/// <param name="TotalWeeklyMinutesOnTask">Sum of every card's weekly minutes.</param>
/// <param name="TotalMonthlyMinutesOnTask">Sum of every card's monthly minutes.</param>
/// <param name="HouseholdReadinessSummary">
/// Short human-readable household-wide summary. Null when no student has
/// an exam target configured. The aggregator does NOT synthesize this —
/// the card source (or a follow-up readiness projection) supplies it and
/// the aggregator passes it through.
/// </param>
public sealed record HouseholdAggregateDto(
    int TotalLinkedStudents,
    int TotalWeeklyMinutesOnTask,
    int TotalMonthlyMinutesOnTask,
    string? HouseholdReadinessSummary);

/// <summary>
/// Full household dashboard response. Primary is always first; siblings
/// are sorted by ordinal ascending so the view is deterministic across
/// requests. The aggregate rollup sums across ALL cards (primary +
/// siblings).
/// </summary>
/// <param name="PrimaryStudent">
/// The student at ordinal 0. Never null on a successful response —
/// the aggregator throws if no primary card is supplied.
/// </param>
/// <param name="Siblings">
/// Siblings (ordinal >= 1), sorted by ordinal ascending. Empty list is
/// legal (single-child household).
/// </param>
/// <param name="HouseholdAggregate">Household-wide rollup.</param>
public sealed record HouseholdDashboardResponseDto(
    HouseholdStudentCardDto PrimaryStudent,
    IReadOnlyList<HouseholdStudentCardDto> Siblings,
    HouseholdAggregateDto HouseholdAggregate);
