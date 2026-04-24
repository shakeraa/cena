// =============================================================================
// Cena Platform — Cost Budget Service (RATE-001 Tier 3)
// Redis-backed per-institute daily budget and global daily spend tracking.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.RateLimit;

/// <summary>
/// Tracks and enforces daily cost budgets per tenant and globally in USD.
/// </summary>
public interface ICostBudgetService
{
    /// <summary>
    /// Attempts to charge <paramref name="estimatedCostUsd"/> against the tenant budget.
    /// Returns true if charge succeeded, false if budget exhausted.
    /// </summary>
    Task<bool> TryChargeTenantAsync(string tenantId, double estimatedCostUsd, CancellationToken ct = default);

    /// <summary>
    /// Gets current tenant budget usage.
    /// </summary>
    Task<(double Used, double Limit)> GetTenantUsageAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets current global spend.
    /// </summary>
    Task<(double Used, double Limit)> GetGlobalUsageAsync(CancellationToken ct = default);
}

/// <summary>
/// Redis-backed implementation of cost budget tracking.
/// </summary>
public sealed class RedisCostBudgetService : ICostBudgetService
{
    private readonly IDatabase _redis;
    private readonly ILogger<RedisCostBudgetService> _logger;
    private readonly double _globalDailyLimitUsd;
    private readonly double _tenantDailyLimitUsd;
    private readonly TimeSpan _keyTtl = TimeSpan.FromHours(25);

    public RedisCostBudgetService(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisCostBudgetService> logger)
    {
        _redis = redis.GetDatabase();
        _logger = logger;
        _globalDailyLimitUsd = configuration.GetValue<double>("Cena:CostBudget:GlobalDailyUsd", 1_000.0);
        _tenantDailyLimitUsd = configuration.GetValue<double>("Cena:CostBudget:TenantDailyUsd", 100.0);
    }

    public async Task<bool> TryChargeTenantAsync(string tenantId, double estimatedCostUsd, CancellationToken ct = default)
    {
        if (estimatedCostUsd <= 0) return true;

        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var globalKey = $"cena:costbudget:global:{today}";
        var tenantKey = $"cena:costbudget:tenant:{tenantId}:{today}";

        try
        {
            var globalUsed = await _redis.StringIncrementAsync(globalKey, estimatedCostUsd);
            if (globalUsed > _globalDailyLimitUsd)
            {
                await _redis.StringDecrementAsync(globalKey, estimatedCostUsd);
                _logger.LogWarning(
                    "Global daily cost budget exhausted. Used: ${Used:F2}, Limit: ${Limit:F2}, Requested: ${Requested:F2}",
                    globalUsed - estimatedCostUsd, _globalDailyLimitUsd, estimatedCostUsd);
                return false;
            }

            var tenantUsed = await _redis.StringIncrementAsync(tenantKey, estimatedCostUsd);
            if (tenantUsed > _tenantDailyLimitUsd)
            {
                await _redis.StringDecrementAsync(globalKey, estimatedCostUsd);
                await _redis.StringDecrementAsync(tenantKey, estimatedCostUsd);
                _logger.LogWarning(
                    "Tenant {TenantId} daily cost budget exhausted. Used: ${Used:F2}, Limit: ${Limit:F2}, Requested: ${Requested:F2}",
                    tenantId, tenantUsed - estimatedCostUsd, _tenantDailyLimitUsd, estimatedCostUsd);
                return false;
            }

            await _redis.KeyExpireAsync(globalKey, _keyTtl);
            await _redis.KeyExpireAsync(tenantKey, _keyTtl);

            _logger.LogDebug(
                "Charged ${Cost:F4} for tenant {TenantId}. Global: ${GlobalUsed:F2}/{GlobalLimit:F2}, Tenant: ${TenantUsed:F2}/{TenantLimit:F2}",
                estimatedCostUsd, tenantId, globalUsed, _globalDailyLimitUsd, tenantUsed, _tenantDailyLimitUsd);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis cost budget check failed for tenant {TenantId} — failing open", tenantId);
            return true;
        }
    }

    public async Task<(double Used, double Limit)> GetTenantUsageAsync(string tenantId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var tenantKey = $"cena:costbudget:tenant:{tenantId}:{today}";
        var used = await _redis.StringGetAsync(tenantKey);
        return (used.TryParse(out double usedValue) ? usedValue : 0.0, _tenantDailyLimitUsd);
    }

    public async Task<(double Used, double Limit)> GetGlobalUsageAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var globalKey = $"cena:costbudget:global:{today}";
        var used = await _redis.StringGetAsync(globalKey);
        return (used.TryParse(out double usedValue) ? usedValue : 0.0, _globalDailyLimitUsd);
    }
}
