// =============================================================================
// Cena Platform — Bagrut Ingest Endpoint (RDY-057 / Phase 3)
//
// Single POST /api/admin/ingestion/bagrut route that accepts a Ministry
// Bagrut PDF + exam code, hands it to IBagrutPdfIngestionService (which
// runs the real ADR-0033 cascade with surface=AdminBatch +
// source_type=bagrut_reference), and returns the PdfIngestionResult with
// drafts + warnings.
//
// SuperAdmin-only per the task: Bagrut reference ingest is a restricted
// operation (provenance audit required, reference-only posture).
//
// The endpoint delegates all validation + service orchestration to
// BagrutIngestHandler.HandleAsync so unit tests can exercise the full
// control flow without spinning up a TestServer.
// =============================================================================

using System.Security.Claims;
using System.Text.RegularExpressions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Ocr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Ingestion;

public static class BagrutIngestEndpoints
{
    public static IEndpointRouteBuilder MapBagrutIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion/bagrut")
            .WithTags("Bagrut Ingestion")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

        group.MapPost("", async (
            HttpRequest request,
            ClaimsPrincipal user,
            IBagrutPdfIngestionService service,
            IBagrutCorpusService corpusService,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return BagrutIngestHandler.InvalidBody("multipart/form-data body required.");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            byte[]? bytes = null;
            if (file is not null && file.Length > 0)
            {
                using var ms = new MemoryStream(capacity: (int)Math.Min(file.Length, BagrutIngestHandler.MaxPdfBytes));
                await using (var stream = file.OpenReadStream())
                    await stream.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }

            var uploadedByClaim = user.FindFirst("user_id")?.Value
                                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return await BagrutIngestHandler.HandleAsync(
                examCode:      form["examCode"].ToString(),
                fileContentType: file?.ContentType,
                fileBytes:     bytes,
                uploadedBy:    form["uploadedBy"].ToString(),
                fallbackUserId: uploadedByClaim,
                ministrySubjectCode: form["ministrySubjectCode"].ToString(),
                ministryQuestionPaperCode: form["ministryQuestionPaperCode"].ToString(),
                units: int.TryParse(form["units"], out var u) ? u : null,
                year: int.TryParse(form["year"], out var y) ? y : null,
                topicId: form["topicId"].ToString(),
                sourceFilename: file?.FileName,
                service:       service,
                corpusService: corpusService,
                ct:            ct);
        })
        .DisableAntiforgery()
        .WithName("IngestBagrutPdf")
        .Produces<PdfIngestionResult>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}

/// <summary>
/// The testable handler. Pure: no HttpRequest, no form parsing — caller
/// pre-parses and hands in primitive values. Returns an IResult mapping
/// the full outcome surface.
/// </summary>
public static class BagrutIngestHandler
{
    public const long MaxPdfBytes = 50 * 1024 * 1024;

    internal static readonly Regex ExamCodeRx = new(
        @"^[a-z0-9\-_]{3,64}$",
        RegexOptions.Compiled);

    public static async Task<IResult> HandleAsync(
        string? examCode,
        string? fileContentType,
        byte[]? fileBytes,
        string? uploadedBy,
        string? fallbackUserId,
        IBagrutPdfIngestionService service,
        IBagrutCorpusService? corpusService = null,
        string? ministrySubjectCode = null,
        string? ministryQuestionPaperCode = null,
        int? units = null,
        int? year = null,
        string? topicId = null,
        string? sourceFilename = null,
        CancellationToken ct = default)
    {
        var normalizedExam = (examCode ?? "").Trim().ToLowerInvariant();
        if (!ExamCodeRx.IsMatch(normalizedExam))
            return Error("invalid_exam_code",
                "examCode must be 3-64 chars, lower-case alphanumerics + - + _.",
                ErrorCategory.Validation, StatusCodes.Status400BadRequest);

        if (fileBytes is null || fileBytes.Length == 0)
            return Error("missing_file", "file (PDF) is required.",
                ErrorCategory.Validation, StatusCodes.Status400BadRequest);

        if (fileBytes.Length > MaxPdfBytes)
            return Error("file_too_large",
                $"file exceeds {MaxPdfBytes / (1024 * 1024)} MB.",
                ErrorCategory.Validation, StatusCodes.Status400BadRequest);

        if (!string.Equals(fileContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return Error("invalid_content_type", "file must be application/pdf.",
                ErrorCategory.Validation, StatusCodes.Status400BadRequest);

        var submitter = string.IsNullOrWhiteSpace(uploadedBy)
            ? (fallbackUserId ?? "unknown-admin")
            : uploadedBy.Trim();

        PdfIngestionResult result;
        try
        {
            result = await service.IngestAsync(fileBytes, normalizedExam, submitter, ct);
        }
        catch (OcrInputException ex)
        {
            return Error("ocr_input_error", ex.Message,
                ErrorCategory.Validation, StatusCodes.Status400BadRequest);
        }
        catch (OcrCircuitOpenException ex)
        {
            return Error("ocr_circuit_open", ex.Message,
                ErrorCategory.ExternalService, StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException ex)
        {
            return Error("invalid_request", ex.Message,
                ErrorCategory.Validation, StatusCodes.Status400BadRequest);
        }

        // prr-242: corpus-item side effect. Skip silently when the minimum
        // metadata is missing (subject/paper code) — the admin UI surfaces
        // this via the returned result.Warnings.
        if (corpusService is not null
            && !string.IsNullOrWhiteSpace(ministrySubjectCode)
            && !string.IsNullOrWhiteSpace(ministryQuestionPaperCode)
            && result.Drafts.Count > 0)
        {
            var context = new BagrutCorpusIngestContext(
                ExamCode: normalizedExam,
                MinistrySubjectCode: ministrySubjectCode!.Trim(),
                MinistryQuestionPaperCode: ministryQuestionPaperCode!.Trim(),
                Units: units,
                Year: year,
                Season: null,
                Moed: null,
                Stream: null,
                DefaultTopicId: string.IsNullOrWhiteSpace(topicId) ? null : topicId!.Trim(),
                SourceFilename: sourceFilename,
                SourcePdfId: result.PdfId,
                UploadedBy: submitter,
                IngestedAt: DateTimeOffset.UtcNow);

            var corpusItems = BagrutCorpusExtractor.Extract(result.Drafts, context);
            if (corpusItems.Count > 0)
                await corpusService.UpsertManyAsync(corpusItems, ct);
        }

        return Results.Ok(result);
    }

    internal static IResult InvalidBody(string message) =>
        Error("invalid_body", message, ErrorCategory.Validation, StatusCodes.Status400BadRequest);

    private static IResult Error(string code, string message, ErrorCategory category, int statusCode) =>
        Results.Json(new CenaError(code, message, category, null, null), statusCode: statusCode);
}
