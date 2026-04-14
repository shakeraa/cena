// =============================================================================
// Cena Platform — Dependency Health Checks (RDY-011)
// Real connectivity checks for PostgreSQL (Marten), Redis, NATS.
// Used by /health/ready to tell K8s whether the pod can serve traffic.
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Infrastructure.Health;

/// <summary>
/// RDY-011: Checks PostgreSQL connectivity via Marten.
/// Runs a lightweight query (SELECT 1) with a 3-second timeout.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly IDocumentStore _store;
    private readonly ILogger<PostgresHealthCheck> _logger;

    public PostgresHealthCheck(IDocumentStore store, ILogger<PostgresHealthCheck> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            await using var session = _store.QuerySession();
            // Lightweight connectivity test via Npgsql
            var conn = session.Connection;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 3;
            await cmd.ExecuteScalarAsync(cts.Token);

            return HealthCheckResult.Healthy("PostgreSQL is reachable");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("PostgreSQL health check timed out (>3s)");
            return HealthCheckResult.Unhealthy("PostgreSQL health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL health check failed");
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable", ex);
        }
    }
}

/// <summary>
/// RDY-011: Checks Redis connectivity via StackExchange.Redis PING.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IConnectionMultiplexer redis, ILogger<RedisHealthCheck> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var latency = await db.PingAsync();

            if (latency > TimeSpan.FromSeconds(3))
            {
                _logger.LogWarning("Redis PING latency {Latency}ms exceeds 3s threshold", latency.TotalMilliseconds);
                return HealthCheckResult.Degraded($"Redis latency high: {latency.TotalMilliseconds:F0}ms");
            }

            return HealthCheckResult.Healthy($"Redis OK ({latency.TotalMilliseconds:F0}ms)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis is unreachable", ex);
        }
    }
}

/// <summary>
/// RDY-011: Checks NATS connectivity.
/// NATS down = degraded (REST still works, real-time features disabled).
/// </summary>
public sealed class NatsHealthCheck : IHealthCheck
{
    private readonly NATS.Client.Core.INatsConnection _nats;
    private readonly ILogger<NatsHealthCheck> _logger;

    public NatsHealthCheck(NATS.Client.Core.INatsConnection nats, ILogger<NatsHealthCheck> logger)
    {
        _nats = nats;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check connection state
            var state = _nats.ConnectionState;
            if (state == NATS.Client.Core.NatsConnectionState.Open)
            {
                return HealthCheckResult.Healthy("NATS connected");
            }

            _logger.LogWarning("NATS connection state: {State}", state);
            return HealthCheckResult.Degraded($"NATS connection state: {state}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NATS health check failed — degraded (REST still functional)");
            return HealthCheckResult.Degraded("NATS unreachable — real-time features disabled", ex);
        }
    }
}

/// <summary>
/// RDY-011: Extension methods to register dependency health checks.
/// </summary>
public static class HealthCheckRegistration
{
    /// <summary>
    /// Register Student API health checks: PostgreSQL (critical), Redis (critical), NATS (degraded).
    /// </summary>
    public static IHealthChecksBuilder AddCenaStudentHealthChecks(this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<PostgresHealthCheck>("postgresql", HealthStatus.Unhealthy, new[] { "ready", "db" })
            .AddCheck<RedisHealthCheck>("redis", HealthStatus.Unhealthy, new[] { "ready", "cache" })
            .AddCheck<NatsHealthCheck>("nats", HealthStatus.Degraded, new[] { "ready", "messaging" });
    }

    /// <summary>
    /// Register Admin API health checks: PostgreSQL (critical), Redis (critical).
    /// Admin API does not use NATS directly.
    /// </summary>
    public static IHealthChecksBuilder AddCenaAdminHealthChecks(this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<PostgresHealthCheck>("postgresql", HealthStatus.Unhealthy, new[] { "ready", "db" })
            .AddCheck<RedisHealthCheck>("redis", HealthStatus.Unhealthy, new[] { "ready", "cache" });
    }
}
