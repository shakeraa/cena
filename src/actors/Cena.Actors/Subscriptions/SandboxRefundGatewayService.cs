// =============================================================================
// Cena Platform — SandboxRefundGatewayService (EPIC-PRR-I PRR-306 dev/test)
//
// Deterministic in-process refund gateway for local development and
// automated tests. Mirrors SandboxPaymentGateway's "fail-" idempotency
// prefix convention so failure paths can be exercised without flakes.
// NEVER registered in Production composition; Stripe's adapter is bound
// instead when Stripe is configured.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Deterministic sandbox refund gateway: idempotency keys prefixed with
/// <c>"fail-"</c> return a failure; all others succeed with a stable
/// <c>sandbox-refund:{key}</c> id. Every call is recorded for inspection.
/// </summary>
public sealed class SandboxRefundGatewayService : IRefundGatewayService
{
    private readonly ConcurrentDictionary<string, RefundGatewayResult> _results = new();
    private readonly ConcurrentQueue<RefundIntent> _observedIntents = new();

    /// <inheritdoc />
    public string Name => "sandbox";

    /// <summary>All refund intents this gateway has processed (dev/test only).</summary>
    public IReadOnlyCollection<RefundIntent> ObservedIntents => _observedIntents;

    /// <inheritdoc />
    public Task<RefundGatewayResult> RefundAsync(RefundIntent intent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.IdempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key is required on every RefundIntent.",
                nameof(intent));
        }

        _observedIntents.Enqueue(intent);

        var result = _results.GetOrAdd(intent.IdempotencyKey, key =>
            key.StartsWith("fail-", StringComparison.Ordinal)
                ? RefundGatewayResult.Failure($"sandbox rejected refund (key={key})")
                : RefundGatewayResult.Success($"sandbox-refund:{key}"));

        return Task.FromResult(result);
    }
}
