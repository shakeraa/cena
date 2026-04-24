// =============================================================================
// Cena Platform — RefundService (EPIC-PRR-I PRR-306)
//
// Application-layer orchestration for parent-initiated refund requests.
// Composes:
//   - ISubscriptionAggregateStore (load + append)
//   - IRefundUsageProbe (abuse-rule inputs)
//   - RefundPolicy (pure eligibility + pro-rata)
//   - IRefundGatewayService (Stripe/Sandbox refund API call)
//   - IOriginalChargeLookup (finds the gateway txn id + gross for the
//     current cycle's most-recent charge)
//
// Every step has a single responsibility; failures at any stage surface
// as RefundOutcome with a stable code so the endpoint returns the same
// shape regardless of which layer rejected.
//
// Why not in the endpoint directly: endpoints should be thin HTTP
// adapters. Putting the multi-step orchestration (probe → policy →
// gateway → event emit) in its own service gives us a seam the
// subscription-cancellation worker can reuse when it auto-refunds
// customer.subscription.deleted flows that arrive inside the window.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Subscriptions;

/// <summary>Outcome record returned from <see cref="RefundService.RequestRefundAsync"/>.</summary>
/// <param name="Succeeded">True only when gateway + aggregate append both succeeded.</param>
/// <param name="RefundAmountAgorot">Amount credited (0 on denial / failure).</param>
/// <param name="DenialReason">Policy-layer denial code (set for policy denials; null otherwise).</param>
/// <param name="GatewayFailureReason">Gateway-layer failure (set when gateway refused).</param>
public sealed record RefundOutcome(
    bool Succeeded,
    long RefundAmountAgorot,
    string? DenialReason,
    string? GatewayFailureReason)
{
    /// <summary>Success shorthand with the refunded amount.</summary>
    public static RefundOutcome Success(long amount) =>
        new(true, amount, null, null);

    /// <summary>Policy denial (never reached the gateway).</summary>
    public static RefundOutcome PolicyDenied(string reason) =>
        new(false, 0, reason, null);

    /// <summary>Gateway refused (after policy approved).</summary>
    public static RefundOutcome GatewayDenied(string reason) =>
        new(false, 0, null, reason);
}

/// <summary>
/// Looks up the canonical gateway txn id and gross charge for the
/// subscription's current cycle so <see cref="RefundService"/> can issue
/// a refund intent without coupling to the checkout path that created it.
/// </summary>
public interface IOriginalChargeLookup
{
    /// <summary>
    /// Return the original charge's gateway txn id and gross agorot
    /// amount, or null if no charge exists (e.g., test-activation that
    /// never touched a gateway). Implementations typically walk the
    /// aggregate's event stream for the most-recent SubscriptionActivated
    /// or RenewalProcessed event and extract the fields from there.
    /// </summary>
    Task<OriginalCharge?> LookupAsync(string parentSubjectIdEncrypted, CancellationToken ct);
}

/// <summary>Original-charge tuple returned by <see cref="IOriginalChargeLookup"/>.</summary>
public sealed record OriginalCharge(
    string PaymentTransactionIdEncrypted,
    long GrossAgorot);

/// <summary>
/// Parent-initiated refund orchestration. One method (RequestRefundAsync)
/// threads load → probe → policy → gateway → event emit.
/// </summary>
public sealed class RefundService
{
    private readonly ISubscriptionAggregateStore _store;
    private readonly IRefundUsageProbe _usageProbe;
    private readonly IRefundGatewayService _gateway;
    private readonly IOriginalChargeLookup _chargeLookup;
    private readonly TimeProvider _clock;
    private readonly ILogger<RefundService> _logger;
    private readonly RefundPolicyOptions _options;

    /// <summary>Construct with all dependencies.</summary>
    public RefundService(
        ISubscriptionAggregateStore store,
        IRefundUsageProbe usageProbe,
        IRefundGatewayService gateway,
        IOriginalChargeLookup chargeLookup,
        TimeProvider clock,
        ILogger<RefundService> logger,
        RefundPolicyOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _usageProbe = usageProbe ?? throw new ArgumentNullException(nameof(usageProbe));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _chargeLookup = chargeLookup ?? throw new ArgumentNullException(nameof(chargeLookup));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? RefundPolicyOptions.Default;
    }

    /// <summary>
    /// Request a refund for the parent's current subscription. Evaluates
    /// policy, calls the gateway, emits SubscriptionRefunded_V1 on success.
    /// </summary>
    public async Task<RefundOutcome> RequestRefundAsync(
        string parentSubjectIdEncrypted, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            return RefundOutcome.PolicyDenied("missing_parent_id");
        }
        reason ??= "requested_by_customer";

        var aggregate = await _store
            .LoadAsync(parentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);

        // Resolve the gateway txn id + gross from the original charge
        // so the policy has the correct charge amount for pro-rata.
        var originalCharge = await _chargeLookup
            .LookupAsync(parentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);
        if (originalCharge is null)
        {
            return RefundOutcome.PolicyDenied("no_original_charge");
        }

        // Probe usage across the window for the abuse rule. Window starts
        // at activation; probe end-clock is `now`.
        var now = _clock.GetUtcNow();
        var windowStart = aggregate.State.ActivatedAt
            ?? throw new InvalidOperationException(
                "Original charge exists but aggregate.ActivatedAt is null; aggregate inconsistency.");
        var usage = await _usageProbe
            .GetAsync(aggregate.State.LinkedStudents, windowStart, now, ct)
            .ConfigureAwait(false);

        // Apply policy. Pure function; no I/O.
        var decision = RefundPolicy.Evaluate(
            aggregate.State, now, originalCharge.GrossAgorot,
            usage.DiagnosticUploads, usage.HintRequests, _options);
        if (!decision.Allowed)
        {
            _logger.LogInformation(
                "[PRR-306] RefundDenied parent={ParentHashPrefix} reason={Reason} "
                + "uploads={Uploads} hints={Hints}",
                HashPrefix(parentSubjectIdEncrypted),
                decision.DenialReason,
                usage.DiagnosticUploads, usage.HintRequests);
            return RefundOutcome.PolicyDenied(decision.DenialReason!);
        }

        // Idempotency key: stable across provider retries for the same
        // refund request. Parent-scoped + activation-scoped so a second
        // refund attempt on the same cycle deduplicates.
        var idemKey = "refund-"
            + parentSubjectIdEncrypted
            + "-"
            + windowStart.ToUnixTimeSeconds();

        var gatewayResult = await _gateway
            .RefundAsync(new RefundIntent(
                ParentSubjectIdEncrypted: parentSubjectIdEncrypted,
                OriginalPaymentTransactionIdEncrypted: originalCharge.PaymentTransactionIdEncrypted,
                Amount: Money.FromAgorot(decision.RefundAmountAgorot),
                IdempotencyKey: idemKey,
                Reason: reason), ct)
            .ConfigureAwait(false);
        if (!gatewayResult.Succeeded)
        {
            _logger.LogWarning(
                "[PRR-306] RefundGatewayDenied parent={ParentHashPrefix} "
                + "gateway={Gateway} reason={Reason}",
                HashPrefix(parentSubjectIdEncrypted),
                _gateway.Name,
                gatewayResult.FailureReason);
            return RefundOutcome.GatewayDenied(
                gatewayResult.FailureReason ?? "gateway_failure");
        }

        // Emit the aggregate event. SubscriptionCommands.Refund enforces
        // the window boundary redundantly — belt-and-braces against races
        // between probe and append.
        var evt = SubscriptionCommands.Refund(
            aggregate.State, decision.RefundAmountAgorot, reason, now);
        await _store
            .AppendAsync(parentSubjectIdEncrypted, evt, ct)
            .ConfigureAwait(false);
        aggregate.Apply(evt);

        _logger.LogInformation(
            "[PRR-306] RefundSucceeded parent={ParentHashPrefix} "
            + "amount={AmountAgorot} gateway={Gateway} txn={GatewayTxn}",
            HashPrefix(parentSubjectIdEncrypted),
            decision.RefundAmountAgorot,
            _gateway.Name,
            gatewayResult.RefundTransactionId);
        return RefundOutcome.Success(decision.RefundAmountAgorot);
    }

    private static string HashPrefix(string raw) =>
        raw.Length <= 8 ? raw : raw[..8];
}
