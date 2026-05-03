// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — S3 Message Reader (Cold Tier)
// Layer: Infrastructure | Runtime: .NET 9
// Reads archived messages from blob storage (S3) with caching.
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Cena.Actors.Messaging.Archival;

public sealed class S3MessageReader
{
    private readonly IMessageArchiveStore _store;

    // Cache manifests for 1 hour (immutable once written)
    private readonly ConcurrentDictionary<string, (ArchivalManifest Manifest, DateTimeOffset CachedAt)>
        _manifestCache = new();
    private static readonly TimeSpan ManifestCacheTtl = TimeSpan.FromHours(1);

    // Cache decompressed thread data for 5 minutes (LRU approximation)
    private readonly ConcurrentDictionary<string, (MessageView[] Messages, DateTimeOffset CachedAt)>
        _threadCache = new();
    private static readonly TimeSpan ThreadCacheTtl = TimeSpan.FromMinutes(5);
    private const int MaxCachedThreads = 100;

    public int ManifestFetchCount { get; private set; }

    public S3MessageReader(IMessageArchiveStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Read messages for a thread from the cold archive.
    /// Returns empty if thread not found in any manifest.
    /// </summary>
    public async Task<MessagePage> GetMessagesAsync(
        string threadId, DateTimeOffset before, int limit,
        CancellationToken ct = default)
    {
        if (limit < 1) limit = 1;
        if (limit > 50) limit = 50;

        // Try to get cached thread data
        var messages = await LoadThreadMessagesAsync(threadId, before, ct);
        if (messages == null || messages.Length == 0)
            return new MessagePage(Array.Empty<MessageView>(), null, false);

        // Filter to messages before the cursor
        var filtered = messages
            .Where(m => m.SentAt < before)
            .OrderByDescending(m => m.SentAt)
            .ToArray();

        if (filtered.Length == 0)
            return new MessagePage(Array.Empty<MessageView>(), null, false);

        bool hasMore = filtered.Length > limit;
        var page = hasMore ? filtered[..limit] : filtered;
        string? nextCursor = hasMore ? page[^1].SentAt.ToString("O") : null;

        return new MessagePage(page, nextCursor, hasMore);
    }

    private async Task<MessageView[]?> LoadThreadMessagesAsync(
        string threadId, DateTimeOffset around, CancellationToken ct)
    {
        // Check thread cache first
        var threadCacheKey = $"{threadId}:{around:yyyy-MM}";
        if (_threadCache.TryGetValue(threadCacheKey, out var cached)
            && DateTimeOffset.UtcNow - cached.CachedAt < ThreadCacheTtl)
        {
            return cached.Messages;
        }

        // Find the archive in manifests (check the month of 'around' and surrounding months)
        var monthsToCheck = new[]
        {
            around,
            around.AddMonths(-1),
            around.AddMonths(1),
        };

        foreach (var month in monthsToCheck)
        {
            var manifest = await LoadManifestAsync(month, ct);
            if (manifest == null) continue;

            var entry = manifest.Threads.FirstOrDefault(t => t.ThreadId == threadId);
            if (entry == null) continue;

            // Download and decompress the archive
            var data = await _store.DownloadArchiveAsync(entry.S3Key, ct);
            if (data == null) continue;

            var messages = DecompressMessages(data);

            // Cache the decompressed data
            EvictOldestIfNeeded();
            _threadCache[threadCacheKey] = (messages, DateTimeOffset.UtcNow);

            return messages;
        }

        return null;
    }

    private async Task<ArchivalManifest?> LoadManifestAsync(
        DateTimeOffset month, CancellationToken ct)
    {
        var manifestKey = ArchivalKeys.ManifestKey(month);

        // Check cache
        if (_manifestCache.TryGetValue(manifestKey, out var cached)
            && DateTimeOffset.UtcNow - cached.CachedAt < ManifestCacheTtl)
        {
            return cached.Manifest;
        }

        ManifestFetchCount++;

        var json = await _store.DownloadManifestAsync(manifestKey, ct);
        if (json == null) return null;

        var manifest = JsonSerializer.Deserialize<ArchivalManifest>(json);
        if (manifest == null) return null;

        _manifestCache[manifestKey] = (manifest, DateTimeOffset.UtcNow);
        return manifest;
    }

    internal static MessageView[] DecompressMessages(byte[] gzippedData)
    {
        var messages = new List<MessageView>();

        using var memStream = new MemoryStream(gzippedData);
        using var gzipStream = new GZipStream(memStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(line);
            if (dict == null) continue;

            messages.Add(new MessageView(
                MessageId: dict.GetValueOrDefault("messageId", ""),
                SenderId: dict.GetValueOrDefault("senderId", ""),
                SenderRole: dict.GetValueOrDefault("senderRole", "System"),
                SenderName: dict.GetValueOrDefault("senderName", ""),
                ContentText: dict.GetValueOrDefault("contentText", ""),
                ContentType: dict.GetValueOrDefault("contentType", "text"),
                ResourceUrl: NullIfEmpty(dict.GetValueOrDefault("resourceUrl", "")),
                ReplyToMessageId: NullIfEmpty(dict.GetValueOrDefault("replyTo", "")),
                Channel: dict.GetValueOrDefault("channel", "InApp"),
                SentAt: DateTimeOffset.TryParse(dict.GetValueOrDefault("sentAt", ""), out var dt)
                    ? dt : DateTimeOffset.UtcNow,
                StreamEntryId: dict.GetValueOrDefault("_streamId", "")));
        }

        return messages.ToArray();
    }

    private void EvictOldestIfNeeded()
    {
        if (_threadCache.Count < MaxCachedThreads) return;

        // Remove oldest entries
        var toRemove = _threadCache
            .OrderBy(kv => kv.Value.CachedAt)
            .Take(_threadCache.Count - MaxCachedThreads + 1)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
            _threadCache.TryRemove(key, out _);
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;
}
