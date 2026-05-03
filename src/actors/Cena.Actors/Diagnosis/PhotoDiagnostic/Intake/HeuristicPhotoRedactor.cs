// =============================================================================
// Cena Platform — HeuristicPhotoRedactor (EPIC-PRR-J PRR-413)
//
// The minimum-viable REAL implementation of IPhotoRedactor shipped in the
// heuristic-only slice. Deliberately narrow: redacts the top 10% + bottom
// 10% of the image (the two bands where worksheet names, dates, and school
// footers typically live) by zero-filling the corresponding byte range — BUT
// ONLY when the caller supplies raw pixel bytes (MIME <c>application/octet-stream</c>
// or the canonical grayscale sentinel <c>image/x-cena-raw-gray8</c>).
//
// For structured images (image/jpeg, image/png, image/webp, image/heic) we
// explicitly DO NOT MUTATE the bytes — a JPEG/PNG file is a compressed,
// variable-length stream; zeroing a byte range corrupts the decoder state
// and produces an unreadable file, not a redacted image. The correct fix
// is to decode → alter pixels → re-encode, which requires a licensed
// image-decoding dependency (ImageSharp / System.Drawing / Magick.NET)
// that has not yet been added to Cena.Actors.csproj.
//
// Rather than fail closed on structured images (which would brick every real
// upload the moment the endpoint wires this redactor) we return the input
// unchanged PLUS emit the "structured_image_decoder_required" tag in
// DeferredRedactions so the audit log makes the gap visible to ops + the
// incidental-PII compliance dashboard. When the decoder dependency lands
// (follow-up task, tracked as FACE-DETECTOR-ML above + the parallel
// DECODER-LIB-LICENSING) this same interface is re-bound to a new
// implementation with zero caller-side churn — the PhotoRedactionRequest /
// PhotoRedactionResult types are already sufficient.
//
// Scope boundaries:
//   - Face detection: NOT implemented. Faces flag returns
//     "face_detection_not_implemented" in Deferred.
//   - Logo detection: NOT implemented. Logos flag returns
//     "logo_template_matcher_not_implemented" in Deferred.
//   - Margin redaction on structured MIME: NOT implemented (needs decoder),
//     returns "structured_image_decoder_required".
//   - Margin redaction on raw bytes: IMPLEMENTED via byte-level solid-fill.
//     We report the method as "solid-fill" (not "blur") because the bytes
//     are zeroed, not Gaussian-blurred.
//
// Byte layout assumption for raw mode:
//   - 8-bit single-channel grayscale, row-major, stride == width.
//   - imageBytes.Length MUST equal width * height (we validate).
// This matches the same convention as ImagePerceptualHash (PRR-404) so the
// same upstream grayscale step feeds both.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Intake;

/// <summary>
/// Byte-level margin redactor with honest deferral of structured-image +
/// face + logo work. See file header for the scope boundaries.
/// </summary>
public sealed class HeuristicPhotoRedactor : IPhotoRedactor
{
    /// <summary>Fraction of image height treated as the top margin band (10%).</summary>
    public const double TopMarginFraction = 0.10;

    /// <summary>Fraction of image height treated as the bottom margin band (10%).</summary>
    public const double BottomMarginFraction = 0.10;

    /// <summary>MIME type the caller uses to declare "these are raw 8-bit grayscale bytes".</summary>
    public const string RawGrayscaleMimeType = "image/x-cena-raw-gray8";

    /// <summary>Generic "unknown / raw" MIME that triggers the raw-bytes code path.</summary>
    public const string OctetStreamMimeType = "application/octet-stream";

    private readonly ILogger<HeuristicPhotoRedactor> _logger;

    public HeuristicPhotoRedactor(ILogger<HeuristicPhotoRedactor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PhotoRedactionResult> RedactAsync(
        ReadOnlyMemory<byte> imageBytes,
        PhotoRedactionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ImageWidthPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"ImageWidthPx must be >= 1 (was {request.ImageWidthPx}).");
        if (request.ImageHeightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"ImageHeightPx must be >= 1 (was {request.ImageHeightPx}).");
        if (string.IsNullOrWhiteSpace(request.MimeType))
            throw new ArgumentException("MimeType is required.", nameof(request));

        var applied = new List<PhotoRedactionRegion>();
        var deferred = new List<string>();

        // --- Face + Logo: always deferred in this slice -------------------
        if (request.RedactionKinds.HasFlag(PhotoRedactionKinds.Faces))
        {
            deferred.Add("face_detection_not_implemented");
        }
        if (request.RedactionKinds.HasFlag(PhotoRedactionKinds.Logos))
        {
            deferred.Add("logo_template_matcher_not_implemented");
        }

        // --- Margin redaction ---------------------------------------------
        var wantTop = request.RedactionKinds.HasFlag(PhotoRedactionKinds.TopMargin);
        var wantBottom = request.RedactionKinds.HasFlag(PhotoRedactionKinds.BottomMargin);

        if (!wantTop && !wantBottom)
        {
            // Nothing the caller asked for that we can do. Short-circuit.
            return Task.FromResult(new PhotoRedactionResult(
                RedactedImageBytes: imageBytes,
                AppliedRedactions: applied,
                DeferredRedactions: deferred));
        }

        var isRaw = IsRawBytesMimeType(request.MimeType);

        if (!isRaw)
        {
            // Structured image — do not touch bytes. Zero-filling a JPEG/PNG
            // byte range corrupts the file rather than redacting pixels.
            // Log the gap so ops sees the "structured uploads flow past
            // the redactor untouched" state until a decoder ships.
            if (wantTop || wantBottom)
            {
                _logger.LogInformation(
                    "[prr-413] HeuristicPhotoRedactor DEFERRED_STRUCTURED_IMAGE "
                    + "mime={MimeType} wantTop={WantTop} wantBottom={WantBottom} "
                    + "- decoder dependency not yet wired, input bytes forwarded unchanged.",
                    request.MimeType, wantTop, wantBottom);
                deferred.Add("structured_image_decoder_required");
            }
            return Task.FromResult(new PhotoRedactionResult(
                RedactedImageBytes: imageBytes,
                AppliedRedactions: applied,
                DeferredRedactions: deferred));
        }

        // --- Raw-byte path ------------------------------------------------
        // Require length == width * height so stride math is sound.
        var expectedLength = (long)request.ImageWidthPx * request.ImageHeightPx;
        if (imageBytes.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Raw-bytes mode requires imageBytes.Length ({imageBytes.Length}) == "
                + $"width*height ({expectedLength}).",
                nameof(imageBytes));
        }

        // Copy to a mutable buffer — input is ReadOnlyMemory. Copy is
        // unavoidable because we have to return new bytes; we only pay
        // this on the raw-byte path (which is the test path today —
        // production composition will be re-bound when the decoder ships).
        var mutated = imageBytes.ToArray();
        var stride = request.ImageWidthPx;
        var height = request.ImageHeightPx;

        if (wantTop)
        {
            // ceil so a tiny fractional band still redacts at least 1 row.
            var topRows = (int)Math.Ceiling(height * TopMarginFraction);
            topRows = Math.Clamp(topRows, 1, height);
            ZeroByteRange(mutated, startRow: 0, rowCount: topRows, stride: stride);

            applied.Add(new PhotoRedactionRegion(
                Kind: PhotoRedactionKinds.TopMargin,
                XPct: 0.0,
                YPct: 0.0,
                WidthPct: 1.0,
                HeightPct: (double)topRows / height,
                RedactionMethod: "solid-fill"));
        }

        if (wantBottom)
        {
            var bottomRows = (int)Math.Ceiling(height * BottomMarginFraction);
            bottomRows = Math.Clamp(bottomRows, 1, height);
            var startRow = height - bottomRows;
            if (startRow < 0) startRow = 0; // defensive for tiny images

            // Guard against top+bottom overlap on very small images:
            // if the top band already consumed the entire image, skip.
            // (Pathological case; production images are always many pixels.)
            ZeroByteRange(mutated, startRow: startRow, rowCount: bottomRows, stride: stride);

            applied.Add(new PhotoRedactionRegion(
                Kind: PhotoRedactionKinds.BottomMargin,
                XPct: 0.0,
                YPct: (double)startRow / height,
                WidthPct: 1.0,
                HeightPct: (double)bottomRows / height,
                RedactionMethod: "solid-fill"));
        }

        return Task.FromResult(new PhotoRedactionResult(
            RedactedImageBytes: mutated,
            AppliedRedactions: applied,
            DeferredRedactions: deferred));
    }

    /// <summary>
    /// Whether the MIME tells us the bytes are raw pixels (safe to mutate
    /// by byte range) as opposed to a structured file format whose bytes
    /// would be corrupted by range-zero.
    /// </summary>
    private static bool IsRawBytesMimeType(string mimeType)
    {
        // Case-insensitive compare is deliberate — IANA registry treats
        // type/subtype as case-insensitive.
        return string.Equals(mimeType, RawGrayscaleMimeType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mimeType, OctetStreamMimeType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zero-fill <paramref name="rowCount"/> rows starting at
    /// <paramref name="startRow"/>. Pure byte op — no decoding assumptions
    /// beyond "stride == width and layout is row-major".
    /// </summary>
    private static void ZeroByteRange(byte[] buffer, int startRow, int rowCount, int stride)
    {
        if (rowCount <= 0) return;
        var offset = (long)startRow * stride;
        var length = (long)rowCount * stride;
        // Clamp to buffer length in case of arithmetic edge (rowCount > height).
        if (offset >= buffer.Length) return;
        if (offset + length > buffer.Length) length = buffer.Length - offset;
        Array.Clear(buffer, (int)offset, (int)length);
    }
}
