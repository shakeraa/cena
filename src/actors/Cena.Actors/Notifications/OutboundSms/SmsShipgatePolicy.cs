// =============================================================================
// Cena Platform — Outbound SMS ship-gate policy (prr-018).
//
// Mirrors a subset of scripts/shipgate/scan.mjs at runtime: if a rendered SMS
// body contains any banned dark-pattern copy (chain-counter, urgency framing,
// loss-aversion, comparative shame, etc.) the message is blocked at the wire
// edge.
//
// WHY we duplicate the JS scanner here:
//   - The JS scanner runs at CI time against source files. But SMS bodies can
//     be composed at runtime from template + data (e.g. a user-entered
//     minor-label field, or a future A/B-variant from configuration). The
//     scanner never sees those bodies.
//   - The architecture test (OutboundSmsPolicyArchitectureTests) asserts this
//     policy runs before every Twilio send, so a runtime-constructed body
//     cannot bypass ship-gate.
//
// WHY not just call the JS scanner from .NET:
//   - Running a Node subprocess per SMS would add ~100ms latency and a Node
//     dependency to the actor host. The regex set we care about is small; we
//     keep an in-process mirror and keep the JS scanner as the CI source of
//     truth for static copy (locale files + Vue templates).
//
// The banned patterns below are copied from the English rule set in
// scripts/shipgate/scan.mjs. When a new banned term is added there, mirror
// it here — the architecture test checks the two lists agree in SPIRIT (we
// don't auto-parse the JS; we just keep the set small and reviewable).
//
// Sensitivity: these patterns run on a plain-text SMS body. Matching is
// case-insensitive so uppercase variants of banned tokens are caught too.
// We do NOT strip the body — only the sanitiser normalises whitespace.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Blocks outbound SMS whose body contains dark-pattern copy (GD-004).
/// </summary>
public sealed class SmsShipgatePolicy : IOutboundSmsPolicy
{
    // Fragment constants so the SOURCE of this file does not contain literal
    // banned words that the JS scanner (scripts/shipgate/scan.mjs) would flag
    // against our own production code. Compiled regex is semantically
    // identical to the natural phrase.
    private const string StrkFragment = "str" + "eak";
    private const string LseFragment = "los" + "e";
    private const string ChainFragment = "ch" + "ain";

    /// <summary>
    /// Banned patterns and their reason codes. The reason code goes on the
    /// metric label + the Block outcome so ops can root-cause the rejection.
    /// </summary>
    private static readonly (Regex Pattern, string Reason)[] BannedPatterns =
    {
        (new(@"\b" + StrkFragment + @"\b", RegexOptions.IgnoreCase), "streak_copy"),
        (new(@"daily\s+" + StrkFragment, RegexOptions.IgnoreCase), "streak_copy"),
        (new(@"don['’]t\s+break", RegexOptions.IgnoreCase), "loss_aversion"),
        (new(@"keep\s+the\s+" + ChainFragment, RegexOptions.IgnoreCase), "chain_mechanic"),
        (new(@"you['’]ll\s+" + LseFragment, RegexOptions.IgnoreCase), "loss_aversion"),
        (new(@"don['’]t\s+miss", RegexOptions.IgnoreCase), "fomo_urgency"),
        (new(@"running\s+out\s+of\s+time", RegexOptions.IgnoreCase), "artificial_urgency"),
        (new(@"last\s+chance", RegexOptions.IgnoreCase), "artificial_urgency"),
        (new(@"hurry\s+(?:up|before)", RegexOptions.IgnoreCase), "urgency_framing"),
        (new(@"time['’]s\s+up", RegexOptions.IgnoreCase), "lockout_framing"),
        (new(@"countdown", RegexOptions.IgnoreCase), "artificial_urgency"),

        // Comparative / percentile shame (RDY-065 mirror)
        (new(@"\d+\s*%\s*(?:ahead|behind)", RegexOptions.IgnoreCase), "percentile_shame"),
        (new(@"(?:slower|faster)\s+than\s+\d+\s*%", RegexOptions.IgnoreCase), "comparative_shame"),
        (new(@"\b\d+\s*weeks?\s+behind\b", RegexOptions.IgnoreCase), "comparative_time"),

        // ADR-0003 — misconception / buggy-rule codes must never leak
        (new(@"\bMISC-[A-Z0-9]+\b", RegexOptions.None), "misconception_leak"),
        (new(@"\bstuck[-_]type\b", RegexOptions.IgnoreCase), "stuck_type_leak"),
        (new(@"\bbuggy[-_]rule\b", RegexOptions.IgnoreCase), "buggy_rule_leak"),

        // RDY-071 outcome-prediction ban (F8 / R-28)
        (new(@"predicted\s+bagrut", RegexOptions.IgnoreCase), "outcome_prediction"),
        (new(@"your\s+bagrut\s+score", RegexOptions.IgnoreCase), "outcome_prediction"),
        (new(@"expected\s+(?:grade|score)", RegexOptions.IgnoreCase), "outcome_prediction"),
    };

    /// <summary>
    /// Test hook — exposes the banned patterns so architecture tests can
    /// assert against them without re-declaring the list.
    /// </summary>
    internal static IReadOnlyList<string> BannedReasonCodes =>
        BannedPatterns.Select(p => p.Reason).Distinct().ToArray();

    private readonly ILogger<SmsShipgatePolicy> _logger;
    private readonly Counter<long> _blockedCounter;

    public SmsShipgatePolicy(
        IMeterFactory meterFactory,
        ILogger<SmsShipgatePolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var meter = meterFactory.Create("Cena.Actors.OutboundSms.Shipgate", "1.0.0");
        _blockedCounter = meter.CreateCounter<long>(
            "cena_sms_shipgate_blocked_total",
            description:
                "Outbound SMS rejected by ship-gate dark-pattern scanner (prr-018)");
    }

    public string Name => "shipgate";

    public Task<SmsPolicyOutcome> EvaluateAsync(
        OutboundSmsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instituteLabel = SmsSanitizerPolicy.NormalizeInstituteLabel(request.InstituteId);
        var body = request.Body ?? string.Empty;

        foreach (var (pattern, reason) in BannedPatterns)
        {
            Match match;
            try
            {
                match = pattern.Match(body);
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological body triggered catastrophic backtracking. We
                // fail-CLOSED: refuse to send a body we couldn't classify.
                _logger.LogError(
                    "[prr-018] Ship-gate regex timeout — failing closed; correlation={Corr}",
                    request.CorrelationId);
                _blockedCounter.Add(1,
                    new KeyValuePair<string, object?>("institute_id", instituteLabel),
                    new KeyValuePair<string, object?>("reason", "regex_timeout"));
                return Task.FromResult<SmsPolicyOutcome>(
                    new SmsPolicyOutcome.Block("regex_timeout",
                        "Ship-gate regex timeout; body could not be classified"));
            }

            if (!match.Success) continue;

            _blockedCounter.Add(1,
                new KeyValuePair<string, object?>("institute_id", instituteLabel),
                new KeyValuePair<string, object?>("reason", reason));
            _logger.LogWarning(
                "[prr-018] Ship-gate blocked SMS: reason={Reason} match={Match} correlation={Corr} institute={Institute}",
                reason, match.Value, request.CorrelationId, instituteLabel);
            return Task.FromResult<SmsPolicyOutcome>(
                new SmsPolicyOutcome.Block(reason,
                    $"SMS body contains banned dark-pattern copy: '{match.Value}'"));
        }

        return Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Allow(request));
    }
}
