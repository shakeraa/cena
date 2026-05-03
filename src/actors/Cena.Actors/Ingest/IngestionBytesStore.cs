// =============================================================================
// Cena Platform — Ingestion bytes store (PRR-RETRY-IMPL).
// Abstraction over the durable place we put the original file bytes during
// ingestion so the retry worker can re-feed the orchestrator. Kept as a
// minimal Put / Get / Delete trio: lifecycle/retention is handled by the
// underlying store (S3 lifecycle rule for prod; on-disk size-cap for dev),
// not by application code.
//
// Two implementations live here (no AWS SDK dependency):
//   - LocalDiskIngestionBytesStore: dev / CI. No LocalStack required.
//     Strips path separators from each key segment + validates the final
//     resolved path stays under the configured base, so a hostile
//     filename in S3Key cannot escape the directory.
//   - NullIngestionBytesStore: explicit no-op for tests + legacy
//     deployments that aren't ready to durably persist yet. Put returns
//     false (not-persisted) so PipelineItemDocument.BytesPersisted stays
//     false and the retry path refuses cleanly.
//
// The S3 impl lives in Cena.Admin.Api (S3IngestionBytesStore.cs) because
// AWSSDK.S3 is a project reference there, not in Cena.Actors. Both
// projects already coexist in the same hosts (Actor + Admin.Api), so
// putting the S3 impl in Admin.Api adds no new transitive dependency.
// =============================================================================

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Ingest;

public interface IIngestionBytesStore
{
    /// <summary>
    /// Persist <paramref name="bytes"/> at <paramref name="key"/>. Returns true
    /// only on durable success. Implementations log + return false on failure
    /// rather than throwing — the orchestrator must not break the upload path
    /// if the bytes store is unavailable.
    /// </summary>
    Task<bool> PutAsync(string key, byte[] bytes, string contentType, CancellationToken ct);

    /// <summary>
    /// Returns the persisted bytes for <paramref name="key"/> or null if the
    /// object is missing. Throws on transport failure so the retry worker
    /// can distinguish "permanently lost" (null) from "store transiently
    /// down" (exception → leave for next tick).
    /// </summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct);

    /// <summary>
    /// Best-effort delete. Used by retention/janitor jobs. No-op if missing.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct);
}

public sealed class IngestionBytesStoreOptions
{
    /// <summary>"s3" | "local" | "null". Default "local" for safe dev.</summary>
    public string Backend { get; set; } = "local";

    /// <summary>Base directory for the local-disk impl (must be writable).</summary>
    public string LocalPath { get; set; } = "/var/cena/incoming";

    /// <summary>S3 bucket for the bytes store (separate from the cloud-dir
    /// AllowedBuckets — those are read-only sources). Must already exist.</summary>
    public string? S3Bucket { get; set; }
}

public sealed class NullIngestionBytesStore : IIngestionBytesStore
{
    private readonly ILogger<NullIngestionBytesStore> _logger;
    public NullIngestionBytesStore(ILogger<NullIngestionBytesStore> logger) => _logger = logger;

    public Task<bool> PutAsync(string key, byte[] bytes, string contentType, CancellationToken ct)
    {
        _logger.LogDebug(
            "NullBytesStore: dropping {Bytes} bytes for {Key} — retries will be refused.",
            bytes.Length, key);
        return Task.FromResult(false);
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct) => Task.FromResult<byte[]?>(null);

    public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
}

public sealed class LocalDiskIngestionBytesStore : IIngestionBytesStore
{
    private readonly string _basePath;
    private readonly ILogger<LocalDiskIngestionBytesStore> _logger;

    public LocalDiskIngestionBytesStore(
        IOptions<IngestionBytesStoreOptions> options,
        ILogger<LocalDiskIngestionBytesStore> logger)
    {
        // Resolve once at construction so the path-traversal guard below
        // can compare against a stable canonical base. Trailing-slash
        // normalization matters: GetFullPath('foo/bar').StartsWith('foo')
        // is true but 'foo/barbaz'.StartsWith('foo') is also true — we
        // use Path.GetRelativePath to detect escapes properly.
        _basePath = Path.GetFullPath(options.Value.LocalPath);
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<bool> PutAsync(string key, byte[] bytes, string contentType, CancellationToken ct)
    {
        try
        {
            var path = ResolveSafePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LocalDiskBytesStore: PUT failed for {Key}", key);
            return false;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        var path = ResolveSafePath(key);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            var path = ResolveSafePath(key);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LocalDiskBytesStore: DELETE failed for {Key}", key);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sanitize then resolve <paramref name="key"/> under _basePath. The key
    /// is split on '/', each segment is stripped of OS path separators and
    /// drive-letter / colon prefixes, and the joined result is forced under
    /// _basePath via GetRelativePath. Any escape (relative path leaving
    /// the base) raises UnauthorizedAccessException — the orchestrator
    /// catches and treats as PUT-failure.
    /// </summary>
    private string ResolveSafePath(string key)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("key required", nameof(key));
        var segments = key.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeSegment)
            .Where(s => s.Length > 0)
            .ToArray();
        if (segments.Length == 0) throw new ArgumentException("key reduces to empty", nameof(key));

        var combined = Path.Combine(new[] { _basePath }.Concat(segments).ToArray());
        var canonical = Path.GetFullPath(combined);
        var rel = Path.GetRelativePath(_basePath, canonical);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new UnauthorizedAccessException($"path escape blocked for key '{key}'");
        return canonical;
    }

    private static string SanitizeSegment(string s)
    {
        // Drop drive-letter (C:) and any embedded path-traversal markers.
        var idx = s.IndexOfAny(Path.GetInvalidFileNameChars());
        var cleaned = idx < 0 ? s : new string(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        if (cleaned == "." || cleaned == "..") return string.Empty;
        return cleaned;
    }
}

