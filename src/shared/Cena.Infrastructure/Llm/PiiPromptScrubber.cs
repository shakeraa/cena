// =============================================================================
// Cena Platform — PII Prompt Scrubber (ADR-0046, prr-022)
//
// Defence-in-depth for the "no PII in LLM prompts" rule. The first line of
// defence is structured placeholders (see ADR-0046 Decision 2) — template
// authors never interpolate a raw student/parent field; the CI + xUnit
// architecture ratchet fails a build that does. This scrubber is the runtime
// backstop: it scans the outgoing prompt string, redacts residual free-text
// PII (email / phone / government-id / address / postal-code patterns) and
// increments a fail-closed metric so a non-zero reading is a real
// severity-1 signal.
//
// Design notes (ADR-0046 Decision 4 — "fail-closed on counter increment"):
//
//   Callers are expected to *refuse the LLM call* when RedactionCount > 0.
//   The scrubber does not itself refuse — it returns the scrubbed text and
//   the counter of how many patterns fired. Callers check, log, emit, and
//   fall back. The contract is intentional: the scrubber is a pure function
//   (easy to test and reason about); the policy is the caller's.
//
//   The `cena_llm_prompt_pii_scrubbed_total` counter is emitted here
//   (not in each caller) so the observability surface is consistent across
//   all [TaskRouting] seams and cannot be forgotten by an individual service.
//
// Why regex at all, given the structured-placeholder rule?
//   Student free-text input cannot be banned — only scrubbed. A student who
//   pastes their parent's email into a chat turn is input we will receive
//   regardless of how disciplined our template source is. The scrubber
//   governs that class of input (the part we do not control) — structured
//   placeholders govern the part we do.
//
// Pattern sources are modelled after TutorPromptScrubber (FIND-privacy-008)
// but intentionally do NOT carry the per-student context — this is the
// generic baseline. Tutor's per-student enricher layers on top of this
// scrubber via [PiiPreScrubbed("...")] upstream declaration.
//
// See docs/adr/0046-no-pii-in-llm-prompts.md.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Result of a single scrub pass over an outgoing LLM prompt string.
/// </summary>
/// <param name="ScrubbedText">
/// The (possibly mutated) prompt. If <see cref="RedactionCount"/> is zero,
/// this is reference-equal to the input.
/// </param>
/// <param name="RedactionCount">
/// Total number of PII patterns that matched and were replaced. Any
/// non-zero value is a severity-1 signal per ADR-0046 — callers must
/// refuse the LLM call and fall back to their tier's static path.
/// </param>
/// <param name="Categories">
/// Ordered list of categories that fired (e.g. "email", "phone",
/// "government_id"). Used for the Prometheus label and the logged
/// warning — never the raw matched text.
/// </param>
public sealed record PiiScrubResult(
    string ScrubbedText,
    int RedactionCount,
    IReadOnlyList<string> Categories);

/// <summary>
/// Scrubs residual PII from an outgoing LLM prompt string. Every
/// [TaskRouting]-tagged service invokes this on its user-prompt immediately
/// before the LLM client call, UNLESS it carries [PiiPreScrubbed(reason)].
/// The fail-closed policy lives in the caller (see file header note).
/// </summary>
public interface IPiiPromptScrubber
{
    /// <summary>
    /// Scrub <paramref name="prompt"/> and record the scrub event on the
    /// <c>cena_llm_prompt_pii_scrubbed_total</c> counter, labelled with
    /// <paramref name="feature"/>.
    /// </summary>
    /// <param name="prompt">Prompt string that will be sent to the LLM.</param>
    /// <param name="feature">
    /// Product-facing cost-center from the caller's <see cref="FeatureTagAttribute"/>.
    /// Used as the <c>feature</c> label on the scrub counter so the incident
    /// responder can jump from "phone leaked in 'socratic'" to the Tutor seam
    /// in one hop.
    /// </param>
    PiiScrubResult Scrub(string prompt, string feature);
}

/// <summary>
/// Default <see cref="IPiiPromptScrubber"/>. Uses pre-compiled regexes so the
/// hot path stays sub-millisecond even on prompts in the tens of KB.
/// </summary>
public sealed class PiiPromptScrubber : IPiiPromptScrubber
{
    /// <summary>Meter name the OTLP collector looks for.</summary>
    public const string MeterName = "Cena.Llm.PiiScrub";

    /// <summary>
    /// Fully-qualified metric name emitted to Prometheus. Matches the dashboard
    /// and alert expressions. Any non-zero value is a severity-1 alert per
    /// ADR-0046 Decision 4.
    /// </summary>
    public const string CounterName = "cena_llm_prompt_pii_scrubbed_total";

    // Email addresses. Mirrors TutorPromptScrubber for consistency.
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    // Phone numbers: international + local formats. Anchored to a minimum of
    // 9 digits after the optional country code to avoid eating sequences that
    // appear in math word problems ("1,234,567 apples").
    //
    // Requires at least two runs of digits separated by a space / dash / paren
    // so a bare 9-digit string is handled by the government-id regex, not this
    // one. A fully-contiguous 9+ digit run with no internal punctuation is
    // far more likely to be an ID or a tracking number than a phone.
    private static readonly Regex PhonePattern = new(
        @"(?:\+\d{1,3}[\s\-])?\(?\d{2,4}\)?[\s\-]\d{3,4}[\s\-]?\d{3,4}",
        RegexOptions.Compiled);

    // Israeli teudat-zehut (9 digits). Must not overlap a phone with dashes —
    // the phone regex above requires internal punctuation, so a bare 9-digit
    // run is unambiguously a potential ID. Word-boundary anchored to avoid
    // clipping longer digit runs inside equations.
    private static readonly Regex IsraeliIdPattern = new(
        @"\b\d{9}\b",
        RegexOptions.Compiled);

    // Street addresses: number + street name + street-word marker. Hebrew +
    // Arabic markers included for ILanguage==he/ar — otherwise a rule that
    // only matches "Main Street" is useless in the target market.
    private static readonly Regex AddressPattern = new(
        @"\b\d{1,5}\s+(?:[A-Za-z\u0590-\u05FF\u0600-\u06FF]+\s*){1,5}(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Way|Place|Pl|Court|Ct|רחוב|שדרות|דרך|شارع|طريق)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Postal codes: IL (7 digits), US (5 or 5+4), UK (alphanumeric).
    // NOTE: intentionally DOES NOT include bare 5-digit codes without the
    // hyphen-4 suffix — too common in math / physics problems. The 7-digit
    // IL form and 5+4 US form and UK form are distinctive enough.
    private static readonly Regex PostalCodePattern = new(
        @"\b(?:\d{5}\-\d{4}|\d{7}|[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2})\b",
        RegexOptions.Compiled);

    // Category order is fixed so a prompt with an address that contains a
    // postal code produces the same audit trail regardless of iteration order.
    // Address is scrubbed BEFORE the postal-code pass so a hit on "12 Main
    // Street, 12345-6789" is attributed to `address` (the narrower, more
    // personally-identifying category) not `postal_code`.
    private readonly (string Category, Regex Pattern, string Replacement)[] _patterns =
    {
        ("email",         EmailPattern,      "<redacted:email>"),
        ("phone",         PhonePattern,      "<redacted:phone>"),
        ("address",       AddressPattern,    "<redacted:address>"),
        ("government_id", IsraeliIdPattern,  "<redacted:id>"),
        ("postal_code",   PostalCodePattern, "<redacted:postal>"),
    };

    private readonly Counter<long> _scrubCounter;
    private readonly ILogger<PiiPromptScrubber> _logger;

    public PiiPromptScrubber(IMeterFactory meterFactory, ILogger<PiiPromptScrubber> logger)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var meter = meterFactory.Create(MeterName, "1.0.0");
        _scrubCounter = meter.CreateCounter<long>(
            CounterName,
            unit: "events",
            description:
                "LLM prompt PII redaction events, by feature+category. " +
                "Any non-zero value is a severity-1 alert (ADR-0046).");
    }

    public PiiScrubResult Scrub(string prompt, string feature)
    {
        // Empty/whitespace prompts bypass both the scrub pass and the metric
        // emission — no data to redact, no event to record.
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new PiiScrubResult(prompt, 0, Array.Empty<string>());
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(feature);

        var text = prompt;
        var categories = new List<string>();
        var totalCount = 0;

        foreach (var (category, pattern, replacement) in _patterns)
        {
            var count = 0;
            text = pattern.Replace(text, _ => { count++; return replacement; });
            if (count > 0)
            {
                totalCount += count;
                categories.Add(category);
                _scrubCounter.Add(
                    count,
                    new KeyValuePair<string, object?>("feature", feature),
                    new KeyValuePair<string, object?>("category", category));
            }
        }

        if (totalCount > 0)
        {
            // Log the event — but NEVER the raw matched text. The redaction
            // categories alone are enough for the on-call runbook to locate
            // the seam; logging the matched content would defeat the whole
            // purpose of the scrubber (PII in the observability store).
            _logger.LogWarning(
                "[PII_SCRUB_FIRED] feature={Feature} redactions={Count} categories=[{Categories}] — caller MUST refuse the LLM call and fall back (ADR-0046).",
                feature,
                totalCount,
                string.Join(", ", categories));
        }

        return new PiiScrubResult(text, totalCount, categories);
    }
}

/// <summary>
/// No-op <see cref="IPiiPromptScrubber"/> for unit tests that want to assert
/// on call-site behaviour without wiring a meter + logger. DI registration
/// uses the real <see cref="PiiPromptScrubber"/>; production hosts NEVER
/// substitute this null.
/// </summary>
public sealed class NullPiiPromptScrubber : IPiiPromptScrubber
{
    /// <summary>Shared instance — stateless.</summary>
    public static readonly NullPiiPromptScrubber Instance = new();

    private NullPiiPromptScrubber() { }

    public PiiScrubResult Scrub(string prompt, string feature) =>
        new(prompt, 0, Array.Empty<string>());
}
