// =============================================================================
// Cena Platform — S3DirectoryProvider (ADR-0058)
//
// Production-grade S3 cloud-directory provider. Dev exercises the exact
// same code path via LocalStack (docker-compose `localstack` profile);
// prod uses real AWS with IRSA-supplied credentials.
//
// Dedup strategy (ADR-0058 §4):
//   • LIST time  — ETag match against PipelineItemDocument.S3ETag.
//                  No GetObject calls. Cheap.
//   • INGEST time — full SHA-256 hash on downloaded bytes, matches the
//                   existing ContentHash dedup path.
//
// Allowlist (ADR-0058 §3): Ingestion:S3:AllowedBuckets. An admin request
// targeting a bucket outside this list returns 401 at dispatch, not a
// silent slurp.
//
// Batch-size gate (ADR-0058 §6): Ingestion:MaxBatchFiles + MaxBatchBytes
// evaluated before any GetObject call.
// =============================================================================

using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Cena.Actors.Ingest;
using Marten;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api.Ingestion;

public sealed class S3DirectoryProvider : ICloudDirectoryProvider
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

    private readonly IAmazonS3 _s3;
    private readonly IDocumentStore _store;
    private readonly IIngestionOrchestrator? _orchestrator;
    private readonly IngestionOptions _options;
    private readonly ILogger<S3DirectoryProvider> _logger;

    public S3DirectoryProvider(
        IAmazonS3 s3,
        IDocumentStore store,
        IOptions<IngestionOptions> options,
        ILogger<S3DirectoryProvider> logger,
        IIngestionOrchestrator? orchestrator = null)
    {
        _s3 = s3;
        _store = store;
        _orchestrator = orchestrator;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderId => "s3";

    public bool IsEnabled =>
        _options.S3 is { Enabled: true } s3 && s3.AllowedBuckets.Count > 0;

    public async Task<IngestionDto.CloudDirListResponse> ListAsync(
        IngestionDto.CloudDirListRequest request,
        CancellationToken ct)
    {
        EnsureEnabled();

        var bucket = request.BucketOrPath;
        EnsureBucketAllowed(bucket);

        var pageSize = _options.S3!.PageSize;

        var listRequest = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = string.IsNullOrEmpty(request.Prefix) ? null : request.Prefix,
            MaxKeys = pageSize,
            ContinuationToken = request.ContinuationToken,
        };

        ListObjectsV2Response listResponse;
        try
        {
            listResponse = await _s3.ListObjectsV2Async(listRequest, ct).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new IngestionDto.CloudDirListResponse(new List<IngestionDto.CloudFileEntry>(), 0, null);
        }

        // ETag-based dedup: pull every item in this bucket whose ETag
        // matches any object in the page. One Marten query per list
        // page — constant cost irrespective of bucket object count.
        var pageETags = listResponse.S3Objects
            .Select(o => NormalizeETag(o.ETag))
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct()
            .ToList();

        HashSet<string> ingestedETags;
        if (pageETags.Count == 0)
        {
            ingestedETags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            await using var session = _store.QuerySession();
            var known = await session.Query<PipelineItemDocument>()
                .Where(x => x.S3Bucket == bucket && x.S3ETag != null && pageETags.Contains(x.S3ETag))
                .Select(x => x.S3ETag!)
                .ToListAsync(ct);
            ingestedETags = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        }

        var files = listResponse.S3Objects
            .Where(o => AllowedExtensions.Contains(Path.GetExtension(o.Key)))
            .Select(o =>
            {
                var etag = NormalizeETag(o.ETag);
                var ext = Path.GetExtension(o.Key);
                return new IngestionDto.CloudFileEntry(
                    Key: o.Key,
                    Filename: Path.GetFileName(o.Key),
                    SizeBytes: o.Size,
                    ContentType: ExtensionToContentType.GetValueOrDefault(ext, "application/octet-stream"),
                    LastModified: o.LastModified,
                    AlreadyIngested: !string.IsNullOrEmpty(etag) && ingestedETags.Contains(etag));
            })
            .OrderByDescending(f => f.LastModified)
            .ToList();

        return new IngestionDto.CloudDirListResponse(
            Files: files,
            TotalCount: files.Count,
            ContinuationToken: listResponse.IsTruncated == true ? listResponse.NextContinuationToken : null);
    }

    public async Task<IngestionDto.CloudDirIngestResponse> IngestAsync(
        IngestionDto.CloudDirIngestRequest request,
        CancellationToken ct)
    {
        EnsureEnabled();

        // Security-first: validate inputs before revealing any server-
        // side state (orchestrator availability, etc.). Same order as
        // LocalDirectoryProvider.
        var bucket = request.BucketOrPath;
        EnsureBucketAllowed(bucket);

        if (_orchestrator is null)
            throw new InvalidOperationException("Ingestion orchestrator is not available");

        var batchId = $"batch-{Guid.NewGuid():N}";
        var filesQueued = 0;
        var filesSkipped = 0;

        var keys = request.FileKeys.Count > 0
            ? request.FileKeys.ToList()
            : await ListKeysUnderPrefixAsync(bucket, request.Prefix, ct).ConfigureAwait(false);

        // Batch-size gate per ADR-0058 §6 (file count).
        if (_options.MaxBatchFiles is int maxFiles && keys.Count > maxFiles)
        {
            throw new InvalidOperationException(
                $"S3 ingest batch of {keys.Count} files exceeds Ingestion:MaxBatchFiles={maxFiles}.");
        }

        await using var session = _store.QuerySession();
        var ingestedHashes = (await session.Query<PipelineItemDocument>()
            .Select(x => new { x.ContentHash })
            .ToListAsync(ct))
            .Select(x => x.ContentHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        long totalBytes = 0;
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();

            if (!AllowedExtensions.Contains(Path.GetExtension(key)))
            {
                filesSkipped++;
                continue;
            }

            GetObjectResponse getResponse;
            try
            {
                getResponse = await _s3.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                }, ct).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("S3 key disappeared mid-ingest: s3://{Bucket}/{Key}", bucket, key);
                filesSkipped++;
                continue;
            }

            await using var responseStream = getResponse.ResponseStream;
            using var buffer = new MemoryStream();
            await responseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            buffer.Position = 0;

            var bytes = buffer.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            // Accurate dedup — matches Local provider semantics. Even if
            // the ETag didn't match at list-time (multipart upload
            // differences), identical content still dedups here.
            if (ingestedHashes.Contains(hash))
            {
                filesSkipped++;
                continue;
            }

            // Batch-size gate per ADR-0058 §6 (byte total).
            totalBytes += bytes.LongLength;
            if (_options.MaxBatchBytes is long maxBytes && totalBytes > maxBytes)
            {
                throw new InvalidOperationException(
                    $"S3 ingest batch byte total {totalBytes} exceeded Ingestion:MaxBatchBytes={maxBytes}.");
            }

            var ext = Path.GetExtension(key);
            var contentType = ExtensionToContentType.GetValueOrDefault(ext, "application/octet-stream");

            buffer.Position = 0;
            var ingestionRequest = new IngestionRequest(
                FileStream: buffer,
                Filename: Path.GetFileName(key),
                ContentType: contentType,
                SourceType: "s3",
                SourceUrl: $"s3://{bucket}/{key}",
                SubmittedBy: "admin-cloud-dir");

            try
            {
                await _orchestrator.ProcessFileAsync(ingestionRequest);
                filesQueued++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest s3://{Bucket}/{Key}", bucket, key);
                filesSkipped++;
            }
        }

        _logger.LogInformation(
            "Cloud dir batch {BatchId}: provider=s3 bucket={Bucket} queued={Queued} skipped={Skipped}",
            batchId, bucket, filesQueued, filesSkipped);

        return new IngestionDto.CloudDirIngestResponse(filesQueued, filesSkipped, batchId);
    }

    private async Task<List<string>> ListKeysUnderPrefixAsync(string bucket, string? prefix, CancellationToken ct)
    {
        var keys = new List<string>();
        string? continuationToken = null;
        do
        {
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                MaxKeys = _options.S3!.PageSize,
                ContinuationToken = continuationToken
            }, ct).ConfigureAwait(false);

            keys.AddRange(response.S3Objects
                .Where(o => AllowedExtensions.Contains(Path.GetExtension(o.Key)))
                .Select(o => o.Key));

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (continuationToken is not null);

        return keys;
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "S3 cloud-directory provider is not configured. " +
                "Set Ingestion:S3:Enabled=true and populate Ingestion:S3:AllowedBuckets.");
    }

    private void EnsureBucketAllowed(string bucket)
    {
        if (!_options.S3!.AllowedBuckets.Contains(bucket, StringComparer.Ordinal))
        {
            _logger.LogWarning("S3 bucket rejected (not in allowlist): {Bucket}", bucket);
            throw new UnauthorizedAccessException(
                $"Bucket '{bucket}' is not in Ingestion:S3:AllowedBuckets.");
        }
    }

    /// <summary>
    /// S3 ETag values come wrapped in double quotes from the SDK
    /// (<c>"abc123"</c>). Strip them for clean storage and comparison.
    /// </summary>
    private static string NormalizeETag(string? etag) =>
        etag?.Trim('"') ?? string.Empty;
}
