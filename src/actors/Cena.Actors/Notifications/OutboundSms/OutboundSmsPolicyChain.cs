// =============================================================================
// Cena Platform — Outbound SMS policy chain (prr-018).
//
// Composes the sanitiser + ship-gate + rate-limit + quiet-hours policies into
// a single evaluation pipeline. The chain is ordered (see below); the first
// terminal outcome (Block or Defer) wins.
//
// Order rationale:
//
//   1. Sanitizer     — must run first so subsequent policies see the CLEANED
//                      body and accurate length metadata. Also it is the
//                      cheapest policy (no Redis).
//   2. Shipgate      — runs on the sanitised body. Catches dark-pattern copy
//                      before we burn rate-limit quota on something we'd
//                      refuse to send anyway.
//   3. Rate-limit    — Redis round-trips happen here. We only pay the cost if
//                      the message passed content checks.
//   4. Quiet-hours   — last. Deferring a message that would have been blocked
//                      anyway wastes the delayed-send queue. Also: we only
//                      record rate-limit consumption for sends we would
//                      actually dispatch if it weren't for quiet hours.
//
// Implementation note: the policy list is explicitly constructed at DI-wire
// time (not reflection-scanned) so an accidental registration reorder cannot
// change the policy priority silently.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Runs the ordered policy chain over a single SMS request.
/// </summary>
public interface IOutboundSmsPolicyChain
{
    Task<OutboundSmsDecision> EvaluateAsync(
        OutboundSmsRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Default implementation. Policies are supplied in explicit order — DI
/// wiring at <see cref="OutboundSmsServiceCollectionExtensions.AddOutboundSmsPolicy"/>
/// constructs them sanitiser → shipgate → rate-limit → quiet-hours.
/// </summary>
public sealed class OutboundSmsPolicyChain : IOutboundSmsPolicyChain
{
    private readonly IReadOnlyList<IOutboundSmsPolicy> _policies;
    private readonly ILogger<OutboundSmsPolicyChain> _logger;

    public OutboundSmsPolicyChain(
        IEnumerable<IOutboundSmsPolicy> policies,
        ILogger<OutboundSmsPolicyChain> logger)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(logger);

        _policies = policies.ToArray();
        _logger = logger;

        if (_policies.Count == 0)
        {
            // A chain with zero policies is a configuration bug. We fail at
            // construction time so the error is visible at startup, not at
            // first-send time.
            throw new InvalidOperationException(
                "OutboundSmsPolicyChain requires at least one policy — refusing to construct " +
                "an empty chain because that would bypass prr-018 and scripts/shipgate/scan.mjs.");
        }
    }

    public async Task<OutboundSmsDecision> EvaluateAsync(
        OutboundSmsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var evaluated = new List<string>(_policies.Count);
        var current = request;

        foreach (var policy in _policies)
        {
            evaluated.Add(policy.Name);
            var outcome = await policy.EvaluateAsync(current, ct).ConfigureAwait(false);

            switch (outcome)
            {
                case SmsPolicyOutcome.Allow allow:
                    current = allow.PossiblyRewritten;
                    continue;

                case SmsPolicyOutcome.Block:
                case SmsPolicyOutcome.Defer:
                    _logger.LogInformation(
                        "[prr-018] SMS policy chain terminated by {Policy} with outcome={Outcome} reason={Reason} correlation={Corr}",
                        policy.Name,
                        outcome.GetType().Name,
                        outcome.ReasonCode,
                        request.CorrelationId);
                    return new OutboundSmsDecision(outcome, evaluated, policy.Name);

                default:
                    throw new InvalidOperationException(
                        $"Unknown SmsPolicyOutcome subtype '{outcome.GetType().Name}' from policy '{policy.Name}'");
            }
        }

        // Every policy allowed — final outcome is the last Allow, which carries
        // any accumulated body rewrites.
        return new OutboundSmsDecision(new SmsPolicyOutcome.Allow(current), evaluated, null);
    }
}
