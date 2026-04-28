// =============================================================================
// Cena Platform — SubscriptionMartenRegistration (EPIC-PRR-I, ADR-0057)
//
// Bounded-context owns its own Marten event + projection registration. Called
// by AddSubscriptionsMarten() via services.ConfigureMarten so the subscription
// registration lives inside this namespace rather than in the cross-cutting
// MartenConfiguration file (which is under the 500-LOC ratchet per ADR-0012).
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using JasperFx.Events.Projections;
using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>Marten event + projection registration for the Subscriptions bounded context.</summary>
public static class SubscriptionMartenRegistration
{
    /// <summary>
    /// Register all subscription event types, the student-entitlement document
    /// schema, and the inline StudentEntitlementProjection.
    /// </summary>
    public static void RegisterSubscriptionsContext(this StoreOptions opts)
    {
        opts.Events.AddEventType<SubscriptionActivated_V1>();
        opts.Events.AddEventType<TierChanged_V1>();
        opts.Events.AddEventType<BillingCycleChanged_V1>();
        opts.Events.AddEventType<SiblingEntitlementLinked_V1>();
        opts.Events.AddEventType<SiblingEntitlementUnlinked_V1>();
        opts.Events.AddEventType<RenewalProcessed_V1>();
        opts.Events.AddEventType<PaymentFailed_V1>();
        opts.Events.AddEventType<SubscriptionCancelled_V1>();
        opts.Events.AddEventType<SubscriptionRefunded_V1>();
        opts.Events.AddEventType<EntitlementSoftCapReached_V1>();

        // task t_dc70d2cd9ab9 — trial-then-paywall Phase 1A: trial state machine.
        // The three trial events live on the same parent-keyed stream as the
        // paid-subscription events; the trial sub-cycle is bracketed by
        // TrialStarted_V1 and TrialExpired_V1 (or TrialConverted_V1 + the
        // existing SubscriptionActivated_V1). See design doc §3 + §11.
        opts.Events.AddEventType<TrialStarted_V1>();
        opts.Events.AddEventType<TrialConverted_V1>();
        opts.Events.AddEventType<TrialExpired_V1>();

        // task t_b89826b8bd60 — platform-wide trial-allotment config.
        //   TrialAllotmentConfig         : singleton row (Id = "current")
        //                                  holding the four trial knobs
        //                                  (duration days + 3 quotas). All
        //                                  defaults = 0 → no trial offered
        //                                  out of the box.
        //   TrialAllotmentConfigChanged_V1: audit-trail event appended to
        //                                  the singleton stream
        //                                  "trial-allotment-config" on every
        //                                  super-admin update.
        opts.Events.AddEventType<TrialAllotmentConfigChanged_V1>();
        opts.Schema.For<TrialAllotmentConfig>().Identity(d => d.Id);

        // task t_bae6b9216b3e — per-user discount-codes (Stripe-backed).
        // discount-{assignmentId} streams; independent of trial-allotment.
        opts.Events.AddEventType<DiscountIssued_V1>();
        opts.Events.AddEventType<DiscountRedeemed_V1>();
        opts.Events.AddEventType<DiscountRevoked_V1>();

        opts.Schema.For<StudentEntitlementDocument>().Identity(d => d.Id);
        opts.Projections.Add<StudentEntitlementProjection>(ProjectionLifecycle.Inline);

        // PRR-330 — Weekly unit-economics snapshot. One row per week keyed
        // by the Sunday-anchored id ("week-YYYY-MM-DD") so the admin
        // history chart renders trend lines by cheap index scan instead
        // of replaying the subscription event stream on every page load.
        opts.Schema.For<UnitEconomicsSnapshotDocument>().Identity(d => d.Id);

        // PRR-344 — Alpha-migration grace marker + operator seed list.
        //   AlphaGraceMarker        : one row per granted parent (Id =
        //                              parentSubjectIdEncrypted).
        //   AlphaMigrationSeedDocument: singleton row (Id = "current") carrying
        //                              the operator-supplied seed list so the
        //                              worker, admin endpoint, and replicas
        //                              share one canonical source.
        opts.Schema.For<AlphaGraceMarker>().Identity(d => d.Id);
        opts.Schema.For<AlphaMigrationSeedDocument>().Identity(d => d.Id);
    }
}
