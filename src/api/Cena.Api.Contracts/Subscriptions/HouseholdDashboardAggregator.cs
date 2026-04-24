// =============================================================================
// Cena Platform — HouseholdDashboardAggregator (EPIC-PRR-I PRR-324)
//
// Why this exists:
//   The household dashboard response has three invariants that must hold on
//   every response:
//     1. Exactly one card has OrdinalInHousehold == 0 (the primary).
//     2. Siblings are sorted by ordinal ascending.
//     3. Household totals (weekly/monthly minutes) equal the sum across
//        ALL cards (primary + siblings) — not a separately-computed figure
//        that could drift from the per-card display.
//
//   Rather than embedding those checks in the endpoint where they'd be
//   one-shot and easily skipped by a refactor, we centralise them here as
//   a pure static assembler. The endpoint calls Assemble(state, cards)
//   and gets back a DTO that is guaranteed to satisfy the three invariants
//   — or an ArgumentException if the inputs can't honour them.
//
//   This matches the pattern used by DisputeRateAggregator and RefundPolicy:
//   policy/invariant code is a clock-free, I/O-free pure function that can
//   be exhaustively unit-tested with a handful of lines of arrangement.
//
// Why here (Cena.Api.Contracts) and not in Cena.Actors:
//   Cena.Api.Contracts already depends on Cena.Actors (for SubscriptionState
//   and SubscriptionStatus). The reverse would be cyclic — Cena.Actors
//   cannot see wire-format DTOs from Cena.Api.Contracts. Since the
//   aggregator needs BOTH the domain state (for the Active-check) AND the
//   wire-format card DTOs, the only compile-legal home is the Contracts
//   assembly. The function is still pure — no I/O, no clock, no DI —
//   which preserves every property that made the "put it next to the
//   domain aggregate" location tempting. Unit tests live in
//   Cena.Actors.Tests (which transitively references Contracts via the
//   Student host).
//
// Duplicate-ordinal decision (documented):
//   If two or more cards claim ordinal 0, the FIRST card at ordinal 0 wins
//   and becomes the primary. Any later card also claiming ordinal 0 is
//   treated as a sibling at its stated ordinal (which will then be 0, so
//   it ends up first in the sibling list). This matches how the underlying
//   event stream would order-of-arrival resolve a hypothetical race — the
//   aggregator is deterministic and doesn't silently discard data. Locked
//   by unit test.
//
// Preconditions (any violation throws ArgumentException with a specific
// message — no silent defaulting):
//   - state.Status == SubscriptionStatus.Active
//     (endpoint should already 403/404 before calling the aggregator, but
//      defence in depth: the aggregator will not return a dashboard for an
//      inactive subscription.)
//   - cards is non-null and non-empty
//   - exactly one card has OrdinalInHousehold == 0
//
// What the aggregator is NOT:
//   - Not a card builder. Minutes / readiness come from IHouseholdCardSource;
//     the aggregator just composes them.
//   - Not a privacy boundary. The DTO shape itself enforces the "summary
//     scalars only" rule (there are no session-id fields on any DTO).
//   - Not async. No I/O, no clock. Pure value in → value out.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>
/// Pure assembler that folds a parent's <see cref="SubscriptionState"/>
/// and a pre-built list of <see cref="HouseholdStudentCardDto"/> into a
/// <see cref="HouseholdDashboardResponseDto"/>. See file banner for
/// invariants and duplicate-ordinal semantics.
/// </summary>
public static class HouseholdDashboardAggregator
{
    /// <summary>
    /// Assemble the household dashboard response.
    /// </summary>
    /// <param name="state">
    /// Parent's folded subscription state. Must be <see cref="SubscriptionStatus.Active"/>.
    /// </param>
    /// <param name="studentCards">
    /// Cards for every linked student. Must contain exactly one card with
    /// <see cref="HouseholdStudentCardDto.OrdinalInHousehold"/> == 0.
    /// Additional cards at ordinal 0 are treated as siblings (see file
    /// banner). Order within the input is irrelevant; the output is
    /// deterministic (primary first, siblings sorted by ordinal asc).
    /// </param>
    /// <param name="householdReadinessSummary">
    /// Optional household-wide readiness label. Passed through to the
    /// aggregate DTO unchanged; the aggregator never synthesizes this
    /// (it has no knowledge of readiness semantics). Null when no
    /// summary is available.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="state"/> or <paramref name="studentCards"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="state"/> is not Active, or <paramref name="studentCards"/>
    /// is empty, or no card has OrdinalInHousehold == 0.
    /// </exception>
    public static HouseholdDashboardResponseDto Assemble(
        SubscriptionState state,
        IReadOnlyList<HouseholdStudentCardDto> studentCards,
        string? householdReadinessSummary = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(studentCards);

        if (state.Status != SubscriptionStatus.Active)
        {
            throw new ArgumentException(
                $"Cannot assemble household dashboard for a non-Active subscription " +
                $"(status={state.Status}). The endpoint layer should 403/404 before " +
                $"reaching the aggregator.",
                nameof(state));
        }

        if (studentCards.Count == 0)
        {
            throw new ArgumentException(
                "Cannot assemble household dashboard from an empty card list. A parent " +
                "with an Active subscription must have at least one linked student.",
                nameof(studentCards));
        }

        // Find the FIRST card at ordinal 0. Subsequent ordinal-0 cards are
        // treated as siblings (the file banner documents this decision).
        HouseholdStudentCardDto? primary = null;
        var siblings = new List<HouseholdStudentCardDto>(studentCards.Count);
        foreach (var card in studentCards)
        {
            if (card.OrdinalInHousehold == 0 && primary is null)
            {
                primary = card;
            }
            else
            {
                siblings.Add(card);
            }
        }

        if (primary is null)
        {
            throw new ArgumentException(
                "Cannot assemble household dashboard: no card has OrdinalInHousehold == 0. " +
                "The primary student slot is a structural invariant (see SubscriptionState).",
                nameof(studentCards));
        }

        siblings.Sort(static (a, b) => a.OrdinalInHousehold.CompareTo(b.OrdinalInHousehold));

        // Aggregate totals sum across ALL cards, including the primary.
        // Using checked arithmetic would be pedantic: minutes per 30 days
        // per card is at most ~43_200 (24*60*30) and N linked students is
        // at most TierCatalog.MaxLinkedStudents (<10), so overflow is
        // unreachable in practice. We still use long accumulators for
        // paranoid safety and cast to int at the end — if the inputs ever
        // overflow int, that's a bug the caller needs to see, not a silent
        // wrap.
        long weekly = 0L;
        long monthly = 0L;
        weekly  += primary.WeeklyMinutesOnTask;
        monthly += primary.MonthlyMinutesOnTask;
        foreach (var s in siblings)
        {
            weekly  += s.WeeklyMinutesOnTask;
            monthly += s.MonthlyMinutesOnTask;
        }
        var aggregate = new HouseholdAggregateDto(
            TotalLinkedStudents: 1 + siblings.Count,
            TotalWeeklyMinutesOnTask: checked((int)weekly),
            TotalMonthlyMinutesOnTask: checked((int)monthly),
            HouseholdReadinessSummary: householdReadinessSummary);

        return new HouseholdDashboardResponseDto(
            PrimaryStudent: primary,
            Siblings: siblings,
            HouseholdAggregate: aggregate);
    }
}
