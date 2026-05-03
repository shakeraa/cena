// =============================================================================
// Cena Platform — Bank-transfer DTOs (EPIC-PRR-I PRR-304)
//
// Wire surface for:
//   POST /api/me/subscription/bank-transfer/reserve  (parent)
//   GET  /api/me/subscription/bank-transfer/{ref}    (parent — status check)
//   POST /api/admin/subscriptions/bank-transfer/{ref}/confirm (finance admin)
//   GET  /api/admin/subscriptions/bank-transfer/pending (finance admin)
//
// Amount is exposed in agorot (ADR-0057 §4) because that's what the DB
// stores; the UI formats it for display. Dates are UTC ISO-8601. No PII
// leaks — parent identity comes from the session claim, not the body.
// =============================================================================

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>
/// Body of POST /api/me/subscription/bank-transfer/reserve. Tier
/// restricted to retail tiers ("Basic" | "Plus" | "Premium") at service
/// layer; BillingCycle is implicitly Annual (bank-transfer is Annual-only
/// per PRR-304 scope).
/// </summary>
public sealed record BankTransferReserveRequest(
    string PrimaryStudentId,
    string Tier);   // "Basic" | "Plus" | "Premium"

/// <summary>
/// Response to a successful reservation. The bank-account details are
/// rendered by the endpoint from configuration — included here so the
/// parent sees everything they need to complete the transfer without a
/// second round-trip.
/// </summary>
public sealed record BankTransferReserveResponse(
    string ReferenceCode,
    long AmountAgorot,
    string Currency,               // always "ILS" for now
    string Tier,
    DateTimeOffset ExpiresAt,
    BankTransferPayeeDetailsDto PayeeDetails);

/// <summary>
/// Payee (Cena's) bank-account details. Populated from configuration;
/// endpoint rejects the request if any required field is missing so the
/// parent is never given a partially-usable code. Not a secret —
/// this is the account a parent is about to transfer money into.
/// </summary>
public sealed record BankTransferPayeeDetailsDto(
    string BankName,
    string BranchCode,
    string AccountNumber,
    string AccountHolder,
    string? Notes);

/// <summary>
/// Status of a reservation returned by GET /api/me/subscription/bank-transfer/{ref}
/// — parents poll this after initiating their bank transfer to see when
/// finance has marked their payment received.
/// </summary>
public sealed record BankTransferStatusResponse(
    string ReferenceCode,
    string Status,                         // "Pending" | "Confirmed" | "Expired"
    long AmountAgorot,
    string Tier,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ExpiredAt);

/// <summary>
/// Admin-only list item returned by GET /api/admin/subscriptions/bank-transfer/pending.
/// </summary>
public sealed record BankTransferPendingItemDto(
    string ReferenceCode,
    long AmountAgorot,
    string Tier,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
