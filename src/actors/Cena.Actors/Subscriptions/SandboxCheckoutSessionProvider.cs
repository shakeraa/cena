// =============================================================================
// Cena Platform — SandboxCheckoutSessionProvider (dev/test)
//
// Returns a deterministic fake checkout URL so the UI flow works end-to-end
// without hitting Stripe. Paired with a local test-only "success" button in
// the UI that POSTs back to /api/me/subscription/activate to simulate the
// webhook-driven activation.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>Deterministic checkout-session provider for dev/test. Do NOT register in prod.</summary>
public sealed class SandboxCheckoutSessionProvider : ICheckoutSessionProvider
{
    private readonly ConcurrentDictionary<string, CheckoutSessionResult> _cache = new();

    /// <inheritdoc/>
    public string Name => "sandbox";

    /// <inheritdoc/>
    public Task<CheckoutSessionResult> CreateSessionAsync(CheckoutSessionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(request));
        }

        // Deterministic replay via idempotency key.
        var result = _cache.GetOrAdd(request.IdempotencyKey, key => new CheckoutSessionResult(
            CheckoutUrl: $"https://sandbox.checkout.cena.test/session/{key}",
            SessionId: $"cs_sandbox_{key}"));
        return Task.FromResult(result);
    }
}
