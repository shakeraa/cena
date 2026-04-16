// =============================================================================
// Cena Platform — Student Photo Capture + OCR Cascade (PHOTO-001, ADR-0033)
//
// POST /api/photos/capture — student photographs a math problem.
//   1. CSAM + moderation (PHOTO-003 + RDY-001)        — IContentModerationPipeline
//   2. OCR cascade (ADR-0033, Layers 0–5)              — IOcrCascadeService
//   3. LaTeX sanitization (LATEX-001)                  — LaTeXSanitizer
//   4. Return recognised LaTeX + confidence + bboxes   — PhotoCaptureResponse
//
// Real implementation. No placeholder, no stub. IOcrCascadeService is
// resolved from DI and every call hits the real cascade
// (Tesseract + CasRouter + optional Mathpix/Gemini + Surya/Pix2Tex sidecar).
//
// PhotoDNA / CSAM moderation stays strictly upstream of OCR (Layer A0 in the
// cascade taxonomy) — CSAM never touches the OCR pipeline.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Moderation;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

public static class PhotoCaptureEndpoints
{
    public static void MapPhotoCaptureEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/photos")
            .RequireAuthorization()
            .WithTags("PhotoCapture")
            .DisableAntiforgery();

        group.MapPost("/capture", CaptureAndRecognize)
            .WithName("CaptureAndRecognize")
            .RequireRateLimiting("photo")
            .RequireImageUploadEnabled()
            .Produces<PhotoCaptureResponse>(200)
            .Produces(400)
            .Produces(403)
            .Produces(422)
            .Produces(503);
    }

    internal static async Task<IResult> CaptureAndRecognize(
        HttpRequest request,
        ClaimsPrincipal user,
        IContentModerationPipeline moderationPipeline,
        IOcrCascadeService ocrCascade,
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

        // Read image bytes
        using var ms = new MemoryStream();
        await file.OpenReadStream().CopyToAsync(ms, ct);
        var imageBytes = ms.ToArray();

        // Step 1 — CSAM + content moderation (upstream of OCR, always runs)
        var ipAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        var moderationResult = await moderationPipeline.ModerateAsync(
            imageBytes,
            file.ContentType,
            studentId,
            new ModerationPolicy(IsMinor: true),
            ipAddress,
            ct);

        if (moderationResult.Verdict == ModerationVerdict.CsamDetected)
        {
            logger.LogCritical("[SIEM] Photo capture blocked — CSAM detected. Student={StudentId}", studentId);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (moderationResult.Verdict == ModerationVerdict.Blocked)
        {
            logger.LogWarning("Photo capture blocked by moderation. Student={StudentId}, Categories={Categories}",
                studentId, moderationResult.FlaggedCategories);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Step 2 — OCR cascade (ADR-0033)
        OcrCascadeResult ocrResult;
        try
        {
            ocrResult = await ocrCascade.RecognizeAsync(
                bytes: imageBytes,
                contentType: string.IsNullOrWhiteSpace(file.ContentType) ? "image/png" : file.ContentType,
                hints: new OcrContextHints(
                    Subject: "math",
                    Language: null,              // cascade auto-detects
                    Track: null,                 // not known from photo
                    SourceType: SourceType.StudentPhoto,
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
            return Results.BadRequest(new { error = "The uploaded image could not be processed." });
        }

        // Catastrophic confidence → tell the student to re-photograph
        if (ocrResult.HumanReviewRequired)
        {
            logger.LogInformation(
                "[OCR_CASCADE] Surface-A 422 Student={StudentId} reasons={Reasons} conf={Conf:F2}",
                studentId, string.Join(",", ocrResult.ReasonsForReview), ocrResult.OverallConfidence);

            return Results.UnprocessableEntity(new
            {
                error = "low_confidence",
                message = "Could not read the math clearly. Try a new photo with better lighting.",
                reasons = ocrResult.ReasonsForReview,
                overall_confidence = ocrResult.OverallConfidence,
            });
        }

        // Step 3 — Build response from CAS-validated math blocks.
        // Per ADR-0002 Layer 5 has already set SympyParsed; we only surface
        // blocks that round-tripped through the CAS oracle.
        var casValidated = ocrResult.MathBlocks
            .Where(m => m.SympyParsed && !string.IsNullOrEmpty(m.Latex))
            .ToArray();

        if (casValidated.Length == 0 && ocrResult.MathBlocks.Count > 0)
        {
            // Math detected but every block failed CAS validation — surface
            // gracefully rather than return empty LaTeX silently.
            logger.LogInformation(
                "[OCR_CASCADE] All math blocks CAS-failed for Student={StudentId}", studentId);
            return Results.UnprocessableEntity(new
            {
                error = "cas_validation_failed",
                message = "The math couldn't be verified as valid. Please re-check the problem and try again.",
            });
        }

        // Step 4 — Sanitize extracted LaTeX (LATEX-001).
        // We emit the first-best CAS-validated block as the primary LaTeX.
        // Additional blocks are returned via BoundingBoxes[].ExtractedLatex.
        var primary = casValidated.FirstOrDefault();
        string primaryLatex = primary?.Latex ?? string.Empty;
        var sanitizedLatex = Cena.Infrastructure.Security.LaTeXSanitizer.Sanitize(primaryLatex);

        var bboxes = casValidated
            .Where(m => m.Bbox is not null)
            .Select(m => new BoundingBox(
                X: m.Bbox!.X,
                Y: m.Bbox.Y,
                Width: m.Bbox.W,
                Height: m.Bbox.H,
                ExtractedLatex: Cena.Infrastructure.Security.LaTeXSanitizer.Sanitize(m.Latex ?? string.Empty)))
            .ToArray();

        var warnings = ocrResult.FallbacksFired
            .Select(f => $"ocr_fallback:{f}")
            .ToArray();

        return Results.Ok(new PhotoCaptureResponse(
            RecognizedLatex: sanitizedLatex,
            Confidence: primary?.Confidence ?? ocrResult.OverallConfidence,
            BoundingBoxes: bboxes,
            OriginalImageId: Guid.NewGuid().ToString("N")[..12],
            Warnings: warnings,
            ModerationVerdict: moderationResult.Verdict.ToString()
        ));
    }
}

public record MathOcrResult(
    string RawLatex,
    double Confidence,
    IReadOnlyList<BoundingBox> BoundingBoxes,
    string[] Warnings
);

public record BoundingBox(
    double X, double Y, double Width, double Height,
    string ExtractedLatex
);

public record PhotoCaptureResponse(
    string RecognizedLatex,
    double Confidence,
    IReadOnlyList<BoundingBox> BoundingBoxes,
    string OriginalImageId,
    string[] Warnings,
    string? ModerationVerdict = null
);
