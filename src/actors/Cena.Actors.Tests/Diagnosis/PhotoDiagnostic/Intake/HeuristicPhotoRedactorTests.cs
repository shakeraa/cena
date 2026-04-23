// =============================================================================
// Cena Platform — HeuristicPhotoRedactor tests (EPIC-PRR-J PRR-413)
//
// Invariants under test:
//   - Raw-byte mode zero-fills the top + bottom margin rows (and only those).
//   - Structured MIME (image/jpeg, image/png, ...) returns bytes unchanged
//     and emits "structured_image_decoder_required" — the honest "we can't
//     do this yet" signal the audit log records.
//   - Faces + Logos flags return their respective deferred tags regardless
//     of MIME path.
//   - Region percentages + RedactionMethod are correct so the audit log is
//     resolution-independent + human-readable.
//   - Null-argument validation is strict.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic.Intake;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic.Intake;

public class HeuristicPhotoRedactorTests
{
    private static HeuristicPhotoRedactor NewRedactor() =>
        new(NullLogger<HeuristicPhotoRedactor>.Instance);

    private static byte[] FilledRawImage(int width, int height, byte fill = 0xAB)
    {
        var bytes = new byte[width * height];
        Array.Fill(bytes, fill);
        return bytes;
    }

    [Fact]
    public async Task RawBytes_TopAndBottomMarginsZeroed_CenterUntouched()
    {
        // 100x100 raw grayscale filled with 0xAB.
        const int width = 100;
        const int height = 100;
        var bytes = FilledRawImage(width, height);

        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            bytes,
            new PhotoRedactionRequest(
                HeuristicPhotoRedactor.RawGrayscaleMimeType,
                width, height,
                PhotoRedactionKinds.TopMargin | PhotoRedactionKinds.BottomMargin),
            CancellationToken.None);

        var mutated = result.RedactedImageBytes.ToArray();
        // Input is not the returned handle (heuristic copies to mutate).
        Assert.NotSame(bytes, mutated);

        // Top 10 rows (10% of 100) all zero.
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(0x00, mutated[y * width + x]);
            }
        }

        // Bottom 10 rows all zero.
        for (int y = height - 10; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(0x00, mutated[y * width + x]);
            }
        }

        // Center rows (y = 10 .. 89) untouched.
        for (int y = 10; y < height - 10; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(0xAB, mutated[y * width + x]);
            }
        }

        Assert.Empty(result.DeferredRedactions); // No Faces/Logos/structured deferrals.
    }

    [Fact]
    public async Task Jpeg_InputBytesUnchanged_EmitsStructuredDecoderRequired()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0x33 };

        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            bytes,
            new PhotoRedactionRequest(
                "image/jpeg", 1200, 1600,
                PhotoRedactionKinds.TopMargin | PhotoRedactionKinds.BottomMargin),
            CancellationToken.None);

        Assert.Equal(bytes, result.RedactedImageBytes.ToArray());
        Assert.Empty(result.AppliedRedactions);
        Assert.Contains("structured_image_decoder_required", result.DeferredRedactions);
    }

    [Fact]
    public async Task Png_InputBytesUnchanged_EmitsStructuredDecoderRequired()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xAA };

        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            bytes,
            new PhotoRedactionRequest(
                "image/png", 800, 1000,
                PhotoRedactionKinds.TopMargin),
            CancellationToken.None);

        Assert.Equal(bytes, result.RedactedImageBytes.ToArray());
        Assert.Empty(result.AppliedRedactions);
        Assert.Contains("structured_image_decoder_required", result.DeferredRedactions);
    }

    [Fact]
    public async Task FacesFlag_EmitsFaceDetectionNotImplemented()
    {
        const int width = 50;
        const int height = 60;
        var bytes = FilledRawImage(width, height);

        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            bytes,
            new PhotoRedactionRequest(
                HeuristicPhotoRedactor.RawGrayscaleMimeType, width, height,
                PhotoRedactionKinds.Faces),
            CancellationToken.None);

        Assert.Contains("face_detection_not_implemented", result.DeferredRedactions);
    }

    [Fact]
    public async Task LogosFlag_EmitsLogoTemplateMatcherNotImplemented()
    {
        const int width = 50;
        const int height = 60;
        var bytes = FilledRawImage(width, height);

        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            bytes,
            new PhotoRedactionRequest(
                HeuristicPhotoRedactor.RawGrayscaleMimeType, width, height,
                PhotoRedactionKinds.Logos),
            CancellationToken.None);

        Assert.Contains("logo_template_matcher_not_implemented", result.DeferredRedactions);
    }

    [Fact]
    public async Task AppliedRegions_HaveCorrectPercentages()
    {
        const int width = 200;
        const int height = 400; // 10% => 40 rows each band.
        var bytes = FilledRawImage(width, height);

        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            bytes,
            new PhotoRedactionRequest(
                HeuristicPhotoRedactor.OctetStreamMimeType, width, height,
                PhotoRedactionKinds.TopMargin | PhotoRedactionKinds.BottomMargin),
            CancellationToken.None);

        Assert.Equal(2, result.AppliedRedactions.Count);

        var top = result.AppliedRedactions.Single(r => r.Kind == PhotoRedactionKinds.TopMargin);
        Assert.Equal(0.0, top.XPct);
        Assert.Equal(0.0, top.YPct);
        Assert.Equal(1.0, top.WidthPct);
        Assert.Equal(0.10, top.HeightPct, precision: 3);

        var bottom = result.AppliedRedactions.Single(r => r.Kind == PhotoRedactionKinds.BottomMargin);
        Assert.Equal(0.0, bottom.XPct);
        Assert.Equal(0.90, bottom.YPct, precision: 3); // (height - 40) / 400
        Assert.Equal(1.0, bottom.WidthPct);
        Assert.Equal(0.10, bottom.HeightPct, precision: 3);
    }

    [Fact]
    public async Task AppliedRegions_UseSolidFillMethod()
    {
        const int width = 100;
        const int height = 100;
        var bytes = FilledRawImage(width, height);

        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            bytes,
            new PhotoRedactionRequest(
                HeuristicPhotoRedactor.RawGrayscaleMimeType, width, height,
                PhotoRedactionKinds.TopMargin | PhotoRedactionKinds.BottomMargin),
            CancellationToken.None);

        Assert.All(result.AppliedRedactions, r =>
            Assert.Equal("solid-fill", r.RedactionMethod));
    }

    [Fact]
    public async Task NullRequest_Throws()
    {
        var redactor = NewRedactor();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            redactor.RedactAsync(ReadOnlyMemory<byte>.Empty, null!, CancellationToken.None));
    }

    [Fact]
    public async Task NullLogger_ToConstructor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HeuristicPhotoRedactor(null!));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RawMode_LengthMismatch_Throws()
    {
        // Declares 10x10 but only hands over 5 bytes.
        var redactor = NewRedactor();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            redactor.RedactAsync(
                new byte[] { 1, 2, 3, 4, 5 },
                new PhotoRedactionRequest(
                    HeuristicPhotoRedactor.RawGrayscaleMimeType, 10, 10,
                    PhotoRedactionKinds.TopMargin),
                CancellationToken.None));
    }

    [Fact]
    public async Task StructuredMime_FacesRequested_DeferredIncludesBoth()
    {
        var redactor = NewRedactor();
        var result = await redactor.RedactAsync(
            new byte[] { 0xFF, 0xD8 },
            new PhotoRedactionRequest(
                "image/jpeg", 100, 100,
                PhotoRedactionKinds.TopMargin | PhotoRedactionKinds.Faces),
            CancellationToken.None);

        Assert.Contains("structured_image_decoder_required", result.DeferredRedactions);
        Assert.Contains("face_detection_not_implemented", result.DeferredRedactions);
        Assert.Empty(result.AppliedRedactions);
    }
}
