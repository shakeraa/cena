// =============================================================================
// Cena Platform — Null Error Aggregator (RDY-064)
//
// Default no-op implementation of IErrorAggregator. Registered when the
// aggregator is disabled (no DSN / ErrorAggregator:Enabled=false) so calling
// code does not need to null-check — the same contract is always present,
// it just does not forward anywhere.
//
// This mirrors the "graceful disabled" pattern used by SMS + email
// peripherals: the feature's absence is observable in /health, but no code
// path raises.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Observability.ErrorAggregator;

public sealed class NullErrorAggregator : IErrorAggregator
{
    private readonly ILogger<NullErrorAggregator> _logger;

    public NullErrorAggregator(ILogger<NullErrorAggregator> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled => false;
    public string Backend => "null";

    public void Capture(Exception exception, ErrorSeverity severity = ErrorSeverity.Error, ErrorContext? context = null)
    {
        // Still log at Debug so tests can confirm the path was exercised
        // without committing to an aggregator backend yet.
        _logger.LogDebug(
            "[ERR_AGG] null-aggregator captured {ExceptionType} severity={Severity} correlation={Correlation}",
            exception.GetType().Name,
            severity,
            context?.CorrelationId ?? "n/a");
    }

    public void CaptureMessage(string message, ErrorSeverity severity = ErrorSeverity.Error, ErrorContext? context = null)
    {
        _logger.LogDebug(
            "[ERR_AGG] null-aggregator message severity={Severity} correlation={Correlation}",
            severity,
            context?.CorrelationId ?? "n/a");
    }

    public void AddBreadcrumb(ErrorBreadcrumb breadcrumb)
    {
        // No-op. A real aggregator would buffer this against the current
        // async-local scope.
    }

    public Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
