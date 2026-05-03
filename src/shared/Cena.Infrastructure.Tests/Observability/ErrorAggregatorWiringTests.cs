// =============================================================================
// RDY-064 / ADR-0058: Error Aggregator DI wiring tests
//
// Proves that:
//   * Default (no config) resolves NullErrorAggregator.
//   * Enabled=true + Backend="sentry" + non-empty DSN resolves the real
//     SentryErrorAggregator (ADR-0058 unblocked this path).
//   * Enabled=true + Backend="sentry" + empty DSN gracefully degrades to
//     NullErrorAggregator (same posture as peripherals with no credentials).
//   * Enabled=true + Backend="appinsights" still falls back to Null
//     (ADR-0058 only covered Sentry).
//   * Unknown backend falls back to null.
//   * IExceptionScrubber is always registered.
// =============================================================================

using Cena.Infrastructure.Observability.ErrorAggregator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cena.Infrastructure.Tests.Observability;

public class ErrorAggregatorWiringTests
{
    private static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCenaErrorAggregator(cfg);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_config_resolves_null_aggregator()
    {
        using var sp = Build(new Dictionary<string, string?>());
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.IsType<NullErrorAggregator>(agg);
        Assert.False(agg.IsEnabled);
        Assert.Equal("null", agg.Backend);
    }

    [Fact]
    public void Disabled_explicit_resolves_null()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["ErrorAggregator:Enabled"] = "false",
            ["ErrorAggregator:Backend"] = "sentry",
            ["ErrorAggregator:Dsn"] = "https://dummy@example.ingest.sentry.io/1"
        });
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.IsType<NullErrorAggregator>(agg);
    }

    [Fact]
    public void Sentry_backend_with_valid_dsn_resolves_real_SentryErrorAggregator()
    {
        // ADR-0058 landed: the decision gate is lifted. A configured backend
        // plus a non-empty DSN now resolves the concrete SentryErrorAggregator.
        // The DSN here is a dummy — SentrySdk.Init does not network-round-trip
        // at construction, it just configures the transport.
        using var sp = Build(new Dictionary<string, string?>
        {
            ["ErrorAggregator:Enabled"] = "true",
            ["ErrorAggregator:Backend"] = "sentry",
            ["ErrorAggregator:Dsn"] = "https://dummy@example.ingest.sentry.io/1",
            ["ErrorAggregator:Environment"] = "test",
            ["ErrorAggregator:Release"] = "abcdef123456"
        });
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.IsType<SentryErrorAggregator>(agg);
        Assert.Equal("sentry", agg.Backend);
    }

    [Fact]
    public void Sentry_backend_with_empty_dsn_falls_back_to_Null_gracefully()
    {
        // Same graceful-disabled posture as SMS/email peripherals without
        // credentials: DSN missing → no aggregator, but startup still works.
        using var sp = Build(new Dictionary<string, string?>
        {
            ["ErrorAggregator:Enabled"] = "true",
            ["ErrorAggregator:Backend"] = "sentry",
            ["ErrorAggregator:Dsn"] = ""
        });
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.IsType<NullErrorAggregator>(agg);
        Assert.Equal("null", agg.Backend);
        Assert.False(agg.IsEnabled);
    }

    [Fact]
    public void AppInsights_backend_still_gated_and_falls_back_to_null()
    {
        // ADR-0058 only covered Sentry. AppInsights still falls back to
        // Null with a warning so ops notices the config is ahead of code.
        using var sp = Build(new Dictionary<string, string?>
        {
            ["ErrorAggregator:Enabled"] = "true",
            ["ErrorAggregator:Backend"] = "appinsights",
            ["ErrorAggregator:Dsn"] = "InstrumentationKey=abc"
        });
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.IsType<NullErrorAggregator>(agg);
        Assert.Equal("null", agg.Backend);
    }

    [Fact]
    public void Unknown_backend_falls_back_to_null()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["ErrorAggregator:Enabled"] = "true",
            ["ErrorAggregator:Backend"] = "bogus-tool"
        });
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.Equal("null", agg.Backend);
    }

    [Fact]
    public void Exception_scrubber_is_always_registered()
    {
        using var sp = Build(new Dictionary<string, string?>());
        var scrubber = sp.GetRequiredService<IExceptionScrubber>();
        Assert.NotNull(scrubber);
    }

    [Fact]
    public void Null_aggregator_capture_is_noop_and_does_not_throw()
    {
        using var sp = Build(new Dictionary<string, string?>());
        var agg = sp.GetRequiredService<IErrorAggregator>();

        agg.Capture(new InvalidOperationException("boom"));
        agg.CaptureMessage("test");
        agg.AddBreadcrumb(new ErrorBreadcrumb("test", "bc"));

        // FlushAsync must always return quickly on the null aggregator.
        var task = agg.FlushAsync(TimeSpan.FromMilliseconds(10));
        Assert.True(task.IsCompletedSuccessfully);
    }
}
