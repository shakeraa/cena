// =============================================================================
// RES-009: Adaptive timeout tests
// =============================================================================

using Cena.Actors.Infrastructure;

namespace Cena.Actors.Tests.Infrastructure;

public sealed class AdaptiveTimeoutTests
{
    private static readonly TimeSpan Base = TimeSpan.FromMilliseconds(2000);

    [Fact]
    public void Healthy_Returns1xBase()
    {
        var result = AdaptiveTimeout.Calculate(Base, SystemHealthLevel.Healthy);
        Assert.Equal(2000, result.TotalMilliseconds);
    }

    [Fact]
    public void Degraded_Returns1_5xBase()
    {
        var result = AdaptiveTimeout.Calculate(Base, SystemHealthLevel.Degraded);
        Assert.Equal(3000, result.TotalMilliseconds);
    }

    [Fact]
    public void Critical_Returns2xBase()
    {
        var result = AdaptiveTimeout.Calculate(Base, SystemHealthLevel.Critical);
        Assert.Equal(4000, result.TotalMilliseconds);
    }

    [Fact]
    public void Emergency_Returns3xBase()
    {
        var result = AdaptiveTimeout.Calculate(Base, SystemHealthLevel.Emergency);
        Assert.Equal(6000, result.TotalMilliseconds);
    }

    [Fact]
    public void ZeroBase_AlwaysReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, AdaptiveTimeout.Calculate(TimeSpan.Zero, SystemHealthLevel.Emergency));
    }
}
