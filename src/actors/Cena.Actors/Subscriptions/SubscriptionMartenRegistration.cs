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

        opts.Schema.For<StudentEntitlementDocument>().Identity(d => d.Id);
        opts.Projections.Add<StudentEntitlementProjection>(ProjectionLifecycle.Inline);
    }
}
