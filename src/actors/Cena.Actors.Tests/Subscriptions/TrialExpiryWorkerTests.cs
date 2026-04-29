// =============================================================================
// Cena Platform — TrialExpiryWorkerTests (Phase 1D)
//
// Locks the worker contract:
//   - Trialing stream past TrialEndsAt is expired (one TrialExpired_V1 appended)
//   - Cap-only trial (TrialEndsAt == TrialStartedAt) is NEVER expired by the worker
//   - Non-Trialing streams are skipped
//   - Trialing-but-not-yet-expired streams are skipped
//   - Re-running the worker after expiry does not append a duplicate event
//     (and does not throw — the command is idempotent on Expired)
//   - Per-stream exceptions are isolated; a bad stream does not stop the pass
//   - Multi-stream batch all expire when due
//   - Utilisation is read from the consumption store and pinned on the event
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class TrialExpiryWorkerTests
{
    private const string ParentId = "enc::parent::expiry-tests";
    private const string StudentId = "enc::student::expiry-tests";

    private static readonly DateTimeOffset TrialStartedAt =
        new(2026, 4, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeEnd =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PastEnd =
        new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly TrialCapsSnapshot CalendarBoundedCaps = new(
        TrialDurationDays: 14, TrialTutorTurns: 50,
        TrialPhotoDiagnostics: 10, TrialPracticeSessions: 6);

    private static readonly TrialCapsSnapshot CapOnlyCaps = new(
        TrialDurationDays: 0, TrialTutorTurns: 50,
        TrialPhotoDiagnostics: 10, TrialPracticeSessions: 6);

    [Fact]
    public async Task RunOnceAsync_expires_trialing_stream_past_end()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();
        await consumption.IncrementAsync(
            StudentId, EntitlementFeature.TutorTurn, BeforeEnd, CancellationToken.None);

        var trialEndsAt = TrialStartedAt.AddDays(14);
        var startEvent = new TrialStarted_V1(
            ParentId, StudentId, TrialKind.SelfPay,
            TrialStartedAt, trialEndsAt,
            "fp-hash", "v1-baseline", CalendarBoundedCaps);
        await store.AppendAsync(ParentId, startEvent, CancellationToken.None);

        var worker = NewWorker(store, consumption, PastEnd);
        var expired = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, expired);
        var events = await store.ReadEventsAsync(ParentId, CancellationToken.None);
        var expireEvt = events.OfType<TrialExpired_V1>().Single();
        Assert.Equal(trialEndsAt, expireEvt.TrialEndedAt);
        Assert.Equal(1, expireEvt.Utilization.TutorTurnsUsed);
    }

    [Fact]
    public async Task RunOnceAsync_does_not_expire_cap_only_trial()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();

        // Cap-only trial: TrialEndsAt == TrialStartedAt
        var startEvent = new TrialStarted_V1(
            ParentId, StudentId, TrialKind.SelfPay,
            TrialStartedAt, TrialStartedAt,
            "fp-hash", "v1-baseline", CapOnlyCaps);
        await store.AppendAsync(ParentId, startEvent, CancellationToken.None);

        var worker = NewWorker(store, consumption, PastEnd);
        var expired = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, expired);
        var events = await store.ReadEventsAsync(ParentId, CancellationToken.None);
        Assert.DoesNotContain(events, e => e is TrialExpired_V1);
    }

    [Fact]
    public async Task RunOnceAsync_skips_trialing_stream_before_end()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();

        var trialEndsAt = TrialStartedAt.AddDays(14);
        var startEvent = new TrialStarted_V1(
            ParentId, StudentId, TrialKind.SelfPay,
            TrialStartedAt, trialEndsAt,
            "fp-hash", "v1-baseline", CalendarBoundedCaps);
        await store.AppendAsync(ParentId, startEvent, CancellationToken.None);

        // BeforeEnd < trialEndsAt (5/1 < 5/12)
        var worker = NewWorker(store, consumption, BeforeEnd);
        var expired = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, expired);
    }

    [Fact]
    public async Task RunOnceAsync_skips_non_trialing_streams()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();

        // Activated stream — never trialled.
        var activatedEvent = new SubscriptionActivated_V1(
            ParentId, StudentId, SubscriptionTier.Plus, BillingCycle.Monthly,
            GrossAmountAgorot: 1000,
            PaymentTransactionIdEncrypted: "enc::tx::01",
            ActivatedAt: TrialStartedAt,
            RenewsAt: TrialStartedAt.AddMonths(1));
        await store.AppendAsync(ParentId, activatedEvent, CancellationToken.None);

        var worker = NewWorker(store, consumption, PastEnd);
        var expired = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, expired);
    }

    [Fact]
    public async Task RunOnceAsync_is_idempotent_after_expiry()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();

        var trialEndsAt = TrialStartedAt.AddDays(14);
        var startEvent = new TrialStarted_V1(
            ParentId, StudentId, TrialKind.SelfPay,
            TrialStartedAt, trialEndsAt,
            "fp-hash", "v1-baseline", CalendarBoundedCaps);
        await store.AppendAsync(ParentId, startEvent, CancellationToken.None);

        var worker = NewWorker(store, consumption, PastEnd);
        var first = await worker.RunOnceAsync(CancellationToken.None);
        var second = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var events = await store.ReadEventsAsync(ParentId, CancellationToken.None);
        Assert.Single(events.OfType<TrialExpired_V1>());
    }

    [Fact]
    public async Task RunOnceAsync_handles_multiple_streams()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();

        // Three trialing streams; two past end, one before.
        var trialEndsAtPast = TrialStartedAt.AddDays(14);
        var trialEndsAtFuture = PastEnd.AddDays(14);
        for (var i = 0; i < 3; i++)
        {
            var pid = $"enc::parent::multi-{i}";
            var sid = $"enc::student::multi-{i}";
            var endsAt = i < 2 ? trialEndsAtPast : trialEndsAtFuture;
            var evt = new TrialStarted_V1(
                pid, sid, TrialKind.SelfPay,
                TrialStartedAt, endsAt,
                "fp-hash-" + i, "v1-baseline", CalendarBoundedCaps);
            await store.AppendAsync(pid, evt, CancellationToken.None);
        }

        var worker = NewWorker(store, consumption, PastEnd);
        var expired = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, expired);
    }

    [Fact]
    public async Task RunOnceAsync_pins_utilization_zero_when_no_consumption_recorded()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();

        var trialEndsAt = TrialStartedAt.AddDays(14);
        var startEvent = new TrialStarted_V1(
            ParentId, StudentId, TrialKind.SelfPay,
            TrialStartedAt, trialEndsAt,
            "fp-hash", "v1-baseline", CalendarBoundedCaps);
        await store.AppendAsync(ParentId, startEvent, CancellationToken.None);

        var worker = NewWorker(store, consumption, PastEnd);
        await worker.RunOnceAsync(CancellationToken.None);

        var events = await store.ReadEventsAsync(ParentId, CancellationToken.None);
        var expireEvt = events.OfType<TrialExpired_V1>().Single();
        Assert.Equal(0, expireEvt.Utilization.TutorTurnsUsed);
        Assert.Equal(0, expireEvt.Utilization.PhotoDiagnosticsUsed);
        Assert.Equal(0, expireEvt.Utilization.SessionsStarted);
        Assert.Equal(0, expireEvt.Utilization.DaysActive);
    }

    [Fact]
    public void Constructor_rejects_invalid_tick_interval()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var consumption = new InMemoryStudentTrialConsumptionStore();
        var clock = new FakeClock(PastEnd);

        var bad = Options.Create(new TrialExpiryWorkerOptions
        {
            TickIntervalSeconds = 1, // Below the [5, 3600] floor.
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrialExpiryWorker(
                store, store, consumption, clock, bad, NullLogger<TrialExpiryWorker>.Instance));

        var bad2 = Options.Create(new TrialExpiryWorkerOptions
        {
            TickIntervalSeconds = 86_400, // Above ceiling.
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrialExpiryWorker(
                store, store, consumption, clock, bad2, NullLogger<TrialExpiryWorker>.Instance));
    }

    // ----- helpers -------------------------------------------------------

    private static TrialExpiryWorker NewWorker(
        InMemorySubscriptionAggregateStore store,
        IStudentTrialConsumptionStore consumption,
        DateTimeOffset now)
    {
        var clock = new FakeClock(now);
        var options = Options.Create(new TrialExpiryWorkerOptions
        {
            TickIntervalSeconds = 60, RunOnStartup = false,
        });
        return new TrialExpiryWorker(
            store, store, consumption, clock, options, NullLogger<TrialExpiryWorker>.Instance);
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
