// =============================================================================
// Cena Platform — Global Cost Circuit Breaker (RATE-001 Tier 4)
// Halts all LLM/CAS calls when daily global spend exceeds threshold.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.RateLimit;

/// <summary>
/// Global cost circuit breaker that opens when daily spend exceeds threshold.
/// </summary>
public interface ICostCircuitBreaker
{
    /// <summary>
    /// Returns true when the circuit breaker is open (spend exceeded).
    /// </summary>
    Task<bool> IsOpenAsync(CancellationToken ct = default);

    /// <summary>
    /// Records an actual spend against the daily threshold.
    /// </summary>
    Task RecordSpendAsync(double costUsd, CancellationToken ct = default);

    /// <summary>
    /// Gets current daily spend and threshold.
    /// </summary>
    Task<(double Used, double Threshold)> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>
/// Redis-backed global cost circuit breaker.
/// </summary>
public sealed class RedisCostCircuitBreaker : ICostCircuitBreaker
{
    private readonly IDatabase _redis;
    private readonly ILogger<RedisCostCircuitBreaker> _logger;
    private readonly double _dailyThresholdUsd;
    private readonly TimeSpan _keyTtl = TimeSpan.FromHours(25);

    public RedisCostCircuitBreaker(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisCostCircuitBreaker> logger)
    {
        _redis = redis.GetDatabase();
        _logger = logger;
        _dailyThresholdUsd = configuration.GetValue<double>("Cena:CostCircuitBreaker:DailyThresholdUsd", 1_000.0);
    }

    public async Task<bool> IsOpenAsync(CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var key = $"cena:costcircuit:global:{today}";
            var used = await _redis.StringGetAsync(key);
            var usedValue = used.TryParse(out double u) ? u : 0.0;
            var isOpen = usedValue >= _dailyThresholdUsd;

            if (isOpen)
            {
                _logger.LogWarning(
                    "Global cost circuit breaker OPEN. Daily spend ${Used:F2} >= threshold ${Threshold:F2}",
                    usedValue, _dailyThresholdUsd);
            }

            return isOpen;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis cost circuit breaker check failed — failing closed (allowing requests)");
            return false;
        }
    }

    public async Task RecordSpendAsync(double costUsd, CancellationToken ct = default)
    {
        if (costUsd <= 0) return;

        try
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var key = $"cena:costcircuit:global:{today}";
            var newValue = await _redis.StringIncrementAsync(key, costUsd);
            await _redis.KeyExpireAsync(key, _keyTtl);

            if (newValue >= _dailyThresholdUsd)
            {
                _logger.LogWarning(
                    "Global cost circuit breaker threshold reached. Daily spend ${Used:F2} >= ${Threshold:F2}",
                    newValue, _dailyThresholdUsd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record global spend of ${Cost:F4}", costUsd);
        }
    }

    public async Task<(double Used, double Threshold)> GetStatusAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var key = $"cena:costcircuit:global:{today}";
        var used = await _redis.StringGetAsync(key);
        return (used.TryParse(out double u) ? u : 0.0, _dailyThresholdUsd);
    }
}
