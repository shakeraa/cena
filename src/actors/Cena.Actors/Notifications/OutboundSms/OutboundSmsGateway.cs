// =============================================================================
// Cena Platform — Outbound SMS gateway (prr-018).
//
// This is the ONE place in the platform that may call ISmsSender.SendAsync
// for parent-nudge traffic. Every call site uses IOutboundSmsGateway; the
// architecture test (OutboundSmsPolicyArchitectureTests) keeps this
// invariant by regex-ratcheting the set of files that call ISmsSender or
// IWhatsAppSender directly.
//
// Flow per request:
//
//   1. Caller constructs OutboundSmsRequest with the raw body + parent TZ.
//   2. Gateway computes the salted phone hash for rate-limit keys + logs.
//   3. Gateway runs the IOutboundSmsPolicyChain.
//   4. On Allow  → dispatch via ISmsSender and emit cena_sms_sent_total.
//      On Block  → surface the block via SmsGatewayResult.Blocked + metric.
//      On Defer  → surface via SmsGatewayResult.Deferred + metric; the
//                  caller enqueues for the earliest-send time (the gateway
//                  does NOT own a delayed-send queue — separation of concerns).
//
// Phone-hashing: HMAC-SHA256 keyed by `Cena:Sms:PhoneHashSalt`. A constant
// salt is acceptable — the goal is log-safety (no plaintext numbers in logs),
// not cryptographic anonymity. The rate-limit keys also use this hash so
// two references to the same number produce the same Redis key.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Outbound SMS gateway — single entry point for every parent-nudge send.
/// </summary>
public interface IOutboundSmsGateway
{
    /// <summary>
    /// Evaluate the policy chain and, on Allow, dispatch via the underlying
    /// ISmsSender. Block and Defer outcomes are surfaced on the return value;
    /// the gateway does not throw on policy rejection.
    /// </summary>
    Task<SmsGatewayResult> SendAsync(
        OutboundSmsGatewayRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Compute the salted phone hash — exposed because some callers build the
    /// OutboundSmsRequest themselves (e.g. a dedicated ParentDigestDispatcher)
    /// and need the same hash for correlation in their own metrics.
    /// </summary>
    string HashPhone(string phoneE164);
}

/// <summary>
/// Public request shape for <see cref="IOutboundSmsGateway.SendAsync"/>.
/// Intentionally narrower than <see cref="OutboundSmsRequest"/>: the caller
/// supplies raw fields and the gateway computes the phone hash + normalises.
/// </summary>
public sealed record OutboundSmsGatewayRequest(
    string? InstituteId,
    string ParentPhoneE164,
    string ParentTimezone,
    string Body,
    string TemplateId,
    string CorrelationId,
    DateTimeOffset ScheduledForUtc);

/// <summary>
/// Terminal result of a gateway invocation.
/// </summary>
public abstract record SmsGatewayResult
{
    public abstract string OutcomeCode { get; }

    /// <summary>Dispatched to Twilio (or graceful-disabled fallback). </summary>
    public sealed record Sent(SmsSendResult VendorResult) : SmsGatewayResult
    {
        public override string OutcomeCode => VendorResult.Success ? "sent" : "vendor_error";
    }

    /// <summary>Policy chain rejected the message.</summary>
    public sealed record Blocked(string Policy, string Reason, string HumanMessage)
        : SmsGatewayResult
    {
        public override string OutcomeCode => $"blocked:{Reason}";
    }

    /// <summary>
    /// Policy chain deferred. Caller must enqueue for <see cref="EarliestSendAtUtc"/>.
    /// </summary>
    public sealed record Deferred(string Policy, string Reason, DateTimeOffset EarliestSendAtUtc)
        : SmsGatewayResult
    {
        public override string OutcomeCode => $"deferred:{Reason}";
    }
}

/// <summary>
/// Default gateway implementation.
/// </summary>
public sealed class OutboundSmsGateway : IOutboundSmsGateway
{
    private readonly IOutboundSmsPolicyChain _chain;
    private readonly ISmsSender _sender;
    private readonly ILogger<OutboundSmsGateway> _logger;
    private readonly Counter<long> _sentCounter;
    private readonly byte[] _salt;

    public OutboundSmsGateway(
        IOutboundSmsPolicyChain chain,
        ISmsSender sender,
        IConfiguration configuration,
        IMeterFactory meterFactory,
        ILogger<OutboundSmsGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _chain = chain;
        _sender = sender;
        _logger = logger;

        var saltString = configuration["Cena:Sms:PhoneHashSalt"]
                         ?? "cena-sms-default-salt-prr018";
        _salt = Encoding.UTF8.GetBytes(saltString);

        var meter = meterFactory.Create("Cena.Actors.OutboundSms.Gateway", "1.0.0");
        _sentCounter = meter.CreateCounter<long>(
            "cena_sms_dispatch_total",
            description:
                "Outbound SMS dispatched via gateway, labeled by institute_id and result (prr-018)");
    }

    public string HashPhone(string phoneE164)
    {
        ArgumentNullException.ThrowIfNull(phoneE164);
        // HMAC-SHA256(salt, phone) → hex-encoded, first 16 chars. 64 bits is
        // plenty for log identification and still extremely collision-resistant
        // within one institute's parent set (a school of ~10k parents has
        // collision probability < 2^-32).
        using var h = new HMACSHA256(_salt);
        var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(phoneE164.Trim()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public async Task<SmsGatewayResult> SendAsync(
        OutboundSmsGatewayRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instituteLabel = SmsSanitizerPolicy.NormalizeInstituteLabel(request.InstituteId);
        var phoneHash = HashPhone(request.ParentPhoneE164);

        var policyRequest = new OutboundSmsRequest(
            InstituteId: request.InstituteId,
            ParentPhoneE164: request.ParentPhoneE164,
            ParentPhoneHash: phoneHash,
            ParentTimezone: request.ParentTimezone,
            Body: request.Body,
            TemplateId: request.TemplateId,
            CorrelationId: request.CorrelationId,
            ScheduledForUtc: request.ScheduledForUtc);

        var decision = await _chain.EvaluateAsync(policyRequest, ct).ConfigureAwait(false);

        switch (decision.FinalOutcome)
        {
            case SmsPolicyOutcome.Block block:
                _sentCounter.Add(1,
                    new KeyValuePair<string, object?>("institute_id", instituteLabel),
                    new KeyValuePair<string, object?>("result", "blocked"),
                    new KeyValuePair<string, object?>("policy", decision.TerminatingPolicy ?? "unknown"));
                return new SmsGatewayResult.Blocked(
                    decision.TerminatingPolicy ?? "unknown",
                    block.Reason,
                    block.HumanMessage);

            case SmsPolicyOutcome.Defer defer:
                _sentCounter.Add(1,
                    new KeyValuePair<string, object?>("institute_id", instituteLabel),
                    new KeyValuePair<string, object?>("result", "deferred"),
                    new KeyValuePair<string, object?>("policy", decision.TerminatingPolicy ?? "unknown"));
                return new SmsGatewayResult.Deferred(
                    decision.TerminatingPolicy ?? "unknown",
                    defer.Reason,
                    defer.EarliestSendAtUtc);

            case SmsPolicyOutcome.Allow allow:
                var cleaned = allow.PossiblyRewritten;
                var vendor = await _sender
                    .SendAsync(cleaned.ParentPhoneE164, cleaned.Body, ct)
                    .ConfigureAwait(false);

                _sentCounter.Add(1,
                    new KeyValuePair<string, object?>("institute_id", instituteLabel),
                    new KeyValuePair<string, object?>("result", vendor.Success ? "sent" : "vendor_error"),
                    new KeyValuePair<string, object?>("policy", "allow"));

                _logger.LogInformation(
                    "[prr-018] SMS dispatched: success={Success} phone_hash={PhoneHash} correlation={Corr} institute={Institute}",
                    vendor.Success, phoneHash, request.CorrelationId, instituteLabel);
                return new SmsGatewayResult.Sent(vendor);

            default:
                throw new InvalidOperationException(
                    $"Unhandled policy outcome {decision.FinalOutcome.GetType().Name}");
        }
    }
}
