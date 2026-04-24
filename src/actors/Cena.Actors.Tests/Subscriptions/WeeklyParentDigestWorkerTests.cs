// =============================================================================
// Cena Platform — WeeklyParentDigestWorker tests (EPIC-PRR-I PRR-323)
//
// Locks two non-trivial seams on the worker:
//   1. The Sunday-08:00-UTC scheduling math (TimeUntilNextSundayMorning)
//      — boundary bugs here silently slide the digest cadence and
//      students' parents wonder "why did I get two this week / none this
//      week".
//   2. The per-subscription eligibility predicate (IsEligibleForDigest) —
//      ties to the "Basic tier opted out; Plus+Premium opt in" DoD line
//      through the tier catalog's ParentDashboard feature flag.
//
// Marten event-scan + IParentDigestDispatcher integration is covered by
// the Phase-2 integration suite; the kernel tests live here.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class WeeklyParentDigestWorkerTests
{
    // ── TimeUntilNextSundayMorning ─────────────────────────────────────────

    [Fact]
    public void Monday_schedules_for_next_Sunday_0800()
    {
        // 2026-04-27 is a Monday. Next Sunday 08:00 UTC = 2026-05-03 08:00.
        var now = new DateTimeOffset(2026, 4, 27, 10, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 3, 8, 0, 0, TimeSpan.Zero) - now;
        var actual = InvokeTimeUntilSunday(now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Sunday_before_0800_schedules_same_day_0800()
    {
        // 2026-05-03 is a Sunday. 07:00 UTC → schedule same-day 08:00.
        var now = new DateTimeOffset(2026, 5, 3, 7, 0, 0, TimeSpan.Zero);
        var actual = InvokeTimeUntilSunday(now);
        Assert.Equal(TimeSpan.FromHours(1), actual);
    }

    [Fact]
    public void Sunday_at_or_after_0800_schedules_next_Sunday()
    {
        // 2026-05-03 is a Sunday. 08:30 UTC → next Sunday 2026-05-10 08:00.
        var now = new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero) - now;
        var actual = InvokeTimeUntilSunday(now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Saturday_2359_schedules_next_morning()
    {
        // 2026-05-02 is a Saturday. 23:59 UTC → Sunday 2026-05-03 08:00 UTC.
        var now = new DateTimeOffset(2026, 5, 2, 23, 59, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 3, 8, 0, 0, TimeSpan.Zero) - now;
        var actual = InvokeTimeUntilSunday(now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Never_returns_negative_timespan()
    {
        // Boundary hardening — the worker uses Task.Delay which throws on
        // negative spans. A defensive min-1-min floor lives on the method.
        var veryFarFuture = new DateTimeOffset(2100, 5, 3, 8, 0, 0, TimeSpan.Zero);
        var actual = InvokeTimeUntilSunday(veryFarFuture);
        Assert.True(actual > TimeSpan.Zero,
            $"TimeUntilNextSundayMorning returned non-positive span: {actual}");
    }

    // ── IsEligibleForDigest ────────────────────────────────────────────────

    [Fact]
    public void Active_Premium_is_eligible()
    {
        var state = NewActiveState(SubscriptionTier.Premium);
        Assert.True(WeeklyParentDigestWorker.IsEligibleForDigest(state));
    }

    [Fact]
    public void Active_Basic_is_NOT_eligible_because_no_ParentDashboard_feature()
    {
        // Basic.ParentDashboard is false per TierCatalog — Basic tier is
        // opted out of the parent dashboard features, including the
        // weekly digest. This is the "Basic opted out" DoD invariant.
        var state = NewActiveState(SubscriptionTier.Basic);
        Assert.False(WeeklyParentDigestWorker.IsEligibleForDigest(state));
    }

    [Fact]
    public void Active_Unsubscribed_is_NOT_eligible()
    {
        var state = new SubscriptionState();   // defaults: Unsubscribed
        Assert.False(WeeklyParentDigestWorker.IsEligibleForDigest(state));
    }

    [Fact]
    public void Cancelled_Premium_is_NOT_eligible()
    {
        // Once Cancelled, the digest must stop — memory "Labels match
        // data" (cancelled subs are not active customers).
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 24_900L,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: DateTimeOffset.UtcNow.AddDays(-5),
            RenewsAt: DateTimeOffset.UtcNow.AddDays(25)));
        aggregate.Apply(new SubscriptionCancelled_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            Reason: "test",
            Initiator: "parent",
            CancelledAt: DateTimeOffset.UtcNow));

        Assert.False(WeeklyParentDigestWorker.IsEligibleForDigest(aggregate.State));
    }

    [Fact]
    public void Null_state_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WeeklyParentDigestWorker.IsEligibleForDigest(null!));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static SubscriptionState NewActiveState(SubscriptionTier tier)
    {
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: tier,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 7_900L,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            RenewsAt: DateTimeOffset.UtcNow.AddDays(29)));
        return aggregate.State;
    }

    /// <summary>
    /// The SUT method is <c>internal static</c>; reflect into it so tests
    /// can stay in the public test assembly without needing InternalsVisibleTo.
    /// </summary>
    private static TimeSpan InvokeTimeUntilSunday(DateTimeOffset now)
    {
        var method = typeof(WeeklyParentDigestWorker).GetMethod(
            "TimeUntilNextSundayMorning",
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);
        Assert.NotNull(method);
        return (TimeSpan)method!.Invoke(null, new object?[] { now })!;
    }
}
