// =============================================================================
// RES-003: Redis circuit breaker configuration and behavior tests
// =============================================================================

using Cena.Actors.Gateway;

namespace Cena.Actors.Tests.Infrastructure;

public sealed class RedisCircuitBreakerTests
{
    [Fact]
    public void RedisConfig_Has5MaxFailures()
    {
        var config = CircuitBreakerConfig.Redis;
        Assert.Equal(5, config.MaxFailures);
    }

    [Fact]
    public void RedisConfig_Has30sOpenDuration()
    {
        var config = CircuitBreakerConfig.Redis;
        Assert.Equal(TimeSpan.FromSeconds(30), config.OpenDuration);
    }

    [Fact]
    public void RedisConfig_ModelNameIsRedis()
    {
        var config = CircuitBreakerConfig.Redis;
        Assert.Equal("redis", config.ModelName);
    }

    [Fact]
    public void RedisConfig_DefaultHalfOpenSuccessesIs3()
    {
        var config = CircuitBreakerConfig.Redis;
        Assert.Equal(3, config.HalfOpenSuccessesRequired);
    }

    [Fact]
    public void ForModel_Redis_ReturnsSameAsStaticProperty()
    {
        var fromFactory = CircuitBreakerConfig.ForModel("redis");
        var fromProp = CircuitBreakerConfig.Redis;

        Assert.Equal(fromProp.ModelName, fromFactory.ModelName);
        Assert.Equal(fromProp.MaxFailures, fromFactory.MaxFailures);
        Assert.Equal(fromProp.OpenDuration, fromFactory.OpenDuration);
    }

    [Fact]
    public void ForModel_Redis_CaseInsensitive()
    {
        var upper = CircuitBreakerConfig.ForModel("REDIS");
        Assert.Equal("redis", upper.ModelName);
        Assert.Equal(5, upper.MaxFailures);
    }
}
