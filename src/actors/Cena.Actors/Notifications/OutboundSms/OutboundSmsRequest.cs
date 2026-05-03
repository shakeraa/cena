// =============================================================================
// Cena Platform — Outbound SMS request + policy-chain contracts (prr-018).
//
// Every outbound SMS in Cena — parent nudges, admin ops alerts, anything else
// we add later — flows through a single IOutboundSmsPolicy chain BEFORE the
// vendor (Twilio, etc.) sees the body. The policy chain owns:
//
//   1. Sanitiser     — strip control/bidi, normalise whitespace, cap length,
//                      reject URLs outside the institute allowlist.
//   2. Ship-gate     — scan body for banned dark-pattern copy (chain-counter,
//                      urgency, loss-aversion) before wire-emit, mirroring
//                      the JS scanner at scripts/shipgate/scan.mjs.
//   3. Rate limit    — per-parent-phone cap, per-institute cap, global cap
//                      backed by Redis. Emits cena_sms_rate_limited_total
//                      with {institute_id, reason} labels.
//   4. Quiet hours   — defer (not drop) messages scheduled during the
//                      institute-configured quiet window in the PARENT's
//                      local timezone. Emits earliest-send-time.
//
// Every IOutboundSmsPolicy returns exactly one of three outcomes:
//
//   - Allow              — policy is satisfied, the chain continues
//   - Block(reason)      — terminal. The caller drops the message,
//                          surfaces the reason to logs + dead-letter.
//   - Defer(sendAtUtc)   — terminal. The caller enqueues the message for
//                          retry at or after the specified time (quiet-hours
//                          and rate-limit use this when a later retry is
//                          legitimate).
//
// WHY a chain vs inlining in the sender:
//   - Every new check (a GDPR data-class scanner, an emergency global kill
//     switch) would otherwise touch the sender. A chain lets us add policies
//     without regression-testing every call site.
//   - The architecture test at OutboundSmsPolicyArchitectureTests.cs asserts
//     every SMS-sending code path runs through this chain (regex ratchet with
//     a small allowlist). If somebody adds a new sender that calls
//     ISmsSender.SendAsync directly, the test breaks and the PR cannot merge.
//   - Policies are independently unit-testable with no Twilio mocking.
//
// WHY outcomes are records not exceptions:
//   - Block/Defer are the NORMAL path (quiet hours fires hundreds of times
//     per evening). Exceptions would make the metric-hot-path allocation-heavy
//     and obscure log noise.
// =============================================================================

using System.Collections.Generic;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Inputs the policy chain needs to classify an outbound SMS. No PII is logged
/// — the phone number is surfaced only to the vendor adapter and, for metrics,
/// via <see cref="ParentPhoneHash"/>.
/// </summary>
/// <param name="InstituteId">
/// Tenant label (ADR-0001). Null or blank is normalised to "unknown" on the
/// metric label; the authoritative tenant scope was already enforced upstream.
/// </param>
/// <param name="ParentPhoneE164">
/// The raw E.164 number (e.g. <c>+972541234567</c>). Never logged verbatim; the
/// gateway computes a salted hash for rate-limit keys + metric correlation.
/// </param>
/// <param name="ParentPhoneHash">
/// Salted SHA-256 hash of <see cref="ParentPhoneE164"/>, produced by the
/// gateway. Safe for log output ({Phone}=hash:abcd1234).
/// </param>
/// <param name="ParentTimezone">
/// IANA timezone ID of the PARENT's phone (e.g. <c>Asia/Jerusalem</c>). When
/// unknown the gateway falls back to the institute's timezone; when THAT is
/// unknown we use <c>Asia/Jerusalem</c> (Cena MVP home timezone).
///
/// WHY parent-TZ not institute-TZ: a parent can live in a different timezone
/// than the school (expat student's parent abroad, shared-custody parent in
/// another country). The 22:00 quiet-hours cutoff is about the RECIPIENT's
/// night — not the sender's business day.
/// </param>
/// <param name="Body">
/// Raw template-rendered body. The sanitiser rewrites this. Do not pre-trim
/// or pre-normalise at the call site — the chain is the single place that
/// owns content discipline.
/// </param>
/// <param name="TemplateId">
/// Stable identifier of the parent-nudge template (e.g. <c>weekly-digest-v1</c>).
/// The sanitiser only permits URLs from the institute allowlist (if the
/// template is allowed to include URLs at all — most are text-only).
/// </param>
/// <param name="CorrelationId">
/// Opaque upstream id used for idempotent retry + cross-service tracing.
/// Passed through to vendor "X-Correlation-Id" header by the gateway.
/// </param>
/// <param name="ScheduledForUtc">
/// When the caller intends to send. Quiet-hours policy compares this to the
/// parent's local time; if deferral is required, the returned Defer outcome
/// contains the earliest acceptable send time (still UTC on the wire).
/// </param>
public sealed record OutboundSmsRequest(
    string? InstituteId,
    string ParentPhoneE164,
    string ParentPhoneHash,
    string ParentTimezone,
    string Body,
    string TemplateId,
    string CorrelationId,
    DateTimeOffset ScheduledForUtc)
{
    /// <summary>
    /// Return a copy of this request with a new (usually sanitised) body.
    /// Policies use this to hand a cleaned body down the chain.
    /// </summary>
    public OutboundSmsRequest WithBody(string newBody) => this with { Body = newBody };
}

/// <summary>
/// Three-valued policy verdict. Allow continues the chain; Block and Defer are
/// terminal. No other states — "warn" is a log statement, not an outcome.
/// </summary>
public abstract record SmsPolicyOutcome
{
    /// <summary>Short, machine-readable reason code. Safe to put on a metric.</summary>
    public abstract string ReasonCode { get; }

    /// <summary>
    /// Policy approves. Chain continues. <see cref="PossiblyRewritten"/>
    /// carries any sanitiser rewrites (e.g., stripped control chars, truncated
    /// body) for the next policy in the chain to see.
    /// </summary>
    public sealed record Allow(OutboundSmsRequest PossiblyRewritten) : SmsPolicyOutcome
    {
        public override string ReasonCode => "allowed";
    }

    /// <summary>
    /// Terminal rejection. The message is dropped; caller should increment the
    /// dead-letter counter and emit a structured log with
    /// <paramref name="Reason"/>.
    /// </summary>
    public sealed record Block(string Reason, string HumanMessage) : SmsPolicyOutcome
    {
        public override string ReasonCode => Reason;
    }

    /// <summary>
    /// Terminal defer. The caller enqueues the message to a delayed-send queue
    /// (the queue mechanism is outside this module's scope — see
    /// ParentDigestDispatcher). <see cref="EarliestSendAtUtc"/> must be strictly
    /// in the future; the queue must not re-examine quiet-hours before that.
    /// </summary>
    public sealed record Defer(
        string Reason,
        DateTimeOffset EarliestSendAtUtc) : SmsPolicyOutcome
    {
        public override string ReasonCode => Reason;
    }
}

/// <summary>
/// One policy in the outbound SMS chain. Implementations must be pure /
/// side-effect-free with respect to the request — any state (Redis counters,
/// metrics) belongs inside the implementation, not in the request object.
/// </summary>
public interface IOutboundSmsPolicy
{
    /// <summary>
    /// Short name for diagnostics + logs. MUST be stable: appears in metric
    /// labels and architecture-test assertions.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execute the policy. Never throws for policy-level rejection — return
    /// <see cref="SmsPolicyOutcome.Block"/> or <see cref="SmsPolicyOutcome.Defer"/>
    /// instead. Cancellation and infrastructure failure (Redis down) are
    /// permitted to throw.
    /// </summary>
    Task<SmsPolicyOutcome> EvaluateAsync(
        OutboundSmsRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Result of running the full chain for a single request.
/// </summary>
/// <param name="FinalOutcome">
/// Whichever of <see cref="SmsPolicyOutcome.Allow"/> / <see cref="SmsPolicyOutcome.Block"/>
/// / <see cref="SmsPolicyOutcome.Defer"/> the chain produced. Allow is the
/// happy path; Block and Defer are terminal.
/// </param>
/// <param name="EvaluatedPolicies">
/// In-order list of policy names that ran, for structured logging. Policies
/// after a terminal outcome are NOT listed here.
/// </param>
/// <param name="TerminatingPolicy">
/// Name of the policy that produced a terminal outcome, or null when every
/// policy allowed. Used for the <c>policy=</c> label on metrics.
/// </param>
public sealed record OutboundSmsDecision(
    SmsPolicyOutcome FinalOutcome,
    IReadOnlyList<string> EvaluatedPolicies,
    string? TerminatingPolicy);
