// =============================================================================
// Cena Platform — ConsentMartenRegistration (prr-155 / ADR-0042 prod)
//
// Bounded-context Marten event-type registration for ConsentAggregate.
// Mirrors StudentPlanMartenRegistration (prr-218), MasteryMartenRegistration
// (prr-222), and SubscriptionMartenRegistration (EPIC-PRR-I) — each
// bounded context owns its own ConfigureMarten snippet so cross-cutting
// MartenConfiguration stays under its 500-LOC ratchet (ADR-0012).
// =============================================================================

using Cena.Actors.Consent.Events;
using Marten;

namespace Cena.Actors.Consent;

/// <summary>Marten event + schema registration for the Consent bounded context.</summary>
public static class ConsentMartenRegistration
{
    /// <summary>
    /// Register every consent event type on the Marten
    /// <see cref="StoreOptions"/> so the event-type resolver can
    /// deserialize payload JSON back to the concrete record type without
    /// falling back to assembly-scan heuristics. No inline projections —
    /// the aggregate is rebuilt via
    /// <see cref="ConsentAggregate.ReplayFrom"/>, and
    /// <see cref="IConsentAggregateStore.ReadEventsAsync"/> is the
    /// canonical audit-export path (prr-130).
    /// </summary>
    public static void RegisterConsentContext(this StoreOptions opts)
    {
        opts.Events.AddEventType<ConsentGranted_V1>();
        opts.Events.AddEventType<ConsentGranted_V2>();
        opts.Events.AddEventType<ConsentRevoked_V1>();
        opts.Events.AddEventType<ConsentPurposeAdded_V1>();
        opts.Events.AddEventType<ConsentReviewedByParent_V1>();
        opts.Events.AddEventType<AdminConsentOverridden_V1>();
        opts.Events.AddEventType<StudentVisibilityVetoed_V1>();

        // ADR-0059 §15.7 / PRR-267 — Bagrut reference-library consent
        // events. Land on the same consent stream so the existing RTBF
        // crypto-shred cascade (ADR-0042) covers them. ItemRendered_V1
        // is pure-audit (no state fold); Granted_V1 / Revoked_V1
        // populate ConsentState.BagrutReference for the issuance gate.
        opts.Events.AddEventType<BagrutReferenceConsentGranted_V1>();
        opts.Events.AddEventType<BagrutReferenceConsentRevoked_V1>();
        opts.Events.AddEventType<BagrutReferenceItemRendered_V1>();
    }
}
