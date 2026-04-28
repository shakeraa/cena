// =============================================================================
// Cena Platform — StudentEntitlementResolverPrecedenceTests (task t_dc70d2cd9ab9)
//
// Locks the §5.5.1 precedence rule. The brief encodes:
//
//     Institute seat  >  Active parent  >  Trialing parent  >
//     PastDue (with grace)  >  Expired / Cancelled / Unsubscribed
//
// (Institute seat lookup is wired in a sibling task; the rank slot is
// reserved in StudentEntitlementResolver.InstituteRank but not exercised
// here.) Each test names the precedence outcome it asserts.
// =============================================================================

using Cena.Actors.Parent;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class StudentEntitlementResolverPrecedenceTests
{
    private const string ParentA = "enc::parent::A-trialing";
    private const string ParentB = "enc::parent::B-active";
    private const string ParentC = "enc::parent::C-pastdue";
    private const string ParentD = "enc::parent::D-expired";
    private const string Student = "enc::student::shared-child";
    private const string InstituteId = "school-precedence";
    private const string PaymentTxnId = "enc::payment::precedence-001";
    private const string Fingerprint = "sha256:precedence-fp";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly TrialCapsSnapshot Caps14d = new(
        TrialDurationDays: 14,
        TrialTutorTurns: 50,
        TrialPhotoDiagnostics: 10,
        TrialPracticeSessions: 6);

    private static readonly TimeProvider FixedClockProvider = new FixedClock(Now);

    // ---- Active > Trialing -----------------------------------------------

    [Fact]
    public async Task Active_parent_outranks_trialing_parent_for_same_student()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();

        // Parent A: Trialing
        await SeedTrialingAsync(store, ParentA);
        // Parent B: Active Premium
        await SeedActiveAsync(store, ParentB, SubscriptionTier.Premium);
        // Both parents bound to the same student.
        await bindings.GrantAsync(ParentA, Student, InstituteId, Now, CancellationToken.None);
        await bindings.GrantAsync(ParentB, Student, InstituteId, Now, CancellationToken.None);
        // Parent B linked the student on its subscription stream as primary.
        // (SeedActiveAsync uses the parent's own student id; we simulate the
        // sibling-link by appending it manually.)
        await store.AppendAsync(ParentB, new SiblingEntitlementLinked_V1(
            ParentSubjectIdEncrypted: ParentB,
            SiblingStudentSubjectIdEncrypted: Student,
            SiblingOrdinal: 1,
            Tier: SubscriptionTier.Premium,
            SiblingMonthlyAgorot: 14_900L,
            LinkedAt: Now), CancellationToken.None);

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.Active, view.EffectiveStatus);
        Assert.Equal(SubscriptionTier.Premium, view.EffectiveTier);
        Assert.Equal(ParentB, view.SourceParentSubjectIdEncrypted);
    }

    // ---- Trialing > Expired ----------------------------------------------

    [Fact]
    public async Task Trialing_parent_outranks_expired_parent_for_same_student()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();

        await SeedTrialingAsync(store, ParentA);
        await SeedExpiredAsync(store, ParentD);
        await bindings.GrantAsync(ParentA, Student, InstituteId, Now, CancellationToken.None);
        await bindings.GrantAsync(ParentD, Student, InstituteId, Now, CancellationToken.None);

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.Trialing, view.EffectiveStatus);
        Assert.Equal(SubscriptionTier.TrialPlus, view.EffectiveTier);
        Assert.Equal(ParentA, view.SourceParentSubjectIdEncrypted);
        // Trial view carries TrialEndsAt as ValidUntil.
        Assert.NotNull(view.ValidUntil);
    }

    // ---- Expired alone → Unsubscribed-equivalent paywall hit -------------

    [Fact]
    public async Task Expired_only_parent_returns_unsubscribed_view()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();

        await SeedExpiredAsync(store, ParentD);
        await bindings.GrantAsync(ParentD, Student, InstituteId, Now, CancellationToken.None);

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);

        Assert.Equal(SubscriptionTier.Unsubscribed, view.EffectiveTier);
        Assert.Equal(SubscriptionStatus.Unsubscribed, view.EffectiveStatus);
    }

    // ---- PastDue > Expired -----------------------------------------------

    [Fact]
    public async Task Pastdue_parent_outranks_expired_parent_for_same_student()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();

        await SeedPastDueAsync(store, ParentC);
        await SeedExpiredAsync(store, ParentD);
        await bindings.GrantAsync(ParentC, Student, InstituteId, Now, CancellationToken.None);
        await bindings.GrantAsync(ParentD, Student, InstituteId, Now, CancellationToken.None);
        // Link the shared student onto Parent C's stream so Parent C is the
        // entitlement source for this student.
        await store.AppendAsync(ParentC, new SiblingEntitlementLinked_V1(
            ParentSubjectIdEncrypted: ParentC,
            SiblingStudentSubjectIdEncrypted: Student,
            SiblingOrdinal: 1,
            Tier: SubscriptionTier.Premium,
            SiblingMonthlyAgorot: 14_900L,
            LinkedAt: Now), CancellationToken.None);

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.PastDue, view.EffectiveStatus);
        Assert.Equal(SubscriptionTier.Premium, view.EffectiveTier);
        Assert.Equal(ParentC, view.SourceParentSubjectIdEncrypted);
    }

    // ---- Active > PastDue ------------------------------------------------

    [Fact]
    public async Task Active_parent_outranks_pastdue_parent_for_same_student()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();

        await SeedActiveAsync(store, ParentB, SubscriptionTier.Plus);
        await SeedPastDueAsync(store, ParentC);
        await bindings.GrantAsync(ParentB, Student, InstituteId, Now, CancellationToken.None);
        await bindings.GrantAsync(ParentC, Student, InstituteId, Now, CancellationToken.None);
        // Link the student to BOTH parents' streams.
        await store.AppendAsync(ParentB, new SiblingEntitlementLinked_V1(
            ParentSubjectIdEncrypted: ParentB,
            SiblingStudentSubjectIdEncrypted: Student,
            SiblingOrdinal: 1,
            Tier: SubscriptionTier.Plus,
            SiblingMonthlyAgorot: 14_900L,
            LinkedAt: Now), CancellationToken.None);
        await store.AppendAsync(ParentC, new SiblingEntitlementLinked_V1(
            ParentSubjectIdEncrypted: ParentC,
            SiblingStudentSubjectIdEncrypted: Student,
            SiblingOrdinal: 1,
            Tier: SubscriptionTier.Premium,
            SiblingMonthlyAgorot: 14_900L,
            LinkedAt: Now), CancellationToken.None);

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.Active, view.EffectiveStatus);
        Assert.Equal(SubscriptionTier.Plus, view.EffectiveTier);
        Assert.Equal(ParentB, view.SourceParentSubjectIdEncrypted);
    }

    // ---- Trialing alone → TrialPlus view --------------------------------

    [Fact]
    public async Task Trialing_only_parent_returns_trial_plus_view()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();
        await SeedTrialingAsync(store, ParentA);
        await bindings.GrantAsync(ParentA, Student, InstituteId, Now, CancellationToken.None);

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);
        Assert.Equal(SubscriptionStatus.Trialing, view.EffectiveStatus);
        Assert.Equal(SubscriptionTier.TrialPlus, view.EffectiveTier);
    }

    // ---- Calendar-expired-but-not-yet-applied → not Trialing ------------

    [Fact]
    public async Task Trialing_state_past_calendar_boundary_does_not_extend_entitlement()
    {
        // Worker hasn't fired ExpireTrial yet, but TrialEndsAt has elapsed.
        // The resolver MUST NOT keep the student on TrialPlus past the
        // pinned end — design §5.3 "no false-positive lockout, but also
        // no extra-free call".
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();
        // Trial that started 30 days ago with a 14-day window — already
        // past calendar end at `Now`.
        var oldStart = Now.AddDays(-30);
        var oldStartEvt = SubscriptionCommands.StartTrial(
            new SubscriptionState(),
            ParentA, Student,
            TrialKind.SelfPay,
            oldStart, oldStart.AddDays(14),
            Fingerprint, "v1-baseline", Caps14d);
        await store.AppendAsync(ParentA, oldStartEvt, CancellationToken.None);
        await bindings.GrantAsync(ParentA, Student, InstituteId, Now, CancellationToken.None);

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);
        Assert.Equal(SubscriptionTier.Unsubscribed, view.EffectiveTier);
        Assert.Equal(SubscriptionStatus.Unsubscribed, view.EffectiveStatus);
    }

    // ---- No bindings → Unsubscribed --------------------------------------

    [Fact]
    public async Task No_parent_binding_returns_unsubscribed_view()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var bindings = new InMemoryParentChildBindingStore();
        // No GrantAsync — student is unbound.

        var resolver = new StudentEntitlementResolver(
            store: store,
            documentStore: null,
            graceReader: null,
            parentBindings: bindings,
            clock: FixedClockProvider);

        var view = await resolver.ResolveAsync(Student, CancellationToken.None);
        Assert.Equal(SubscriptionTier.Unsubscribed, view.EffectiveTier);
        Assert.Equal(SubscriptionStatus.Unsubscribed, view.EffectiveStatus);
    }

    // ---- Helpers ---------------------------------------------------------

    private static async Task SeedTrialingAsync(
        ISubscriptionAggregateStore store, string parentId)
    {
        // Trial starts at Now, ends 14d later.
        var startEvt = SubscriptionCommands.StartTrial(
            new SubscriptionState(),
            parentId, Student,
            TrialKind.SelfPay,
            Now, Now.AddDays(14),
            Fingerprint, "v1-baseline", Caps14d);
        await store.AppendAsync(parentId, startEvt, CancellationToken.None);
    }

    private static async Task SeedActiveAsync(
        ISubscriptionAggregateStore store, string parentId, SubscriptionTier tier)
    {
        // Use a parent-specific primary student so the seed doesn't
        // collide with the resolver-target Student id; precedence tests
        // explicitly link the target student via a sibling-link event.
        var primary = parentId + "::primary";
        var activateEvt = SubscriptionCommands.Activate(
            new SubscriptionState(),
            parentId, primary, tier, BillingCycle.Monthly, PaymentTxnId, Now);
        await store.AppendAsync(parentId, activateEvt, CancellationToken.None);
    }

    private static async Task SeedPastDueAsync(
        ISubscriptionAggregateStore store, string parentId)
    {
        var primary = parentId + "::primary";
        var activateEvt = SubscriptionCommands.Activate(
            new SubscriptionState(),
            parentId, primary, SubscriptionTier.Premium, BillingCycle.Monthly, PaymentTxnId, Now.AddDays(-40));
        await store.AppendAsync(parentId, activateEvt, CancellationToken.None);

        var paymentFailed = new PaymentFailed_V1(
            ParentSubjectIdEncrypted: parentId,
            Reason: "card_declined",
            AttemptNumber: 2,
            FailedAt: Now.AddDays(-1));
        await store.AppendAsync(parentId, paymentFailed, CancellationToken.None);
    }

    private static async Task SeedExpiredAsync(
        ISubscriptionAggregateStore store, string parentId)
    {
        // StartTrial → ExpireTrial fully drives Status to Expired.
        var startEvt = SubscriptionCommands.StartTrial(
            new SubscriptionState(),
            parentId, Student,
            TrialKind.SelfPay,
            Now.AddDays(-30), Now.AddDays(-16),
            Fingerprint, "v1-baseline", Caps14d);
        await store.AppendAsync(parentId, startEvt, CancellationToken.None);

        // Replay forward to apply the start event so we can hand a real
        // state to ExpireTrial.
        var agg = SubscriptionAggregate.ReplayFrom(
            await store.ReadEventsAsync(parentId, CancellationToken.None));
        var expireEvt = SubscriptionCommands.ExpireTrial(
            agg.State,
            new TrialUtilization(0, 0, 0, 0, false),
            Now.AddDays(-16));
        await store.AppendAsync(parentId, expireEvt, CancellationToken.None);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
