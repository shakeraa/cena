// =============================================================================
// Cena Platform — SessionRiskLogEnricher (prr-013, ADR-0003, RDY-080)
//
// Serilog enricher that runs SessionRiskLogRedactor over every log event's
// rendered text and binds the result as a property named `RedactedMessage`.
// Host outputTemplates reference `{RedactedMessage}` instead of `{Message:lj}`
// so every sink emits the scrubbed form.
//
// Why an enricher (Option C) rather than a destructuring policy (Option A)
// or a sink wrapper (Option B)?
//
//   - PiiDestructuringPolicy (already registered) only fires on complex
//     object destructuring. It cannot scrub a scalar property value like
//     `theta=0.42` or a literal template fragment like `"readiness score
//     0.45"` — both of which the redactor is specifically designed to
//     catch. (See SessionRiskLogRedactor.cs file-header for scope.)
//
//   - A sink wrapper would need to rebuild a synthetic MessageTemplate per
//     event and re-plumb every sink (Console, File, OTLP, Prometheus…),
//     which is both invasive and easy to forget when a new sink is added.
//
//   - An enricher runs once per LogEvent in one central place. The only
//     coordinated change is in each Host's outputTemplate, which is
//     already bespoke per Host.
//
// RedactedMessage is also safe to reference if the enricher somehow isn't
// registered — Serilog will render a property-not-found placeholder rather
// than throwing, and the regular `{Message:lj}` is still available for
// sinks that want unredacted diagnostics locally (it's the enricher that
// decides what to expose; sinks choose the template).
// =============================================================================

using Serilog.Core;
using Serilog.Events;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Serilog <see cref="ILogEventEnricher"/> that renders each event's message
/// template, applies <see cref="SessionRiskLogRedactor"/>, and binds the
/// scrubbed text as a <c>RedactedMessage</c> property. Host output templates
/// reference <c>{RedactedMessage}</c> instead of <c>{Message:lj}</c>.
/// </summary>
public sealed class SessionRiskLogEnricher : ILogEventEnricher
{
    /// <summary>Property name consumed by the Host outputTemplate strings.</summary>
    public const string PropertyName = "RedactedMessage";

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent is null) return;
        if (propertyFactory is null) return;

        // RenderMessage walks the MessageTemplate and substitutes each bound
        // property value. The output is the exact string a `{Message:lj}`
        // sink would emit, so running the redactor over it catches both
        // keyword-near-numeric fragments inside the template text AND
        // numeric property values rendered via placeholders.
        string rendered = logEvent.RenderMessage(formatProvider: null);
        string redacted = SessionRiskLogRedactor.Redact(rendered);

        var prop = propertyFactory.CreateProperty(
            PropertyName, redacted, destructureObjects: false);
        logEvent.AddOrUpdateProperty(prop);
    }
}
