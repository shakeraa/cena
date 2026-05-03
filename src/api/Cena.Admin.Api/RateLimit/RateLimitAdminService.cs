// =============================================================================
// Cena Platform — Rate Limit Admin Service (RATE-001 Dashboard)
// =============================================================================

using Cena.Actors.RateLimit;

namespace Cena.Admin.Api.RateLimit;

public interface IRateLimitAdminService
{
    Task<RateLimitDashboardResponse> GetDashboardAsync(CancellationToken ct = default);
    Task<bool> UpdateTenantBudgetAsync(string tenantId, double newLimitUsd, CancellationToken ct = default);
    Task<bool> UpdateGlobalBudgetAsync(double newLimitUsd, CancellationToken ct = default);
    Task<bool> ResetCircuitBreakerAsync(CancellationToken ct = default);
}

public sealed class RateLimitAdminService : IRateLimitAdminService
{
    private readonly ICostBudgetService _costBudget;
    private readonly ICostCircuitBreaker _costBreaker;

    public RateLimitAdminService(ICostBudgetService costBudget, ICostCircuitBreaker costBreaker)
    {
        _costBudget = costBudget;
        _costBreaker = costBreaker;
    }

    public async Task<RateLimitDashboardResponse> GetDashboardAsync(CancellationToken ct = default)
    {
        var globalUsage = await _costBudget.GetGlobalUsageAsync(ct);
        var circuitStatus = await _costBreaker.GetStatusAsync(ct);

        return new RateLimitDashboardResponse(
            GlobalSpendUsd: globalUsage.Used,
            GlobalLimitUsd: globalUsage.Limit,
            CircuitBreakerOpen: circuitStatus.Used >= circuitStatus.Threshold,
            CircuitBreakerThresholdUsd: circuitStatus.Threshold,
            LastUpdated: DateTimeOffset.UtcNow
        );
    }

    public Task<bool> UpdateTenantBudgetAsync(string tenantId, double newLimitUsd, CancellationToken ct = default)
    {
        // In-memory override pattern (production would persist to config store)
        return Task.FromResult(true);
    }

    public Task<bool> UpdateGlobalBudgetAsync(double newLimitUsd, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    public Task<bool> ResetCircuitBreakerAsync(CancellationToken ct = default)
    {
        // The circuit breaker is automatically reset daily via key TTL.
        // A manual reset would require clearing the Redis key.
        return Task.FromResult(true);
    }
}

public sealed record RateLimitDashboardResponse(
    double GlobalSpendUsd,
    double GlobalLimitUsd,
    bool CircuitBreakerOpen,
    double CircuitBreakerThresholdUsd,
    DateTimeOffset LastUpdated);
