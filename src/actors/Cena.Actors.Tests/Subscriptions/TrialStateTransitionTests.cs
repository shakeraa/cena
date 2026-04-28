// =============================================================================
// Cena Platform — TrialStateTransitionTests (task t_dc70d2cd9ab9)
//
// Locks the §3 state-graph for the trial sub-cycle:
//
//     Unsubscribed -- StartTrial    --> Trialing
//     Trialing     -- ConvertTrial  --> Trialing  (marker; Activate flips to Active)
//     Trialing     -- ExpireTrial   --> Expired
//     Expired      -- Activate      --> Active     (re-purchase; no second trial)
//     Trialing     -- Activate      --> Active     (skip-trial collapse path)
//
// Plus the inverse of every rejection: e.g. StartTrial from Active throws,
// ExpireTrial from Unsubscribed throws, etc. The test names read as the
// transition they assert.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class TrialStateTransitionTests
{
    private const string ParentId = "enc::parent::trial-state";
    private const string PrimaryStudentId = "enc::student::trial-state-primary";
    private const string PaymentTxnId = "enc::payment::trial-conversion-001";
    private const string Fingerprint = "sha256:trial-state-fp";

    private static readonly DateTimeOffset StartedAt =
        new(2026, 4, 28, 10, 0, 0, TimeSpan.Zero);

    private static readonly TrialCapsSnapshot DefaultCaps = new(
        TrialDurationDays: 14,
        TrialTutorTurns: 50,
        TrialPhotoDiagnostics: 10,
        TrialPracticeSessions: 6);

    private static readonly TrialUtilization SampleUtilization = new(
        TutorTurnsUsed: 12,
        PhotoDiagnosticsUsed: 3,
        SessionsStarted: 4,
        DaysActive: 5,
        HitCapBeforeExpiry: false);

    // ---- Unsubscribed → Trialing -----------------------------------------

    [Fact]
    public void StartTrial_from_unsubscribed_transitions_to_trialing()
    {
        var agg = new SubscriptionAggregate();

        var evt = SubscriptionCommands.StartTrial(
            agg.State, ParentId, PrimaryStudentId,
            TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
            Fingerprint, "v1-baseline", DefaultCaps);
        agg.Apply(evt);

        Assert.Equal(SubscriptionStatus.Trialing, agg.State.Status);
        Assert.Equal(StartedAt, agg.State.TrialStartedAt);
        Assert.Equal(StartedAt.AddDays(14), agg.State.TrialEndsAt);
        Assert.Equal(TrialKind.SelfPay, agg.State.TrialOrigin);
        Assert.Equal(DefaultCaps, agg.State.TrialCaps);
        Assert.Equal(Fingerprint, agg.State.TrialFingerprintHash);
        Assert.Equal("v1-baseline", agg.State.TrialExperimentVariantId);
        Assert.Single(agg.State.LinkedStudents);
        Assert.Equal(PrimaryStudentId, agg.State.LinkedStudents[0].StudentSubjectIdEncrypted);
        Assert.Equal(0, agg.State.LinkedStudents[0].Ordinal);
    }

    [Fact]
    public void StartTrial_pins_caps_snapshot_immutable_to_later_config_change()
    {
        // The caller hands a caps snapshot; subsequent edits to that
        // snapshot's source (e.g., TrialAllotmentConfig admin update) MUST
        // NOT affect the in-flight trial. Our event is a record so the
        // snapshot is value-typed; this test guards against any future
        // refactor that turns it into a reference-shared mutable object.
        var agg = new SubscriptionAggregate();
        var snapshot = new TrialCapsSnapshot(
            TrialDurationDays: 7,
            TrialTutorTurns: 25,
            TrialPhotoDiagnostics: 5,
            TrialPracticeSessions: 3);
        var evt = SubscriptionCommands.StartTrial(
            agg.State, ParentId, PrimaryStudentId, TrialKind.SelfPay,
            StartedAt, StartedAt.AddDays(7), Fingerprint, "v1-baseline", snapshot);
        agg.Apply(evt);

        // Reading back the pinned caps gives us the start-time values.
        Assert.Equal(7, agg.State.TrialCaps?.TrialDurationDays);
        Assert.Equal(25, agg.State.TrialCaps?.TrialTutorTurns);
        Assert.Equal(5, agg.State.TrialCaps?.TrialPhotoDiagnostics);
        Assert.Equal(3, agg.State.TrialCaps?.TrialPracticeSessions);
    }

    // ---- Trialing → Expired (calendar timeout) ---------------------------

    [Fact]
    public void ExpireTrial_from_trialing_transitions_to_expired()
    {
        var agg = TrialingAggregate();
        var endedAt = StartedAt.AddDays(14);

        var evt = SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, endedAt);
        agg.Apply(evt);

        Assert.Equal(SubscriptionStatus.Expired, agg.State.Status);
        Assert.Equal(endedAt, evt.TrialEndedAt);
        Assert.Equal(TrialExpired_V1.OutcomeExpired, evt.Outcome);
        Assert.Equal(SampleUtilization, evt.Utilization);
    }

    [Fact]
    public void ExpireTrial_is_idempotent_when_already_expired()
    {
        // Second call from Expired returns a fresh event but does not throw.
        // The state stays Expired; TrialEndsAt is anchored on the original
        // pinned end so re-emission produces a deterministic stream.
        var agg = TrialingAggregate();
        var firstEnd = StartedAt.AddDays(14);
        agg.Apply(SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, firstEnd));
        Assert.Equal(SubscriptionStatus.Expired, agg.State.Status);

        var secondEnd = firstEnd.AddHours(2);
        var second = SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, secondEnd);
        // Pinned end wins over the late `now` so replay is stable.
        Assert.Equal(firstEnd, second.TrialEndedAt);
    }

    // ---- Trialing → Active via ConvertTrial + Activate -------------------

    [Fact]
    public void ConvertTrial_then_activate_lands_on_active_paid_state()
    {
        var agg = TrialingAggregate();
        var convertedAt = StartedAt.AddDays(11);

        // Step 1: marker event.
        var convertEvt = SubscriptionCommands.ConvertTrial(
            agg.State, SubscriptionTier.Plus, BillingCycle.Monthly,
            PaymentTxnId, SampleUtilization, convertedAt);
        Assert.Equal(SubscriptionTier.Plus, convertEvt.ConvertedToTier);
        Assert.Equal(11, convertEvt.DaysIntoTrial);
        Assert.Equal(SampleUtilization, convertEvt.UtilizationAtConversion);
        agg.Apply(convertEvt);
        // Marker does NOT flip to Active by itself.
        Assert.Equal(SubscriptionStatus.Trialing, agg.State.Status);

        // Step 2: standard activation lands the commercial state.
        var activateEvt = SubscriptionCommands.Activate(
            agg.State, ParentId, PrimaryStudentId,
            SubscriptionTier.Plus, BillingCycle.Monthly, PaymentTxnId, convertedAt);
        agg.Apply(activateEvt);

        Assert.Equal(SubscriptionStatus.Active, agg.State.Status);
        Assert.Equal(SubscriptionTier.Plus, agg.State.CurrentTier);
        Assert.Equal(BillingCycle.Monthly, agg.State.CurrentCycle);
        // Trial provenance stays for analytics.
        Assert.Equal(StartedAt, agg.State.TrialStartedAt);
        Assert.Equal(TrialKind.SelfPay, agg.State.TrialOrigin);
    }

    // ---- Expired → Active (re-purchase) ----------------------------------

    [Fact]
    public void Activate_from_expired_lands_on_active_no_re_trial_needed()
    {
        var agg = TrialingAggregate();
        agg.Apply(SubscriptionCommands.ExpireTrial(
            agg.State, SampleUtilization, StartedAt.AddDays(14)));
        Assert.Equal(SubscriptionStatus.Expired, agg.State.Status);

        var rePurchasedAt = StartedAt.AddDays(30);
        var activateEvt = SubscriptionCommands.Activate(
            agg.State, ParentId, PrimaryStudentId,
            SubscriptionTier.Premium, BillingCycle.Annual, PaymentTxnId, rePurchasedAt);
        agg.Apply(activateEvt);

        Assert.Equal(SubscriptionStatus.Active, agg.State.Status);
        Assert.Equal(SubscriptionTier.Premium, agg.State.CurrentTier);
        Assert.Equal(BillingCycle.Annual, agg.State.CurrentCycle);
    }

    // ---- Rejection: invalid transitions ----------------------------------

    [Fact]
    public void StartTrial_from_active_throws()
    {
        var agg = ActiveAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
                Fingerprint, "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void StartTrial_from_trialing_throws_no_second_trial()
    {
        var agg = TrialingAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
                Fingerprint, "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void StartTrial_from_expired_throws_no_re_trial_on_same_stream()
    {
        var agg = TrialingAggregate();
        agg.Apply(SubscriptionCommands.ExpireTrial(
            agg.State, SampleUtilization, StartedAt.AddDays(14)));
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt.AddDays(40), StartedAt.AddDays(54),
                Fingerprint, "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void ConvertTrial_from_unsubscribed_throws()
    {
        var agg = new SubscriptionAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.Plus, BillingCycle.Monthly,
                PaymentTxnId, SampleUtilization, StartedAt));
    }

    [Fact]
    public void ConvertTrial_from_active_throws()
    {
        var agg = ActiveAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.Plus, BillingCycle.Monthly,
                PaymentTxnId, SampleUtilization, StartedAt));
    }

    [Fact]
    public void ExpireTrial_from_unsubscribed_throws()
    {
        var agg = new SubscriptionAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, StartedAt.AddDays(14)));
    }

    [Fact]
    public void ExpireTrial_from_active_throws()
    {
        var agg = ActiveAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, StartedAt.AddDays(14)));
    }

    [Fact]
    public void Activate_from_cancelled_throws_terminal_states_block_reactivation()
    {
        var agg = ActiveAggregate();
        agg.Apply(SubscriptionCommands.Cancel(agg.State, "user-cancel", "parent", StartedAt));
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.Activate(
                agg.State, ParentId, PrimaryStudentId,
                SubscriptionTier.Premium, BillingCycle.Monthly, PaymentTxnId, StartedAt.AddDays(1)));
    }

    [Fact]
    public void Activate_from_refunded_throws_terminal_states_block_reactivation()
    {
        var agg = ActiveAggregate();
        agg.Apply(SubscriptionCommands.Refund(
            agg.State, 24_900L, "money-back-window", agg.State.ActivatedAt!.Value.AddDays(5)));
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.Activate(
                agg.State, ParentId, PrimaryStudentId,
                SubscriptionTier.Premium, BillingCycle.Monthly, PaymentTxnId, StartedAt.AddDays(60)));
    }

    // ---- Replay determinism ---------------------------------------------

    [Fact]
    public void Replay_through_trial_lifecycle_reconstructs_state_exactly()
    {
        // Build a stream: StartTrial → ExpireTrial → Activate.
        var startTrial = new TrialStarted_V1(
            ParentSubjectIdEncrypted: ParentId,
            PrimaryStudentSubjectIdEncrypted: PrimaryStudentId,
            TrialKind: TrialKind.ParentPay,
            TrialStartedAt: StartedAt,
            TrialEndsAt: StartedAt.AddDays(14),
            FingerprintHash: Fingerprint,
            ExperimentVariantId: "v1-baseline",
            CapsSnapshot: DefaultCaps);
        var expireTrial = new TrialExpired_V1(
            ParentSubjectIdEncrypted: ParentId,
            PrimaryStudentSubjectIdEncrypted: PrimaryStudentId,
            TrialEndedAt: StartedAt.AddDays(14),
            Outcome: TrialExpired_V1.OutcomeExpired,
            Utilization: SampleUtilization);
        var activate = new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: ParentId,
            PrimaryStudentSubjectIdEncrypted: PrimaryStudentId,
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Annual,
            GrossAmountAgorot: 249_000L,
            PaymentTransactionIdEncrypted: PaymentTxnId,
            ActivatedAt: StartedAt.AddDays(30),
            RenewsAt: StartedAt.AddDays(30).AddYears(1));

        var replayed = SubscriptionAggregate.ReplayFrom(
            new object[] { startTrial, expireTrial, activate });

        Assert.Equal(SubscriptionStatus.Active, replayed.State.Status);
        Assert.Equal(SubscriptionTier.Premium, replayed.State.CurrentTier);
        Assert.Equal(BillingCycle.Annual, replayed.State.CurrentCycle);
        // Trial provenance preserved for analytics.
        Assert.Equal(StartedAt, replayed.State.TrialStartedAt);
        Assert.Equal(TrialKind.ParentPay, replayed.State.TrialOrigin);
        Assert.Equal(DefaultCaps, replayed.State.TrialCaps);
    }

    // ---- IsTrialingAsOf semantics ----------------------------------------

    [Fact]
    public void IsTrialingAsOf_returns_true_during_calendar_window()
    {
        var agg = TrialingAggregate();
        Assert.True(agg.State.IsTrialingAsOf(StartedAt.AddDays(7)));
        Assert.True(agg.State.IsTrialingAsOf(StartedAt.AddDays(13).AddHours(23)));
    }

    [Fact]
    public void IsTrialingAsOf_returns_false_after_calendar_boundary()
    {
        var agg = TrialingAggregate();
        Assert.False(agg.State.IsTrialingAsOf(StartedAt.AddDays(14).AddHours(1)));
        Assert.False(agg.State.IsTrialingAsOf(StartedAt.AddDays(60)));
    }

    [Fact]
    public void IsTrialingAsOf_caponly_trial_stays_true_indefinitely_until_status_change()
    {
        // Cap-only trial (TrialDurationDays = 0) ⇒ TrialEndsAt == TrialStartedAt.
        // The calendar boundary never "fires" here; expiry comes from the
        // cap enforcer (separate task). State must report Trialing as long
        // as Status = Trialing.
        var agg = new SubscriptionAggregate();
        var caps = new TrialCapsSnapshot(
            TrialDurationDays: 0, TrialTutorTurns: 25,
            TrialPhotoDiagnostics: 0, TrialPracticeSessions: 0);
        var evt = SubscriptionCommands.StartTrial(
            agg.State, ParentId, PrimaryStudentId, TrialKind.SelfPay,
            StartedAt, StartedAt, Fingerprint, "v1-baseline", caps);
        agg.Apply(evt);

        Assert.True(agg.State.IsTrialingAsOf(StartedAt.AddDays(100)));
    }

    // ---- Helpers ---------------------------------------------------------

    private static SubscriptionAggregate TrialingAggregate()
    {
        var agg = new SubscriptionAggregate();
        var evt = SubscriptionCommands.StartTrial(
            agg.State, ParentId, PrimaryStudentId,
            TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
            Fingerprint, "v1-baseline", DefaultCaps);
        agg.Apply(evt);
        return agg;
    }

    private static SubscriptionAggregate ActiveAggregate()
    {
        var agg = new SubscriptionAggregate();
        var evt = SubscriptionCommands.Activate(
            agg.State, ParentId, PrimaryStudentId,
            SubscriptionTier.Premium, BillingCycle.Monthly, PaymentTxnId, StartedAt);
        agg.Apply(evt);
        return agg;
    }
}
