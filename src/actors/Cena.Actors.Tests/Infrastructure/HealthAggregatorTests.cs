// =============================================================================
// RES-005: Health Aggregator tests
// =============================================================================

using Cena.Actors.Infrastructure;

namespace Cena.Actors.Tests.Infrastructure;

public sealed class HealthAggregatorTests
{
    [Fact]
    public void ComputeHealthLevel_AllClear_ReturnsHealthy()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 0, poolUtilizationPercent: 50);
        Assert.Equal(SystemHealthLevel.Healthy, level);
    }

    [Fact]
    public void ComputeHealthLevel_OneCbOpen_ReturnsDegraded()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 1, poolUtilizationPercent: 50);
        Assert.Equal(SystemHealthLevel.Degraded, level);
    }

    [Fact]
    public void ComputeHealthLevel_Pool70Pct_ReturnsDegraded()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 0, poolUtilizationPercent: 70);
        Assert.Equal(SystemHealthLevel.Degraded, level);
    }

    [Fact]
    public void ComputeHealthLevel_TwoCbsOpen_ReturnsCritical()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 2, poolUtilizationPercent: 50);
        Assert.Equal(SystemHealthLevel.Critical, level);
    }

    [Fact]
    public void ComputeHealthLevel_Pool90Pct_ReturnsCritical()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 0, poolUtilizationPercent: 90);
        Assert.Equal(SystemHealthLevel.Critical, level);
    }

    [Fact]
    public void ComputeHealthLevel_ThreeCbsOpen_ReturnsEmergency()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 3, poolUtilizationPercent: 50);
        Assert.Equal(SystemHealthLevel.Emergency, level);
    }

    [Fact]
    public void ComputeHealthLevel_Pool95Pct_ReturnsEmergency()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 0, poolUtilizationPercent: 95);
        Assert.Equal(SystemHealthLevel.Emergency, level);
    }

    [Fact]
    public void ComputeHealthLevel_Pool69_ZeroCbs_IsHealthy()
    {
        var level = HealthAggregatorActor.ComputeHealthLevel(openCircuitBreakers: 0, poolUtilizationPercent: 69.9);
        Assert.Equal(SystemHealthLevel.Healthy, level);
    }
}
