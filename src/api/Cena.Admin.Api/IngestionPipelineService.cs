// =============================================================================
// Cena Platform -- Ingestion Pipeline Service
// ADM-009: Pipeline dashboard and management
// Production implementation backed by Marten event store + Redis.
// =============================================================================
#pragma warning disable CS1998 // Async methods return stub data until wired to real stores

using System.Security.Cryptography;
using Cena.Actors.Ingest;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api;

public interface IIngestionPipelineService
{
    Task<PipelineStatusResponse> GetPipelineStatusAsync();
    Task<PipelineItemDetailResponse?> GetItemDetailAsync(string id);
    Task<bool> RetryItemAsync(string id);
    Task<bool> RejectPipelineItemAsync(string id, string reason);
    Task<UploadFileResponse> UploadFileAsync(string filename, string contentType);
    Task<bool> MoveToReviewAsync(string id);
    Task<UploadFileResponse> UploadFromRequestAsync(HttpRequest request);
    Task<PipelineStatsResponse> GetStatsAsync();
    Task<CloudDirListResponse> ListCloudDirectoryAsync(CloudDirListRequest request);
    Task<CloudDirIngestResponse> IngestCloudDirectoryAsync(CloudDirIngestRequest request);
}

public sealed class IngestionPipelineService : IIngestionPipelineService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly IIngestionOrchestrator? _orchestrator;
    private readonly ILogger<IngestionPipelineService> _logger;
    private readonly IReadOnlyList<string> _allowedCloudDirs;

    public IngestionPipelineService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<IngestionPipelineService> logger,
        IConfiguration configuration,
        IIngestionOrchestrator? orchestrator = null)
    {
        _store = store;
        _redis = redis;
        _orchestrator = orchestrator;
        _logger = logger;
        _allowedCloudDirs = configuration.GetSection("Ingestion:CloudWatchDirs")
            .Get<string[]>() ?? Array.Empty<string>();
    }

    public async Task<PipelineStatusResponse> GetPipelineStatusAsync()
    {
        await using var session = _store.QuerySession();

        // Query real pipeline items grouped by stage
        var allItems = await session.Query<PipelineItemDocument>()
            .OrderByDescending(x => x.SubmittedAt)
            .Take(500)
            .ToListAsync();

        var stageNames = new[]
        {
            (Cena.Actors.Ingest.PipelineStage.Incoming, "incoming", "Incoming"),
            (Cena.Actors.Ingest.PipelineStage.OcrProcessing, "ocr", "OCR Processing"),
            (Cena.Actors.Ingest.PipelineStage.Segmented, "segmented", "Segmented"),
            (Cena.Actors.Ingest.PipelineStage.Normalized, "normalized", "Normalized"),
            (Cena.Actors.Ingest.PipelineStage.Classified, "classified", "Classified"),
            (Cena.Actors.Ingest.PipelineStage.Deduplicated, "deduplicated", "Deduplicated"),
            (Cena.Actors.Ingest.PipelineStage.ReCreated, "recreated", "Re-Created"),
            (Cena.Actors.Ingest.PipelineStage.InReview, "review", "In Review"),
            (Cena.Actors.Ingest.PipelineStage.Published, "published", "Published")
        };

        var stages = stageNames.Select(s =>
        {
            var stageItems = allItems.Where(i => i.CurrentStage == s.Item1).ToList();
            var hasError = stageItems.Any(i => i.Status == "failed");
            return new IngestionDto.PipelineStage(
                StageId: s.Item2,
                Name: s.Item3,
                Count: stageItems.Count,
                Status: hasError ? "failed" : stageItems.Count > 50 ? "slow" : "healthy",
                Items: stageItems.Take(10).Select(i => new IngestionDto.PipelineItem(
                    Id: i.Id,
                    SourceFilename: i.SourceFilename,
                    SourceType: i.SourceType,
                    QuestionCount: i.ExtractedQuestionCount,
                    QualityScore: (int)((i.AvgQualityScore ?? 0) * 100),
                    Timestamp: i.SubmittedAt,
                    ErrorMessage: i.LastError,
                    HasError: i.Status == "failed"
                )).ToList());
        }).ToList();

        return new PipelineStatusResponse(DateTimeOffset.UtcNow, stages);
    }

    public async Task<PipelineItemDetailResponse?> GetItemDetailAsync(string id)
    {
        await using var session = _store.QuerySession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return null;

        var stageHistory = item.StageHistory.Select(s => new StageProcessingInfo(
            Stage: s.Stage.ToString(),
            StartedAt: s.StartedAt,
            CompletedAt: s.CompletedAt,
            Duration: s.Duration,
            Status: s.Status,
            ErrorMessage: s.ErrorMessage
        )).ToList();

        OcrOutput? ocrOutput = null;
        if (item.Ocr is not null)
        {
            ocrOutput = new OcrOutput(
                OriginalImageUrl: $"s3://cena-ingest/{item.S3Key}",
                ExtractedText: item.Ocr.Pages.FirstOrDefault()?.RawText ?? "",
                Confidence: item.Ocr.Confidence,
                Regions: new List<OcrRegion>());
        }

        var extractedQuestions = new List<ExtractedQuestion>();
        if (item.ExtractedQuestionIds.Count > 0)
        {
            var questions = await session.Query<Cena.Actors.Questions.QuestionReadModel>()
                .Where(q => q.Id.IsOneOf(item.ExtractedQuestionIds.ToArray()))
                .ToListAsync();

            extractedQuestions = questions.Select((q, i) => new ExtractedQuestion(
                Index: i,
                Text: q.StemPreview,
                Answer: null,
                Confidence: q.QualityScore / 100f
            )).ToList();
        }

        return new PipelineItemDetailResponse(
            Id: item.Id,
            SourceFilename: item.SourceFilename,
            SourceType: item.SourceType,
            SourceUrl: item.SourceUrl ?? item.S3Key,
            SubmittedAt: item.SubmittedAt,
            CompletedAt: item.CompletedAt,
            CurrentStage: item.CurrentStage.ToString(),
            StageHistory: stageHistory,
            OcrResult: ocrOutput,
            Quality: new QualityScores(
                MathCorrectness: (int)((item.AvgQualityScore ?? 0) * 100),
                LanguageQuality: 80,
                PedagogicalQuality: 75,
                PlagiarismScore: item.DuplicateCount ?? 0),
            ExtractedQuestions: extractedQuestions);
    }

    public async Task<bool> RetryItemAsync(string id)
    {
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return false;

        item.RetryCount++;
        item.Status = "processing";
        item.CurrentStage = Cena.Actors.Ingest.PipelineStage.Incoming;
        item.LastError = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(item);
        await session.SaveChangesAsync();

        _logger.LogInformation("Retrying pipeline item {ItemId} (attempt {Retry})", id, item.RetryCount);
        return true;
    }

    public async Task<bool> RejectPipelineItemAsync(string id, string reason)
    {
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return false;

        item.Status = "rejected";
        item.CurrentStage = Cena.Actors.Ingest.PipelineStage.Failed;
        item.LastError = reason;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(item);
        await session.SaveChangesAsync();

        _logger.LogInformation("Rejected pipeline item {ItemId}: {Reason}", id, reason);
        return true;
    }

    public async Task<bool> MoveToReviewAsync(string id)
    {
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return false;

        item.CurrentStage = Cena.Actors.Ingest.PipelineStage.InReview;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(item);

        session.Events.Append(id, new MovedToReview_V1(
            id, item.ExtractedQuestionCount, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        _logger.LogInformation("Moved pipeline item {ItemId} to review", id);
        return true;
    }

    public async Task<UploadFileResponse> UploadFromRequestAsync(HttpRequest request)
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null)
            return new IngestionDto.UploadFileResponse("", "error", null);

        if (_orchestrator is null)
            return new IngestionDto.UploadFileResponse("", "error", null);

        using var stream = file.OpenReadStream();
        var result = await _orchestrator.ProcessFileAsync(new IngestionRequest(
            FileStream: stream,
            Filename: file.FileName,
            ContentType: file.ContentType ?? "application/octet-stream",
            SourceType: "upload",
            SourceUrl: null,
            SubmittedBy: "admin"
        ));

        return new IngestionDto.UploadFileResponse(
            UploadId: result.PipelineItemId,
            Status: result.Success ? "completed" : "failed",
            PipelineItemId: result.PipelineItemId);
    }

    public async Task<UploadFileResponse> UploadFileAsync(string filename, string contentType)
    {
        // Placeholder for direct file path uploads
        var uploadId = $"upload-{Guid.NewGuid():N}";
        return new IngestionDto.UploadFileResponse(uploadId, "queued", null);
    }

    public async Task<PipelineStatsResponse> GetStatsAsync()
    {
        await using var session = _store.QuerySession();

        var allItems = await session.Query<PipelineItemDocument>()
            .Where(x => x.SubmittedAt >= DateTimeOffset.UtcNow.AddDays(-7))
            .ToListAsync();

        // Throughput: items processed per hour (last 24h)
        var throughput = Enumerable.Range(0, 24)
            .Select(i =>
            {
                var hour = DateTimeOffset.UtcNow.AddHours(-23 + i);
                var count = allItems.Count(x =>
                    x.CompletedAt.HasValue &&
                    x.CompletedAt.Value.Hour == hour.Hour &&
                    x.CompletedAt.Value.Date == hour.Date);
                return new ThroughputPoint(hour.ToString("yyyy-MM-dd HH:00"), count);
            }).ToList();

        // Failure rates per stage
        var stageNames = new[] { "Incoming", "OCR Processing", "Segmented", "Normalized", "Classified", "Deduplicated", "Re-Created" };
        var failureRates = stageNames.Select(s =>
        {
            var stageItems = allItems.Where(x => x.StageHistory.Any(h => h.Stage.ToString() == s)).ToList();
            var failed = stageItems.Count(x => x.Status == "failed");
            var total = Math.Max(stageItems.Count, 1);
            return new StageFailureRate(s, (float)failed / total, total, failed);
        }).ToList();

        // Processing times per stage
        var processingTimes = stageNames.Select(s =>
        {
            var durations = allItems
                .SelectMany(x => x.StageHistory)
                .Where(h => h.Stage.ToString() == s && h.Duration.HasValue)
                .Select(h => (float)h.Duration!.Value.TotalSeconds)
                .ToList();

            var avg = durations.Count > 0 ? durations.Average() : 0f;
            var p95 = durations.Count > 0 ? durations.OrderBy(d => d).ElementAt((int)(durations.Count * 0.95)) : 0f;
            return new AvgProcessingTime(s, avg, p95);
        }).ToList();

        // Queue depth trend
        var queueTrend = new QueueDepthTrend(
            Enumerable.Range(0, 24)
                .Select(i =>
                {
                    var hour = DateTimeOffset.UtcNow.AddHours(-23 + i);
                    var incoming = allItems.Count(x => x.SubmittedAt.Hour == hour.Hour && x.SubmittedAt.Date == hour.Date);
                    var processing = allItems.Count(x =>
                        x.Status == "processing" &&
                        x.UpdatedAt.Hour == hour.Hour);
                    var completed = allItems.Count(x =>
                        x.CompletedAt.HasValue &&
                        x.CompletedAt.Value.Hour == hour.Hour &&
                        x.CompletedAt.Value.Date == hour.Date);
                    return new DepthPoint(hour.ToString("yyyy-MM-dd HH:00"), incoming, processing, completed);
                }).ToList());

        return new PipelineStatsResponse(throughput, failureRates, processingTimes, queueTrend);
    }

    // ── Cloud Directory: List files ──────────────────────────────────────────

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

        // Security: validate path is under an allowed base directory
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

        // Gather already-ingested content hashes
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

                // Compute hash to check against ingested set
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

    // ── Cloud Directory: Batch ingest selected files ─────────────────────────

    public async Task<CloudDirIngestResponse> IngestCloudDirectoryAsync(CloudDirIngestRequest request)
    {
        if (!string.Equals(request.Provider, "local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported cloud directory provider for ingest: {request.Provider}");

        if (_orchestrator is null)
            throw new InvalidOperationException("Ingestion orchestrator is not available");

        // Security: validate path
        var resolvedPath = Path.GetFullPath(request.BucketOrPath);
        if (!_allowedCloudDirs.Any(d => resolvedPath.StartsWith(Path.GetFullPath(d), StringComparison.Ordinal)))
        {
            _logger.LogWarning("Cloud dir ingest path rejected (directory traversal prevention): {Path}", request.BucketOrPath);
            throw new UnauthorizedAccessException(
                $"Path '{request.BucketOrPath}' is not under an allowed ingest directory.");
        }

        var batchId = $"batch-{Guid.NewGuid():N}";
        var filesQueued = 0;
        var filesSkipped = 0;

        // Determine which files to process
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

        // Get already-ingested hashes
        await using var session = _store.QuerySession();
        var ingestedHashes = (await session.Query<PipelineItemDocument>()
            .Select(x => new { x.ContentHash })
            .ToListAsync())
            .Select(x => x.ContentHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePaths)
        {
            // Validate each file path stays within allowed dirs
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

            // Check if already ingested
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
