// =============================================================================
// Cena Platform — LocalDirectoryProvider (ADR-0058)
//
// Filesystem-backed cloud-directory provider. Extracted from the prior
// in-service `local` branch of IngestionPipelineCloudDir (pre-ADR-0058).
// Behavior is preserved exactly — path-traversal guard via the
// Ingestion:CloudWatchDirs allowlist, SHA-256 dedup against existing
// PipelineItemDocument.ContentHash, content-type mapping by extension,
// lastModified descending order.
// =============================================================================

using System.Security.Cryptography;
using Cena.Actors.Ingest;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api.Ingestion;

public sealed class LocalDirectoryProvider : ICloudDirectoryProvider
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".csv", ".xlsx"
    };

    private static readonly Dictionary<string, string> ExtensionToContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".csv"] = "text/csv",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    private readonly IDocumentStore _store;
    private readonly IIngestionOrchestrator? _orchestrator;
    private readonly IReadOnlyList<string> _staticAllowedDirs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionOptions _options;
    private readonly ILogger<LocalDirectoryProvider> _logger;

    public LocalDirectoryProvider(
        IDocumentStore store,
        IOptions<IngestionOptions> options,
        ILogger<LocalDirectoryProvider> logger,
        IServiceScopeFactory scopeFactory,
        IIngestionOrchestrator? orchestrator = null)
    {
        _store = store;
        _orchestrator = orchestrator;
        _options = options.Value;
        _staticAllowedDirs = _options.CloudWatchDirs;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string ProviderId => "local";

    // The provider is now driven by two sources of truth:
    //   1) Ingestion:CloudWatchDirs (appsettings static allowlist)
    //   2) IngestionSettingsDocument.CloudDirectories (runtime, edited
    //      via the admin Settings UI; merged at request-time inside
    //      ListAsync / IngestAsync)
    // Either one being non-empty is enough to consider the provider
    // enabled. We do not async-check the settings doc here — that runs
    // per-request — but we permit dispatch so the admin Settings page
    // can drive ingestion even when the appsettings allowlist is empty.
    public bool IsEnabled => true;

    public async Task<IngestionDto.CloudDirListResponse> ListAsync(
        IngestionDto.CloudDirListRequest request,
        CancellationToken ct)
    {
        EnsureEnabled();

        var allowedDirs = await GetMergedAllowedDirsAsync();
        var resolvedPath = Path.GetFullPath(request.BucketOrPath);
        if (!allowedDirs.Any(d => resolvedPath.StartsWith(Path.GetFullPath(d), StringComparison.Ordinal)))
        {
            _logger.LogWarning("Cloud dir path rejected (directory traversal prevention): {Path}", request.BucketOrPath);
            throw new UnauthorizedAccessException(
                $"Path '{request.BucketOrPath}' is not under an allowed ingest directory. " +
                "Add it via the admin Ingestion Settings page or configure " +
                "Ingestion:CloudWatchDirs in appsettings.json.");
        }

        var searchPath = string.IsNullOrEmpty(request.Prefix)
            ? resolvedPath
            : Path.Combine(resolvedPath, request.Prefix);

        if (!Directory.Exists(searchPath))
            return new IngestionDto.CloudDirListResponse(new List<IngestionDto.CloudFileEntry>(), 0, null);

        await using var session = _store.QuerySession();
        var ingestedHashes = (await session.Query<PipelineItemDocument>()
            .Select(x => new { x.ContentHash })
            .ToListAsync(ct))
            .Select(x => x.ContentHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories)
            .Where(f => AllowedExtensions.Contains(Path.GetExtension(f)))
            .Select(f =>
            {
                var info = new FileInfo(f);
                var ext = Path.GetExtension(f);
                var key = Path.GetRelativePath(resolvedPath, f);

                using var stream = File.OpenRead(f);
                var hash = Convert.ToHexStringLower(SHA256.HashData(stream));

                return new IngestionDto.CloudFileEntry(
                    Key: key,
                    Filename: info.Name,
                    SizeBytes: info.Length,
                    ContentType: ExtensionToContentType.GetValueOrDefault(ext, "application/octet-stream"),
                    LastModified: info.LastWriteTimeUtc,
                    AlreadyIngested: ingestedHashes.Contains(hash));
            })
            .OrderByDescending(f => f.LastModified)
            .ToList();

        return new IngestionDto.CloudDirListResponse(files, files.Count, null);
    }

    public async Task<IngestionDto.CloudDirIngestResponse> IngestAsync(
        IngestionDto.CloudDirIngestRequest request,
        CancellationToken ct)
    {
        EnsureEnabled();

        // Security-first: validate inputs before revealing any server-
        // side state (orchestrator availability, etc.).
        var allowedDirs = await GetMergedAllowedDirsAsync();
        var resolvedPath = Path.GetFullPath(request.BucketOrPath);
        if (!allowedDirs.Any(d => resolvedPath.StartsWith(Path.GetFullPath(d), StringComparison.Ordinal)))
        {
            _logger.LogWarning("Cloud dir ingest path rejected: {Path}", request.BucketOrPath);
            throw new UnauthorizedAccessException(
                $"Path '{request.BucketOrPath}' is not under an allowed ingest directory.");
        }

        if (_orchestrator is null)
            throw new InvalidOperationException("Ingestion orchestrator is not available");

        var batchId = $"batch-{Guid.NewGuid():N}";
        var filesQueued = 0;
        var filesSkipped = 0;

        IEnumerable<string> filePaths;
        if (request.FileKeys.Count > 0)
        {
            filePaths = request.FileKeys.Select(key => Path.Combine(resolvedPath, key));
        }
        else
        {
            var searchPath = string.IsNullOrEmpty(request.Prefix)
                ? resolvedPath
                : Path.Combine(resolvedPath, request.Prefix);
            filePaths = Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories)
                .Where(f => AllowedExtensions.Contains(Path.GetExtension(f)));
        }

        var materialized = filePaths.ToList();

        // Batch-size gate per ADR-0058 §6.
        if (_options.MaxBatchFiles is int maxFiles && materialized.Count > maxFiles)
        {
            throw new InvalidOperationException(
                $"Local ingest batch of {materialized.Count} files exceeds Ingestion:MaxBatchFiles={maxFiles}.");
        }

        await using var session = _store.QuerySession();
        var ingestedHashes = (await session.Query<PipelineItemDocument>()
            .Select(x => new { x.ContentHash })
            .ToListAsync(ct))
            .Select(x => x.ContentHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        long totalBytes = 0;
        foreach (var filePath in materialized)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(filePath);
            if (!allowedDirs.Any(d => fullPath.StartsWith(Path.GetFullPath(d), StringComparison.Ordinal)))
            {
                _logger.LogWarning("Skipping file outside allowed dirs: {Path}", filePath);
                filesSkipped++;
                continue;
            }

            if (!File.Exists(fullPath))
            {
                filesSkipped++;
                continue;
            }

            using var hashStream = File.OpenRead(fullPath);
            var hash = Convert.ToHexStringLower(SHA256.HashData(hashStream));
            if (ingestedHashes.Contains(hash))
            {
                filesSkipped++;
                continue;
            }

            var fileLen = new FileInfo(fullPath).Length;
            totalBytes += fileLen;
            if (_options.MaxBatchBytes is long maxBytes && totalBytes > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Local ingest batch byte total {totalBytes} exceeded Ingestion:MaxBatchBytes={maxBytes}.");
            }

            var ext = Path.GetExtension(fullPath);
            var contentType = ExtensionToContentType.GetValueOrDefault(ext, "application/octet-stream");

            using var fileStream = File.OpenRead(fullPath);
            var ingestionRequest = new IngestionRequest(
                FileStream: fileStream,
                Filename: Path.GetFileName(fullPath),
                ContentType: contentType,
                SourceType: "batch",
                SourceUrl: fullPath,
                SubmittedBy: "admin-cloud-dir");

            try
            {
                await _orchestrator.ProcessFileAsync(ingestionRequest);
                filesQueued++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest file from cloud dir: {Path}", fullPath);
                filesSkipped++;
            }
        }

        _logger.LogInformation(
            "Cloud dir batch {BatchId}: provider=local queued={Queued} skipped={Skipped}",
            batchId, filesQueued, filesSkipped);

        return new IngestionDto.CloudDirIngestResponse(filesQueued, filesSkipped, batchId);
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "Local cloud-directory provider is not configured. " +
                "Add paths to Ingestion:CloudWatchDirs in appsettings.json.");
    }

    // Merges the static appsettings allowlist with the runtime allowlist
    // persisted in IngestionSettingsDocument.CloudDirectories (admin
    // Settings UI). Called once per request so a freshly-saved directory
    // works without an app restart. Disabled and non-local entries are
    // excluded; the static allowlist is preserved exactly so existing
    // CI / curator workflows continue to work.
    private async Task<IReadOnlyList<string>> GetMergedAllowedDirsAsync()
    {
        var merged = new List<string>(_staticAllowedDirs);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsService = scope.ServiceProvider
                .GetService<IIngestionSettingsService>();
            if (settingsService is null) return merged;

            var settings = await settingsService.GetSettingsAsync();
            foreach (var entry in settings.CloudDirectories)
            {
                if (!entry.Enabled) continue;
                if (!string.Equals(entry.Provider, "local", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(entry.Path)) continue;
                if (entry.Path.Contains("..") || entry.Path.Contains('~'))
                {
                    _logger.LogWarning(
                        "Skipping saved cloud-dir entry with invalid characters: {Path}",
                        entry.Path);
                    continue;
                }
                merged.Add(entry.Path);
            }
        }
        catch (Exception ex)
        {
            // Settings load failure should not bring down ingestion that
            // was already legitimised by appsettings — log and fall back.
            _logger.LogWarning(ex,
                "Failed to merge IngestionSettingsDocument.CloudDirectories into allowlist");
        }
        return merged;
    }
}
