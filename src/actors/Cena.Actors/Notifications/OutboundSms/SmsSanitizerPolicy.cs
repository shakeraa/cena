// =============================================================================
// Cena Platform — Outbound SMS sanitizer policy (prr-018).
//
// First link in the policy chain. Responsible for:
//
//   1. Stripping C0/C1 control characters, Unicode bidi overrides, and Cf
//      format characters (SmsEncodingRules.StripControlAndBidi).
//   2. Normalising whitespace (collapse horizontal runs, trim ends).
//   3. Classifying the encoding (GSM-7 vs UCS-2) and enforcing the
//      single-segment length cap for the resulting encoding.
//   4. Rejecting URLs whose host/eTLD+1 is not in the institute's allowlist.
//
// This policy NEVER silently truncates. If the body is too long after cleanup
// it returns Block("body_too_long"). The author is expected to pre-shorten.
//
// WHY reject unknown URLs rather than strip them:
//   - Stripping a URL from an SMS changes meaning. A parent who receives
//     "Open the portal: " with no link is more confused than one who receives
//     nothing. Fail loudly — the author has to author a legal URL.
//
// WHY institute-allowlist instead of a global allowlist:
//   - Each institute may use its own custom domain (school.edu.il,
//     ort-haifa.org.il). The allowlist is a configuration list under
//     Cena:Sms:UrlAllowlist with support for per-institute overrides.
//   - Empty allowlist == "no URLs allowed". Default stance for MVP.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Options bound from the <c>Cena:Sms:Sanitizer</c> configuration section.
/// Empty defaults are the safe stance: no URLs allowed anywhere.
/// </summary>
public sealed class SmsSanitizerOptions
{
    public const string SectionName = "Cena:Sms:Sanitizer";

    /// <summary>
    /// Global URL host allowlist (lowercase, no scheme, no trailing slash).
    /// Example: <c>["cena.app", "parent.cena.app"]</c>. A blank list = no URLs
    /// allowed, which is the MVP default for parent-nudge traffic.
    /// </summary>
    public List<string> GlobalUrlAllowlist { get; set; } = new();

    /// <summary>
    /// Per-institute URL allowlist override. The effective list for a given
    /// message is <c>GlobalUrlAllowlist ∪ InstituteUrlAllowlist[instituteId]</c>.
    /// A missing institute key falls back to the global list.
    /// </summary>
    public Dictionary<string, List<string>> InstituteUrlAllowlist { get; set; } = new();

    /// <summary>
    /// Maximum accepted measured length in the detected encoding. Defaults to
    /// the single-segment cap (160 GSM-7 / 70 UCS-2). Setting this to a lower
    /// number gives you headroom for a future "Reply STOP to opt out" footer
    /// appended by the gateway.
    /// </summary>
    public int? MaxMeasuredLengthOverride { get; set; }
}

/// <summary>
/// Sanitizer policy — rewrites the body, classifies encoding, enforces the
/// length cap, and rejects URLs outside the allowlist.
/// </summary>
public sealed class SmsSanitizerPolicy : IOutboundSmsPolicy
{
    // Greedy but non-catastrophic URL matcher. Matches http/https scheme OR
    // domain-ish tokens ("www.example.com", "example.com/path"). Timeout is
    // enforced via RegexOptions.NonBacktracking where available; .NET 9 has it
    // enabled by default on simple patterns like this.
    private static readonly Regex UrlPattern = new(
        @"(?i)\b((?:https?://)?(?:[a-z0-9\-]+\.)+[a-z]{2,}(?:/[^\s]*)?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(100));

    private readonly SmsSanitizerOptions _options;
    private readonly ILogger<SmsSanitizerPolicy> _logger;
    private readonly Counter<long> _blockedCounter;

    public SmsSanitizerPolicy(
        IOptions<SmsSanitizerOptions> options,
        IMeterFactory meterFactory,
        ILogger<SmsSanitizerPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        var meter = meterFactory.Create("Cena.Actors.OutboundSms.Sanitizer", "1.0.0");
        _blockedCounter = meter.CreateCounter<long>(
            "cena_sms_sanitizer_blocked_total",
            description:
                "Outbound SMS rejected by sanitiser, labeled by institute_id and reason (prr-018)");
    }

    public string Name => "sanitizer";

    public Task<SmsPolicyOutcome> EvaluateAsync(
        OutboundSmsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instituteLabel = NormalizeInstituteLabel(request.InstituteId);

        // 1. Strip control/bidi and normalise whitespace.
        var cleaned = SmsEncodingRules.StripControlAndBidi(request.Body ?? string.Empty);
        cleaned = SmsEncodingRules.NormalizeWhitespace(cleaned);

        if (string.IsNullOrEmpty(cleaned))
        {
            return Block(instituteLabel, "empty_body",
                "SMS body is empty after sanitisation",
                request.CorrelationId);
        }

        // 2. Classify encoding + measure wire length.
        var encoding = SmsEncodingRules.Classify(cleaned);
        var measured = SmsEncodingRules.MeasuredLength(cleaned, encoding);
        var limit = _options.MaxMeasuredLengthOverride
                    ?? SmsEncodingRules.SingleSegmentCap(encoding);

        if (measured > limit)
        {
            return Block(instituteLabel, "body_too_long",
                $"SMS body {measured} units exceeds single-segment cap of {limit} for {encoding}",
                request.CorrelationId);
        }

        // 3. URL allowlist check. We match URLs against the effective allowlist
        //    for this institute; if any URL is not on the list, reject the
        //    whole message rather than silently strip.
        var effectiveAllowlist = BuildEffectiveAllowlist(request.InstituteId);
        foreach (Match m in UrlPattern.Matches(cleaned))
        {
            var host = ExtractHost(m.Value);
            if (host is null) continue; // false positive; conservative
            if (!IsHostAllowed(host, effectiveAllowlist))
            {
                return Block(instituteLabel, "url_not_allowlisted",
                    $"URL host '{host}' is not on the institute URL allowlist",
                    request.CorrelationId);
            }
        }

        // 4. All clear — hand the cleaned body forward.
        var rewritten = request.WithBody(cleaned);
        return Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Allow(rewritten));
    }

    private Task<SmsPolicyOutcome> Block(
        string instituteLabel,
        string reason,
        string humanMessage,
        string correlationId)
    {
        _blockedCounter.Add(1,
            new KeyValuePair<string, object?>("institute_id", instituteLabel),
            new KeyValuePair<string, object?>("reason", reason));
        _logger.LogWarning(
            "[prr-018] SMS sanitizer blocked message: reason={Reason} correlation={Corr} institute={Institute}",
            reason, correlationId, instituteLabel);
        return Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Block(reason, humanMessage));
    }

    private HashSet<string> BuildEffectiveAllowlist(string? instituteId)
    {
        // The allowlist is compared against extracted hosts, which we lowercase.
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in _options.GlobalUrlAllowlist ?? new()) set.Add(h.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(instituteId)
            && _options.InstituteUrlAllowlist is { } map
            && map.TryGetValue(instituteId!, out var perInst))
        {
            foreach (var h in perInst ?? new()) set.Add(h.ToLowerInvariant());
        }

        return set;
    }

    private static bool IsHostAllowed(string host, HashSet<string> allowlist)
    {
        if (allowlist.Contains(host)) return true;
        // Allow a parent domain entry to cover subdomains ("cena.app" allows
        // "parent.cena.app"). We don't use a full public-suffix list — the
        // allowlist author is expected to be explicit.
        foreach (var entry in allowlist)
        {
            if (host.EndsWith("." + entry, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static string? ExtractHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var s = url.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
        {
            // Scheme-less: prepend http so Uri can parse.
            s = "http://" + s;
        }
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;
        return uri.Host.ToLowerInvariant();
    }

    internal static string NormalizeInstituteLabel(string? instituteId)
    {
        if (string.IsNullOrWhiteSpace(instituteId)) return "unknown";
        return instituteId.Trim();
    }
}
