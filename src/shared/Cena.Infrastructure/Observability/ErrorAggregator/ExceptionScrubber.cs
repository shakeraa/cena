// =============================================================================
// Cena Platform — Default Exception Scrubber implementation (RDY-064)
//
// Runs a catalogue of PII patterns over exception messages / breadcrumbs.
// Patterns are compiled once (thread-safe) and applied in an order that
// scrubs longer / more-specific shapes first, so inner patterns do not
// corrupt outer matches (e.g. email before domain-looking host).
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Infrastructure.Observability.ErrorAggregator;

public sealed class ExceptionScrubber : IExceptionScrubber
{
    // Order matters: specific → generic.

    // JWT: three base64url groups joined by dots, each ≥ 10 chars.
    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
        RegexOptions.Compiled);

    // Bearer <token> — 20+ chars of [A-Za-z0-9._-]
    private static readonly Regex BearerTokenPattern = new(
        @"(?i)\bbearer\s+[A-Za-z0-9._\-]{20,}\b",
        RegexOptions.Compiled);

    // Email
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    // IPv4
    private static readonly Regex IPv4Pattern = new(
        @"\b(?:25[0-5]|2[0-4]\d|[01]?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|[01]?\d?\d)){3}\b",
        RegexOptions.Compiled);

    // IPv6 (abbreviated)
    private static readonly Regex IPv6Pattern = new(
        @"\b(?:[A-Fa-f0-9]{1,4}:){2,7}[A-Fa-f0-9]{1,4}\b",
        RegexOptions.Compiled);

    // Phone numbers (international, local)
    private static readonly Regex PhonePattern = new(
        @"\+?\d{1,3}[\s\-]?\(?\d{2,4}\)?[\s\-]?\d{3,4}[\s\-]?\d{3,4}",
        RegexOptions.Compiled);

    // Israeli ID (9 digits standalone)
    private static readonly Regex IsraeliIdPattern = new(
        @"\b\d{9}\b",
        RegexOptions.Compiled);

    // UK postcode
    private static readonly Regex UkPostcodePattern = new(
        @"\b[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}\b",
        RegexOptions.Compiled);

    // Credit-card-ish (13-19 digits with optional separators). We don't need
    // to accept them in the system at all; if one slips in we redact.
    private static readonly Regex CardNumberPattern = new(
        @"\b(?:\d[ -]?){13,19}\b",
        RegexOptions.Compiled);

    // A Cena-specific marker. Any string shaped `studentId=<value>` or
    // `studentId:<value>` gets redacted.
    private static readonly Regex StudentIdMarkerPattern = new(
        @"(?i)student(?:_?id)\s*[=:]\s*""?[A-Za-z0-9_\-]+""?",
        RegexOptions.Compiled);

    public string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        try
        {
            var result = text;
            result = JwtPattern.Replace(result, "<redacted:jwt>");
            result = BearerTokenPattern.Replace(result, "<redacted:bearer>");
            result = StudentIdMarkerPattern.Replace(result, "studentId=<redacted:student>");
            result = EmailPattern.Replace(result, "<redacted:email>");
            result = CardNumberPattern.Replace(result, "<redacted:card>");
            result = IPv6Pattern.Replace(result, "<redacted:ipv6>");
            result = IPv4Pattern.Replace(result, "<redacted:ipv4>");
            // IsraeliId (9-digit standalone) MUST run BEFORE PhonePattern —
            // the phone regex would otherwise consume a bare "123456789" and
            // misclassify it as <redacted:phone>.
            result = IsraeliIdPattern.Replace(result, "<redacted:id>");
            result = PhonePattern.Replace(result, "<redacted:phone>");
            result = UkPostcodePattern.Replace(result, "<redacted:postal>");
            return result;
        }
        catch
        {
            // Scrubbing MUST NOT throw. If a pattern engine trips, fall back
            // to a safe placeholder rather than leaking the original.
            return "<scrub-failed>";
        }
    }

    public Exception ScrubException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // We cannot rewrite an Exception's Message in place (it's init-only
        // on many subclasses), so wrap in a ScrubbedException that carries
        // the cleaned message and the original type name.
        var cleanedMessage = Scrub(exception.Message);
        var cleanedStack = Scrub(exception.StackTrace ?? string.Empty);

        Exception? cleanedInner = exception.InnerException is not null
            ? ScrubException(exception.InnerException)
            : null;

        return new ScrubbedException(
            originalTypeName: exception.GetType().FullName ?? exception.GetType().Name,
            cleanedMessage: cleanedMessage,
            cleanedStackTrace: cleanedStack,
            inner: cleanedInner);
    }
}

/// <summary>
/// Wrapper exception returned by <see cref="ExceptionScrubber.ScrubException"/>.
/// Preserves type-name and cleaned stack trace for aggregator fingerprinting
/// without exposing the original Message / StackTrace strings.
/// </summary>
public sealed class ScrubbedException : Exception
{
    public string OriginalTypeName { get; }
    public string CleanedStackTrace { get; }

    public ScrubbedException(
        string originalTypeName,
        string cleanedMessage,
        string cleanedStackTrace,
        Exception? inner)
        : base(cleanedMessage, inner)
    {
        OriginalTypeName = originalTypeName;
        CleanedStackTrace = cleanedStackTrace;
    }

    public override string? StackTrace => CleanedStackTrace;

    public override string ToString()
        => $"{OriginalTypeName}: {Message}\n{CleanedStackTrace}"
           + (InnerException is not null ? $"\n ---> {InnerException}" : string.Empty);
}
