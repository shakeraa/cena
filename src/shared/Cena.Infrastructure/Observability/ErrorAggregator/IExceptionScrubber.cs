// =============================================================================
// Cena Platform — Exception Scrubber (RDY-064)
//
// Every exception that leaves the process for an external aggregator (Sentry,
// AppInsights) MUST pass through this scrubber. We cannot rely on the
// TutorPromptScrubber alone because:
//
//   * TutorPromptScrubber needs a StudentPiiContext — we do not have that
//     in a generic unhandled-exception path.
//   * Exception messages contain StackTrace file paths with argument values
//     that can leak emails, student IDs, and Israeli government IDs.
//   * Breadcrumbs (logs, HTTP bodies) reach the aggregator as strings.
//
// So this scrubber runs a context-free pattern pass: anything that matches
// an obvious PII shape (email, phone, ID number, postal code, IPv4, IPv6,
// JWT token, bearer token) is replaced with a category placeholder.
// =============================================================================

namespace Cena.Infrastructure.Observability.ErrorAggregator;

/// <summary>
/// Context-free PII scrubber for error payloads. Deliberately over-scrubs
/// rather than under-scrubs — a redacted stack trace is still debuggable
/// from file/line numbers plus the exception type.
/// </summary>
public interface IExceptionScrubber
{
    /// <summary>
    /// Returns a cleaned copy of <paramref name="text"/>. Null / empty pass
    /// through unchanged. This method MUST NOT throw.
    /// </summary>
    string Scrub(string? text);

    /// <summary>
    /// Scrub an exception into a new exception of the same type whose
    /// Message is scrubbed. The returned instance is safe to forward to
    /// any external aggregator. Inner exceptions are scrubbed recursively.
    /// </summary>
    Exception ScrubException(Exception exception);
}
