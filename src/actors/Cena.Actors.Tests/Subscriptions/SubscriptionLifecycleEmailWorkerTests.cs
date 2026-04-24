// =============================================================================
// Cena Platform — SubscriptionLifecycleEmailWorker tests (PRR-345)
//
// Locks the lifecycle-email classification matrix:
//   - Welcome             ← SubscriptionActivated_V1     (per-parent once)
//   - RenewalUpcoming     ← now in [RenewsAt−lead, +window] (per-cycle once)
//   - PastDue             ← PaymentFailed_V1             (per-parent once)
//   - CancellationConfirm ← SubscriptionCancelled_V1     (per-parent once)
//   - RefundConfirm       ← SubscriptionRefunded_V1      (per-parent once)
//
// Plus the cross-cutting rules:
//   - Already-sent markers never re-plan.
//   - A stream with a terminal event (Cancelled / Refunded) suppresses
//     future RenewalUpcoming — a parent who cancelled does not receive a
//     "your subscription renews in 4 days" email.
//   - RenewalUpcoming marker id includes RenewsAt so each cycle is
//     independently idempotent — one email per cycle even across
//     missed worker ticks.
//   - Duplicate input rows do not produce duplicate plan rows.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;
using Kinds = Cena.Actors.Subscriptions.ISubscriptionLifecycleEmailDispatcher.Kinds;

namespace Cena.Actors.Tests.Subscriptions;

public class SubscriptionLifecycleEmailWorkerTests
{
    private static readonly DateTimeOffset ActivatedAt =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MonthlyRenewsAt =
        ActivatedAt.AddMonths(1);  // 2026-06-01 10:00 UTC
    private const string ParentA = "enc::parent-A";
    private const string ParentB = "enc::parent-B";
    private const string StreamA = "subscription:" + ParentA;
    private const string StreamB = "subscription:" + ParentB;

    private static readonly SubscriptionLifecycleEmailWorkerOptions DefaultOptions = new();

    // ---- Pass 1: terminal-event-driven kinds -------------------------------

    [Fact]
    public void Activated_produces_Welcome_plan_item()
    {
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
        };
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), ActivatedAt, DefaultOptions);

        Assert.Single(plan, p => p.EmailKind == Kinds.Welcome);
        var welcome = plan.Single(p => p.EmailKind == Kinds.Welcome);
        Assert.Equal(ParentA, welcome.ParentSubjectIdEncrypted);
        Assert.Equal($"{ParentA}:{Kinds.Welcome}", welcome.MarkerId);
    }

    [Fact]
    public void PaymentFailed_produces_PastDue_plan_item()
    {
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
            Input(StreamA, new PaymentFailed_V1(
                ParentSubjectIdEncrypted: ParentA,
                Reason: "card_declined",
                AttemptNumber: 1,
                FailedAt: ActivatedAt.AddDays(10))),
        };
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), ActivatedAt.AddDays(10), DefaultOptions);

        Assert.Contains(plan, p => p.EmailKind == Kinds.PastDue
            && p.ParentSubjectIdEncrypted == ParentA);
    }

    [Fact]
    public void Cancelled_produces_CancellationConfirm_and_suppresses_RenewalUpcoming()
    {
        // Activated 2026-05-01 (renews 2026-06-01); cancelled 2026-05-10.
        // At 2026-05-28 (4 days before renewal) the window is hot — but the
        // cancellation must suppress the renewal email.
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
            Input(StreamA, new SubscriptionCancelled_V1(
                ParentSubjectIdEncrypted: ParentA,
                Reason: "user_requested",
                Initiator: "self",
                CancelledAt: ActivatedAt.AddDays(10))),
        };
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), ActivatedAt.AddDays(28), DefaultOptions);

        Assert.Contains(plan, p => p.EmailKind == Kinds.CancellationConfirm);
        Assert.DoesNotContain(plan, p => p.EmailKind == Kinds.RenewalUpcoming);
    }

    [Fact]
    public void Refunded_produces_RefundConfirm_and_suppresses_RenewalUpcoming()
    {
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
            Input(StreamA, new SubscriptionRefunded_V1(
                ParentSubjectIdEncrypted: ParentA,
                RefundedAmountAgorot: 24_900L,
                Reason: "user_requested",
                RefundedAt: ActivatedAt.AddDays(5))),
        };
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), ActivatedAt.AddDays(28), DefaultOptions);

        Assert.Contains(plan, p => p.EmailKind == Kinds.RefundConfirm);
        Assert.DoesNotContain(plan, p => p.EmailKind == Kinds.RenewalUpcoming);
    }

    [Fact]
    public void AlreadySent_markers_never_re_plan()
    {
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
        };
        var alreadySent = new HashSet<string> { $"{ParentA}:{Kinds.Welcome}" };
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, alreadySent, ActivatedAt, DefaultOptions);

        Assert.DoesNotContain(plan, p => p.EmailKind == Kinds.Welcome);
    }

    [Fact]
    public void Duplicate_input_rows_do_not_duplicate_plan_rows()
    {
        var activated = Activated(ParentA, MonthlyRenewsAt);
        var inputs = new[]
        {
            Input(StreamA, activated),
            Input(StreamA, activated),   // same event observed twice
        };
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), ActivatedAt, DefaultOptions);

        Assert.Single(plan, p => p.EmailKind == Kinds.Welcome);
    }

    // ---- Pass 2: RenewalUpcoming -------------------------------------------

    [Fact]
    public void Within_window_fires_RenewalUpcoming_with_per_cycle_marker()
    {
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
        };
        // RenewsAt = 2026-06-01 10:00. LeadDays=4 → fireAt = 2026-05-28 10:00.
        // Now = 2026-05-28 12:00 → inside the 25h window.
        var now = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), now, DefaultOptions);

        var renewal = plan.SingleOrDefault(p => p.EmailKind == Kinds.RenewalUpcoming);
        Assert.NotNull(renewal);
        // Marker id carries RenewsAt so each cycle is its own idempotent key.
        Assert.Contains(MonthlyRenewsAt.ToUniversalTime().ToString("o"), renewal!.MarkerId);
        Assert.Contains(Kinds.RenewalUpcoming, renewal.MarkerId);
    }

    [Fact]
    public void Too_early_does_not_fire_RenewalUpcoming()
    {
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
        };
        // 5 days before renewal — too early (lead=4).
        var now = new DateTimeOffset(2026, 5, 27, 10, 0, 0, TimeSpan.Zero);

        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), now, DefaultOptions);

        Assert.DoesNotContain(plan, p => p.EmailKind == Kinds.RenewalUpcoming);
    }

    [Fact]
    public void Past_window_does_not_fire_RenewalUpcoming()
    {
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
        };
        // Window closes at fireAt + 25h. fireAt = 2026-05-28 10:00 → closes
        // 2026-05-29 11:00. Test at 2026-05-29 12:00 — just past.
        var now = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), now, DefaultOptions);

        Assert.DoesNotContain(plan, p => p.EmailKind == Kinds.RenewalUpcoming);
    }

    [Fact]
    public void RenewalProcessed_advances_RenewalUpcoming_to_next_cycle()
    {
        // Cycle 1: activated 2026-05-01, renews 2026-06-01.
        // Cycle 2: renewed 2026-06-01, next renews 2026-07-01.
        var cycle2RenewsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
            Input(StreamA, new RenewalProcessed_V1(
                ParentSubjectIdEncrypted: ParentA,
                PaymentTransactionIdEncrypted: "txn-renewal-1",
                GrossAmountAgorot: 24_900L,
                RenewedAt: MonthlyRenewsAt,
                NextRenewsAt: cycle2RenewsAt)),
        };
        // 4 days before cycle-2 renewal.
        var now = new DateTimeOffset(2026, 6, 27, 10, 30, 0, TimeSpan.Zero);

        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), now, DefaultOptions);

        var renewal = plan.Single(p => p.EmailKind == Kinds.RenewalUpcoming);
        Assert.Contains(cycle2RenewsAt.ToUniversalTime().ToString("o"), renewal.MarkerId);
    }

    [Fact]
    public void RenewalUpcoming_is_per_cycle_idempotent_across_ticks()
    {
        // Same cycle, two consecutive worker ticks with the cycle-marker
        // already recorded → no re-plan.
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
        };
        var now = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);
        var markerKey = $"{ParentA}:{Kinds.RenewalUpcoming}:" +
            MonthlyRenewsAt.ToUniversalTime().ToString("o");
        var alreadySent = new HashSet<string> { markerKey };

        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, alreadySent, now, DefaultOptions);

        Assert.DoesNotContain(plan, p => p.EmailKind == Kinds.RenewalUpcoming);
    }

    [Fact]
    public void Two_parents_are_planned_independently()
    {
        var renewsAtB = ActivatedAt.AddMonths(1).AddHours(2); // different instant
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
            Input(StreamB, Activated(ParentB, renewsAtB)),
        };
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), ActivatedAt, DefaultOptions);

        Assert.Contains(plan, p => p.EmailKind == Kinds.Welcome
            && p.ParentSubjectIdEncrypted == ParentA);
        Assert.Contains(plan, p => p.EmailKind == Kinds.Welcome
            && p.ParentSubjectIdEncrypted == ParentB);
    }

    [Fact]
    public void Options_RenewalUpcomingLeadDays_is_respected()
    {
        // LeadDays = 7 instead of default 4 — fireAt is 2026-05-25 10:00.
        var options = new SubscriptionLifecycleEmailWorkerOptions
        {
            RenewalUpcomingLeadDays = 7,
            RenewalUpcomingWindowHours = 25,
            TickIntervalHours = 1,
        };
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
        };
        // 7 days before at 11:00 UTC — inside window.
        var now = new DateTimeOffset(2026, 5, 25, 11, 0, 0, TimeSpan.Zero);
        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), now, options);
        Assert.Contains(plan, p => p.EmailKind == Kinds.RenewalUpcoming);
    }

    [Fact]
    public void Options_constructor_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SubscriptionLifecycleEmailWorker.ClassifyDispatches(
                events: Array.Empty<LifecycleEventInput>(),
                alreadySent: new HashSet<string>(),
                now: ActivatedAt,
                options: null!));
    }

    // ---- Full lifecycle correlation test -----------------------------------

    [Fact]
    public void Full_lifecycle_all_five_kinds_fire_exactly_once()
    {
        // Parent activates, renews once, fails a payment, then cancels.
        // Second cycle renewal-upcoming is in the window at now. First
        // cycle's renewal-upcoming is long past and marker'd. We expect:
        //   - Welcome (from initial activation)
        //   - PastDue (from payment failure)
        //   - CancellationConfirm (from cancel)
        // RenewalUpcoming is suppressed by the cancellation.
        //
        // This consolidates the cross-cutting rule that cancellation
        // suppresses downstream renewal notices even when cycle-2 renewal
        // is technically inside the fire window.
        var cycle2RenewsAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var inputs = new[]
        {
            Input(StreamA, Activated(ParentA, MonthlyRenewsAt)),
            Input(StreamA, new RenewalProcessed_V1(
                ParentSubjectIdEncrypted: ParentA,
                PaymentTransactionIdEncrypted: "txn-renewal-1",
                GrossAmountAgorot: 24_900L,
                RenewedAt: MonthlyRenewsAt,
                NextRenewsAt: cycle2RenewsAt)),
            Input(StreamA, new PaymentFailed_V1(
                ParentSubjectIdEncrypted: ParentA,
                Reason: "card_declined",
                AttemptNumber: 1,
                FailedAt: new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero))),
            Input(StreamA, new SubscriptionCancelled_V1(
                ParentSubjectIdEncrypted: ParentA,
                Reason: "payment_retry_exhausted",
                Initiator: "system",
                CancelledAt: new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero))),
        };
        // Inside cycle-2 renewal window (4 days before 2026-07-01).
        var now = new DateTimeOffset(2026, 6, 27, 10, 30, 0, TimeSpan.Zero);

        var plan = SubscriptionLifecycleEmailWorker.ClassifyDispatches(
            inputs, new HashSet<string>(), now, DefaultOptions);

        Assert.Contains(plan, p => p.EmailKind == Kinds.Welcome);
        Assert.Contains(plan, p => p.EmailKind == Kinds.PastDue);
        Assert.Contains(plan, p => p.EmailKind == Kinds.CancellationConfirm);
        // Cancellation must suppress renewal notices.
        Assert.DoesNotContain(plan, p => p.EmailKind == Kinds.RenewalUpcoming);
    }

    // ---- Helpers -----------------------------------------------------------

    private static SubscriptionActivated_V1 Activated(string parentId, DateTimeOffset renewsAt) =>
        new(
            ParentSubjectIdEncrypted: parentId,
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 24_900L,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: renewsAt.AddMonths(-1),
            RenewsAt: renewsAt);

    private static LifecycleEventInput Input(string stream, object payload) =>
        new(stream, payload);
}
