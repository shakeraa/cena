// =============================================================================
// Cena Platform — SubscriptionCommandsTrialTests (task t_dc70d2cd9ab9)
//
// Locks the validation rules in StartTrial / ConvertTrial / ExpireTrial.
// Companion to TrialStateTransitionTests — that file owns the state-graph
// transitions; this file owns the per-command argument and pre-condition
// validation.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class SubscriptionCommandsTrialTests
{
    private const string ParentId = "enc::parent::cmd-tests";
    private const string PrimaryStudentId = "enc::student::cmd-tests-primary";
    private const string PaymentTxnId = "enc::payment::cmd-tests-001";
    private const string Fingerprint = "sha256:cmd-tests-fp";

    private static readonly DateTimeOffset StartedAt =
        new(2026, 4, 28, 10, 0, 0, TimeSpan.Zero);

    private static readonly TrialCapsSnapshot DefaultCaps = new(
        TrialDurationDays: 14,
        TrialTutorTurns: 50,
        TrialPhotoDiagnostics: 10,
        TrialPracticeSessions: 6);

    private static readonly TrialCapsSnapshot AllZeroCaps = new(0, 0, 0, 0);

    private static readonly TrialUtilization SampleUtilization = new(
        TutorTurnsUsed: 12,
        PhotoDiagnosticsUsed: 3,
        SessionsStarted: 4,
        DaysActive: 5,
        HitCapBeforeExpiry: false);

    // ---- StartTrial argument validation ----------------------------------

    [Fact]
    public void StartTrial_rejects_all_zero_caps_with_trial_not_offered()
    {
        var agg = new SubscriptionAggregate();
        var ex = Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt,
                Fingerprint, "v1-baseline", AllZeroCaps));
        Assert.Equal("trial_not_offered", ex.Message);
    }

    [Fact]
    public void StartTrial_rejects_empty_parent_id()
    {
        var agg = new SubscriptionAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, "", PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
                Fingerprint, "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void StartTrial_rejects_empty_primary_student_id()
    {
        var agg = new SubscriptionAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, "",
                TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
                Fingerprint, "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void StartTrial_rejects_endsAt_not_after_startedAt_when_duration_is_positive()
    {
        var agg = new SubscriptionAggregate();
        // Duration = 14 days but ends-at == started-at: invariant violation.
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt,
                Fingerprint, "v1-baseline", DefaultCaps));
        // And rejects strictly-earlier ends-at.
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt.AddSeconds(-1),
                Fingerprint, "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void StartTrial_rejects_caponly_trial_with_explicit_endsAt()
    {
        // TrialDurationDays = 0 means "no calendar bound" — the caller MUST
        // pass endsAt == startedAt; passing a different end is a wiring bug.
        var agg = new SubscriptionAggregate();
        var caps = new TrialCapsSnapshot(0, 25, 0, 0);
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt.AddDays(7),
                Fingerprint, "v1-baseline", caps));
    }

    [Fact]
    public void StartTrial_caponly_with_zero_duration_and_endsAt_equal_startedAt_succeeds()
    {
        var agg = new SubscriptionAggregate();
        var caps = new TrialCapsSnapshot(0, 25, 0, 0);
        var evt = SubscriptionCommands.StartTrial(
            agg.State, ParentId, PrimaryStudentId,
            TrialKind.SelfPay, StartedAt, StartedAt,
            Fingerprint, "v1-baseline", caps);
        Assert.Equal(StartedAt, evt.TrialStartedAt);
        Assert.Equal(StartedAt, evt.TrialEndsAt);
    }

    [Fact]
    public void StartTrial_self_pay_requires_fingerprint()
    {
        var agg = new SubscriptionAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
                "", "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void StartTrial_parent_pay_requires_fingerprint()
    {
        var agg = new SubscriptionAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.ParentPay, StartedAt, StartedAt.AddDays(14),
                "   ", "v1-baseline", DefaultCaps));
    }

    [Fact]
    public void StartTrial_institute_code_allows_empty_fingerprint()
    {
        var agg = new SubscriptionAggregate();
        var evt = SubscriptionCommands.StartTrial(
            agg.State, ParentId, PrimaryStudentId,
            TrialKind.InstituteCode, StartedAt, StartedAt.AddDays(14),
            fingerprintHash: "", experimentVariantId: "v1-baseline",
            capsSnapshot: DefaultCaps);
        Assert.Equal(string.Empty, evt.FingerprintHash);
        Assert.Equal(TrialKind.InstituteCode, evt.TrialKind);
    }

    [Fact]
    public void StartTrial_defaults_blank_experiment_variant_to_v1_baseline()
    {
        var agg = new SubscriptionAggregate();
        var evt = SubscriptionCommands.StartTrial(
            agg.State, ParentId, PrimaryStudentId,
            TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
            Fingerprint, experimentVariantId: "", capsSnapshot: DefaultCaps);
        Assert.Equal("v1-baseline", evt.ExperimentVariantId);
    }

    // ---- ConvertTrial validation -----------------------------------------

    [Fact]
    public void ConvertTrial_rejects_unsubscribed_tier_target()
    {
        var agg = TrialingAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.Unsubscribed, BillingCycle.Monthly,
                PaymentTxnId, SampleUtilization, StartedAt.AddDays(11)));
    }

    [Fact]
    public void ConvertTrial_rejects_school_sku_target()
    {
        var agg = TrialingAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.SchoolSku, BillingCycle.Monthly,
                PaymentTxnId, SampleUtilization, StartedAt.AddDays(11)));
    }

    [Fact]
    public void ConvertTrial_rejects_trialplus_synthetic_tier_target()
    {
        var agg = TrialingAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.TrialPlus, BillingCycle.Monthly,
                PaymentTxnId, SampleUtilization, StartedAt.AddDays(11)));
    }

    [Fact]
    public void ConvertTrial_rejects_billing_cycle_none()
    {
        var agg = TrialingAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.Plus, BillingCycle.None,
                PaymentTxnId, SampleUtilization, StartedAt.AddDays(11)));
    }

    [Fact]
    public void ConvertTrial_rejects_blank_payment_transaction_id()
    {
        var agg = TrialingAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.Plus, BillingCycle.Monthly,
                paymentTransactionIdEncrypted: "   ",
                utilizationAtConversion: SampleUtilization,
                convertedAt: StartedAt.AddDays(11)));
    }

    [Fact]
    public void ConvertTrial_clamps_negative_days_into_trial_to_zero()
    {
        // Defensive: replayed events with clock skew may produce a converted-at
        // earlier than started-at. Funnel analytics never sees a negative
        // bucket — clamp to 0.
        var agg = TrialingAggregate();
        var evt = SubscriptionCommands.ConvertTrial(
            agg.State, SubscriptionTier.Plus, BillingCycle.Monthly,
            PaymentTxnId, SampleUtilization, StartedAt.AddDays(-2));
        Assert.Equal(0, evt.DaysIntoTrial);
    }

    [Fact]
    public void ConvertTrial_computes_whole_utc_days_into_trial()
    {
        var agg = TrialingAggregate();
        var convertedAt = StartedAt.AddDays(7).AddHours(13);
        var evt = SubscriptionCommands.ConvertTrial(
            agg.State, SubscriptionTier.Premium, BillingCycle.Annual,
            PaymentTxnId, SampleUtilization, convertedAt);
        Assert.Equal(7, evt.DaysIntoTrial);
    }

    // ---- ExpireTrial idempotency / argument validation -------------------

    [Fact]
    public void ExpireTrial_returns_event_with_pinned_endedAt()
    {
        var agg = TrialingAggregate();
        var endedAt = StartedAt.AddDays(14);
        var evt = SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, endedAt);
        Assert.Equal(endedAt, evt.TrialEndedAt);
        Assert.Equal(TrialExpired_V1.OutcomeExpired, evt.Outcome);
    }

    [Fact]
    public void ExpireTrial_idempotent_re_call_returns_event_anchored_on_original_end()
    {
        // Worker may fire a second ExpireTrial after the stream is already
        // Expired. The state stays Expired; the freshly-returned event
        // anchors on the originally-pinned TrialEndsAt so replay is stable.
        var agg = TrialingAggregate();
        var firstEnd = StartedAt.AddDays(14);
        agg.Apply(SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, firstEnd));
        Assert.Equal(SubscriptionStatus.Expired, agg.State.Status);

        var second = SubscriptionCommands.ExpireTrial(agg.State, SampleUtilization, firstEnd.AddHours(2));
        Assert.Equal(firstEnd, second.TrialEndedAt);
    }

    [Fact]
    public void ExpireTrial_throws_argumentnull_on_null_utilisation()
    {
        var agg = TrialingAggregate();
        Assert.Throws<ArgumentNullException>(() =>
            SubscriptionCommands.ExpireTrial(agg.State, null!, StartedAt.AddDays(14)));
    }

    [Fact]
    public void ConvertTrial_throws_argumentnull_on_null_utilisation()
    {
        var agg = TrialingAggregate();
        Assert.Throws<ArgumentNullException>(() =>
            SubscriptionCommands.ConvertTrial(
                agg.State, SubscriptionTier.Plus, BillingCycle.Monthly,
                PaymentTxnId, null!, StartedAt.AddDays(11)));
    }

    [Fact]
    public void StartTrial_throws_argumentnull_on_null_caps()
    {
        var agg = new SubscriptionAggregate();
        Assert.Throws<ArgumentNullException>(() =>
            SubscriptionCommands.StartTrial(
                agg.State, ParentId, PrimaryStudentId,
                TrialKind.SelfPay, StartedAt, StartedAt.AddDays(14),
                Fingerprint, "v1-baseline", null!));
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
}
