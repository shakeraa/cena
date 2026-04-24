// =============================================================================
// Cena Platform -- Subject Key Production Fail-Fast Tests (ADR-0038, prr-003b)
//
// Verifies that SubjectKeyDevFallbackCheck returns Unhealthy in Production
// when SubjectKeyDerivation.IsUsingDevFallback == true, and Healthy in
// Development + Testing.
//
// This replaces the earlier compliance-health-check-as-ADR-note by making
// the check an enforced IHealthCheck wired into /health/ready via
// SubjectKeyStoreRegistration.AddSubjectKeyStore(config, env).
// =============================================================================

using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Cena.Actors.Tests.Compliance;

public sealed class SubjectKeyProductionFailFastTests
{
    private static readonly byte[] ProductionRootKey = new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    };

    // -------------------------------------------------------------------------
    // Test 1 — Production + dev fallback = Unhealthy (prevents boot).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Production_environment_with_dev_fallback_reports_unhealthy()
    {
        var derivation = new SubjectKeyDerivation(
            SeedForDevFallback(), "test-install", isDevFallback: true);
        var env = new StubHostEnvironment("Production");
        var check = new SubjectKeyDevFallbackCheck(derivation, env);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DEV-ONLY fallback", result.Description);
        Assert.Contains("CENA_PII_ROOT_KEY_BASE64", result.Description);
    }

    [Fact]
    public async Task Production_environment_with_real_key_reports_healthy()
    {
        var derivation = new SubjectKeyDerivation(
            ProductionRootKey, "test-install", isDevFallback: false);
        var env = new StubHostEnvironment("Production");
        var check = new SubjectKeyDevFallbackCheck(derivation, env);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Development tolerates dev fallback without failing boot.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Development_environment_with_dev_fallback_reports_healthy()
    {
        var derivation = new SubjectKeyDerivation(
            SeedForDevFallback(), "test-install", isDevFallback: true);
        var env = new StubHostEnvironment(Environments.Development);
        var check = new SubjectKeyDevFallbackCheck(derivation, env);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // -------------------------------------------------------------------------
    // Test 3 — Testing environment also tolerates the dev fallback (xUnit,
    //          CI, integration test containers default to Testing).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Testing_environment_with_dev_fallback_reports_healthy()
    {
        var derivation = new SubjectKeyDerivation(
            SeedForDevFallback(), "test-install", isDevFallback: true);
        var env = new StubHostEnvironment("Testing");
        var check = new SubjectKeyDevFallbackCheck(derivation, env);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // -------------------------------------------------------------------------
    // Test 4 — Staging is treated as production-grade (any non-Dev, non-Test).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Staging_environment_with_dev_fallback_reports_unhealthy()
    {
        var derivation = new SubjectKeyDerivation(
            SeedForDevFallback(), "test-install", isDevFallback: true);
        var env = new StubHostEnvironment("Staging");
        var check = new SubjectKeyDevFallbackCheck(derivation, env);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // -------------------------------------------------------------------------
    // Test 5 — Constructor null-guards.
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_rejects_null_derivation()
    {
        var env = new StubHostEnvironment("Production");
        Assert.Throws<ArgumentNullException>(
            () => new SubjectKeyDevFallbackCheck(null!, env));
    }

    [Fact]
    public void Constructor_rejects_null_environment()
    {
        var derivation = new SubjectKeyDerivation(
            ProductionRootKey, "test-install", isDevFallback: false);
        Assert.Throws<ArgumentNullException>(
            () => new SubjectKeyDevFallbackCheck(derivation, null!));
    }

    // -------------------------------------------------------------------------
    // Test 6 — End-to-end with FromEnvironment: env var unset in production
    //          leads to an unhealthy probe (the boot-time contract).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FromEnvironment_without_env_var_in_production_is_unhealthy()
    {
        var saved = Environment.GetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar, null);
            var derivation = SubjectKeyDerivation.FromEnvironment();
            Assert.True(derivation.IsUsingDevFallback);

            var env = new StubHostEnvironment("Production");
            var check = new SubjectKeyDevFallbackCheck(derivation, env);

            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar, saved);
        }
    }

    [Fact]
    public async Task FromEnvironment_with_env_var_in_production_is_healthy()
    {
        var saved = Environment.GetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(
                SubjectKeyDerivation.RootKeyEnvVar,
                Convert.ToBase64String(ProductionRootKey));

            var derivation = SubjectKeyDerivation.FromEnvironment();
            Assert.False(derivation.IsUsingDevFallback);

            var env = new StubHostEnvironment("Production");
            var check = new SubjectKeyDevFallbackCheck(derivation, env);

            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar, saved);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // The real dev fallback seed is private to SubjectKeyDerivation. For
    // unit tests we build a distinct 32-byte stand-in and mark the
    // derivation as dev-fallback explicitly.
    private static byte[] SeedForDevFallback()
    {
        var seed = new byte[32];
        for (var i = 0; i < seed.Length; i++) seed[i] = 0x42;
        return seed;
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Cena.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
