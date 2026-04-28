// =============================================================================
// Cena Platform — DiscountIssued_V1 (per-user discount-codes feature)
//
// Emitted when a super-admin issues a personal discount to a target email.
// First event in the stream `discount-{assignmentId}`.
//
// PII handling:
//   - TargetEmailNormalized is the canonical lower-case Gmail-folded form
//     (per EmailNormalizer). It is used for lookup at student-side. Storage
//     of the original casing/format isn't needed — admins type the address
//     fresh each time and the normaliser is deterministic.
//   - StripeCouponId / StripePromotionCodeId are external-gateway identifiers,
//     not free-standing PII; treated as PII-adjacent and not surfaced to
//     unauthenticated callers.
//   - IssuedByAdminSubjectIdEncrypted carries the admin's encrypted subject
//     id for audit trail.
//
// Discount semantics:
//   - DiscountKind: PercentOff or AmountOff
//   - DiscountValue: basis points (1..10_000) for PercentOff;
//                    agorot (>0, ≤ tier annual price) for AmountOff
//   - DurationMonths: 1..36, mapped onto Stripe Coupon `duration: repeating`
//     with `duration_in_months: N` so Stripe natively reverts to full price
//     after N invoices.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>Discount kind.</summary>
public enum DiscountKind
{
    /// <summary>Percent-off, value in basis points (1..10000 = 0.01%..100%).</summary>
    PercentOff = 1,

    /// <summary>Amount-off in agorot (smallest currency unit).</summary>
    AmountOff = 2,
}

/// <summary>
/// Emitted when a super-admin issues a personal discount to a target email.
/// </summary>
/// <param name="AssignmentId">
/// Stable id of the discount assignment. Drives the event stream key
/// (<c>discount-{AssignmentId}</c>) and the Stripe Promotion Code metadata.
/// </param>
/// <param name="TargetEmailNormalized">
/// Lower-cased, Gmail-folded canonical form of the recipient email
/// (per <see cref="EmailNormalizer.Normalize"/>). Used for student-side
/// lookup at /api/me/applicable-discount.
/// </param>
/// <param name="DiscountKind">PercentOff or AmountOff.</param>
/// <param name="DiscountValue">
/// Basis points for PercentOff (1..10000); agorot for AmountOff (&gt;0, ≤ tier
/// annual price). Validation happens in <c>DiscountAssignmentCommands.Issue</c>.
/// </param>
/// <param name="DurationMonths">
/// Number of paid invoices the discount applies to. 1..36. Stripe natively
/// reverts to full price after N invoices when <c>duration: repeating</c>.
/// </param>
/// <param name="IssuedByAdminSubjectIdEncrypted">Encrypted admin subject id (audit).</param>
/// <param name="Reason">Free-text audit reason (admin-supplied; non-empty).</param>
/// <param name="StripeCouponId">
/// Stripe Coupon id created server-side. Empty for non-Stripe (in-memory)
/// providers in dev/test composition.
/// </param>
/// <param name="StripePromotionCodeId">
/// Stripe Promotion Code id (server-readable handle); the human-readable
/// promotion code string is delivered separately to the admin in the API
/// response, not stored on the event.
/// </param>
/// <param name="IssuedAt">UTC timestamp of issuance.</param>
public sealed record DiscountIssued_V1(
    string AssignmentId,
    string TargetEmailNormalized,
    DiscountKind DiscountKind,
    int DiscountValue,
    int DurationMonths,
    string IssuedByAdminSubjectIdEncrypted,
    string Reason,
    string StripeCouponId,
    string StripePromotionCodeId,
    DateTimeOffset IssuedAt);
