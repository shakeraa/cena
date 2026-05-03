// =============================================================================
// Cena Platform — Serilog Redaction Integration Tests (prr-013 / RDY-080)
//
// Wires SessionRiskLogEnricher into a real Serilog pipeline with an in-memory
// sink and confirms that session-risk scalars (theta / ability / risk /
// readiness) are replaced with `[redacted]` in the rendered output — both
// when the number comes from a bound property and when it appears as a
// literal in the message template.
//
// The tests also confirm that the unredacted `{Message:lj}` template still
// contains the original number, so local dev tooling that opts out of the
// `{RedactedMessage}` template isn't silently broken. This is intentional:
// `{RedactedMessage}` is what every production sink must use; `{Message:lj}`
// remains available for local diagnostic overrides.
// =============================================================================

using System.Globalization;
using Cena.Infrastructure.Compliance;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Cena.Actors.Tests.Compliance;

public sealed class SerilogRedactionTests
{
    // ── Minimal in-memory sink so we don't pull in a 3rd-party test package ──

    private sealed class InMemorySink : ILogEventSink
    {
        private readonly ITextFormatter _formatter;
        public List<string> Lines { get; } = new();

        public InMemorySink(ITextFormatter formatter) => _formatter = formatter;

        public void Emit(LogEvent logEvent)
        {
            using var sw = new StringWriter(CultureInfo.InvariantCulture);
            _formatter.Format(logEvent, sw);
            Lines.Add(sw.ToString().TrimEnd('\r', '\n'));
        }
    }

    private sealed class LambdaFormatter : ITextFormatter
    {
        private readonly Action<LogEvent, TextWriter> _write;
        public LambdaFormatter(Action<LogEvent, TextWriter> write) => _write = write;
        public void Format(LogEvent logEvent, TextWriter output) => _write(logEvent, output);
    }

    // xUnit's Serilog APIs don't offer a templated sink out of the box, so we
    // build two formatters: one that emits {RedactedMessage} and one that
    // emits {Message} so we can assert both halves of the contract in one test.
    private static ITextFormatter RedactedMessageFormatter() =>
        new LambdaFormatter((evt, writer) =>
        {
            if (evt.Properties.TryGetValue(SessionRiskLogEnricher.PropertyName, out var p)
                && p is ScalarValue sv && sv.Value is string s)
            {
                writer.Write(s);
            }
            else
            {
                writer.Write(evt.RenderMessage(CultureInfo.InvariantCulture));
            }
        });

    private static ITextFormatter RawMessageFormatter() =>
        new LambdaFormatter((evt, writer) =>
            writer.Write(evt.RenderMessage(CultureInfo.InvariantCulture)));

    private static (ILogger logger, InMemorySink redacted, InMemorySink raw) BuildLogger()
    {
        var redactedSink = new InMemorySink(RedactedMessageFormatter());
        var rawSink      = new InMemorySink(RawMessageFormatter());

        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With<SessionRiskLogEnricher>()
            .WriteTo.Sink(redactedSink)
            .WriteTo.Sink(rawSink)
            .CreateLogger();

        return (logger, redactedSink, rawSink);
    }

    [Fact]
    public void Enricher_redacts_theta_scalar_from_bound_property()
    {
        var (logger, redacted, raw) = BuildLogger();

        logger.Information("ComputeRisk theta={Theta} n=18", 0.85);

        Assert.Single(redacted.Lines);
        Assert.Contains("[redacted]", redacted.Lines[0]);
        Assert.DoesNotContain("0.85", redacted.Lines[0]);

        // Raw sink still sees the original — this is the "local dev opt-out"
        // contract; only sinks that use {RedactedMessage} get the scrubbed form.
        Assert.Single(raw.Lines);
        Assert.Contains("0.85", raw.Lines[0]);
    }

    [Fact]
    public void Enricher_redacts_literal_number_near_readiness_keyword_in_template()
    {
        var (logger, redacted, _) = BuildLogger();

        // Number baked into the template itself — PiiDestructuringPolicy
        // can't help here; the enricher is the only layer that sees it.
        logger.Information("student completed session with readiness score 0.45 after recovery");

        Assert.Single(redacted.Lines);
        Assert.Contains("[redacted]", redacted.Lines[0]);
        Assert.DoesNotContain("0.45", redacted.Lines[0]);
    }

    [Fact]
    public void Enricher_redacts_risk_keyword_value()
    {
        var (logger, redacted, _) = BuildLogger();

        // "risk=high" is a non-numeric value — the redactor's regex only
        // fires on digits within 24 chars of the keyword. This test pins
        // the intended behaviour: non-numeric risk labels pass through.
        // If we later extend the redactor to cover string risk labels,
        // flip this assertion.
        logger.Information("student session risk=high, theta=0.42");

        Assert.Single(redacted.Lines);
        var line = redacted.Lines[0];
        // Numeric scalar near theta must be scrubbed.
        Assert.Contains("[redacted]", line);
        Assert.DoesNotContain("0.42", line);
        // Non-numeric "high" is intentionally left intact — proves the
        // regex isn't over-redacting random text.
        Assert.Contains("high", line);
    }

    [Fact]
    public void Enricher_preserves_non_sensitive_messages_verbatim()
    {
        var (logger, redacted, _) = BuildLogger();

        logger.Information("student {Email} enrolled in course {Course}", "alice@school.org", "algebra-1");

        Assert.Single(redacted.Lines);
        Assert.DoesNotContain("[redacted]", redacted.Lines[0]);
        Assert.Contains("alice@school.org", redacted.Lines[0]);
        Assert.Contains("algebra-1", redacted.Lines[0]);
    }
}
