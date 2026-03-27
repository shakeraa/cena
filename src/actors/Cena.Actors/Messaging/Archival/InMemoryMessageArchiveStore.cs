// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — In-Memory Message Archive Store
// Layer: Test Infrastructure | Runtime: .NET 9
// In-memory implementation of IMessageArchiveStore for unit tests.
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Concurrent;

namespace Cena.Actors.Messaging.Archival;

/// <summary>
/// In-memory blob store for testing. Production uses S3.
/// </summary>
public sealed class InMemoryMessageArchiveStore : IMessageArchiveStore
{
    private readonly ConcurrentDictionary<string, byte[]> _archives = new();
    private readonly ConcurrentDictionary<string, string> _manifests = new();

    public int UploadCount => _archives.Count;
    public int ManifestCount => _manifests.Count;

    public Task UploadArchiveAsync(string key, byte[] gzippedData, CancellationToken ct = default)
    {
        _archives[key] = gzippedData;
        return Task.CompletedTask;
    }

    public Task<byte[]?> DownloadArchiveAsync(string key, CancellationToken ct = default)
    {
        _archives.TryGetValue(key, out var data);
        return Task.FromResult(data);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        return Task.FromResult(_archives.ContainsKey(key));
    }

    public Task UploadManifestAsync(string key, string manifestJson, CancellationToken ct = default)
    {
        _manifests[key] = manifestJson;
        return Task.CompletedTask;
    }

    public Task<string?> DownloadManifestAsync(string key, CancellationToken ct = default)
    {
        _manifests.TryGetValue(key, out var json);
        return Task.FromResult(json);
    }

    public IReadOnlyDictionary<string, byte[]> Archives => _archives;
    public IReadOnlyDictionary<string, string> Manifests => _manifests;
}
