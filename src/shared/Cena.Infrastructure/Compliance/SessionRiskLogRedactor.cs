// =============================================================================
// Cena Platform — SessionRiskLogRedactor (prr-013, ADR-0003, RDY-080)
//
// Log-line scrubber for session-risk telemetry. Removes decimal numbers that
// appear near the keywords {theta, ability, risk, readiness} so a naked
// point estimate never leaks into a structured log sink.
//
// Scope:
//   - PiiLogSanitizer (in this same namespace) is a reflection-based Serilog
//     destructuring policy: it removes properties whose declaring type is
//     annotated [Pii(ExcludeFromLogs=true)]. It does NOT redact arbitrary
//     substrings of rendered log messages — so it cannot help us scrub a
//     message like: "ComputeRisk theta=0.42 n=18".
//   - This redactor fills that gap with a narrow regex pass over already-
//     rendered log strings. It is intentionally additive: it does not
//     replace PiiLogSanitizer and has no dependency on it.
//   - Integration with PiiLogSanitizer (wiring this redactor into an
//     enrichment step on the Serilog pipeline) is tracked as a follow-up
//     task under prr-013; for now this class exposes a pure function so
//     callers can opt in at emission time.
//
// Semantics:
//   - Scans the input for the keywords {theta, ability, risk, readiness}
//     (case-insensitive, whole-word).
//   - Within <= 24 characters after the keyword (allowing for `=`, `:`,
//     space, quote, etc.), replaces the first decimal/integer literal with
//     the literal "[redacted]".
//   - Leaves everything else untouched. Non-numeric context is preserved so
//     a developer reading the log can still understand what happened.
//
// Example:
//   "ComputeRisk sessionId=abc theta=0.42 n=18 readiness: 0.33 ability=-1.2"
//   →
//   "ComputeRisk sessionId=abc theta=[redacted] n=18 readiness: [redacted] ability=[redacted]"
//
// This deliberately does not handle every pathological shape (exponents,
// non-ASCII minus signs, etc.). A zero-false-negative regex is impossible
// without an AST pass; the job here is to cut the 99% case.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Scrubs session-risk point estimates (theta / ability / risk / readiness
/// scalars) out of rendered log strings before they reach any sink. See
/// prr-013, ADR-0003, RDY-080.
/// </summary>
public static class SessionRiskLogRedactor
{
    private const string Placeholder = "[redacted]";

    // Capture: the trigger keyword + up to 24 characters of lookahead "glue"
    // (=, :, space, quote, etc.) + one numeric literal (optional leading -,
    // optional decimal point). Keywords are case-insensitive, whole-word.
    private static readonly Regex ScalarNearKeyword = new(
        @"\b(?<kw>theta|ability|risk|readiness)\b(?<glue>[\s\S]{0,24}?)(?<num>-?\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns a copy of <paramref name="logLine"/> with session-risk scalars
    /// replaced by <c>[redacted]</c>. Returns <paramref name="logLine"/>
    /// unchanged if no sensitive scalar is found.
    /// </summary>
    public static string Redact(string? logLine)
    {
        if (string.IsNullOrEmpty(logLine)) return logLine ?? string.Empty;

        return ScalarNearKeyword.Replace(
            logLine,
            m => m.Groups["kw"].Value + m.Groups["glue"].Value + Placeholder);
    }
}
