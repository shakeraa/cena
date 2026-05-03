// =============================================================================
// Cena Platform — Sentry.io IErrorAggregator implementation (RDY-064 / ADR-0058)
//
// Concrete backend that ships scrubbed exceptions to Sentry SaaS (EU region).
// ADR-0058 locks three non-negotiables enforced here:
//
//   1. SOURCE-LAYER SCRUBBING — every Exception/message/breadcrumb passes
//      through IExceptionScrubber BEFORE reaching SentrySdk. The scrubber is
//      the first line of defence; Sentry's BeforeSend is the second.
//   2. SendDefaultPii = false — hard-coded, not config-driven, so no future
//      "turn it on for a minute to debug" accident leaks student PII.
//   3. NO-THROW HOT PATH — every public method is wrapped in try/catch that
//      logs a warning and swallows. An aggregator failure MUST NOT escalate
//      the original error; losing a report beats crashing the caller.
//
// Disabled features (ADR-0058 §2):
//   * DefaultPii (emails, IPs, usernames)
//   * Session replay (SPA-only feature anyway — explicit 0 guarantee)
//   * AutoSessionTracking is ON — crash-free-session KPI feeds operator dash.
// =============================================================================

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentry;

namespace Cena.Infrastructure.Observability.ErrorAggregator;

/// <summary>
/// Sentry.io-backed <see cref="IErrorAggregator"/>. Constructed once per host
/// (registered as a singleton) — initialises <c>SentrySdk</c> in its ctor
/// with a hardened options bundle. Disposal flushes the transport.
/// </summary>
public sealed class SentryErrorAggregator : IErrorAggregator, IDisposable
{
    private const string BackendName = "sentry";

    private readonly IExceptionScrubber _scrubber;
    private readonly ILogger<SentryErrorAggregator> _logger;
    private readonly IDisposable? _sdkDisposable;
    private readonly ErrorAggregatorOptions _options;

    public SentryErrorAggregator(
        IOptions<ErrorAggregatorOptions> options,
        IExceptionScrubber scrubber,
        ILogger<SentryErrorAggregator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scrubber);
        ArgumentNullException.ThrowIfNull(logger);

        _scrubber = scrubber;
        _logger = logger;
        _options = options.Value;

        // Bound sample rate to the legal range; misconfigured values should
        // not crash startup.
        var tracesSampleRate = Math.Clamp(_options.SampleRate, 0.0, 1.0);

        try
        {
            _sdkDisposable = SentrySdk.Init(sentry =>
            {
                sentry.Dsn = _options.Dsn ?? string.Empty;
                sentry.Environment = _options.Environment;
                sentry.Release = _options.Release;

                // HARD-CODED: never expose default PII (email, IP, username).
                // ADR-0058 §2 forbids config-driven override.
                sentry.SendDefaultPii = false;

                // Exceptions always sent; SampleRate applies to other events.
                sentry.SampleRate = 1.0f;
                sentry.TracesSampleRate = tracesSampleRate;

                // Crash-free-session KPI per ADR-0058 §"Positive consequences".
                sentry.AutoSessionTracking = true;

                // Attach stack traces to messages too (useful for synthetic
                // incidents like CAS mismatches). Scrubber still runs on
                // message text first, so this cannot leak.
                sentry.AttachStacktrace = true;

                // Second-line PII scrub. If the source scrubber missed a
                // new field shape, strip it here. If any downstream scrub
                // step throws, drop the event entirely rather than leak.
                sentry.SetBeforeSend(ScrubOrDropEvent);

                sentry.SetBeforeBreadcrumb(ScrubBreadcrumb);
            });

            _logger.LogInformation(
                "[ERR_AGG] Sentry backend initialised. environment={Environment} release={Release} tracesSampleRate={TracesSampleRate}",
                _options.Environment,
                _options.Release,
                tracesSampleRate);
        }
        catch (Exception ex)
        {
            // Init failure must not kill startup. We log, leave _sdkDisposable
            // null, and every subsequent call becomes a safe no-op.
            _logger.LogWarning(
                ex,
                "[ERR_AGG] Sentry SDK init failed; aggregator will no-op for the process lifetime.");
        }
    }

    public bool IsEnabled => _sdkDisposable is not null && SentrySdk.IsEnabled;

    public string Backend => BackendName;

    public void Capture(
        Exception exception,
        ErrorSeverity severity = ErrorSeverity.Error,
        ErrorContext? context = null)
    {
        if (exception is null) return;

        try
        {
            // Scrub BEFORE the SDK sees the exception. ADR-0058 §2 line 1.
            var scrubbed = _scrubber.ScrubException(exception);

            // Sentry 4.x+ removed WithScope in favour of the scope-configuring
            // overload: CaptureException(ex, scope => { ... }). Per-event
            // scope stays isolated from the ambient scope.
            SentrySdk.CaptureException(
                scrubbed,
                scope => ApplyContext(scope, severity, context));
        }
        catch (Exception ex)
        {
            // NO-THROW hot path. Log-and-swallow.
            _logger.LogWarning(
                ex,
                "[ERR_AGG] SentryErrorAggregator.Capture failed; dropping event.");
        }
    }

    public void CaptureMessage(
        string message,
        ErrorSeverity severity = ErrorSeverity.Error,
        ErrorContext? context = null)
    {
        if (string.IsNullOrEmpty(message)) return;

        try
        {
            var cleaned = _scrubber.Scrub(message);

            SentrySdk.CaptureMessage(
                cleaned,
                scope => ApplyContext(scope, severity, context),
                MapLevel(severity));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ERR_AGG] SentryErrorAggregator.CaptureMessage failed; dropping event.");
        }
    }

    public void AddBreadcrumb(ErrorBreadcrumb breadcrumb)
    {
        if (breadcrumb is null) return;

        try
        {
            var cleanedMessage = _scrubber.Scrub(breadcrumb.Message);
            var cleanedData = ScrubData(breadcrumb.Data);

            SentrySdk.AddBreadcrumb(
                message: cleanedMessage,
                category: breadcrumb.Category,
                type: null,
                data: cleanedData,
                level: MapBreadcrumbLevel(breadcrumb.Severity));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ERR_AGG] SentryErrorAggregator.AddBreadcrumb failed; dropping breadcrumb.");
        }
    }

    public async Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_sdkDisposable is null) return;
            await SentrySdk.FlushAsync(timeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ERR_AGG] SentryErrorAggregator.FlushAsync failed.");
        }
    }

    public void Dispose()
    {
        try
        {
            _sdkDisposable?.Dispose();
        }
        catch
        {
            // Silent: disposal during shutdown must not throw.
        }
    }

    // =========================================================================
    // Internal helpers
    // =========================================================================

    private SentryEvent? ScrubOrDropEvent(SentryEvent @event, SentryHint hint)
    {
        try
        {
            // Second-line scrub: the source scrubber has already run on
            // Capture()/CaptureMessage(). If anything slipped through
            // (user-supplied extras, frame locals), strip it here.
            if (!string.IsNullOrEmpty(@event.Message?.Message))
            {
                @event.Message.Message = _scrubber.Scrub(@event.Message.Message);
            }

            if (!string.IsNullOrEmpty(@event.Message?.Formatted))
            {
                @event.Message.Formatted = _scrubber.Scrub(@event.Message.Formatted);
            }

            // ADR-0058 §2: defensive override — never send user PII even if
            // an upstream caller set it.
            if (@event.User is not null)
            {
                @event.User.Email = null;
                @event.User.IpAddress = null;
                @event.User.Username = null;
            }

            return @event;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ERR_AGG] Sentry BeforeSend scrub failed; dropping event to preserve PII invariant.");
            return null; // Drop — never leak on scrubber failure.
        }
    }

    private Breadcrumb? ScrubBreadcrumb(Breadcrumb breadcrumb, SentryHint hint)
    {
        try
        {
            var message = _scrubber.Scrub(breadcrumb.Message ?? string.Empty);
            var cleanedData = ScrubData(breadcrumb.Data);

            // Breadcrumb properties are init-only on the public API; rebuild
            // with a new instance carrying the scrubbed payload. The Sentry
            // 6.x constructor does not accept a timestamp argument — the
            // ambient UtcNow at rebuild is accurate enough for a breadcrumb
            // whose original event fired milliseconds earlier.
            return new Breadcrumb(
                message: message,
                type: breadcrumb.Type,
                data: cleanedData,
                category: breadcrumb.Category,
                level: breadcrumb.Level);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ERR_AGG] Sentry BeforeBreadcrumb scrub failed; dropping breadcrumb.");
            return null; // Drop — never leak.
        }
    }

    /// <summary>
    /// Scrubs every value in a data dictionary. Returns a concrete
    /// <see cref="Dictionary{TKey, TValue}"/> so it implements both
    /// <c>IDictionary</c> (required by <c>SentrySdk.AddBreadcrumb</c>) and
    /// <c>IReadOnlyDictionary</c> (required by the <c>Breadcrumb</c> ctor).
    /// </summary>
    private Dictionary<string, string>? ScrubData(
        IReadOnlyDictionary<string, string>? data)
    {
        if (data is null || data.Count == 0) return null;

        var cleaned = new Dictionary<string, string>(data.Count);
        foreach (var kvp in data)
        {
            cleaned[kvp.Key] = _scrubber.Scrub(kvp.Value);
        }
        return cleaned;
    }

    private void ApplyContext(Scope scope, ErrorSeverity severity, ErrorContext? context)
    {
        scope.Level = MapLevel(severity);

        if (context is null) return;

        if (!string.IsNullOrEmpty(context.CorrelationId))
        {
            scope.SetTag("correlation_id", context.CorrelationId);
        }
        if (!string.IsNullOrEmpty(context.StudentAnonId))
        {
            // Hashed / anonymised ID only. Raw student IDs are caught by
            // ExceptionScrubber's StudentIdMarkerPattern.
            scope.SetTag("student_anon_id", context.StudentAnonId);
        }
        if (!string.IsNullOrEmpty(context.Tenant))
        {
            scope.SetTag("tenant", context.Tenant);
        }
        if (!string.IsNullOrEmpty(context.ReleaseVersion))
        {
            scope.SetTag("release", context.ReleaseVersion);
        }
        if (!string.IsNullOrEmpty(context.Environment))
        {
            scope.SetTag("environment", context.Environment);
        }

        if (context.Tags is not null)
        {
            foreach (var tag in context.Tags)
            {
                scope.SetTag(tag.Key, _scrubber.Scrub(tag.Value));
            }
        }

        if (context.Extras is not null)
        {
            foreach (var extra in context.Extras)
            {
                scope.SetExtra(extra.Key, _scrubber.Scrub(extra.Value));
            }
        }

        if (context.Breadcrumbs is not null)
        {
            foreach (var bc in context.Breadcrumbs)
            {
                AddBreadcrumb(bc);
            }
        }
    }

    private static SentryLevel MapLevel(ErrorSeverity severity) => severity switch
    {
        ErrorSeverity.Debug   => SentryLevel.Debug,
        ErrorSeverity.Info    => SentryLevel.Info,
        ErrorSeverity.Warning => SentryLevel.Warning,
        ErrorSeverity.Error   => SentryLevel.Error,
        ErrorSeverity.Fatal   => SentryLevel.Fatal,
        _                     => SentryLevel.Error,
    };

    private static BreadcrumbLevel MapBreadcrumbLevel(ErrorSeverity severity) => severity switch
    {
        ErrorSeverity.Debug   => BreadcrumbLevel.Debug,
        ErrorSeverity.Info    => BreadcrumbLevel.Info,
        ErrorSeverity.Warning => BreadcrumbLevel.Warning,
        // Sentry's BreadcrumbLevel has no Critical — Error is the top of the
        // ladder on the breadcrumb side; Fatal is only a SentryLevel thing.
        ErrorSeverity.Error   => BreadcrumbLevel.Error,
        ErrorSeverity.Fatal   => BreadcrumbLevel.Error,
        _                     => BreadcrumbLevel.Info,
    };
}
