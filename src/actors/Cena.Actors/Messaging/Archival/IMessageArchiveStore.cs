// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Message Archive Store Interface
// Layer: Domain Interface | Runtime: .NET 9
// Abstracts S3 (or any blob storage) for message archival.
// Implementation provided by Cena.Infrastructure when AWSSDK is wired.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Messaging.Archival;

/// <summary>
/// Abstracts blob storage for archived messages. The implementation
/// uses S3 in production; tests use an in-memory implementation.
/// </summary>
public interface IMessageArchiveStore
{
    /// <summary>Upload a gzipped archive to the store.</summary>
    Task UploadArchiveAsync(string key, byte[] gzippedData, CancellationToken ct = default);

    /// <summary>Download an archive from the store.</summary>
    Task<byte[]?> DownloadArchiveAsync(string key, CancellationToken ct = default);

    /// <summary>Check if an archive exists.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Upload/overwrite the monthly manifest.</summary>
    Task UploadManifestAsync(string key, string manifestJson, CancellationToken ct = default);

    /// <summary>Download the monthly manifest.</summary>
    Task<string?> DownloadManifestAsync(string key, CancellationToken ct = default);
}
