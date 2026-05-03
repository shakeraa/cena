// =============================================================================
// FIND-sec-015: AI Token Budget Service
//
// Provides global and per-tenant caps on AI tutor costs.
// Uses Redis for distributed daily token tracking.
//
// Budgets are checked BEFORE the LLM call to prevent overspend.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Infrastructure.Ai;

/// <summary>
/// Tracks and enforces AI token budgets per tenant and globally.
/// </summary>
public interface IAiTokenBudgetService
{
    /// <summary>
    /// Attempts to reserve estimated tokens from the daily budget.
    /// Returns true if reservation succeeded, false if budget exhausted.
    /// </summary>
    Task<bool> TryReserveAsync(string tenantId, int estimatedTokens, CancellationToken ct = default);
    
    /// <summary>
    /// Gets current budget usage for a tenant.
    /// </summary>
    Task<(long Used, long Limit)> GetTenantUsageAsync(string tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets current global budget usage.
    /// </summary>
    Task<(long Used, long Limit)> GetGlobalUsageAsync(CancellationToken ct = default);
}

/// <summary>
/// Redis-backed implementation of AI token budget tracking.
/// </summary>
public sealed class AiTokenBudgetService : IAiTokenBudgetService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AiTokenBudgetService> _logger;
    private readonly long _globalDailyTokenLimit;
    private readonly long _tenantDailyTokenLimit;
    private readonly TimeSpan _keyTtl = TimeSpan.FromHours(25); // Slightly over 24h for timezone safety

    public AiTokenBudgetService(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<AiTokenBudgetService> logger)
    {
        _redis = redis;
        _logger = logger;
        
        // Configurable limits with sensible defaults
        _globalDailyTokenLimit = configuration.GetValue<long>("Cena:LlmBudget:GlobalDailyTokens", 10_000_000); // 10M tokens/day
        _tenantDailyTokenLimit = configuration.GetValue<long>("Cena:LlmBudget:TenantDailyTokens", 500_000);   // 500K tokens/day per tenant
    }

    public async Task<bool> TryReserveAsync(string tenantId, int estimatedTokens, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        
        var globalKey = $"cena:llm:budget:global:{today}";
        var tenantKey = $"cena:llm:budget:tenant:{tenantId}:{today}";
        
        // Check and increment global budget atomically
        var globalUsed = await db.StringIncrementAsync(globalKey, estimatedTokens);
        if (globalUsed > _globalDailyTokenLimit)
        {
            // Rollback global reservation
            await db.StringDecrementAsync(globalKey, estimatedTokens);
            _logger.LogWarning(
                "Global AI token budget exhausted. Used: {Used}, Limit: {Limit}, Requested: {Requested}",
                globalUsed - estimatedTokens, _globalDailyTokenLimit, estimatedTokens);
            return false;
        }
        
        // Check and increment tenant budget atomically
        var tenantUsed = await db.StringIncrementAsync(tenantKey, estimatedTokens);
        if (tenantUsed > _tenantDailyTokenLimit)
        {
            // Rollback both reservations
            await db.StringDecrementAsync(globalKey, estimatedTokens);
            await db.StringDecrementAsync(tenantKey, estimatedTokens);
            _logger.LogWarning(
                "Tenant {TenantId} AI token budget exhausted. Used: {Used}, Limit: {Limit}, Requested: {Requested}",
                tenantId, tenantUsed - estimatedTokens, _tenantDailyTokenLimit, estimatedTokens);
            return false;
        }
        
        // Set expiry on both keys (idempotent - multiple sets don't hurt)
        await db.KeyExpireAsync(globalKey, _keyTtl);
        await db.KeyExpireAsync(tenantKey, _keyTtl);
        
        _logger.LogDebug(
            "Reserved {Tokens} tokens for tenant {TenantId}. Global: {GlobalUsed}/{GlobalLimit}, Tenant: {TenantUsed}/{TenantLimit}",
            estimatedTokens, tenantId, globalUsed, _globalDailyTokenLimit, tenantUsed, _tenantDailyTokenLimit);
        
        return true;
    }

    public async Task<(long Used, long Limit)> GetTenantUsageAsync(string tenantId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var tenantKey = $"cena:llm:budget:tenant:{tenantId}:{today}";
        
        var used = await db.StringGetAsync(tenantKey);
        return (used.TryParse(out long usedValue) ? usedValue : 0, _tenantDailyTokenLimit);
    }

    public async Task<(long Used, long Limit)> GetGlobalUsageAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var globalKey = $"cena:llm:budget:global:{today}";
        
        var used = await db.StringGetAsync(globalKey);
        return (used.TryParse(out long usedValue) ? usedValue : 0, _globalDailyTokenLimit);
    }
}

/// <summary>
/// DI registration for AI token budget service.
/// </summary>
public static class AiTokenBudgetRegistration
{
    public static IServiceCollection AddAiTokenBudget(this IServiceCollection services)
    {
        services.AddSingleton<IAiTokenBudgetService, AiTokenBudgetService>();
        return services;
    }
}
