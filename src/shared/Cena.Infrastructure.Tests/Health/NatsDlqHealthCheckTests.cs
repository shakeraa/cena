// =============================================================================
// Cena Platform — NatsDlqHealthCheck tests (RDY-017a sub-task 2)
//
// Covers the pure threshold-to-result mapping extracted in RDY-017a. The
// SQL count query is deliberately NOT exercised here — it's validated end
// to end by the nightly NATS staging sweep (docker-compose + real
// JetStream instance). What this suite guarantees is the contract the
// health-check endpoint promises downstream:
//
//   • dlqCount < 50  → Healthy, message "DLQ depth: N", data.dlq_depth = N
//   • dlqCount = 50  → Degraded (threshold is inclusive)
//   • dlqCount = 500 → Degraded, message includes both N and the threshold
//   • Any branch populates the canonical "dlq_depth" data key
//
// Kept alongside the Infrastructure tests so the InternalsVisibleTo in
// Cena.Infrastructure.csproj keeps the surface area tight.
// =============================================================================

using Cena.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Infrastructure.Tests.Health;

public sealed class NatsDlqHealthCheckTests
{
    [Fact]
    public void BuildResult_ZeroCount_ReturnsHealthy()
    {
        var result = NatsDlqHealthCheck.BuildResult(0);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0, result.Data[NatsDlqHealthCheck.DepthKey]);
        Assert.Contains("DLQ depth: 0", result.Description);
    }

    [Fact]
    public void BuildResult_JustUnderThreshold_ReturnsHealthy()
    {
        var result = NatsDlqHealthCheck.BuildResult(NatsDlqHealthCheck.AlertThreshold - 1);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(49, result.Data[NatsDlqHealthCheck.DepthKey]);
    }

    [Fact]
    public void BuildResult_ExactlyAtThreshold_ReturnsDegraded()
    {
        // The threshold is inclusive — depth == threshold degrades.
        var result = NatsDlqHealthCheck.BuildResult(NatsDlqHealthCheck.AlertThreshold);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(50, result.Data[NatsDlqHealthCheck.DepthKey]);
        Assert.Contains("50", result.Description);
        Assert.Contains("threshold", result.Description);
    }

    [Fact]
    public void BuildResult_FarOverThreshold_ReturnsDegraded()
    {
        var result = NatsDlqHealthCheck.BuildResult(500);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(500, result.Data[NatsDlqHealthCheck.DepthKey]);
        Assert.Contains("500", result.Description);
    }

    [Fact]
    public void BuildResult_AllBranches_AlwaysIncludeDepthKey()
    {
        // dlq_depth is the key Prometheus + admin health.vue consume.
        // Drift here silently breaks the dashboard. Pin it.
        foreach (var count in new[] { 0, 1, 49, 50, 100, 10_000 })
        {
            var result = NatsDlqHealthCheck.BuildResult(count);

            Assert.Contains(NatsDlqHealthCheck.DepthKey, result.Data.Keys);
            Assert.Equal(count, result.Data[NatsDlqHealthCheck.DepthKey]);
        }
    }

    [Fact]
    public void BuildResult_DegradedBranch_EmitsWarningLog()
    {
        var logger = new ListLogger();

        NatsDlqHealthCheck.BuildResult(NatsDlqHealthCheck.AlertThreshold + 5, logger);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("[DLQ]") && e.Message.Contains("dead-lettered"));
    }

    [Fact]
    public void BuildResult_HealthyBranch_DoesNotLogWarning()
    {
        var logger = new ListLogger();

        NatsDlqHealthCheck.BuildResult(0, logger);

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("[DLQ]"));
    }

    [Fact]
    public void BuildResult_NullLogger_DoesNotThrow()
    {
        // Logger is optional — the static helper should be usable from
        // contexts without a DI-resolved ILogger.
        var ex = Record.Exception(() =>
            NatsDlqHealthCheck.BuildResult(NatsDlqHealthCheck.AlertThreshold + 1, null));

        Assert.Null(ex);
    }

    // ── ListLogger test double ───────────────────────────────────────────
    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger<NatsDlqHealthCheck>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel level,
            Microsoft.Extensions.Logging.EventId _,
            TState state,
            Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((level, formatter(state, ex)));
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
