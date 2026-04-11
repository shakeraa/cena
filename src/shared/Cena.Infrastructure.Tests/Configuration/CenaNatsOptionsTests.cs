// =============================================================================
// Cena Platform — NATS Options Tests (FIND-sec-003)
//
// Tests for CenaNatsOptions.GetApiAuth() to ensure proper credential resolution
// and environment-gated dev fallback.
// =============================================================================

using Cena.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Cena.Infrastructure.Tests.Configuration;

public sealed class CenaNatsOptionsTests
{
    /// <summary>
    /// Verifies that explicit configuration values are used when provided.
    /// </summary>
    [Fact]
    public void GetApiAuth_ExplicitConfig_ReturnsConfiguredValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NATS:ApiUsername"] = "test_user",
                ["NATS:ApiPassword"] = "test_pass"
            })
            .Build();

        var env = new TestHostEnvironment(isDevelopment: false);
        var (username, password) = CenaNatsOptions.GetApiAuth(config, env);

        Assert.Equal("test_user", username);
        Assert.Equal("test_pass", password);
    }

    /// <summary>
    /// Verifies that legacy Nats:User/Nats:Password keys are also supported.
    /// </summary>
    [Fact]
    public void GetApiAuth_LegacyConfigKeys_ReturnsConfiguredValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nats:User"] = "legacy_user",
                ["Nats:Password"] = "legacy_pass"
            })
            .Build();

        var env = new TestHostEnvironment(isDevelopment: false);
        var (username, password) = CenaNatsOptions.GetApiAuth(config, env);

        Assert.Equal("legacy_user", username);
        Assert.Equal("legacy_pass", password);
    }

    /// <summary>
    /// Verifies that environment variables are used when config is not set.
    /// </summary>
    [Fact]
    public void GetApiAuth_EnvironmentVariables_ReturnsEnvValues()
    {
        // Set environment variables
        Environment.SetEnvironmentVariable("NATS_API_USERNAME", "env_user");
        Environment.SetEnvironmentVariable("NATS_API_PASSWORD", "env_pass");

        try
        {
            var config = new ConfigurationBuilder().Build();
            var env = new TestHostEnvironment(isDevelopment: false);
            var (username, password) = CenaNatsOptions.GetApiAuth(config, env);

            Assert.Equal("env_user", username);
            Assert.Equal("env_pass", password);
        }
        finally
        {
            // Clean up
            Environment.SetEnvironmentVariable("NATS_API_USERNAME", null);
            Environment.SetEnvironmentVariable("NATS_API_PASSWORD", null);
        }
    }

    /// <summary>
    /// Verifies that non-Development environment throws when credentials not configured.
    /// </summary>
    [Fact]
    public void GetApiAuth_NonDevelopmentMissingConfig_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment(isDevelopment: false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CenaNatsOptions.GetApiAuth(config, env));

        Assert.Contains("NATS API credentials not configured", ex.Message);
    }

    /// <summary>
    /// Verifies that Development environment uses fallback defaults when not configured.
    /// </summary>
    [Fact]
    public void GetApiAuth_DevelopmentMissingConfig_UsesDevDefaults()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment(isDevelopment: true);
        var (username, password) = CenaNatsOptions.GetApiAuth(config, env);

        Assert.Equal("cena_api_user", username);
        Assert.Equal("dev_api_pass", password);
    }

    /// <summary>
    /// Verifies that Development environment uses explicit config over defaults.
    /// </summary>
    [Fact]
    public void GetApiAuth_DevelopmentWithConfig_UsesConfiguredValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NATS:ApiUsername"] = "dev_config_user",
                ["NATS:ApiPassword"] = "dev_config_pass"
            })
            .Build();

        var env = new TestHostEnvironment(isDevelopment: true);
        var (username, password) = CenaNatsOptions.GetApiAuth(config, env);

        Assert.Equal("dev_config_user", username);
        Assert.Equal("dev_config_pass", password);
    }

    /// <summary>
    /// Minimal IHostEnvironment implementation for testing.
    /// </summary>
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "TestApp";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;

        public TestHostEnvironment(bool isDevelopment)
        {
            EnvironmentName = isDevelopment ? "Development" : "Production";
        }
    }
}
