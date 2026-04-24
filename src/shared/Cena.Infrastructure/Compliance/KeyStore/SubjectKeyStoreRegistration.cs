// =============================================================================
// Cena Platform -- Subject Key Store DI Registration (ADR-0038, prr-003b)
//
// Wires:
//   - SubjectKeyDerivation       (from CENA_PII_ROOT_KEY_BASE64 or dev fallback)
//   - ISubjectKeyStore           (Postgres-backed in production, in-memory otherwise)
//   - EncryptedFieldAccessor     (facade used by write + read paths)
//   - SubjectKeyDevFallbackCheck (health-check that fails prod boot when
//                                 SubjectKeyDerivation.IsUsingDevFallback == true)
//
// Backing selection:
//   1. Explicit override via configuration key "Cena:SubjectKeyStore:Backing"
//      with value "postgres" or "in-memory". Highest priority.
//   2. Environment-based default: postgres when the host environment is NOT
//      Development/Testing; in-memory otherwise.
//   3. Fallback: in-memory (safe for unit tests and DI calls that happen
//      before the NpgsqlDataSource is available).
//
// The Postgres backing requires V0002__cena_subject_keys.sql to have been
// applied. The PgVectorMigrationService or Cena.Db.Migrator applies this
// at startup.
// =============================================================================

using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Extension methods for registering the ADR-0038 crypto-shredding primitives.
/// </summary>
public static class SubjectKeyStoreRegistration
{
    /// <summary>
    /// Configuration key controlling which ISubjectKeyStore backing to use.
    /// Values: "postgres", "in-memory". Any other value falls through to the
    /// environment-based default.
    /// </summary>
    public const string BackingConfigKey = "Cena:SubjectKeyStore:Backing";

    /// <summary>
    /// Register the subject key store + encrypted field accessor. Safe to
    /// call multiple times; uses TryAddSingleton.
    /// </summary>
    /// <remarks>
    /// This overload preserves the legacy in-memory default. New hosts
    /// should prefer <see cref="AddSubjectKeyStore(IServiceCollection, IConfiguration, IHostEnvironment)"/>
    /// which selects Postgres automatically in non-Development environments.
    /// </remarks>
    public static IServiceCollection AddSubjectKeyStore(this IServiceCollection services)
    {
        services.TryAddSingleton(sp =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(SubjectKeyDerivation));
            return SubjectKeyDerivation.FromEnvironment(logger);
        });

        services.TryAddSingleton<ISubjectKeyStore>(sp =>
        {
            var derivation = sp.GetRequiredService<SubjectKeyDerivation>();
            var logger = sp.GetService<ILogger<InMemorySubjectKeyStore>>();
            return new InMemorySubjectKeyStore(derivation, logger);
        });

        services.TryAddSingleton<EncryptedFieldAccessor>();

        return services;
    }

    /// <summary>
    /// Register the subject key store with environment-aware backing selection.
    /// In non-Development environments the Postgres backing is used by default;
    /// Development falls back to in-memory unless
    /// <c>Cena:SubjectKeyStore:Backing</c> overrides.
    /// </summary>
    public static IServiceCollection AddSubjectKeyStore(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.TryAddSingleton(sp =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(SubjectKeyDerivation));
            return SubjectKeyDerivation.FromEnvironment(logger);
        });

        var backing = ResolveBacking(configuration, environment);

        services.TryAddSingleton<ISubjectKeyStore>(sp =>
        {
            var derivation = sp.GetRequiredService<SubjectKeyDerivation>();

            if (backing == SubjectKeyStoreBacking.Postgres)
            {
                var dataSource = sp.GetService<NpgsqlDataSource>();
                if (dataSource is null)
                {
                    var fallbackLogger = sp.GetService<ILoggerFactory>()
                        ?.CreateLogger(typeof(SubjectKeyStoreRegistration));
                    fallbackLogger?.LogWarning(
                        "[SIEM] SubjectKeyStore: Postgres backing selected but no NpgsqlDataSource is registered. " +
                        "Falling back to in-memory — tombstones will NOT survive restart. " +
                        "Call AddCenaDataSource before AddSubjectKeyStore.");
                    var inMemLogger = sp.GetService<ILogger<InMemorySubjectKeyStore>>();
                    return new InMemorySubjectKeyStore(derivation, inMemLogger);
                }

                var pgLogger = sp.GetService<ILogger<PostgresSubjectKeyStore>>();
                return new PostgresSubjectKeyStore(dataSource, derivation, pgLogger);
            }

            var memLogger = sp.GetService<ILogger<InMemorySubjectKeyStore>>();
            return new InMemorySubjectKeyStore(derivation, memLogger);
        });

        services.TryAddSingleton<EncryptedFieldAccessor>();

        // Dev-fallback health-check: production boots are refused when
        // SubjectKeyDerivation.IsUsingDevFallback is true. The check is
        // idempotent-registered here so every host that wires the key store
        // gets it automatically without per-host duplication.
        services.AddHealthChecks()
            .AddCheck<SubjectKeyDevFallbackCheck>(
                name: "subject-key-dev-fallback",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "compliance" });

        return services;
    }

    internal static SubjectKeyStoreBacking ResolveBacking(
        IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration[BackingConfigKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase))
            {
                return SubjectKeyStoreBacking.Postgres;
            }
            if (string.Equals(configured, "in-memory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(configured, "inmemory", StringComparison.OrdinalIgnoreCase))
            {
                return SubjectKeyStoreBacking.InMemory;
            }
        }

        // Environment-based default: anything outside Development / Testing
        // is treated as production-grade and gets Postgres.
        return environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? SubjectKeyStoreBacking.InMemory
            : SubjectKeyStoreBacking.Postgres;
    }
}

/// <summary>Selector for the ISubjectKeyStore implementation to register.</summary>
internal enum SubjectKeyStoreBacking
{
    InMemory,
    Postgres
}
