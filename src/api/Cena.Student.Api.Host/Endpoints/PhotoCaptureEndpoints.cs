// =============================================================================
// Cena Platform — Student Photo Capture + Gemini Vision (PHOTO-001)
//
// POST /api/photos/capture — student photographs a math problem
// The image is moderated (PHOTO-003 + RDY-001), then sent to Gemini Vision for OCR.
// Extracted LaTeX is sanitized (LATEX-001) and returned for step solving.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Moderation;
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

        // Step 1: Content moderation (PHOTO-003 + RDY-001)
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

        // Step 2: Send to Gemini Vision for math OCR
        var ocrResult = await RecognizeMathAsync(imageBytes, ct);

        if (ocrResult is null)
        {
            return Results.UnprocessableEntity(new { error = "Could not recognize math content in the image" });
        }

        // Step 3: Sanitize extracted LaTeX (LATEX-001)
        var sanitizedLatex = Cena.Infrastructure.Security.LaTeXSanitizer.Sanitize(ocrResult.RawLatex);

        return Results.Ok(new PhotoCaptureResponse(
            RecognizedLatex: sanitizedLatex,
            Confidence: ocrResult.Confidence,
            BoundingBoxes: ocrResult.BoundingBoxes,
            OriginalImageId: Guid.NewGuid().ToString("N")[..12],
            Warnings: ocrResult.Warnings,
            ModerationVerdict: moderationResult.Verdict.ToString()
        ));
    }

    /// <summary>
    /// Call Gemini Vision API for math OCR.
    /// Production: uses Google AI Gemini 1.5 Pro with vision capability.
    /// </summary>
    private static Task<MathOcrResult?> RecognizeMathAsync(byte[] imageBytes, CancellationToken ct)
    {
        // Production: POST to Gemini API with image + prompt:
        // "Extract all mathematical expressions from this image as LaTeX.
        //  Return structured JSON with bounding boxes."
        _ = ct;
        _ = imageBytes;

        // Placeholder — returns null to indicate OCR not yet wired
        return Task.FromResult<MathOcrResult?>(null);
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
