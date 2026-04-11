// =============================================================================
// Cena Platform -- Ingestion Pipeline Service — Cloud Directory partial
// Split from IngestionPipelineService.cs to keep both files under 500 LOC.
// Handles the cloud-directory list/ingest pair used by the admin upload UI.
// =============================================================================

using System.Security.Cryptography;
using Cena.Actors.Ingest;
using Marten;
using Microsoft.Extensions.Logging;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api;

public sealed partial class IngestionPipelineService
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

    public async Task<CloudDirListResponse> ListCloudDirectoryAsync(CloudDirListRequest request)
    {
        if (string.Equals(request.Provider, "s3", StringComparison.OrdinalIgnoreCase))
        {
            // S3 provider: placeholder — requires AWS SDK integration
            return new CloudDirListResponse(
                Files: new List<CloudFileEntry>(),
                TotalCount: 0,
                ContinuationToken: null);
        }

        if (!string.Equals(request.Provider, "local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported cloud directory provider: {request.Provider}");

        var resolvedPath = Path.GetFullPath(request.BucketOrPath);
        if (!_allowedCloudDirs.Any(d => resolvedPath.StartsWith(Path.GetFullPath(d), StringComparison.Ordinal)))
        {
            _logger.LogWarning("Cloud dir path rejected (directory traversal prevention): {Path}", request.BucketOrPath);
            throw new UnauthorizedAccessException(
                $"Path '{request.BucketOrPath}' is not under an allowed ingest directory. " +
                "Configure Ingestion:CloudWatchDirs in appsettings.json.");
        }

        var searchPath = string.IsNullOrEmpty(request.Prefix)
            ? resolvedPath
            : Path.Combine(resolvedPath, request.Prefix);

        if (!Directory.Exists(searchPath))
            return new CloudDirListResponse(new List<CloudFileEntry>(), 0, null);

        await using var session = _store.QuerySession();
        var ingestedHashes = (await session.Query<PipelineItemDocument>()
            .Select(x => new { x.ContentHash })
            .ToListAsync())
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

                return new CloudFileEntry(
                    Key: key,
                    Filename: info.Name,
                    SizeBytes: info.Length,
                    ContentType: ExtensionToContentType.GetValueOrDefault(ext, "application/octet-stream"),
                    LastModified: info.LastWriteTimeUtc,
                    AlreadyIngested: ingestedHashes.Contains(hash));
            })
            .OrderByDescending(f => f.LastModified)
            .ToList();

        return new CloudDirListResponse(files, files.Count, null);
    }

    public async Task<CloudDirIngestResponse> IngestCloudDirectoryAsync(CloudDirIngestRequest request)
    {
        if (!string.Equals(request.Provider, "local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported cloud directory provider for ingest: {request.Provider}");

        if (_orchestrator is null)
            throw new InvalidOperationException("Ingestion orchestrator is not available");

        var resolvedPath = Path.GetFullPath(request.BucketOrPath);
        if (!_allowedCloudDirs.Any(d => resolvedPath.StartsWith(Path.GetFullPath(d), StringComparison.Ordinal)))
        {
            _logger.LogWarning("Cloud dir ingest path rejected: {Path}", request.BucketOrPath);
            throw new UnauthorizedAccessException(
                $"Path '{request.BucketOrPath}' is not under an allowed ingest directory.");
        }

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

        await using var session = _store.QuerySession();
        var ingestedHashes = (await session.Query<PipelineItemDocument>()
            .Select(x => new { x.ContentHash })
            .ToListAsync())
            .Select(x => x.ContentHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePaths)
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!_allowedCloudDirs.Any(d => fullPath.StartsWith(Path.GetFullPath(d), StringComparison.Ordinal)))
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
            "Cloud dir batch {BatchId}: queued={Queued}, skipped={Skipped}",
            batchId, filesQueued, filesSkipped);

        return new CloudDirIngestResponse(filesQueued, filesSkipped, batchId);
    }
}
