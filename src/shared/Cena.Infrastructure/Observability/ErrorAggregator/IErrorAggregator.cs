// =============================================================================
// Cena Platform — Error Aggregator interface (RDY-064)
//
// Abstraction over the production exception-aggregation service (Sentry,
// AppInsights, etc.) so that application code never takes a direct dependency
// on the concrete SDK.
//
// Concrete selection is ADR-gated; see tasks/readiness/RDY-064-error-aggregator.md
// §Decision gate. Until that ADR lands, the NullErrorAggregator is registered
// so code paths can call IErrorAggregator.Capture(...) unconditionally.
// =============================================================================

using System.Collections.Generic;

namespace Cena.Infrastructure.Observability.ErrorAggregator;

/// <summary>
/// Severity levels mirroring Sentry's taxonomy so swapping in a Sentry
/// implementation does not require a translation layer.
/// </summary>
public enum ErrorSeverity
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

/// <summary>
/// Contextual breadcrumb leading up to an aggregated exception. Breadcrumbs
/// MUST flow through the <see cref="IExceptionScrubber"/> before being
/// forwarded to any external service. No raw user input here.
/// </summary>
public sealed record ErrorBreadcrumb(
    string Category,
    string Message,
    ErrorSeverity Severity = ErrorSeverity.Info,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Structured context accompanying an aggregated event. Kept deliberately
/// small so every field is auditable. Never attach raw student free-text;
/// use the anonymised <c>StudentAnonId</c> slot instead.
/// </summary>
public sealed record ErrorContext(
    string? CorrelationId = null,
    string? StudentAnonId = null,
    string? Tenant = null,
    string? ReleaseVersion = null,
    string? Environment = null,
    IReadOnlyDictionary<string, string>? Tags = null,
    IReadOnlyDictionary<string, string>? Extras = null,
    IReadOnlyList<ErrorBreadcrumb>? Breadcrumbs = null);

/// <summary>
/// Product-facing aggregator. Implementations MUST be no-throw on the hot
/// path so a failure in the aggregator never escalates the underlying error.
/// </summary>
public interface IErrorAggregator
{
    /// <summary>
    /// Capture an unhandled or caught exception. Runs scrubbing → dispatch.
    /// </summary>
    void Capture(Exception exception, ErrorSeverity severity = ErrorSeverity.Error, ErrorContext? context = null);

    /// <summary>
    /// Capture a free-form message (no exception). Useful for synthetic
    /// incidents or non-exception error states (e.g. CAS oracle mismatch).
    /// </summary>
    void CaptureMessage(string message, ErrorSeverity severity = ErrorSeverity.Error, ErrorContext? context = null);

    /// <summary>
    /// Add a breadcrumb to the current async flow. No-op when aggregator
    /// is disabled.
    /// </summary>
    void AddBreadcrumb(ErrorBreadcrumb breadcrumb);

    /// <summary>
    /// True when this instance forwards to an external service. False for
    /// <see cref="NullErrorAggregator"/>. Used in health checks and tests.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Human-readable identifier for this aggregator's backend
    /// (e.g. <c>"null"</c>, <c>"sentry"</c>, <c>"appinsights"</c>). Surfaced
    /// via <c>/health</c> so on-call can verify which backend is wired.
    /// </summary>
    string Backend { get; }

    /// <summary>
    /// Flush buffered events. Called during graceful shutdown.
    /// </summary>
    Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
