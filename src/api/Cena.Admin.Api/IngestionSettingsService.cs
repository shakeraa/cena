// =============================================================================
// Cena Platform -- Ingestion Settings Service
// Manages persisted ingestion configuration: cloud dirs, email, messaging,
// and pipeline defaults. Backed by Marten document store.
// =============================================================================

using System.Net;
using System.Text.Json;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Azure.Storage.Blobs;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using MailKit.Net.Imap;
using MailKit.Security;
using Marten;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Cena.Admin.Api;

// ---------------------------------------------------------------------------
// Document model (persisted in PostgreSQL via Marten)
// ---------------------------------------------------------------------------

public sealed class IngestionSettingsDocument
{
    public string Id { get; set; } = "ingestion-settings-singleton";

    public List<CloudDirConfig> CloudDirectories { get; set; } = new();
    public EmailIngestionConfig? Email { get; set; }
    public List<MessagingChannelConfig> MessagingChannels { get; set; } = new();
    public PipelineConfig Pipeline { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public sealed class CloudDirConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "local";   // local, s3, gcs, azure
    public string Path { get; set; } = "";
    public string? Prefix { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AutoWatch { get; set; } = false;
    public int? WatchIntervalMinutes { get; set; }
    public string? AccessKeyId { get; set; }
    public string? Region { get; set; }
    // Note: SecretKey stored separately in secure storage, not in this document
}

public sealed class EmailIngestionConfig
{
    public bool Enabled { get; set; } = false;
    public string? ImapHost { get; set; }
    public int ImapPort { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string? EmailAddress { get; set; }
    // Note: password stored in secure storage, not here
    public string? AllowedSenders { get; set; }
    public int PollIntervalMinutes { get; set; } = 5;
    public string? SubjectFilter { get; set; }
}

public sealed class MessagingChannelConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "";             // whatsapp, telegram, slack
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = false;
    public string? WebhookUrl { get; set; }
    public string? BotToken { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? AllowedSenders { get; set; }
}

public sealed class PipelineConfig
{
    public int MaxConcurrentIngestions { get; set; } = 5;
    public int MaxFileSizeMb { get; set; } = 20;
    public bool AutoClassify { get; set; } = true;
    public bool AutoDedup { get; set; } = true;
    public float MinQualityScore { get; set; } = 0.6f;
    public List<string> AllowedFileTypes { get; set; } = new() { "pdf", "png", "jpg", "jpeg", "webp", "csv", "xlsx" };
    public string DefaultLanguage { get; set; } = "he";
    public string DefaultSubject { get; set; } = "math";
}

// ---------------------------------------------------------------------------
// Service interface
// ---------------------------------------------------------------------------

public interface IIngestionSettingsService
{
    Task<IngestionSettingsDocument> GetSettingsAsync();
    Task<IngestionSettingsDocument> UpdateSettingsAsync(IngestionSettingsDocument settings, string updatedBy);
    Task<ConnectionTestResult> TestEmailConnectionAsync(EmailIngestionConfig config, string? password);
    Task<ConnectionTestResult> TestCloudDirAsync(CloudDirConfig config, string? secretKey);
}

// ---------------------------------------------------------------------------
// Connection test result
// ---------------------------------------------------------------------------

public sealed record ConnectionTestResult(
    bool Success,
    string? Error = null,
    string? Details = null)
{
    public static ConnectionTestResult Ok(string? details = null) => new(true, null, details);
    public static ConnectionTestResult Fail(string error, string? details = null) => new(false, error, details);
}

// ---------------------------------------------------------------------------
// Service implementation
// ---------------------------------------------------------------------------

public sealed class IngestionSettingsService : IIngestionSettingsService
{
    private const string SingletonId = "ingestion-settings-singleton";

    private readonly IDocumentStore _store;
    private readonly ILogger<IngestionSettingsService> _logger;

    public IngestionSettingsService(IDocumentStore store, ILogger<IngestionSettingsService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<IngestionSettingsDocument> GetSettingsAsync()
    {
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<IngestionSettingsDocument>(SingletonId);
        return doc ?? new IngestionSettingsDocument();
    }

    public async Task<IngestionSettingsDocument> UpdateSettingsAsync(
        IngestionSettingsDocument settings, string updatedBy)
    {
        // Validate cloud dir paths for local provider
        foreach (var dir in settings.CloudDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir.Name))
                throw new ArgumentException($"Cloud directory name is required.");

            if (string.IsNullOrWhiteSpace(dir.Path))
                throw new ArgumentException($"Cloud directory path is required for '{dir.Name}'.");

            // Prevent directory traversal
            if (dir.Provider == "local" && (dir.Path.Contains("..") || dir.Path.Contains("~")))
                throw new ArgumentException($"Directory path for '{dir.Name}' contains invalid characters.");

            // Ensure IDs are set
            if (string.IsNullOrEmpty(dir.Id))
                dir.Id = Guid.NewGuid().ToString("N")[..8];
        }

        // Ensure messaging channel IDs
        foreach (var ch in settings.MessagingChannels)
        {
            if (string.IsNullOrEmpty(ch.Id))
                ch.Id = Guid.NewGuid().ToString("N")[..8];
        }

        // Clamp pipeline values
        settings.Pipeline.MaxConcurrentIngestions = Math.Clamp(settings.Pipeline.MaxConcurrentIngestions, 1, 20);
        settings.Pipeline.MaxFileSizeMb = Math.Clamp(settings.Pipeline.MaxFileSizeMb, 1, 50);
        settings.Pipeline.MinQualityScore = Math.Clamp(settings.Pipeline.MinQualityScore, 0f, 1f);

        settings.Id = SingletonId;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = updatedBy;

        await using var session = _store.LightweightSession();
        session.Store(settings);
        await session.SaveChangesAsync();

        _logger.LogInformation("Ingestion settings updated by {User}", updatedBy);

        return settings;
    }

    public async Task<ConnectionTestResult> TestEmailConnectionAsync(EmailIngestionConfig config, string? password)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(config.ImapHost))
            return ConnectionTestResult.Fail("IMAP host is required", "CONFIG_MISSING_HOST");

        if (string.IsNullOrWhiteSpace(config.EmailAddress))
            return ConnectionTestResult.Fail("Email address is required", "CONFIG_MISSING_EMAIL");

        if (string.IsNullOrWhiteSpace(password))
            return ConnectionTestResult.Fail("Password is required for connection test", "CONFIG_MISSING_PASSWORD");

        if (config.ImapPort <= 0 || config.ImapPort > 65535)
            return ConnectionTestResult.Fail($"Invalid port: {config.ImapPort}", "CONFIG_INVALID_PORT");

        using var client = new ImapClient();
        client.Timeout = 30000; // 30 second timeout

        try
        {
            // Attempt connection with appropriate security settings
            var secureSocketOptions = config.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            _logger.LogInformation("Testing IMAP connection to {Host}:{Port} (SSL={Ssl}) for {Email}",
                config.ImapHost, config.ImapPort, config.UseSsl, config.EmailAddress);

            await client.ConnectAsync(config.ImapHost, config.ImapPort, secureSocketOptions);

            // Attempt authentication
            await client.AuthenticateAsync(new NetworkCredential(config.EmailAddress, password));

            // Verify we can access the inbox
            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

            var messageCount = inbox.Count;

            await client.DisconnectAsync(true);

            _logger.LogInformation("IMAP connection test succeeded for {Email} at {Host}. Inbox contains {Count} messages.",
                config.EmailAddress, config.ImapHost, messageCount);

            return ConnectionTestResult.Ok($"Connected successfully. Inbox contains {messageCount} messages.");
        }
        catch (MailKit.Security.AuthenticationException authEx)
        {
            _logger.LogWarning(authEx, "IMAP authentication failed for {Email} at {Host}:{Port}",
                config.EmailAddress, config.ImapHost, config.ImapPort);
            return ConnectionTestResult.Fail("Authentication failed: invalid username or password", "AUTH_FAILED");
        }
        catch (MailKit.Net.Imap.ImapProtocolException protoEx)
        {
            _logger.LogWarning(protoEx, "IMAP protocol error for {Host}:{Port}",
                config.ImapHost, config.ImapPort);
            return ConnectionTestResult.Fail("IMAP protocol error: check host and port settings", "PROTOCOL_ERROR");
        }
        catch (System.Net.Sockets.SocketException sockEx)
        {
            _logger.LogWarning(sockEx, "IMAP connection failed for {Host}:{Port} - {Message}",
                config.ImapHost, config.ImapPort, sockEx.Message);
            return ConnectionTestResult.Fail($"Connection failed: {sockEx.Message}", "CONNECTION_FAILED");
        }
        catch (TimeoutException timeoutEx)
        {
            _logger.LogWarning(timeoutEx, "IMAP connection timeout for {Host}:{Port}",
                config.ImapHost, config.ImapPort);
            return ConnectionTestResult.Fail("Connection timed out: host may be unreachable", "TIMEOUT");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IMAP connection test failed for {Email} at {Host}:{Port}",
                config.EmailAddress, config.ImapHost, config.ImapPort);
            return ConnectionTestResult.Fail($"Connection failed: {ex.Message}", "UNEXPECTED_ERROR");
        }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    public async Task<ConnectionTestResult> TestCloudDirAsync(CloudDirConfig config, string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
            return ConnectionTestResult.Fail("Path is required", "CONFIG_MISSING_PATH");

        // Prevent directory traversal for local provider
        if (config.Provider == "local" && (config.Path.Contains("..") || config.Path.Contains("~")))
        {
            _logger.LogWarning("Cloud dir test rejected: path contains invalid characters '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Path contains invalid characters", "INVALID_PATH");
        }

        switch (config.Provider.ToLowerInvariant())
        {
            case "local":
                return await TestLocalDirectoryAsync(config);

            case "s3":
            case "s3-compatible":
                return await TestS3ConnectionAsync(config, secretKey);

            case "azure":
            case "azureblob":
                return await TestAzureBlobConnectionAsync(config, secretKey);

            case "gcs":
            case "google":
                return await TestGcsConnectionAsync(config, secretKey);

            default:
                _logger.LogWarning("Unknown cloud provider '{Provider}' for path '{Path}'",
                    config.Provider, config.Path);
                return ConnectionTestResult.Fail($"Unknown provider: {config.Provider}", "UNKNOWN_PROVIDER");
        }
    }

    private Task<ConnectionTestResult> TestLocalDirectoryAsync(CloudDirConfig config)
    {
        try
        {
            var exists = Directory.Exists(config.Path);
            var canRead = exists && HasReadPermission(config.Path);

            if (!exists)
            {
                _logger.LogWarning("Local directory does not exist: '{Path}'", config.Path);
                return Task.FromResult(ConnectionTestResult.Fail("Directory does not exist", "DIR_NOT_FOUND"));
            }

            if (!canRead)
            {
                _logger.LogWarning("Local directory exists but is not readable: '{Path}'", config.Path);
                return Task.FromResult(ConnectionTestResult.Fail("Directory exists but is not readable", "PERMISSION_DENIED"));
            }

            var fileCount = Directory.GetFiles(config.Path).Length;
            _logger.LogInformation("Local directory test succeeded for '{Path}': {FileCount} files found", config.Path, fileCount);

            return Task.FromResult(ConnectionTestResult.Ok($"Directory exists with {fileCount} files."));
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Access denied to local directory: '{Path}'", config.Path);
            return Task.FromResult(ConnectionTestResult.Fail("Access denied to directory", "PERMISSION_DENIED"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local directory test failed for '{Path}'", config.Path);
            return Task.FromResult(ConnectionTestResult.Fail($"Directory test failed: {ex.Message}", "TEST_FAILED"));
        }
    }

    private async Task<ConnectionTestResult> TestS3ConnectionAsync(CloudDirConfig config, string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(config.AccessKeyId))
            return ConnectionTestResult.Fail("Access Key ID is required for S3", "CONFIG_MISSING_ACCESS_KEY");

        if (string.IsNullOrWhiteSpace(secretKey))
            return ConnectionTestResult.Fail("Secret Key is required for S3", "CONFIG_MISSING_SECRET_KEY");

        try
        {
            // Parse bucket name from path (format: bucket-name/prefix)
            var pathParts = config.Path.Split('/', 2);
            var bucketName = pathParts[0];
            var prefix = pathParts.Length > 1 ? pathParts[1] : null;

            if (string.IsNullOrWhiteSpace(bucketName))
                return ConnectionTestResult.Fail("S3 path must start with bucket name", "CONFIG_INVALID_PATH");

            var clientConfig = new AmazonS3Config
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxErrorRetry = 2
            };

            // Set region if specified
            if (!string.IsNullOrWhiteSpace(config.Region))
            {
                if (RegionEndpoint.EnumerableAllRegions.FirstOrDefault(r =>
                    r.SystemName.Equals(config.Region, StringComparison.OrdinalIgnoreCase)) is { } region)
                {
                    clientConfig.RegionEndpoint = region;
                }
                else
                {
                    _logger.LogWarning("Unknown AWS region '{Region}', using default", config.Region);
                }
            }

            // Support S3-compatible endpoints (e.g., MinIO, Wasabi)
            if (config.Path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                config.Path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var endpointUri = new Uri(config.Path);
                clientConfig.ServiceURL = endpointUri.GetLeftPart(UriPartial.Authority);
                clientConfig.ForcePathStyle = true; // Required for MinIO
            }

            using var client = new AmazonS3Client(config.AccessKeyId, secretKey, clientConfig);

            // Test by listing objects (limited to 1 to minimize data transfer)
            var listRequest = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix,
                MaxKeys = 1
            };

            var response = await client.ListObjectsV2Async(listRequest);

            _logger.LogInformation("S3 connection test succeeded for bucket '{Bucket}'. Found {Count} objects with prefix '{Prefix}'.",
                bucketName, response.KeyCount, prefix ?? "(none)");

            return ConnectionTestResult.Ok($"Connected to S3 bucket '{bucketName}'. Found {response.KeyCount} objects.");
        }
        catch (AmazonS3Exception s3Ex) when (s3Ex.ErrorCode == "InvalidAccessKeyId" || s3Ex.ErrorCode == "SignatureDoesNotMatch")
        {
            _logger.LogWarning(s3Ex, "S3 authentication failed for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Authentication failed: invalid access key or secret key", "AUTH_FAILED");
        }
        catch (AmazonS3Exception s3Ex) when (s3Ex.ErrorCode == "NoSuchBucket")
        {
            _logger.LogWarning(s3Ex, "S3 bucket not found for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Bucket not found", "BUCKET_NOT_FOUND");
        }
        catch (AmazonS3Exception s3Ex) when (s3Ex.ErrorCode == "AccessDenied")
        {
            _logger.LogWarning(s3Ex, "S3 access denied for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Access denied: check IAM permissions", "PERMISSION_DENIED");
        }
        catch (AmazonS3Exception s3Ex)
        {
            _logger.LogWarning(s3Ex, "S3 error for path '{Path}': {ErrorCode} - {Message}",
                config.Path, s3Ex.ErrorCode, s3Ex.Message);
            return ConnectionTestResult.Fail($"S3 error: {s3Ex.ErrorCode}", "S3_ERROR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 connection test failed for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail($"Connection failed: {ex.Message}", "CONNECTION_FAILED");
        }
    }

    private async Task<ConnectionTestResult> TestAzureBlobConnectionAsync(CloudDirConfig config, string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(config.AccessKeyId) && string.IsNullOrWhiteSpace(secretKey))
            return ConnectionTestResult.Fail("Storage account name or connection string is required", "CONFIG_MISSING_CREDENTIALS");

        try
        {
            // Parse container name from path (format: container-name/prefix)
            var pathParts = config.Path.Split('/', 2);
            var containerName = pathParts[0];
            var prefix = pathParts.Length > 1 ? pathParts[1] : null;

            if (string.IsNullOrWhiteSpace(containerName))
                return ConnectionTestResult.Fail("Azure path must start with container name", "CONFIG_INVALID_PATH");

            BlobServiceClient serviceClient;

            // Try connection string first (secretKey), then account name + key
            if (!string.IsNullOrWhiteSpace(secretKey) && secretKey.Contains("AccountName="))
            {
                serviceClient = new BlobServiceClient(secretKey);
            }
            else if (!string.IsNullOrWhiteSpace(config.AccessKeyId) && !string.IsNullOrWhiteSpace(secretKey))
            {
                var accountName = config.AccessKeyId;
                var accountKey = secretKey;
                var credential = new Azure.Storage.StorageSharedKeyCredential(accountName, accountKey);
                var blobUri = new Uri($"https://{accountName}.blob.core.windows.net");
                serviceClient = new BlobServiceClient(blobUri, credential);
            }
            else
            {
                return ConnectionTestResult.Fail("Azure Storage credentials incomplete", "CONFIG_INVALID_CREDENTIALS");
            }

            // Test by getting container properties
            var containerClient = serviceClient.GetBlobContainerClient(containerName);
            var properties = await containerClient.GetPropertiesAsync();

            // Count blobs with prefix (limited to 1 to minimize data transfer)
            var blobCount = 0;
            await foreach (var _ in containerClient.GetBlobsAsync(prefix: prefix))
            {
                blobCount++;
                break;
            }

            _logger.LogInformation("Azure Blob connection test succeeded for container '{Container}'. Access level: {Access}.",
                containerName, properties.Value.PublicAccess);

            return ConnectionTestResult.Ok($"Connected to Azure container '{containerName}'. Found {blobCount} blobs with prefix.");
        }
        catch (Azure.RequestFailedException azEx) when (azEx.Status == 403)
        {
            _logger.LogWarning(azEx, "Azure Blob authentication failed for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Authentication failed: invalid credentials", "AUTH_FAILED");
        }
        catch (Azure.RequestFailedException azEx) when (azEx.Status == 404)
        {
            _logger.LogWarning(azEx, "Azure Blob container not found for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Container not found", "CONTAINER_NOT_FOUND");
        }
        catch (Azure.RequestFailedException azEx)
        {
            _logger.LogWarning(azEx, "Azure Blob error for path '{Path}': {Status} - {Message}",
                config.Path, azEx.Status, azEx.Message);
            return ConnectionTestResult.Fail($"Azure error: {azEx.ErrorCode}", "AZURE_ERROR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Blob connection test failed for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail($"Connection failed: {ex.Message}", "CONNECTION_FAILED");
        }
    }

    private async Task<ConnectionTestResult> TestGcsConnectionAsync(CloudDirConfig config, string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            return ConnectionTestResult.Fail("Service account JSON key is required for GCS", "CONFIG_MISSING_CREDENTIALS");

        try
        {
            // Parse bucket name from path (format: bucket-name/prefix)
            var pathParts = config.Path.Split('/', 2);
            var bucketName = pathParts[0];
            var prefix = pathParts.Length > 1 ? pathParts[1] : null;

            if (string.IsNullOrWhiteSpace(bucketName))
                return ConnectionTestResult.Fail("GCS path must start with bucket name", "CONFIG_INVALID_PATH");

            // Parse service account JSON
            GoogleCredential credential;
            try
            {
                credential = GoogleCredential.FromJson(secretKey);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Invalid GCS service account JSON");
                return ConnectionTestResult.Fail("Invalid service account JSON", "CONFIG_INVALID_CREDENTIALS");
            }

            var storage = await StorageClient.CreateAsync(credential);

            // Test by getting bucket metadata
            var bucket = await storage.GetBucketAsync(bucketName);

            // Count objects with prefix (limited to 1)
            var objectCount = 0;
            await foreach (var _ in storage.ListObjectsAsync(bucketName, prefix))
            {
                objectCount++;
                break;
            }

            _logger.LogInformation("GCS connection test succeeded for bucket '{Bucket}'. Storage class: {StorageClass}.",
                bucketName, bucket.StorageClass);

            return ConnectionTestResult.Ok($"Connected to GCS bucket '{bucketName}'. Found {objectCount} objects with prefix.");
        }
        catch (Google.GoogleApiException gEx) when (gEx.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(gEx, "GCS authentication failed for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Authentication failed: invalid service account", "AUTH_FAILED");
        }
        catch (Google.GoogleApiException gEx) when (gEx.HttpStatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(gEx, "GCS bucket not found for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Bucket not found", "BUCKET_NOT_FOUND");
        }
        catch (Google.GoogleApiException gEx) when (gEx.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(gEx, "GCS access denied for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail("Access denied: check IAM permissions", "PERMISSION_DENIED");
        }
        catch (Google.GoogleApiException gEx)
        {
            _logger.LogWarning(gEx, "GCS error for path '{Path}': {Status} - {Message}",
                config.Path, gEx.HttpStatusCode, gEx.Message);
            return ConnectionTestResult.Fail($"GCS error: {gEx.Error?.Code}", "GCS_ERROR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GCS connection test failed for path '{Path}'", config.Path);
            return ConnectionTestResult.Fail($"Connection failed: {ex.Message}", "CONNECTION_FAILED");
        }
    }

    private static bool HasReadPermission(string path)
    {
        try
        {
            // Try to list files to verify read permission
            Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
