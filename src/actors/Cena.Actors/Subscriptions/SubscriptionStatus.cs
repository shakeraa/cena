// =============================================================================
// Cena Platform — SubscriptionStatus enum (EPIC-PRR-I, ADR-0057)
//
// Canonical lifecycle states. State transitions are enforced by
// SubscriptionCommands; unknown transitions throw.
//
// Canonical state graph (trial-then-paywall design §3):
//
//   Unsubscribed  -- StartTrial         --> Trialing
//   Unsubscribed  -- Activate           --> Active        (skip-trial path)
//   Trialing      -- ConvertTrial       --> Active        (paid conversion;
//                                                          existing Activate
//                                                          finalises tier+txn)
//   Trialing      -- ExpireTrial        --> Expired       (timer-driven only)
//   Expired       -- Activate           --> Active        (re-purchase; no
//                                                          re-trial — see
//                                                          §5.7 abuse defense)
//   Active        -- PaymentFails       --> PastDue
//   Active        -- Cancel             --> Cancelled
//   Active        -- Renew              --> Active
//   Active        -- Refund             --> Refunded
//   PastDue       -- PaymentSucceeds    --> Active
//   PastDue       -- RetryExhausted     --> Cancelled
//   Cancelled     -- (terminal, cannot reactivate; new stream required)
//   Refunded      -- (terminal)
//
// Trialing is a STATE not a tier — TierCatalog still answers "what does
// Trialing entitle me to?" via the synthetic TrialPlus tier; the resolver
// fans the live trial-allotment caps onto the synthesized view at read time.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Canonical subscription lifecycle state.</summary>
public enum SubscriptionStatus
{
    /// <summary>Never activated, or re-entered on full stream crypto-shred.</summary>
    Unsubscribed = 0,

    /// <summary>Paid up, entitlements effective.</summary>
    Active = 1,

    /// <summary>Payment failure occurred; retry schedule in progress.</summary>
    PastDue = 2,

    /// <summary>Terminal — subscription ended by user or retry-exhaustion.</summary>
    Cancelled = 3,

    /// <summary>Terminal — money returned under money-back guarantee.</summary>
    Refunded = 4,

    /// <summary>
    /// Trial in progress. Entitlements are governed by the per-trial
    /// caps-snapshot pinned at <c>StartTrial</c> time, NOT by a tier
    /// definition. Transitions: Trialing → Active (ConvertTrial then
    /// Activate) or Trialing → Expired (timer-driven).
    /// </summary>
    Trialing = 5,

    /// <summary>
    /// Trial timed out without a conversion. Re-entry into Active requires
    /// a fresh Activate (Stripe checkout completed); a fresh trial is NOT
    /// permitted on this stream — abuse defense per design §5.7.
    /// </summary>
    Expired = 6,
}
