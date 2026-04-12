// =============================================================================
// Cena Platform -- Ingestion Settings Service Tests
// FIND-ARCH-020: Tests for real IMAP and Cloud directory connection tests
// =============================================================================

using Cena.Admin.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests;

public class IngestionSettingsServiceTests
{
    private readonly IngestionSettingsService _service;

    public IngestionSettingsServiceTests()
    {
        // We can't use a real document store in unit tests,
        // so we test the connection methods which don't need the store
        _service = new IngestionSettingsService(
            null!,
            new NullLogger<IngestionSettingsService>());
    }

    // ── Email Connection Tests ──

    [Fact]
    public async Task TestEmailConnectionAsync_MissingHost_ReturnsFail()
    {
        var config = new EmailIngestionConfig
        {
            ImapHost = "",
            ImapPort = 993,
            EmailAddress = "test@example.com"
        };

        var result = await _service.TestEmailConnectionAsync(config, "password");

        Assert.False(result.Success);
        Assert.Equal("IMAP host is required", result.Error);
        Assert.Equal("CONFIG_MISSING_HOST", result.Details);
    }

    [Fact]
    public async Task TestEmailConnectionAsync_MissingEmail_ReturnsFail()
    {
        var config = new EmailIngestionConfig
        {
            ImapHost = "imap.example.com",
            ImapPort = 993,
            EmailAddress = ""
        };

        var result = await _service.TestEmailConnectionAsync(config, "password");

        Assert.False(result.Success);
        Assert.Equal("Email address is required", result.Error);
        Assert.Equal("CONFIG_MISSING_EMAIL", result.Details);
    }

    [Fact]
    public async Task TestEmailConnectionAsync_MissingPassword_ReturnsFail()
    {
        var config = new EmailIngestionConfig
        {
            ImapHost = "imap.example.com",
            ImapPort = 993,
            EmailAddress = "test@example.com"
        };

        var result = await _service.TestEmailConnectionAsync(config, null);

        Assert.False(result.Success);
        Assert.Equal("Password is required for connection test", result.Error);
        Assert.Equal("CONFIG_MISSING_PASSWORD", result.Details);
    }

    [Fact]
    public async Task TestEmailConnectionAsync_InvalidPort_ReturnsFail()
    {
        var config = new EmailIngestionConfig
        {
            ImapHost = "imap.example.com",
            ImapPort = 99999,
            EmailAddress = "test@example.com"
        };

        var result = await _service.TestEmailConnectionAsync(config, "password");

        Assert.False(result.Success);
        Assert.Contains("Invalid port", result.Error);
        Assert.Equal("CONFIG_INVALID_PORT", result.Details);
    }

    // ── Cloud Directory Tests ──

    [Fact]
    public async Task TestCloudDirAsync_MissingPath_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "local",
            Path = ""
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.False(result.Success);
        Assert.Equal("Path is required", result.Error);
        Assert.Equal("CONFIG_MISSING_PATH", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_LocalPathWithTraversal_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "local",
            Path = "../secret"
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.False(result.Success);
        Assert.Equal("Path contains invalid characters", result.Error);
        Assert.Equal("INVALID_PATH", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_LocalNonExistentPath_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "local",
            Path = "/non/existent/path/xyz123"
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.False(result.Success);
        Assert.Equal("Directory does not exist", result.Error);
        Assert.Equal("DIR_NOT_FOUND", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_LocalValidPath_ReturnsSuccess()
    {
        var config = new CloudDirConfig
        {
            Provider = "local",
            Path = "/tmp"  // /tmp should exist on most Unix systems
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.True(result.Success);
        Assert.NotNull(result.Details);
        Assert.Contains("files", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_S3MissingAccessKey_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "s3",
            Path = "my-bucket/prefix"
        };

        var result = await _service.TestCloudDirAsync(config, "secret");

        Assert.False(result.Success);
        Assert.Equal("Access Key ID is required for S3", result.Error);
        Assert.Equal("CONFIG_MISSING_ACCESS_KEY", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_S3MissingSecretKey_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "s3",
            Path = "my-bucket/prefix",
            AccessKeyId = "AKIAIOSFODNN7EXAMPLE"
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.False(result.Success);
        Assert.Equal("Secret Key is required for S3", result.Error);
        Assert.Equal("CONFIG_MISSING_SECRET_KEY", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_AzureMissingCredentials_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "azure",
            Path = "my-container/prefix"
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.False(result.Success);
        Assert.Equal("Storage account name or connection string is required", result.Error);
        Assert.Equal("CONFIG_MISSING_CREDENTIALS", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_GcsMissingCredentials_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "gcs",
            Path = "my-bucket/prefix"
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.False(result.Success);
        Assert.Equal("Service account JSON key is required for GCS", result.Error);
        Assert.Equal("CONFIG_MISSING_CREDENTIALS", result.Details);
    }

    [Fact]
    public async Task TestCloudDirAsync_UnknownProvider_ReturnsFail()
    {
        var config = new CloudDirConfig
        {
            Provider = "unknown-provider",
            Path = "/some/path"
        };

        var result = await _service.TestCloudDirAsync(config, null);

        Assert.False(result.Success);
        Assert.Contains("Unknown provider", result.Error);
        Assert.Equal("UNKNOWN_PROVIDER", result.Details);
    }

    // ── Connection Result Record Tests ──

    [Fact]
    public void ConnectionTestResult_Ok_ReturnsSuccess()
    {
        var result = ConnectionTestResult.Ok("All good");

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal("All good", result.Details);
    }

    [Fact]
    public void ConnectionTestResult_Fail_ReturnsFailure()
    {
        var result = ConnectionTestResult.Fail("Auth failed", "AUTH_ERROR");

        Assert.False(result.Success);
        Assert.Equal("Auth failed", result.Error);
        Assert.Equal("AUTH_ERROR", result.Details);
    }
}
