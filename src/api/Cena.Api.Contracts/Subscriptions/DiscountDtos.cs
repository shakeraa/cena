// =============================================================================
// Cena Platform — Discount DTOs (per-user discount-codes feature)
//
// Wire-format records for the per-user discount-codes admin + student APIs.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>
/// Body of POST /api/admin/discounts (issue a personal discount).
/// </summary>
/// <param name="TargetEmail">
/// Recipient's email. Server normalises (lowercase + Gmail dot/+ strip).
/// </param>
/// <param name="DiscountKind">"PercentOff" | "AmountOff".</param>
/// <param name="DiscountValue">
/// Basis points (1..10_000) for PercentOff (e.g. 5_000 = 50%); agorot for
/// AmountOff (e.g. 5_000 = ₪50).
/// </param>
/// <param name="DurationMonths">1..36 — number of paid invoices the discount applies to.</param>
/// <param name="Reason">Free-text audit reason (required, ≤ 1024 chars).</param>
public sealed record DiscountIssueRequestDto(
    [property: JsonPropertyName("targetEmail")] string TargetEmail,
    [property: JsonPropertyName("discountKind")] string DiscountKind,
    [property: JsonPropertyName("discountValue")] int DiscountValue,
    [property: JsonPropertyName("durationMonths")] int DurationMonths,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// Response from POST /api/admin/discounts. Returns the assignment id and
/// the human-readable promotion code so the admin UI can show + the admin
/// can communicate the code out-of-band as a fallback.
/// </summary>
public sealed record DiscountIssueResponseDto(
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("targetEmailNormalized")] string TargetEmailNormalized,
    [property: JsonPropertyName("promotionCodeString")] string PromotionCodeString,
    [property: JsonPropertyName("status")] string Status);

/// <summary>One row in the admin discount list view.</summary>
public sealed record DiscountAssignmentDto(
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("targetEmailNormalized")] string TargetEmailNormalized,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("discountKind")] string DiscountKind,
    [property: JsonPropertyName("discountValue")] int DiscountValue,
    [property: JsonPropertyName("durationMonths")] int DurationMonths,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("issuedAt")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("redeemedAt")] DateTimeOffset? RedeemedAt,
    [property: JsonPropertyName("revokedAt")] DateTimeOffset? RevokedAt);

/// <summary>Response from GET /api/me/applicable-discount.</summary>
public sealed record ApplicableDiscountDto(
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("discountKind")] string DiscountKind,
    [property: JsonPropertyName("discountValue")] int DiscountValue,
    [property: JsonPropertyName("durationMonths")] int DurationMonths,
    [property: JsonPropertyName("status")] string Status);
