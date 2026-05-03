// =============================================================================
// Cena Platform — Secret-store abstraction (prr-017, scaffold)
//
// Minimal scaffold for the secret-store abstraction called by
// Cena.Actors.Host / Cena.Admin.Api.Host during bootstrap. Production
// adapters (AWS Secrets Manager, GCP Secret Manager, HashiCorp Vault) land
// in prr-017's next slice; for now we ship an in-process `NullSecretStore`
// that reads from `IConfiguration` so local + CI hosts boot cleanly.
//
// Contract highlights:
//   * `ISecretStore.GetAsync(key)` returns the current secret value.
//   * `AddCenaSecretStore(env)` wires the correct implementation for the
//     environment. In Development the Null (config-backed) store is
//     acceptable; outside Development the caller MUST have already
//     registered a real adapter before this call — we fail-fast if the
//     container resolves to the Null default in non-Development.
//
// This scaffold intentionally does not implement rotation, versioning, or
// vault round-trip; those are tracked in the prr-017 task body. It exists
// here so the solution builds cleanly and so the secret-store seam is
// present for downstream consumers (Mashov credentials, etc).
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Secrets;

/// <summary>
/// Read-side contract for the secret store. Writes happen out-of-band
/// (infra IaC / admin console), not through application code.
/// </summary>
public interface ISecretStore
{
    /// <summary>Fetch the current value of a secret. Null if the key is unknown.</summary>
    ValueTask<string?> GetAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Configuration-backed store. Safe for Development + CI only — it reads
/// from <c>appsettings.Development.json</c> / env vars, which is exactly
/// the posture we ship for local hosts.
/// </summary>
public sealed class ConfigurationSecretStore : ISecretStore
{
    private readonly IConfiguration _config;
    public ConfigurationSecretStore(IConfiguration config) { _config = config; }

    public ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return new ValueTask<string?>((string?)null);
        return new ValueTask<string?>(_config[key]);
    }
}

public static class SecretStoreServiceCollectionExtensions
{
    /// <summary>
    /// Wire the secret store for the host. Development gets the config-
    /// backed Null fallback; other environments MUST have a real adapter
    /// pre-registered, otherwise boot fails hard (prr-017 contract).
    /// </summary>
    public static IServiceCollection AddCenaSecretStore(
        this IServiceCollection services, IHostEnvironment environment)
    {
        // Idempotent — if a real adapter is already registered, do nothing.
        foreach (var s in services)
        {
            if (s.ServiceType == typeof(ISecretStore)) return services;
        }

        if (environment.IsDevelopment())
        {
            services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
            return services;
        }

        // Non-Development fallback to Null is a misconfiguration bug.
        // Fail fast rather than boot with silent placeholders.
        services.AddSingleton<ISecretStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Cena.Secrets");
            logger.LogCritical(
                "[SECRETS] No ISecretStore adapter registered in environment '{Env}'. "
                + "Register AWS Secrets Manager / GCP Secret Manager / Vault adapter before "
                + "AddCenaSecretStore on non-Development hosts (prr-017).",
                environment.EnvironmentName);
            throw new InvalidOperationException(
                "No ISecretStore adapter is registered in non-Development environment. "
                + "See docs/ops/runbooks/mashov-credentials-rotation.md for the prr-017 "
                + "rotation runbook.");
        });
        return services;
    }
}
