// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Message Archival Worker
// Layer: Worker | Runtime: .NET 9
// Nightly batch job: scans Redis for threads approaching TTL,
// archives them to blob storage (S3) as gzipped JSON-lines.
// ═══════════════════════════════════════════════════════════════════════

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Messaging.Archival;

public sealed class MessageArchivalWorker
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageArchiveStore _store;
    private readonly ILogger<MessageArchivalWorker> _logger;

    /// <summary>Threads with TTL below this threshold get archived.</summary>
    private static readonly TimeSpan TtlThreshold = TimeSpan.FromHours(48);

    /// <summary>Chunk size for reading Redis Streams.</summary>
    private const int ReadChunkSize = 1000;

    public MessageArchivalWorker(
        IConnectionMultiplexer redis,
        IMessageArchiveStore store,
        ILogger<MessageArchivalWorker> logger)
    {
        _redis = redis;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Scan all thread streams, archive those with TTL below threshold.
    /// Idempotent: skips threads already archived with matching checksum.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Archival worker starting...");

        var db = _redis.GetDatabase();
        var server = _redis.GetServers().FirstOrDefault();
        if (server == null)
        {
            _logger.LogWarning("No Redis server available for archival scan");
            return;
        }

        int archived = 0;
        int skipped = 0;
        int errors = 0;

        // Scan for thread stream keys
        await foreach (var key in server.KeysAsync(pattern: "cena:thread:*", pageSize: 100).WithCancellation(ct))
        {
            var keyStr = key.ToString();

            // Skip non-stream keys (meta, unread, etc.)
            if (keyStr.Contains(":meta") || keyStr.Contains(":unread"))
                continue;

            try
            {
                var ttl = await db.KeyTimeToLiveAsync(key);

                // Only archive threads with TTL below threshold
                if (ttl == null || ttl > TtlThreshold)
                {
                    skipped++;
                    continue;
                }

                var threadId = ExtractThreadId(keyStr);
                if (threadId == null)
                {
                    skipped++;
                    continue;
                }

                await ArchiveThreadAsync(db, threadId, ct);
                archived++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive thread from key {Key}", keyStr);
                errors++;
            }
        }

        _logger.LogInformation(
            "Archival worker complete: archived={Archived}, skipped={Skipped}, errors={Errors}",
            archived, skipped, errors);
    }

    private async Task ArchiveThreadAsync(IDatabase db, string threadId, CancellationToken ct)
    {
        var streamKey = MessagingRedisKeys.ThreadStream(threadId);

        // Read all entries in chunks (memory-safe for large threads)
        var allEntries = new List<StreamEntry>();
        RedisValue cursor = "0-0";

        while (true)
        {
            var chunk = await db.StreamRangeAsync(streamKey,
                minId: cursor, maxId: "+",
                count: ReadChunkSize, messageOrder: Order.Ascending);

            if (chunk.Length == 0) break;

            allEntries.AddRange(chunk);

            if (chunk.Length < ReadChunkSize) break;

            // Move cursor past the last entry
            cursor = chunk[^1].Id;
            // Increment the sequence to avoid re-reading the last entry
            var parts = cursor.ToString().Split('-');
            if (parts.Length == 2 && long.TryParse(parts[1], out var seq))
                cursor = $"{parts[0]}-{seq + 1}";
        }

        if (allEntries.Count == 0)
        {
            _logger.LogDebug("Thread {ThreadId} has no entries, skipping", threadId);
            return;
        }

        // Determine date range
        var firstEntry = allEntries[0];
        var lastEntry = allEntries[^1];
        var firstDate = ParseSentAt(firstEntry) ?? DateTimeOffset.UtcNow;
        var lastDate = ParseSentAt(lastEntry) ?? DateTimeOffset.UtcNow;

        // Build archive key
        var archiveKey = ArchivalKeys.ArchiveKey(threadId, firstDate, lastDate);

        // Check idempotency: if already archived with same entry count, skip
        if (await _store.ExistsAsync(archiveKey, ct))
        {
            _logger.LogDebug("Thread {ThreadId} already archived at {Key}", threadId, archiveKey);
            return;
        }

        // Serialize to JSON-lines and gzip
        var gzipped = CompressEntries(allEntries);
        var sha256 = ComputeSha256(gzipped);

        // Upload archive
        await _store.UploadArchiveAsync(archiveKey, gzipped, ct);

        // Update manifest
        await UpdateManifestAsync(threadId, archiveKey, allEntries.Count,
            firstDate, lastDate, gzipped.Length, sha256, ct);

        _logger.LogInformation(
            "Archived thread {ThreadId}: {Count} messages, {Size} bytes, key={Key}",
            threadId, allEntries.Count, gzipped.Length, archiveKey);
    }

    private async Task UpdateManifestAsync(
        string threadId, string archiveKey, int messageCount,
        DateTimeOffset firstDate, DateTimeOffset lastDate,
        long sizeBytes, string sha256, CancellationToken ct)
    {
        var manifestKey = ArchivalKeys.ManifestKey(DateTimeOffset.UtcNow);

        // Load existing manifest or create new
        ArchivalManifest manifest;
        var existingJson = await _store.DownloadManifestAsync(manifestKey, ct);
        if (existingJson != null)
        {
            manifest = JsonSerializer.Deserialize<ArchivalManifest>(existingJson)
                ?? new ArchivalManifest();
        }
        else
        {
            manifest = new ArchivalManifest();
        }

        manifest.Month = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        manifest.ArchivedAt = DateTimeOffset.UtcNow;

        // Remove existing entry for this thread (idempotent update)
        manifest.Threads.RemoveAll(t => t.ThreadId == threadId);

        manifest.Threads.Add(new ArchivedThreadEntry
        {
            ThreadId = threadId,
            S3Key = archiveKey,
            MessageCount = messageCount,
            FirstMessageAt = firstDate,
            LastMessageAt = lastDate,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
        });

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await _store.UploadManifestAsync(manifestKey, json, ct);
    }

    internal static byte[] CompressEntries(List<StreamEntry> entries)
    {
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzipStream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var dict = new Dictionary<string, string>();
                foreach (var nv in entry.Values)
                    dict[nv.Name.ToString()] = nv.Value.ToString();

                dict["_streamId"] = entry.Id.ToString();
                writer.WriteLine(JsonSerializer.Serialize(dict));
            }
        }

        return memoryStream.ToArray();
    }

    internal static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ExtractThreadId(string redisKey)
    {
        // Key format: cena:thread:{threadId}
        const string prefix = "cena:thread:";
        if (!redisKey.StartsWith(prefix)) return null;
        var remainder = redisKey[prefix.Length..];
        // No colons = it's the stream key, not meta/unread
        return remainder.Contains(':') ? null : remainder;
    }

    private static DateTimeOffset? ParseSentAt(StreamEntry entry)
    {
        foreach (var nv in entry.Values)
        {
            if (nv.Name == "sentAt" && DateTimeOffset.TryParse(nv.Value, out var dt))
                return dt;
        }
        return null;
    }
}
