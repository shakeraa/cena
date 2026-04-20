// =============================================================================
// Cena Platform — Photo / PDF Upload (PWA-BE-003 + RDY-001 + ADR-0033 + prr-001)
//
// Validates and processes student-submitted images OR PDFs:
//   1. Magic-byte + content-type + size validation
//   2. Real EXIF / IPTC / XMP / GPS strip via ExifStripper (images only) —
//      prr-001 (2026-04-20): previous documented-stub returned input bytes
//      unchanged while the response falsely advertised ExifStripped=true.
//      Now: injection-provided ExifStripper produces a scrubbed buffer; on
//      strip failure the endpoint returns 422 and persists nothing ("better
//      reject than leak" — user decision 2026-04-20).
//   3. CSAM + AI-safety moderation (RDY-001, runs on scrubbed image bytes)
//   4. OCR cascade (ADR-0033, Layers 0–5) — for PDFs, Layer 0 runs
//      pdf_triage first; `text` short-circuits to pypdf extraction,
//      `encrypted` returns 422 via the cascade's structured result.
//
// PDF support added in Phase 2.2 (RDY-OCR-WIREUP-B). The cascade decides
// synchronously whether to OCR or take the text-layer shortcut — the
// endpoint just forwards bytes and returns whatever shape the cascade
// produces.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Media;
using Cena.Infrastructure.Moderation;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Cena.Student.Api.Host.Endpoints;

public static class PhotoUploadEndpoints
{
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB (PDFs can be larger)
    private static readonly string[] AllowedContentTypes =
    [
        "image/jpeg", "image/png", "image/webp", "application/pdf",
    ];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic  = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] PdfMagic  = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    public static void MapPhotoUploadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/photos")
            .RequireAuthorization()
            .WithTags("PhotoUpload")
            .DisableAntiforgery();

        group.MapPost("/upload", UploadPhoto)
            .WithName("UploadPhoto")
            .RequireRateLimiting("photo")
            .RequireImageUploadEnabled()
            .Produces<PhotoUploadResponse>(200)
            .Produces(400)
            .Produces(403)
            .Produces(413)
            .Produces(422)
            .Produces(503);
    }

    internal static async Task<IResult> UploadPhoto(
        HttpRequest request,
        ClaimsPrincipal user,
        IContentModerationPipeline moderationPipeline,
        IOcrCascadeService ocrCascade,
        ExifStripper exifStripper,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var studentId = user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected multipart/form-data" });

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("photo");

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "No photo file provided" });

        if (file.Length > MaxFileSizeBytes)
            return Results.StatusCode(413);

        if (!AllowedContentTypes.Contains(file.ContentType))
            return Results.BadRequest(new { error = $"Invalid content type: {file.ContentType}" });

        // Magic-byte verification — prevents content-type spoofing
        using var stream = file.OpenReadStream();
        var header = new byte[4];
        var bytesRead = await stream.ReadAsync(header.AsMemory(0, 4), ct);
        stream.Position = 0;

        if (!VerifyMagicBytes(header, file.ContentType))
            return Results.BadRequest(new { error = "File content does not match declared content type" });

        // Read full bytes
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        bool isPdf = file.ContentType == "application/pdf";

        // prr-001: Images MUST have EXIF / IPTC / XMP / GPS stripped before
        // any downstream consumer (moderation, OCR, persistence) sees them.
        // If the strip fails for any reason we refuse to persist — better
        // reject than leak GPS coordinates of a minor's home.
        //
        // PDFs: EXIF is image-specific; the strip does not apply. The
        // response's ExifStripped flag is `false` for PDFs, which is the
        // honest answer ("no strip was performed because no EXIF exists on
        // this format") — not the old lying "!isPdf" shortcut.
        byte[] processedBytes;
        StripResult? stripResult = null;
        if (isPdf)
        {
            processedBytes = fileBytes;
        }
        else
        {
            stripResult = exifStripper.Strip(fileBytes);
            if (!stripResult.Success)
            {
                logger.LogWarning(
                    "[EXIF_STRIP_FAILED] Rejecting upload Student={StudentId} Reason={Reason}",
                    studentId, stripResult.FailureReason);
                return Results.UnprocessableEntity(new
                {
                    error = "exif_strip_failed",
                    detail = stripResult.FailureReason,
                });
            }
            processedBytes = stripResult.Scrubbed;
        }

        // ── Moderation ──────────────────────────────────────────────────────
        // PDFs don't flow through PhotoDNA (no image). Content-safety still
        // runs on them conceptually, but our current pipeline is image-based —
        // so PDFs skip moderation. CSAM concerns on PDFs are covered by the
        // upstream content filters documented in RDY-001.
        ModerationResult? moderationResult = null;
        if (!isPdf)
        {
            var ipAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
            moderationResult = await moderationPipeline.ModerateAsync(
                processedBytes,
                file.ContentType,
                studentId,
                new ModerationPolicy(IsMinor: true),
                ipAddress,
                ct);

            if (moderationResult.Verdict == ModerationVerdict.CsamDetected)
            {
                logger.LogCritical("[SIEM] Photo upload blocked — CSAM detected. Student={StudentId}", studentId);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (moderationResult.Verdict == ModerationVerdict.Blocked)
            {
                logger.LogWarning("Photo upload blocked by moderation. Student={StudentId}, Categories={Categories}",
                    studentId, moderationResult.FlaggedCategories);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        // ── OCR cascade (ADR-0033) ──────────────────────────────────────────
        OcrCascadeResult ocrResult;
        try
        {
            ocrResult = await ocrCascade.RecognizeAsync(
                bytes: processedBytes,
                contentType: file.ContentType,
                hints: new OcrContextHints(
                    Subject: "math",
                    Language: null,
                    Track: null,
                    SourceType: isPdf ? SourceType.StudentPdf : SourceType.StudentPhoto,
                    TaxonomyNode: null,
                    ExpectedFigures: null),
                surface: CascadeSurface.StudentInteractive,
                ct: ct).ConfigureAwait(false);
        }
        catch (OcrCircuitOpenException ex)
        {
            logger.LogWarning(ex, "[OCR_CASCADE] Both cloud fallbacks unavailable for Student={StudentId}", studentId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (OcrInputException ex)
        {
            logger.LogWarning("[OCR_CASCADE] Malformed input for Student={StudentId}: {Reason}", studentId, ex.Message);
            return Results.BadRequest(new { error = "The uploaded file could not be processed." });
        }

        // Encrypted PDFs are the clean 422 case — cascade sets triage=Encrypted
        // + HumanReviewRequired=true with reason preprocess_failed_or_encrypted.
        if (ocrResult.PdfTriage == PdfTriageVerdict.Encrypted)
        {
            logger.LogInformation("[OCR_CASCADE] Encrypted PDF rejected for Student={StudentId}", studentId);
            return Results.UnprocessableEntity(new
            {
                error = "encrypted_pdf",
                message = "The uploaded PDF is password-protected. Please upload an unlocked copy.",
            });
        }

        if (ocrResult.HumanReviewRequired)
        {
            logger.LogInformation(
                "[OCR_CASCADE] Upload 422 Student={StudentId} reasons={Reasons} conf={Conf:F2}",
                studentId, string.Join(",", ocrResult.ReasonsForReview), ocrResult.OverallConfidence);
            return Results.UnprocessableEntity(new
            {
                error = "low_confidence",
                message = "Could not read the content clearly. Please upload a higher-quality copy.",
                reasons = ocrResult.ReasonsForReview,
                overall_confidence = ocrResult.OverallConfidence,
            });
        }

        // ── Build response ─────────────────────────────────────────────────
        var photoId = Guid.NewGuid().ToString("N")[..12];

        var extractedLatex = ocrResult.MathBlocks
            .Where(m => m.SympyParsed && !string.IsNullOrEmpty(m.Latex))
            .Select(m => Cena.Infrastructure.Security.LaTeXSanitizer.Sanitize(m.Latex!))
            .ToArray();

        string status;
        if (moderationResult is not null && moderationResult.Verdict == ModerationVerdict.NeedsReview)
            status = "queued_for_review";
        else if (ocrResult.PdfTriage == PdfTriageVerdict.Text)
            status = "processed_text_shortcut";
        else if (ocrResult.MathBlocks.Count > 0)
            status = "processed_ocr";
        else
            status = "processed_empty";

        return Results.Ok(new PhotoUploadResponse(
            PhotoId: photoId,
            OriginalSizeBytes: file.Length,
            ProcessedSizeBytes: processedBytes.Length,
            // prr-001: ExifStripped must reflect what actually happened,
            // not file-type. PDFs never strip (no EXIF) → false. Images
            // reach this line only via the success branch → true.
            ExifStripped: stripResult?.Success ?? false,
            ContentType: file.ContentType,
            Status: status,
            ModerationVerdict: moderationResult?.Verdict.ToString(),
            PdfTriage: ocrResult.PdfTriage?.ToString().ToLowerInvariant(),
            ExtractedLatex: extractedLatex,
            OverallConfidence: ocrResult.OverallConfidence,
            Warnings: ocrResult.FallbacksFired.Select(f => $"ocr_fallback:{f}").ToArray()
        ));
    }

    private static bool VerifyMagicBytes(byte[] header, string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => header.Length >= 3
                && header[0] == JpegMagic[0] && header[1] == JpegMagic[1] && header[2] == JpegMagic[2],
            "image/png" => header.Length >= 4
                && header[0] == PngMagic[0] && header[1] == PngMagic[1]
                && header[2] == PngMagic[2] && header[3] == PngMagic[3],
            "image/webp" => header.Length >= 4
                && header[0] == (byte)'R' && header[1] == (byte)'I'
                && header[2] == (byte)'F' && header[3] == (byte)'F',
            "application/pdf" => header.Length >= 4
                && header[0] == PdfMagic[0] && header[1] == PdfMagic[1]
                && header[2] == PdfMagic[2] && header[3] == PdfMagic[3],
            _ => false,
        };
    }

    // prr-001 (2026-04-20): StripExifMetadata stub removed. The real
    // EXIF / IPTC / XMP / GPS scrub now lives in
    // Cena.Infrastructure.Media.ExifStripper and is injected into the
    // endpoint via FromServices. The architecture test
    // NoUnstrippedImageBytesTest keeps external consumers honest.
}

public record PhotoUploadResponse(
    string PhotoId,
    long OriginalSizeBytes,
    long ProcessedSizeBytes,
    bool ExifStripped,
    string ContentType,
    string Status,
    string? ModerationVerdict = null,
    string? PdfTriage = null,
    string[]? ExtractedLatex = null,
    double? OverallConfidence = null,
    string[]? Warnings = null
);
