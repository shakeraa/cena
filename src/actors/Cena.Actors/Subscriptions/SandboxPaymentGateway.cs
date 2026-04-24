// =============================================================================
// Cena Platform — SandboxPaymentGateway (EPIC-PRR-I PRR-301 dev/test)
//
// Production-grade deterministic sandbox gateway. NOT a stub — this is a
// real gateway implementation whose backing store is in-process memory,
// used for local dev and automated tests. Behavior:
//   - Always succeeds UNLESS the idempotency key starts with "fail-" (for
//     deterministic failure-path testing).
//   - Returns a stable transaction id = "sandbox:" + idempotency key, so
//     replays produce the same result (idempotency contract honored).
//   - Records every call in memory for observability tests.
//
// NEVER register this in Production. Composition root picks the right
// gateway based on environment (Stripe/Bit/PayBox in prod).
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Deterministic sandbox payment gateway. Intent with idempotency key
/// starting with "fail-" is rejected with a fixed reason string; all others
/// succeed. Stores every call for inspection in tests.
/// </summary>
public sealed class SandboxPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, PaymentResult> _results = new();
    private readonly ConcurrentQueue<PaymentIntent> _observedIntents = new();

    /// <inheritdoc/>
    public string Name => "sandbox";

    /// <summary>Inspect all intents this gateway has seen (dev/test only).</summary>
    public IReadOnlyCollection<PaymentIntent> ObservedIntents => _observedIntents;

    /// <inheritdoc/>
    public Task<PaymentResult> AuthorizeAsync(PaymentIntent intent, CancellationToken ct)
    {
        if (intent is null) throw new ArgumentNullException(nameof(intent));
        if (string.IsNullOrWhiteSpace(intent.IdempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key is required on every PaymentIntent.", nameof(intent));
        }

        _observedIntents.Enqueue(intent);

        var result = _results.GetOrAdd(intent.IdempotencyKey, key =>
            key.StartsWith("fail-", StringComparison.Ordinal)
                ? PaymentResult.Failure($"sandbox rejected intent (key={key})")
                : PaymentResult.Success($"sandbox:{key}"));

        return Task.FromResult(result);
    }
}
