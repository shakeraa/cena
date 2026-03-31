// =============================================================================
// Cena Platform -- Ingestion Settings Service
// Manages persisted ingestion configuration: cloud dirs, email, messaging,
// and pipeline defaults. Backed by Marten document store.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

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
    Task<bool> TestEmailConnectionAsync(EmailIngestionConfig config);
    Task<bool> TestCloudDirAsync(CloudDirConfig config);
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

    public Task<bool> TestEmailConnectionAsync(EmailIngestionConfig config)
    {
        // Placeholder: validate required fields are present
        if (string.IsNullOrWhiteSpace(config.ImapHost))
            return Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(config.EmailAddress))
            return Task.FromResult(false);
        if (config.ImapPort <= 0 || config.ImapPort > 65535)
            return Task.FromResult(false);

        // In production this would attempt an actual IMAP connection
        _logger.LogInformation("Email connection test (placeholder) for {Host}:{Port}",
            config.ImapHost, config.ImapPort);
        return Task.FromResult(true);
    }

    public Task<bool> TestCloudDirAsync(CloudDirConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
            return Task.FromResult(false);

        if (config.Provider == "local")
        {
            // Prevent directory traversal
            if (config.Path.Contains("..") || config.Path.Contains("~"))
                return Task.FromResult(false);

            var exists = Directory.Exists(config.Path);
            _logger.LogInformation("Local dir test for '{Path}': exists={Exists}", config.Path, exists);
            return Task.FromResult(exists);
        }

        // S3 / GCS / Azure: placeholder success (would use SDK in production)
        _logger.LogInformation("Cloud dir test (placeholder) for provider={Provider}, path={Path}",
            config.Provider, config.Path);
        return Task.FromResult(true);
    }
}
