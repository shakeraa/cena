// =============================================================================
// Cena Platform — HouseholdDashboardAggregator tests (EPIC-PRR-I PRR-324)
//
// Locks the three assembler invariants:
//   1. Primary slot (ordinal 0) is always filled; missing = throw.
//   2. Siblings are sorted by ordinal ascending.
//   3. Household totals == sum across ALL cards (primary + siblings).
// Plus the input-validation preconditions (empty list, inactive state) and
// the documented duplicate-ordinal semantics (first ordinal-0 wins).
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Api.Contracts.Subscriptions;
using Xunit;

// Aggregator lives in Cena.Api.Contracts.Subscriptions alongside the DTOs
// (see HouseholdDashboardAggregator file banner for the architectural
// rationale). Both are in the same namespace so no extra using needed.

namespace Cena.Actors.Tests.Subscriptions;

public class HouseholdDashboardAggregatorTests
{
    // ── Happy path: primary-only household ─────────────────────────────────

    [Fact]
    public void Assemble_primary_only_household_returns_empty_siblings_and_correct_totals()
    {
        var state = BuildActiveState();
        var cards = new List<HouseholdStudentCardDto>
        {
            Card(ordinal: 0, weekly: 120, monthly: 480, name: "Alice"),
        };

        var response = HouseholdDashboardAggregator.Assemble(state, cards);

        Assert.Equal("Alice", response.PrimaryStudent.DisplayName);
        Assert.Equal(0, response.PrimaryStudent.OrdinalInHousehold);
        Assert.Empty(response.Siblings);
        Assert.Equal(1, response.HouseholdAggregate.TotalLinkedStudents);
        Assert.Equal(120, response.HouseholdAggregate.TotalWeeklyMinutesOnTask);
        Assert.Equal(480, response.HouseholdAggregate.TotalMonthlyMinutesOnTask);
    }

    // ── Primary + 2 siblings; sibling order asserted ────────────────────────

    [Fact]
    public void Assemble_primary_plus_two_siblings_sorts_siblings_by_ordinal_ascending()
    {
        var state = BuildActiveState();
        // Deliberately mis-order the input to prove the aggregator sorts.
        var cards = new List<HouseholdStudentCardDto>
        {
            Card(ordinal: 2, weekly: 30, monthly: 120, name: "Carol"),
            Card(ordinal: 0, weekly: 150, monthly: 600, name: "Alice"),
            Card(ordinal: 1, weekly: 60, monthly: 240, name: "Bob"),
        };

        var response = HouseholdDashboardAggregator.Assemble(state, cards);

        Assert.Equal("Alice", response.PrimaryStudent.DisplayName);
        Assert.Equal(2, response.Siblings.Count);
        Assert.Equal(1, response.Siblings[0].OrdinalInHousehold);
        Assert.Equal("Bob", response.Siblings[0].DisplayName);
        Assert.Equal(2, response.Siblings[1].OrdinalInHousehold);
        Assert.Equal("Carol", response.Siblings[1].DisplayName);
    }

    // ── Aggregate totals: sum across ALL cards ──────────────────────────────

    [Fact]
    public void Assemble_household_totals_equal_sum_across_all_cards_including_primary()
    {
        var state = BuildActiveState();
        var cards = new List<HouseholdStudentCardDto>
        {
            Card(ordinal: 0, weekly: 150, monthly: 600, name: "Alice"),
            Card(ordinal: 1, weekly: 60, monthly: 240, name: "Bob"),
            Card(ordinal: 2, weekly: 30, monthly: 120, name: "Carol"),
        };

        var response = HouseholdDashboardAggregator.Assemble(state, cards);

        Assert.Equal(3, response.HouseholdAggregate.TotalLinkedStudents);
        Assert.Equal(240, response.HouseholdAggregate.TotalWeeklyMinutesOnTask);   // 150+60+30
        Assert.Equal(960, response.HouseholdAggregate.TotalMonthlyMinutesOnTask);  // 600+240+120
    }

    // ── Precondition: missing primary ───────────────────────────────────────

    [Fact]
    public void Assemble_missing_primary_card_throws_argument_exception()
    {
        var state = BuildActiveState();
        var cards = new List<HouseholdStudentCardDto>
        {
            Card(ordinal: 1, weekly: 60, monthly: 240, name: "Bob"),
            Card(ordinal: 2, weekly: 30, monthly: 120, name: "Carol"),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            HouseholdDashboardAggregator.Assemble(state, cards));
        Assert.Contains("OrdinalInHousehold == 0", ex.Message);
    }

    // ── Precondition: empty cards list ──────────────────────────────────────

    [Fact]
    public void Assemble_empty_cards_list_throws_argument_exception()
    {
        var state = BuildActiveState();
        var cards = new List<HouseholdStudentCardDto>();

        var ex = Assert.Throws<ArgumentException>(() =>
            HouseholdDashboardAggregator.Assemble(state, cards));
        Assert.Contains("empty card list", ex.Message);
    }

    // ── Precondition: inactive state ────────────────────────────────────────

    [Fact]
    public void Assemble_inactive_state_throws_argument_exception()
    {
        // Default SubscriptionState has Status = Unsubscribed.
        var state = new SubscriptionState();
        var cards = new List<HouseholdStudentCardDto>
        {
            Card(ordinal: 0, weekly: 0, monthly: 0),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            HouseholdDashboardAggregator.Assemble(state, cards));
        Assert.Contains("non-Active subscription", ex.Message);
    }

    // ── Decision test: duplicate ordinal 0 (first-wins) ─────────────────────

    [Fact]
    public void Assemble_duplicate_ordinal_zero_first_wins_second_becomes_sibling()
    {
        // Documented decision: if two cards claim ordinal 0, the first (by
        // input order) becomes the primary; the second is treated as a
        // sibling at its stated ordinal (0). See the file banner.
        var state = BuildActiveState();
        var cards = new List<HouseholdStudentCardDto>
        {
            Card(ordinal: 0, weekly: 100, monthly: 400, name: "First"),
            Card(ordinal: 0, weekly: 50, monthly: 200, name: "SecondWithSameOrdinal"),
            Card(ordinal: 1, weekly: 20, monthly: 80, name: "LegitimateSibling"),
        };

        var response = HouseholdDashboardAggregator.Assemble(state, cards);

        Assert.Equal("First", response.PrimaryStudent.DisplayName);
        Assert.Equal(2, response.Siblings.Count);
        // Sorted by ordinal asc: ordinal 0 sibling first, then ordinal 1.
        Assert.Equal(0, response.Siblings[0].OrdinalInHousehold);
        Assert.Equal("SecondWithSameOrdinal", response.Siblings[0].DisplayName);
        Assert.Equal(1, response.Siblings[1].OrdinalInHousehold);
        Assert.Equal("LegitimateSibling", response.Siblings[1].DisplayName);
        // Household totals sum ALL three (primary + both "siblings").
        Assert.Equal(3, response.HouseholdAggregate.TotalLinkedStudents);
        Assert.Equal(170, response.HouseholdAggregate.TotalWeeklyMinutesOnTask);
        Assert.Equal(680, response.HouseholdAggregate.TotalMonthlyMinutesOnTask);
    }

    // ── Argument null guards ────────────────────────────────────────────────

    [Fact]
    public void Assemble_null_state_throws_argument_null()
    {
        var cards = new List<HouseholdStudentCardDto> { Card(0, 0, 0) };
        Assert.Throws<ArgumentNullException>(() =>
            HouseholdDashboardAggregator.Assemble(null!, cards));
    }

    [Fact]
    public void Assemble_null_cards_throws_argument_null()
    {
        var state = BuildActiveState();
        Assert.Throws<ArgumentNullException>(() =>
            HouseholdDashboardAggregator.Assemble(state, null!));
    }

    // ── Readiness pass-through ──────────────────────────────────────────────

    [Fact]
    public void Assemble_passes_household_readiness_summary_through_unchanged()
    {
        var state = BuildActiveState();
        var cards = new List<HouseholdStudentCardDto> { Card(0, 0, 0) };

        var response = HouseholdDashboardAggregator.Assemble(
            state, cards, householdReadinessSummary: "Household on track for 4-unit");

        Assert.Equal(
            "Household on track for 4-unit",
            response.HouseholdAggregate.HouseholdReadinessSummary);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static SubscriptionState BuildActiveState()
    {
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 7_900L,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            RenewsAt: DateTimeOffset.UtcNow.AddDays(29)));
        return aggregate.State;
    }

    private static HouseholdStudentCardDto Card(
        int ordinal, int weekly, int monthly, string? name = null) => new(
            StudentSubjectIdEncrypted: $"enc::student::{ordinal}",
            DisplayName: name,
            OrdinalInHousehold: ordinal,
            WeeklyMinutesOnTask: weekly,
            MonthlyMinutesOnTask: monthly,
            ReadinessSnapshot: null,
            Tier: "Premium");
}
