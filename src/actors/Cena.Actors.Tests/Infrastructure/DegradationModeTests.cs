// =============================================================================
// RES-006: Graceful degradation tier tests
// =============================================================================

using Cena.Actors.Infrastructure;

namespace Cena.Actors.Tests.Infrastructure;

public sealed class DegradationModeTests
{
    [Theory]
    [InlineData(SystemHealthLevel.Healthy, false)]
    [InlineData(SystemHealthLevel.Degraded, true)]
    [InlineData(SystemHealthLevel.Critical, true)]
    [InlineData(SystemHealthLevel.Emergency, true)]
    public void ShouldUseFallbackQuestions(SystemHealthLevel level, bool expected)
    {
        Assert.Equal(expected, DegradationMode.ShouldUseFallbackQuestions(level));
    }

    [Theory]
    [InlineData(SystemHealthLevel.Healthy, false)]
    [InlineData(SystemHealthLevel.Degraded, false)]
    [InlineData(SystemHealthLevel.Critical, true)]
    [InlineData(SystemHealthLevel.Emergency, true)]
    public void ShouldBufferEvents(SystemHealthLevel level, bool expected)
    {
        Assert.Equal(expected, DegradationMode.ShouldBufferEvents(level));
    }

    [Theory]
    [InlineData(SystemHealthLevel.Healthy, false)]
    [InlineData(SystemHealthLevel.Degraded, false)]
    [InlineData(SystemHealthLevel.Critical, false)]
    [InlineData(SystemHealthLevel.Emergency, true)]
    public void ShouldRejectNewSessions(SystemHealthLevel level, bool expected)
    {
        Assert.Equal(expected, DegradationMode.ShouldRejectNewSessions(level));
    }

    [Theory]
    [InlineData(SystemHealthLevel.Healthy, false)]
    [InlineData(SystemHealthLevel.Degraded, false)]
    [InlineData(SystemHealthLevel.Critical, true)]
    [InlineData(SystemHealthLevel.Emergency, true)]
    public void ShouldServeCachedState(SystemHealthLevel level, bool expected)
    {
        Assert.Equal(expected, DegradationMode.ShouldServeCachedState(level));
    }

    [Theory]
    [InlineData(SystemHealthLevel.Healthy, false)]
    [InlineData(SystemHealthLevel.Degraded, false)]
    [InlineData(SystemHealthLevel.Critical, false)]
    [InlineData(SystemHealthLevel.Emergency, true)]
    public void ShouldAggressivelyPassivate(SystemHealthLevel level, bool expected)
    {
        Assert.Equal(expected, DegradationMode.ShouldAggressivelyPassivate(level));
    }
}
