// =============================================================================
// Cena Platform — TrialConverted_V1 (EPIC-PRR-I, trial-then-paywall §11.8)
//
// Emitted when a trialling parent converts to a paid plan. Emitted as a
// MARKER event before the existing <see cref="SubscriptionActivated_V1"/>
// is appended by the standard activation flow — the marker carries the
// utilisation telemetry that the activation event does not (it's a
// commercial event, not a behavioural event).
//
// Caller pattern (design §3 + task body item 4):
//
//   var convertedEvt = SubscriptionCommands.ConvertTrial(state, target, ...);
//   await store.AppendAsync(parentId, convertedEvt, ct);
//   var activateEvt = SubscriptionCommands.Activate(state, ..., target, ...);
//   await store.AppendAsync(parentId, activateEvt, ct);
//
// The two events live in the same stream; the marker is observable to
// analytics independently of the activation. Apply on the aggregate
// flips the state from Trialing → (still Trialing, as a marker) and
// stores the conversion timestamp. The actual Active state arrives on
// the subsequent SubscriptionActivated_V1 Apply.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Marker event: the trial converted to paid. Followed in the same stream
/// by <see cref="SubscriptionActivated_V1"/> with the actual paid tier
/// + payment transaction id.
/// </summary>
/// <param name="ParentSubjectIdEncrypted">Encrypted parent subject id (ADR-0038).</param>
/// <param name="PrimaryStudentSubjectIdEncrypted">Encrypted primary student id (ADR-0038).</param>
/// <param name="ConvertedAt">Wall-clock conversion instant (UTC).</param>
/// <param name="DaysIntoTrial">
/// Whole UTC days between <c>TrialStartedAt</c> and <c>ConvertedAt</c>
/// (zero when same calendar day, regardless of timezone offset). Used
/// by funnel analytics; never returned to clients.
/// </param>
/// <param name="ConvertedToTier">
/// Retail tier the parent converted into. Validated at command time to
/// be a retail tier (Basic/Plus/Premium) — never Unsubscribed and never
/// SchoolSku (school SKUs do not flow through trials per ADR-0057 §8).
/// </param>
/// <param name="BillingCycle">Monthly or Annual; never None on conversion.</param>
/// <param name="PaymentTransactionIdEncrypted">
/// Encrypted gateway transaction id (ADR-0038). Mirrors the value that
/// the subsequent <see cref="SubscriptionActivated_V1"/> carries; we
/// snapshot it here so the marker event is self-contained for analytics.
/// </param>
/// <param name="UtilizationAtConversion">
/// Utilisation snapshot at the moment of conversion — same shape as
/// <see cref="TrialExpired_V1.Utilization"/>.
/// </param>
public sealed record TrialConverted_V1(
    string ParentSubjectIdEncrypted,
    string PrimaryStudentSubjectIdEncrypted,
    DateTimeOffset ConvertedAt,
    int DaysIntoTrial,
    SubscriptionTier ConvertedToTier,
    BillingCycle BillingCycle,
    string PaymentTransactionIdEncrypted,
    TrialUtilization UtilizationAtConversion);
