// =============================================================================
// RDY-064: Error Aggregator DI wiring tests
//
// Proves that:
//   * Default (no config) resolves NullErrorAggregator.
//   * Enabled=true + Backend="sentry" (gated on ADR) falls back to null
//     with a warning. The concrete Sentry wire-up is blocked on RDY-064 ADR.
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
            ["ErrorAggregator:Backend"] = "sentry"
        });
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.IsType<NullErrorAggregator>(agg);
    }

    [Fact]
    public void Sentry_backend_enabled_falls_back_to_null_until_ADR_lands()
    {
        // RDY-064 decision gate: concrete Sentry wire-up blocked on ADR.
        // Configuring Enabled=true + Backend=sentry must NOT throw at
        // startup — it logs a warning and registers Null so ops notices
        // the config is ahead of code.
        using var sp = Build(new Dictionary<string, string?>
        {
            ["ErrorAggregator:Enabled"] = "true",
            ["ErrorAggregator:Backend"] = "sentry",
            ["ErrorAggregator:Dsn"] = "https://example.ingest.sentry.io/123"
        });
        var agg = sp.GetRequiredService<IErrorAggregator>();
        Assert.False(agg.IsEnabled);
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
