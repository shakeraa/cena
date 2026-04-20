// =============================================================================
// Cena Platform -- Subject Key Dev-Fallback Health Check (ADR-0038, prr-003b)
//
// Fails /health/ready in Production environments when
// SubjectKeyDerivation.IsUsingDevFallback is true. The hardcoded dev seed
// makes crypto-shred a no-op (every host with that seed derives the same
// keys, and destroying the column value does not destroy the key material
// known to the code). Production hosts MUST set CENA_PII_ROOT_KEY_BASE64
// to a 32-byte Base64-encoded secret.
//
// Development and Testing environments return Healthy even when the
// dev fallback is active — local loops otherwise require setting the env
// var for every test run.
//
// Registered automatically by
// SubjectKeyStoreRegistration.AddSubjectKeyStore(services, config, env).
// =============================================================================

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance.KeyStore;

/// <summary>
/// Fails the readiness probe in Production when the subject-key derivation
/// is using the dev-only fallback seed. See ADR-0038 §"Root key provisioning".
/// </summary>
public sealed class SubjectKeyDevFallbackCheck : IHealthCheck
{
    internal const string FailureDescription =
        "SubjectKeyDerivation is using the DEV-ONLY fallback seed in a non-Development environment. "
        + "Crypto-shred (ADR-0038) is effectively a no-op in this state. "
        + "Set the CENA_PII_ROOT_KEY_BASE64 environment variable to a 32-byte Base64-encoded secret "
        + "before starting the host.";

    private readonly SubjectKeyDerivation _derivation;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SubjectKeyDevFallbackCheck>? _logger;

    public SubjectKeyDevFallbackCheck(
        SubjectKeyDerivation derivation,
        IHostEnvironment environment,
        ILogger<SubjectKeyDevFallbackCheck>? logger = null)
    {
        _derivation = derivation ?? throw new ArgumentNullException(nameof(derivation));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_derivation.IsUsingDevFallback)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "SubjectKeyDerivation is using a production root key."));
        }

        // Dev and Testing environments: dev fallback is expected, log only.
        if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"SubjectKeyDerivation is using the dev fallback seed (environment: {_environment.EnvironmentName})."));
        }

        _logger?.LogCritical(
            "[SIEM] SubjectKeyDevFallbackCheck FAILED: environment={Environment}. {Description}",
            _environment.EnvironmentName, FailureDescription);

        return Task.FromResult(HealthCheckResult.Unhealthy(FailureDescription));
    }
}
