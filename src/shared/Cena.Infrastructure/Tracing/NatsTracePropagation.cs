// =============================================================================
// Cena Platform -- NATS Trace Context Propagation (INF-020)
// Injects/extracts W3C traceparent + tracestate headers into NATS messages
// so distributed traces flow across the NATS bus without breaks.
// =============================================================================

using System.Diagnostics;
using NATS.Client.Core;

namespace Cena.Infrastructure.Tracing;

/// <summary>
/// W3C Trace Context propagation helpers for NATS messages.
/// Uses <see cref="Activity"/> (the .NET equivalent of OpenTelemetry spans)
/// to inject the current trace into outgoing NATS headers and to restore
/// the parent context on the receiving side.
/// </summary>
public static class NatsTracePropagation
{
    /// <summary>
    /// Well-known ActivitySource shared by all NATS tracing spans.
    /// Register this name with the OpenTelemetry TracerProvider so spans are exported.
    /// </summary>
    public const string ActivitySourceName = "Cena.Infrastructure.NatsTracing";

    private static readonly ActivitySource Source = new(ActivitySourceName, "1.0.0");

    private const string TraceparentHeader = "traceparent";
    private const string TracestateHeader = "tracestate";

    // ── Injection (publisher side) ──

    /// <summary>
    /// Injects the current <see cref="Activity"/>'s W3C traceparent and tracestate
    /// into the provided <see cref="NatsHeaders"/>. If no headers instance is
    /// supplied, a new one is created and returned.
    /// Safe to call when there is no current Activity (headers are returned unchanged).
    /// </summary>
    public static NatsHeaders InjectTraceContext(NatsHeaders? headers = null)
    {
        headers ??= new NatsHeaders();

        var activity = Activity.Current;
        if (activity is null)
            return headers;

        // W3C traceparent: 00-{traceId}-{spanId}-{flags}
        var traceparent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
        headers[TraceparentHeader] = traceparent;

        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            headers[TracestateHeader] = activity.TraceStateString;
        }

        return headers;
    }

    // ── Extraction (consumer side) ──

    /// <summary>
    /// Extracts W3C trace context from NATS message headers and starts a new
    /// <see cref="Activity"/> linked to the remote parent. The caller owns the
    /// returned Activity and should dispose it when the message is fully processed
    /// (best done via <c>using</c>).
    /// Returns <c>null</c> when the ActivitySource has no listeners (tracing disabled)
    /// or headers contain no trace context.
    /// </summary>
    /// <param name="headers">NATS message headers (may be null).</param>
    /// <param name="operationName">Span name, e.g. "NatsBusRouter.HandleStartSession".</param>
    /// <param name="kind">
    /// Activity kind. Defaults to <see cref="ActivityKind.Consumer"/> for NATS subscribers.
    /// </param>
    public static Activity? ExtractTraceContext(
        NatsHeaders? headers,
        string operationName,
        ActivityKind kind = ActivityKind.Consumer)
    {
        ActivityContext parentContext = default;

        if (headers is not null
            && headers.TryGetValue(TraceparentHeader, out var traceparentValues)
            && traceparentValues.Count > 0)
        {
            var traceparent = traceparentValues[0]!;
            if (ActivityContext.TryParse(traceparent, null, out var parsed))
            {
                // Layer on tracestate if present
                string? tracestate = null;
                if (headers.TryGetValue(TracestateHeader, out var tracestateValues)
                    && tracestateValues.Count > 0)
                {
                    tracestate = tracestateValues[0];
                }

                if (tracestate is not null
                    && ActivityContext.TryParse(traceparent, tracestate, out var parsedWithState))
                {
                    parentContext = parsedWithState;
                }
                else
                {
                    parentContext = parsed;
                }
            }
        }

        // Start a new activity linked to the remote parent.
        // When parentContext is default (no trace in headers), this becomes a new root span.
        var activity = Source.StartActivity(
            operationName,
            kind,
            parentContext);

        return activity;
    }
}
