// =============================================================================
// Cena Platform — SubscriptionPaymentMethodAttachedTests (Phase 1D-fix item 2)
//
// Locks the contract for SubscriptionPaymentMethodAttached_V1 and its Apply
// onto SubscriptionState:
//
//   - Apply sets HasPaymentMethodOnFile = true
//   - LastAttachedPaymentMethodIdEncrypted is pinned
//   - LastAttachedPaymentMethodFingerprintHash is pinned
//   - LastPaymentMethodAttachedAt is pinned
//   - Re-applying with the same fingerprint is benign (overwrites with same
//     values — caller side de-dupes; aggregate Apply is order-tolerant)
//   - Apply on an empty stream pins ParentSubjectIdEncrypted as a side-effect
//     (defensive — start-trial path always TrialStarted_V1 first, but a
//     future payment-method-only flow shouldn't crash on missing parent id)
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class SubscriptionPaymentMethodAttachedTests
{
    private const string ParentId = "enc::parent::pm-attach-tests";
    private const string PaymentMethodIdEncrypted = "pm-enc:abc";
    private const string FingerprintHash = "card:test-fp-001";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_after_TrialStarted_pins_payment_method_state()
    {
        var aggregate = new SubscriptionAggregate();
        var caps = new TrialCapsSnapshot(14, 50, 10, 6);
        aggregate.Apply(new TrialStarted_V1(
            ParentId, "enc::student::pri", TrialKind.SelfPay,
            Now, Now.AddDays(14), "card:fp-existing", "v1-baseline", caps));

        Assert.False(aggregate.State.HasPaymentMethodOnFile);

        aggregate.Apply(new SubscriptionPaymentMethodAttached_V1(
            ParentSubjectIdEncrypted: ParentId,
            PaymentMethodIdEncrypted: PaymentMethodIdEncrypted,
            FingerprintHash: FingerprintHash,
            AttachedAt: Now,
            Source: PaymentMethodAttachSource.TrialStartSetupIntent));

        Assert.True(aggregate.State.HasPaymentMethodOnFile);
        Assert.Equal(PaymentMethodIdEncrypted,
            aggregate.State.LastAttachedPaymentMethodIdEncrypted);
        Assert.Equal(FingerprintHash,
            aggregate.State.LastAttachedPaymentMethodFingerprintHash);
        Assert.Equal(Now, aggregate.State.LastPaymentMethodAttachedAt);
    }

    [Fact]
    public void Apply_on_empty_stream_pins_parent_id_defensively()
    {
        var aggregate = new SubscriptionAggregate();
        Assert.Null(aggregate.State.ParentSubjectIdEncrypted);

        aggregate.Apply(new SubscriptionPaymentMethodAttached_V1(
            ParentSubjectIdEncrypted: ParentId,
            PaymentMethodIdEncrypted: PaymentMethodIdEncrypted,
            FingerprintHash: FingerprintHash,
            AttachedAt: Now,
            Source: PaymentMethodAttachSource.AccountBillingSetupIntent));

        Assert.Equal(ParentId, aggregate.State.ParentSubjectIdEncrypted);
        Assert.True(aggregate.State.HasPaymentMethodOnFile);
    }

    [Fact]
    public void Re_applying_same_event_overwrites_with_same_values_no_throw()
    {
        var aggregate = new SubscriptionAggregate();
        var evt = new SubscriptionPaymentMethodAttached_V1(
            ParentSubjectIdEncrypted: ParentId,
            PaymentMethodIdEncrypted: PaymentMethodIdEncrypted,
            FingerprintHash: FingerprintHash,
            AttachedAt: Now,
            Source: PaymentMethodAttachSource.TrialStartSetupIntent);

        aggregate.Apply(evt);
        aggregate.Apply(evt);

        Assert.True(aggregate.State.HasPaymentMethodOnFile);
        Assert.Equal(FingerprintHash,
            aggregate.State.LastAttachedPaymentMethodFingerprintHash);
    }

    [Fact]
    public void Second_attach_with_different_fingerprint_replaces_pinned_state()
    {
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionPaymentMethodAttached_V1(
            ParentSubjectIdEncrypted: ParentId,
            PaymentMethodIdEncrypted: PaymentMethodIdEncrypted,
            FingerprintHash: FingerprintHash,
            AttachedAt: Now,
            Source: PaymentMethodAttachSource.TrialStartSetupIntent));

        var laterTimestamp = Now.AddMonths(3);
        aggregate.Apply(new SubscriptionPaymentMethodAttached_V1(
            ParentSubjectIdEncrypted: ParentId,
            PaymentMethodIdEncrypted: "pm-enc:newer",
            FingerprintHash: "card:newer-fp",
            AttachedAt: laterTimestamp,
            Source: PaymentMethodAttachSource.AccountBillingSetupIntent));

        Assert.True(aggregate.State.HasPaymentMethodOnFile);
        Assert.Equal("pm-enc:newer",
            aggregate.State.LastAttachedPaymentMethodIdEncrypted);
        Assert.Equal("card:newer-fp",
            aggregate.State.LastAttachedPaymentMethodFingerprintHash);
        Assert.Equal(laterTimestamp,
            aggregate.State.LastPaymentMethodAttachedAt);
    }

    [Fact]
    public void Replay_through_aggregate_dispatcher_works()
    {
        var events = new object[]
        {
            new TrialStarted_V1(
                ParentId, "enc::student::pri", TrialKind.SelfPay,
                Now, Now.AddDays(14), "card:fp-existing", "v1-baseline",
                new TrialCapsSnapshot(14, 50, 10, 6)),
            new SubscriptionPaymentMethodAttached_V1(
                ParentSubjectIdEncrypted: ParentId,
                PaymentMethodIdEncrypted: PaymentMethodIdEncrypted,
                FingerprintHash: FingerprintHash,
                AttachedAt: Now,
                Source: PaymentMethodAttachSource.TrialStartSetupIntent),
        };
        var aggregate = SubscriptionAggregate.ReplayFrom(events);
        Assert.True(aggregate.State.HasPaymentMethodOnFile);
        Assert.Equal(SubscriptionStatus.Trialing, aggregate.State.Status);
    }
}
