// =============================================================================
// Cena Platform — LocalFileSystemBagrutPdfStore
//
// Filesystem implementation of IBagrutPdfStore. Mirrors the storage shape
// of Layer2cFigureExtraction (content-addressed PNGs under a bind-mount
// directory) so the operational story is consistent: one volume mount per
// content type (figures, source PDFs), filename = content hash, no DB
// rows holding bytes.
//
// Layout under the configured RootDirectory:
//   {root}/{hash[:2]}/{hash}.pdf
//
// The two-character prefix sharding keeps any one directory under ~256
// thousand files at a million-PDF scale (256 buckets × ~4096 files per
// bucket before ext4 perf drops noticeably).
//
// Atomic writes: tmp file + rename. Two ingestions of the same PDF
// content racing each other never produce a torn file. The second writer
// silently wins (overwrite=true on Move, content is identical).
//
// Configuration: bind from the "Ingestion:BagrutPdfStorage" section.
// Defaults to {TempPath}/cena-source-pdfs so dev works without config —
// matches the FigureStorageOptions convention. Production hosts override
// to /var/cena/source-pdfs (bind-mount in docker-compose.app.yml).
// =============================================================================

using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Ingestion;

public sealed class BagrutPdfStorageOptions
{
    public string RootDirectory { get; init; } =
        Path.Combine(Path.GetTempPath(), "cena-source-pdfs");
}

public sealed class LocalFileSystemBagrutPdfStore : IBagrutPdfStore
{
    private readonly string _root;

    public LocalFileSystemBagrutPdfStore(IOptions<BagrutPdfStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = options.Value.RootDirectory;
        Directory.CreateDirectory(_root);
    }

    public async Task PersistAsync(string pdfId, byte[] pdfBytes, CancellationToken ct = default)
    {
        ValidateId(pdfId);
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length == 0)
            throw new ArgumentException("PDF bytes are empty.", nameof(pdfBytes));

        var (dir, path) = ResolvePath(pdfId);
        Directory.CreateDirectory(dir);

        if (File.Exists(path))
        {
            // Already persisted (content-addressable; same id = same bytes).
            return;
        }

        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, pdfBytes, ct).ConfigureAwait(false);
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
            // Concurrent writer raced us and won. Bytes are identical
            // (content-addressable); silently discard our copy.
            try { File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    public Task<bool> ExistsAsync(string pdfId, CancellationToken ct = default)
    {
        ValidateId(pdfId);
        var (_, path) = ResolvePath(pdfId);
        return Task.FromResult(File.Exists(path));
    }

    public Task<Stream?> OpenReadAsync(string pdfId, CancellationToken ct = default)
    {
        ValidateId(pdfId);
        var (_, path) = ResolvePath(pdfId);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        // FileShare.Read so concurrent readers don't lock each other.
        // FileOptions.Asynchronous keeps the read non-blocking under load.
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    /// <summary>
    /// Internal — exposed for the unit test that asserts the layout.
    /// </summary>
    internal (string Directory, string FullPath) ResolvePath(string pdfId)
    {
        var safe = pdfId.ToLowerInvariant();
        var bucket = safe.Length >= 2 ? safe[..2] : "00";
        var dir = Path.Combine(_root, bucket);
        var path = Path.Combine(dir, $"{safe}.pdf");
        return (dir, path);
    }

    private static void ValidateId(string pdfId)
    {
        if (string.IsNullOrWhiteSpace(pdfId))
            throw new ArgumentException("pdfId is required.", nameof(pdfId));

        // Tighten to hex to avoid path traversal (`..`) or directory
        // separators slipping in. BagrutPdfIngestionService.GeneratePdfId
        // emits sha256 hex (64 chars).
        foreach (var ch in pdfId)
        {
            var ok = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
            if (!ok)
                throw new ArgumentException($"pdfId must be hex. Got: '{pdfId}'", nameof(pdfId));
        }
    }
}
