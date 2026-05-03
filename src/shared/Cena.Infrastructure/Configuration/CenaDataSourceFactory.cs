// =============================================================================
// Cena Platform -- Shared NpgsqlDataSource Factory
// Creates a single NpgsqlDataSource per process with explicit pool sizing.
// All PostgreSQL consumers (Marten, pgvector, raw queries) share this pool.
//
// Why: Without this, Npgsql creates a separate pool per connection string
// variant (default max 100 each). Under peak load (200+ students at 8am),
// multiple implicit pools exhaust PostgreSQL max_connections.
//
// With a shared NpgsqlDataSource:
//   - One pool per process, explicitly sized
//   - Marten sessions, pgvector queries, and admin queries all draw from it
//   - Pool exhaustion becomes visible via OpenTelemetry metrics
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cena.Infrastructure.Configuration;

public static class CenaDataSourceFactory
{
    /// <summary>
    /// Registers a singleton NpgsqlDataSource with explicit pool sizing.
    /// Call BEFORE AddMarten() so Marten can consume the data source.
    ///
    /// Pool sizing guide:
    ///   - Actor Host: 50 max (actor activations + event flush + background services)
    ///   - API Host:   30 max (admin endpoints + pgvector queries)
    ///   - Total:      80 connections against PostgreSQL max_connections=150
    /// </summary>
    public static IServiceCollection AddCenaDataSource(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        int maxPoolSize = 50,
        int minPoolSize = 5)
    {
        var connectionString = CenaConnectionStrings.GetPostgres(configuration, environment);

        var builder = new NpgsqlDataSourceBuilder(connectionString);

        // Override pool sizing regardless of what's in the connection string
        builder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        builder.ConnectionStringBuilder.MinPoolSize = minPoolSize;

        // Connection lifetime: recycle connections after 15 minutes to handle
        // DNS changes, PG failover, and connection state drift
        builder.ConnectionStringBuilder.ConnectionLifetime = 900; // seconds

        // Timeout waiting for a free connection from the pool.
        // At peak, better to fail fast (5s) than queue for 30s (default).
        builder.ConnectionStringBuilder.Timeout = 5;

        // Command timeout: 30s (default) is fine for event sourcing writes
        builder.ConnectionStringBuilder.CommandTimeout = 30;

        // Enable multiplexing: allows multiple commands on a single connection
        // when they don't overlap. Reduces pool pressure significantly.
        // Safe for Marten's session-per-operation pattern.
        builder.ConnectionStringBuilder.Multiplexing = true;

        var dataSource = builder.Build();

        services.AddSingleton(dataSource);

        return services;
    }
}
