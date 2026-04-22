// =============================================================================
// Cena Platform — SubscriptionStatus enum (EPIC-PRR-I, ADR-0057)
//
// Canonical lifecycle states. State transitions are enforced by
// SubscriptionCommands; unknown transitions throw.
//
// Canonical state graph:
//
//   Unsubscribed  -- Activate -->  Active
//   Active        -- PaymentFails -->  PastDue
//   Active        -- Cancel -->  Cancelled
//   Active        -- Renew -->  Active
//   Active        -- Refund -->  Refunded
//   PastDue       -- PaymentSucceeds -->  Active
//   PastDue       -- RetryExhausted -->  Cancelled
//   Cancelled     -- (terminal, cannot reactivate; new stream required)
//   Refunded      -- (terminal)
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
}
