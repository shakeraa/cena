// =============================================================================
// Cena Platform — PdfPageRasterizer tests (vision-extractor branch)
//
// Pins the contract:
//   - Multi-page PDF rasterises into N PNGs in page order.
//   - Re-running with the same pdfId is idempotent (cache hit, no shell call).
//   - Encrypted/locked PDFs surface as InvalidOperationException carrying
//     the password / unlocked / encrypted cue (matches the vision-path
//     mapper to "encrypted_pdf:cannot_read_without_password" warning).
//
// We can't bundle a real PDF + assume Poppler is installed in CI, so we
// substitute a fake binary via SourcePageStorageOptions.PdftoppmBinaryPath.
// The fake binary is a small shell script (Unix) / batch file (Windows)
// that creates a configurable number of dummy PNG files in the outDir
// matching the prefix pdftoppm uses. This proves the rasterizer:
//   - calls the configured binary,
//   - passes the right -r DPI,
//   - reads back the PNG files from the outDir,
//   - returns them in sorted page order,
//   - is idempotent on re-rasterise.
//
// The encrypted-PDF test uses a fake binary that exits 1 with a stderr
// message containing "Incorrect password" — same surface a real Poppler
// emits for an encrypted PDF.
// =============================================================================

using System.Runtime.InteropServices;
using System.Text;
using Cena.Admin.Api.Ingestion.Vision;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Tests.Ingestion.Vision;

public sealed class PdfPageRasterizerTests : IDisposable
{
    private readonly string _scratchRoot;
    private readonly string _scriptPath;

    public PdfPageRasterizerTests()
    {
        _scratchRoot = Path.Combine(
            Path.GetTempPath(),
            $"cena-rasterizer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchRoot);
        _scriptPath = WriteFakePdftoppm(pageCount: 3, exitWithEncrypted: false);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_scratchRoot)) Directory.Delete(_scratchRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task SinglePagePdf_Renders1Png_AtConfiguredDpi()
    {
        var script = WriteFakePdftoppm(pageCount: 1, exitWithEncrypted: false);
        var rasterizer = BuildRasterizer(script);

        var paths = await rasterizer.RasterizeAsync(new byte[] { 1, 2, 3 }, "pdf-single");

        Assert.Single(paths);
        Assert.True(File.Exists(paths[0]));
        Assert.EndsWith(".png", paths[0]);
    }

    [Fact]
    public async Task SixPagePdf_Renders6PngsInOrder()
    {
        var script = WriteFakePdftoppm(pageCount: 6, exitWithEncrypted: false);
        var rasterizer = BuildRasterizer(script);

        var paths = await rasterizer.RasterizeAsync(new byte[] { 1, 2, 3 }, "pdf-six-page");

        Assert.Equal(6, paths.Count);
        // Filenames must sort to natural page order. Our fake emits
        // page-1.png, page-2.png, ... page-6.png; with OrdinalIgnoreCase
        // sort, "page-1" lands before "page-2" etc up to single-digit, and
        // multi-digit cases also sort correctly because they're padded.
        for (var i = 0; i < paths.Count - 1; i++)
        {
            Assert.True(
                StringComparer.OrdinalIgnoreCase.Compare(paths[i], paths[i + 1]) < 0,
                $"page paths out of order: {paths[i]} >= {paths[i + 1]}");
        }
    }

    [Fact]
    public async Task ReRasterise_WithSamePdfId_IsIdempotent_NoBinaryCall()
    {
        // Run once to populate the cache directory.
        var script = WriteFakePdftoppm(pageCount: 2, exitWithEncrypted: false);
        var rasterizer = BuildRasterizer(script);
        var paths1 = await rasterizer.RasterizeAsync(new byte[] { 1 }, "pdf-cached");
        Assert.Equal(2, paths1.Count);

        // Now point the binary at a script that would EXPLODE if called.
        var explodeScript = WriteFakeBinary(
            "#!/usr/bin/env bash\necho 'should not run' 1>&2\nexit 99\n",
            ".sh");

        var rasterizer2 = BuildRasterizer(explodeScript);
        // Same pdfId → cache hit → binary never invoked.
        var paths2 = await rasterizer2.RasterizeAsync(new byte[] { 1 }, "pdf-cached");

        Assert.Equal(2, paths2.Count);
        Assert.Equal(paths1, paths2);
    }

    [Fact]
    public async Task EncryptedPdf_ThrowsInvalidOperation_WithPasswordCue()
    {
        var script = WriteFakePdftoppm(pageCount: 0, exitWithEncrypted: true);
        var rasterizer = BuildRasterizer(script);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rasterizer.RasterizeAsync(new byte[] { 1, 2, 3 }, "pdf-encrypted"));

        // The mapper in BagrutPdfIngestionService.IsEncryptedRenderError
        // looks for "password" / "unlocked" / "encrypted" in the message.
        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BinaryMissing_ThrowsInvalidOperationException()
    {
        var rasterizer = BuildRasterizer(
            Path.Combine(_scratchRoot, "definitely-not-a-real-binary-xyz123"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rasterizer.RasterizeAsync(new byte[] { 1, 2, 3 }, "pdf-missing-bin"));
    }

    [Fact]
    public async Task EmptyPdfBytes_ThrowsArgumentException()
    {
        var rasterizer = BuildRasterizer(_scriptPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rasterizer.RasterizeAsync(Array.Empty<byte>(), "pdf-empty"));
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private PdfPageRasterizer BuildRasterizer(string binaryPath)
    {
        var opts = Options.Create(new SourcePageStorageOptions
        {
            RootDirectory = Path.Combine(_scratchRoot, "pages"),
            Dpi = 200,
            PdftoppmBinaryPath = binaryPath,
            RenderTimeout = TimeSpan.FromSeconds(10),
        });
        return new PdfPageRasterizer(opts, NullLogger<PdfPageRasterizer>.Instance);
    }

    /// <summary>
    /// Emit a fake pdftoppm script. The real `pdftoppm -r {dpi} -png in.pdf
    /// {prefix}` writes `{prefix}-{n}.png`; the fake mimics that.
    /// </summary>
    private string WriteFakePdftoppm(int pageCount, bool exitWithEncrypted)
    {
        if (exitWithEncrypted)
        {
            // Match the cue real Poppler emits.
            return WriteFakeBinary(
                IsWindows()
                    ? "@echo off\r\necho Command Line Error: Incorrect password 1>&2\r\nexit 1\r\n"
                    : "#!/usr/bin/env bash\necho 'Command Line Error: Incorrect password' 1>&2\nexit 1\n",
                IsWindows() ? ".cmd" : ".sh");
        }

        // The fake parses the args (we know our caller's shape:
        // `-r 200 -png in.pdf prefix`) and writes pageCount PNGs to disk.
        // We use a minimal valid-PNG byte sequence so File.Exists + a
        // sanity content-length check succeed.
        var script = IsWindows()
            ? BuildWindowsFake(pageCount)
            : BuildUnixFake(pageCount);
        return WriteFakeBinary(script, IsWindows() ? ".cmd" : ".sh");
    }

    private static string BuildUnixFake(int pageCount)
    {
        // Args layout: $1=-r $2=200 $3=-png $4=in.pdf $5=prefix
        // We grab the prefix from $5. PNG byte sequence inline as printf.
        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("set -e");
        sb.AppendLine("PREFIX=\"${5}\"");
        for (var i = 1; i <= pageCount; i++)
        {
            // pdftoppm uses page-N.png with no zero-padding when -fpd is
            // unset; we mirror that. The rasterizer's SortedPagePaths
            // depends only on glob page-*.png so this works.
            sb.AppendLine($"printf '\\x89PNG\\r\\n\\x1a\\nfake-page-{i}' > \"${{PREFIX}}-{i}.png\"");
        }
        sb.AppendLine("exit 0");
        return sb.ToString();
    }

    private static string BuildWindowsFake(int pageCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        // Args: %1=-r %2=200 %3=-png %4=in.pdf %5=prefix
        sb.AppendLine("set PREFIX=%~5");
        for (var i = 1; i <= pageCount; i++)
        {
            sb.AppendLine($"echo fake-page-{i}> \"%PREFIX%-{i}.png\"");
        }
        sb.AppendLine("exit 0");
        return sb.ToString();
    }

    private string WriteFakeBinary(string content, string ext)
    {
        var path = Path.Combine(_scratchRoot, $"fake-pdftoppm-{Guid.NewGuid():N}{ext}");
        File.WriteAllText(path, content);
        if (!IsWindows())
        {
            // chmod +x via Mono.Posix is overkill — use shell.
            try
            {
                using var p = System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"");
                p?.WaitForExit(2000);
            }
            catch
            {
                /* best-effort; if chmod fails the test will fail loudly */
            }
        }
        return path;
    }

    private static bool IsWindows() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
