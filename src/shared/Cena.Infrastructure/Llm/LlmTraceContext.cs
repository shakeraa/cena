// =============================================================================
// Cena Platform — LLM trace-id propagation (prr-143, ADR-0026)
//
// Every LLM call site — every class that consumes an LLM client and carries
// [TaskRouting] — MUST stamp a trace_id on its outbound request path and on
// its cost-metric emission, so a single session's LLM fan-out is stitchable
// end-to-end in the observability backend (Tempo / Jaeger / OTLP → Grafana).
//
// prr-143 rationale (persona-sre + persona-finops, 2026-04-20 pre-release
// review): when a Bagrut-morning incident fires at 03:00 and the on-call
// engineer opens Grafana, they need to pivot from a single failed student
// turn to the full LLM fan-out (hint ladder → Socratic tutor → CAS gate →
// explanation cache) without reconstructing session IDs. Trace-id is the
// cheapest stitching key: one label on every metric, one tag on every span.
//
// Design
// ------
// IActivityPropagator is an OpenTelemetry-integrated helper that resolves the
// current Activity.Id (when an Activity is open) or falls back to the
// Activity.TraceId of the current Activity (when the exporter uses W3C
// TraceContext). When no Activity is open, it returns the caller-supplied
// correlation id so a background job or an NATS-driven handler without an
// ambient Activity still emits a non-null trace id.
//
// CorrelationContext (existing ERR-001.4 machinery) provides the fallback
// — CorrelationIdMiddleware is already the authoritative request-scoped id
// source.
//
// The propagator is an interface (not a static helper) so tests can
// substitute deterministic trace ids and so a future swap to a different
// observability SDK is a DI change rather than a codebase-wide rewrite.
//
// Contract for LLM services
// -------------------------
// Every class carrying [TaskRouting(...)] must:
//
//   1. Inject IActivityPropagator (as a constructor param).
//   2. Call _propagator.GetTraceId() immediately before the outbound LLM
//      call — the value is the `trace_id` tag/label/metadata entry.
//   3. Stamp the trace id on:
//         a. the ILlmCostMetric.Record call (as instituteId has already
//            demonstrated the pattern for tenant labels) OR
//         b. a per-call structured-log entry with property `trace_id` OR
//         c. the outbound provider request metadata when the SDK supports it.
//   4. Log a single structured-log line tagged with trace_id on both
//      success and failure paths so the exporter picks it up.
//
// Arch-test surface: EveryLlmServiceEmitsTraceIdTest scans for
// `IActivityPropagator` and `GetTraceId(` tokens in every [TaskRouting] file
// and fails the build if either is missing.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Correlation;

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Resolves the current OpenTelemetry trace id for LLM call stitching
/// (prr-143). See file header for the contract every LLM service must honor.
/// </summary>
public interface IActivityPropagator
{
    /// <summary>
    /// Returns a non-empty trace id for the current call context.
    /// <para>
    /// Resolution order:
    /// <list type="number">
    ///   <item><description>Current <see cref="Activity.TraceId"/> when an Activity is open
    ///     and its <see cref="ActivityTraceId.ToString"/> yields a non-zero value.</description></item>
    ///   <item><description>Current <see cref="Activity.Id"/> when no W3C trace id is
    ///     available but a legacy Activity id is.</description></item>
    ///   <item><description><see cref="CorrelationContext.Current"/> when set by
    ///     CorrelationIdMiddleware (HTTP request scope).</description></item>
    ///   <item><description>A freshly-generated <see cref="ActivityTraceId"/> value as a
    ///     last resort so no LLM call emits a null/empty trace id.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    string GetTraceId();

    /// <summary>
    /// Starts an LLM-scoped child Activity on the configured ActivitySource
    /// ("Cena.Llm") with the task name as operation name. Returns null when
    /// no listener is registered (lets the JIT eliminate the tagging
    /// fast-path in non-instrumented environments).
    /// </summary>
    Activity? StartLlmActivity(string taskName);
}

/// <summary>
/// Default <see cref="IActivityPropagator"/> implementation backed by
/// <see cref="Activity"/> + <see cref="CorrelationContext"/>.
/// </summary>
public sealed class ActivityPropagator : IActivityPropagator
{
    /// <summary>
    /// ActivitySource name dedicated to LLM call spans. Grafana/Tempo
    /// dashboards filter on this source name; keep stable.
    /// </summary>
    public const string LlmActivitySourceName = "Cena.Llm";

    private static readonly ActivitySource LlmSource = new(LlmActivitySourceName, "1.0.0");

    public string GetTraceId()
    {
        var current = Activity.Current;
        if (current is not null)
        {
            // Prefer the W3C TraceId when the activity carries one (it does
            // when the IdFormat is W3C, which is the .NET 9 default).
            var traceId = current.TraceId;
            if (traceId != default)
            {
                var asString = traceId.ToHexString();
                if (!IsAllZeroes(asString))
                    return asString;
            }

            if (!string.IsNullOrWhiteSpace(current.Id))
                return current.Id!;
        }

        var correlation = CorrelationContext.Current;
        if (!string.IsNullOrWhiteSpace(correlation))
            return correlation!;

        // Fall back to a freshly-generated W3C trace id so the caller never
        // emits null/empty — we would rather have an isolated trace than a
        // blank one in the exporter.
        return ActivityTraceId.CreateRandom().ToHexString();
    }

    public Activity? StartLlmActivity(string taskName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        return LlmSource.StartActivity(taskName, ActivityKind.Client);
    }

    private static bool IsAllZeroes(string s)
    {
        for (var i = 0; i < s.Length; i++)
            if (s[i] != '0') return false;
        return true;
    }
}
