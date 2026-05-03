// =============================================================================
// Cena Platform — Checkout session DTOs (EPIC-PRR-I PRR-301)
// =============================================================================

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>Body of POST /api/me/subscription/checkout-session.</summary>
/// <param name="PrimaryStudentId">Encrypted primary student id.</param>
/// <param name="Tier">"Basic" | "Plus" | "Premium".</param>
/// <param name="BillingCycle">"Monthly" | "Annual".</param>
/// <param name="IdempotencyKey">
/// Client-chosen key (UUID recommended); replays with the same key return the
/// same checkout URL without creating a new session on the gateway.
/// </param>
public sealed record CheckoutSessionRequestDto(
    string PrimaryStudentId,
    string Tier,
    string BillingCycle,
    string IdempotencyKey);

/// <summary>Response: the gateway-hosted checkout URL + its session id.</summary>
public sealed record CheckoutSessionResponseDto(
    string CheckoutUrl,
    string SessionId,
    string ProviderName);
