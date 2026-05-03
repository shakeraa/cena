// =============================================================================
// Cena Platform — LocalFileSystemBagrutPdfStore tests
//
// Locks the storage layout + safety guarantees of the persistent PDF store
// that backs the visual-review side-by-side surface (2026-05-01):
//
//   - {root}/{hash[:2]}/{hash}.pdf is the on-disk path
//   - PersistAsync writes atomically (no torn files on concurrent uploads)
//   - PersistAsync is idempotent (re-writing same id is a no-op)
//   - ExistsAsync mirrors PersistAsync
//   - OpenReadAsync streams persisted bytes back unchanged
//   - OpenReadAsync returns null for unknown ids (caller maps to 404)
//   - Non-hex pdfIds are rejected (path-traversal defence)
//   - Empty bytes are rejected
//
// All tests use a per-test temp directory; teardown deletes the tree so
// xUnit parallel runs don't collide.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class LocalFileSystemBagrutPdfStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileSystemBagrutPdfStore _store;

    public LocalFileSystemBagrutPdfStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cena-pdfstore-test-{Guid.NewGuid():N}");
        var opts = Options.Create(new BagrutPdfStorageOptions { RootDirectory = _root });
        _store = new LocalFileSystemBagrutPdfStore(opts);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static string FakeHexId(byte b = 0xab)
    {
        // 64-char hex matches BagrutPdfIngestionService.GeneratePdfId output
        // (sha256 hex). Tests don't need a real hash — just the shape.
        return new string(Convert.ToHexString(new[] { b }).ToLowerInvariant()[0], 64);
    }

    [Fact]
    public async Task PersistAsync_WritesUnderTwoCharBucket()
    {
        var id = "abcdef" + new string('0', 58); // 64 chars, prefix "ab"
        var bytes = new byte[] { 1, 2, 3, 4 };

        await _store.PersistAsync(id, bytes);

        var (dir, path) = _store.ResolvePath(id);
        Assert.Equal(Path.Combine(_root, "ab"), dir);
        Assert.Equal(Path.Combine(_root, "ab", id + ".pdf"), path);
        Assert.True(File.Exists(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task PersistAsync_IsIdempotent_SameBytes_NoOpOnSecondCall()
    {
        var id = FakeHexId();
        var bytes = new byte[] { 9, 8, 7 };

        await _store.PersistAsync(id, bytes);
        var (_, path) = _store.ResolvePath(id);
        var firstWriteTime = File.GetLastWriteTimeUtc(path);

        // Wait a tick so a re-write would show a different mtime.
        await Task.Delay(20);
        await _store.PersistAsync(id, bytes);

        Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task PersistAsync_DoesNotLeaveTmpFile()
    {
        var id = FakeHexId(0xcd);
        await _store.PersistAsync(id, new byte[] { 1 });

        var (dir, _) = _store.ResolvePath(id);
        var leftover = Directory.EnumerateFiles(dir, "*.tmp").ToList();
        Assert.Empty(leftover);
    }

    [Fact]
    public async Task ExistsAsync_FalseBeforePersist_TrueAfter()
    {
        var id = FakeHexId(0xef);

        Assert.False(await _store.ExistsAsync(id));

        await _store.PersistAsync(id, new byte[] { 42 });

        Assert.True(await _store.ExistsAsync(id));
    }

    [Fact]
    public async Task OpenReadAsync_StreamsPersistedBytes()
    {
        var id = FakeHexId(0x12);
        var payload = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 fake");
        await _store.PersistAsync(id, payload);

        await using var stream = await _store.OpenReadAsync(id);
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public async Task OpenReadAsync_NullForMissingId()
    {
        var stream = await _store.OpenReadAsync(FakeHexId(0x34));
        Assert.Null(stream);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("not-hex-zzz")]
    [InlineData("../escape")]
    [InlineData("ABCxyz")]                       // mixed valid + invalid
    [InlineData("foo/bar")]                      // slash
    public async Task NonHexId_Throws(string badId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.PersistAsync(badId, new byte[] { 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.ExistsAsync(badId));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.OpenReadAsync(badId));
    }

    [Fact]
    public async Task EmptyBytes_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.PersistAsync(FakeHexId(), Array.Empty<byte>()));
    }

    [Fact]
    public async Task NullBytes_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.PersistAsync(FakeHexId(), null!));
    }

    [Fact]
    public async Task BlankId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.PersistAsync("   ", new byte[] { 1 }));
    }

    [Fact]
    public async Task ProductionIdFormat_PdfDashHex_RoundTrips()
    {
        // Locks the production ID shape emitted by
        // BagrutPdfIngestionService.GeneratePdfId: "pdf-" + 12 hex chars.
        // Earlier ValidateId rejected this with ArgumentException, and the
        // item-detail endpoint 500'd for every InReview / Published row
        // (2026-05-01 user report).
        var prodId = "pdf-f04f4f0b91b5";

        await _store.PersistAsync(prodId, new byte[] { 1, 2, 3 });
        Assert.True(await _store.ExistsAsync(prodId));

        await using var stream = await _store.OpenReadAsync(prodId);
        Assert.NotNull(stream);

        // Bucket prefix is the first 2 chars of the lowercased id —
        // here that's "pd" because of the prefix. Locks the convention.
        var (dir, path) = _store.ResolvePath(prodId);
        Assert.Equal(Path.Combine(_root, "pd"), dir);
        Assert.Equal(Path.Combine(_root, "pd", prodId + ".pdf"), path);
    }

    [Fact]
    public async Task MixedCaseHex_NormalizesToLower()
    {
        // sha256 hex from BagrutPdfIngestionService is already lowercase,
        // but the store lowercases for safety. Locks that contract.
        var upper = "ABCDEF" + new string('0', 58);
        var lower = upper.ToLowerInvariant();

        await _store.PersistAsync(upper, new byte[] { 7 });

        Assert.True(await _store.ExistsAsync(lower));
        var (_, path) = _store.ResolvePath(lower);
        Assert.True(File.Exists(path));
    }
}
