// =============================================================================
// Cena Platform — Visual Review Endpoints
//
// Two read-only streams that back the curator's side-by-side review panel:
//
//   GET /api/admin/ingestion/items/{id}/source.pdf
//       → application/pdf bytes for the original Bagrut upload.
//         Resolved via BagrutDraftPayloadDocument.SourcePdfId →
//         IBagrutPdfStore.OpenReadAsync. 404 for items uploaded before
//         the PDF store landed (BytesPersisted=false equivalent).
//
//   GET /api/admin/ingestion/items/{id}/figures/{figureIndex}
//       → image/* bytes (PNG/JPEG) for the OCR figure crop indexed by
//         FigureSpecJson order. Layer2cFigureExtraction wrote them to
//         FigureStorageOptions.OutputDirectory; we resolve the cropped
//         path, sandbox-check it stays inside that root (path-traversal
//         defence), then stream the file back.
//
// Both routes:
//   - require ModeratorOrAbove (matches the rest of /api/admin/ingestion)
//   - rate-limited via the "api" bucket
//   - return 404 (not 500) when the underlying blob is missing so the
//     SPA can render a "PDF not retained — re-upload" / "figure missing"
//     fallback panel without an error toast.
//
// Why /source.pdf and not /source — extension hint helps browsers /
// <embed type="application/pdf"> pick a sensible default viewer when
// the SPA renders <embed src=".../source.pdf"> directly.
// =============================================================================

using System.Text.Json;
using Cena.Admin.Api.Ingestion.Vision;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Ocr.Layers;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Ingestion;

public static class VisualReviewEndpoints
{
    public static IEndpointRouteBuilder MapVisualReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion/items")
            .WithTags("Visual Review")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/{id}/source.pdf", async (
            string id,
            IDocumentStore store,
            IBagrutPdfStore pdfStore,
            CancellationToken ct) =>
        {
            await using var session = store.QuerySession();
            var payload = await session.LoadAsync<BagrutDraftPayloadDocument>(id, ct);
            if (payload is null || string.IsNullOrWhiteSpace(payload.SourcePdfId))
                return Results.NotFound(new CenaError(
                    "source_pdf_unavailable",
                    "This item has no associated source PDF (uploaded before persistent PDF storage was added, or the upload was not a Bagrut PDF).",
                    ErrorCategory.NotFound, null, null));

            var stream = await pdfStore.OpenReadAsync(payload.SourcePdfId, ct);
            if (stream is null)
                return Results.NotFound(new CenaError(
                    "source_pdf_not_retained",
                    "The source PDF for this item is not retained on disk. Re-upload to enable side-by-side review.",
                    ErrorCategory.NotFound, null, null));

            // Inline so the browser shows it, not download. Client uses
            // <embed type="application/pdf" src=".../source.pdf">.
            return Results.File(stream, "application/pdf", enableRangeProcessing: true);
        }).WithName("GetIngestionItemSourcePdf")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id}/figures/{figureIndex:int}", async (
            string id,
            int figureIndex,
            IDocumentStore store,
            IOptions<FigureStorageOptions> figureOpts,
            CancellationToken ct) =>
        {
            if (figureIndex < 0)
                return Results.BadRequest(new CenaError(
                    "invalid_figure_index", "figureIndex must be non-negative.",
                    ErrorCategory.Validation, null, null));

            await using var session = store.QuerySession();
            var payload = await session.LoadAsync<BagrutDraftPayloadDocument>(id, ct);
            if (payload is null || string.IsNullOrWhiteSpace(payload.FigureSpecJson))
                return Results.NotFound(new CenaError(
                    "figure_unavailable",
                    "This item has no figure spec (no figures extracted, or item predates figure storage).",
                    ErrorCategory.NotFound, null, null));

            FigureSpecEntry? entry;
            try
            {
                var spec = JsonSerializer.Deserialize<FigureSpecPayload>(
                    payload.FigureSpecJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                entry = spec?.Figures is { } list && figureIndex < list.Length
                    ? list[figureIndex]
                    : null;
            }
            catch (JsonException)
            {
                // The persistence path serialises this — a malformed value
                // is a server-side bug, not a client problem.
                return Results.Json(new CenaError(
                    "figure_spec_malformed",
                    "FigureSpecJson on this item is not valid JSON.",
                    ErrorCategory.Internal, null, null),
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            if (entry is null || string.IsNullOrWhiteSpace(entry.CroppedPath))
                return Results.NotFound(new CenaError(
                    "figure_not_found",
                    $"No figure at index {figureIndex} for this item.",
                    ErrorCategory.NotFound, null, null));

            // Path-traversal defence: the cropped path must resolve under
            // the configured FigureStorageOptions.OutputDirectory. Layer2c
            // writes there, but a tampered FigureSpecJson row from outside
            // the pipeline could point anywhere. Resolve both to absolute
            // canonical form and require prefix match.
            var rootFull = Path.GetFullPath(figureOpts.Value.OutputDirectory);
            var fileFull = Path.GetFullPath(entry.CroppedPath);
            if (!fileFull.StartsWith(
                    rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                && !string.Equals(fileFull, rootFull, StringComparison.Ordinal))
            {
                return Results.NotFound(new CenaError(
                    "figure_path_outside_root",
                    "Figure path is not under the configured figure-storage root.",
                    ErrorCategory.NotFound, null, null));
            }

            if (!File.Exists(fileFull))
                return Results.NotFound(new CenaError(
                    "figure_blob_missing",
                    "The figure crop file is missing on disk.",
                    ErrorCategory.NotFound, null, null));

            var contentType = ResolveImageContentType(fileFull);
            Stream stream = new FileStream(
                fileFull,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Results.File(stream, contentType, enableRangeProcessing: true);
        }).WithName("GetIngestionItemFigure")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        // vision-extractor branch (2026-05-04): serve rasterised page PNGs
        // so the SPA can replace the brittle <embed src=source.pdf#page=N>
        // surface with <img src=.../page/N.png>. Endpoint exists even when
        // the vision flag is OFF — the legacy cascade does not produce
        // these PNGs, so the endpoint 404s for those items, and the SPA
        // can fall back to the existing source.pdf surface.
        group.MapGet("/{id}/page/{pageNumber:int}.png", async (
            string id,
            int pageNumber,
            IDocumentStore store,
            IOptions<SourcePageStorageOptions> pageOpts,
            CancellationToken ct) =>
        {
            if (pageNumber < 1)
                return Results.BadRequest(new CenaError(
                    "invalid_page_number", "pageNumber must be >= 1.",
                    ErrorCategory.Validation, null, null));

            await using var session = store.QuerySession();
            var payload = await session.LoadAsync<BagrutDraftPayloadDocument>(id, ct);
            if (payload is null || string.IsNullOrWhiteSpace(payload.SourcePdfId))
                return Results.NotFound(new CenaError(
                    "source_page_unavailable",
                    "This item has no associated source PDF (uploaded before persistent storage was added).",
                    ErrorCategory.NotFound, null, null));

            // Sandbox the resolved path: must stay under the configured
            // SourcePageStorageOptions root. Prevents directory-traversal
            // through a tampered SourcePdfId.
            var rootFull = Path.GetFullPath(pageOpts.Value.RootDirectory);
            var pdfDir = Path.Combine(rootFull, SafeIdSegment(payload.SourcePdfId!));
            var fileName = $"page-{pageNumber:D3}.png";
            // pdftoppm may also pad to fewer digits depending on page count.
            // Try the canonical D3 padding first, then fall back to D2/D1
            // for older renders.
            string? resolvedPath = null;
            foreach (var candidate in new[]
            {
                Path.Combine(pdfDir, fileName),
                Path.Combine(pdfDir, $"page-{pageNumber:D2}.png"),
                Path.Combine(pdfDir, $"page-{pageNumber}.png"),
            })
            {
                var fileFull = Path.GetFullPath(candidate);
                if (!fileFull.StartsWith(
                        rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (File.Exists(fileFull))
                {
                    resolvedPath = fileFull;
                    break;
                }
            }
            if (resolvedPath is null)
                return Results.NotFound(new CenaError(
                    "source_page_not_rendered",
                    $"No rasterised page {pageNumber} for this item. The vision-extractor pipeline persists pages on ingestion; older items use the source.pdf surface.",
                    ErrorCategory.NotFound, null, null));

            Stream stream = new FileStream(
                resolvedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Results.File(stream, "image/png", enableRangeProcessing: true);
        }).WithName("GetIngestionItemSourcePagePng")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    /// <summary>
    /// Strict whitelist for path-segment use of pdfId. Mirrors
    /// PdfPageRasterizer.SafeId so the rasteriser's directory and the
    /// endpoint resolver agree on the path.
    /// </summary>
    private static string SafeIdSegment(string id)
    {
        var span = id.AsSpan();
        var buf = new char[span.Length];
        int j = 0;
        foreach (var ch in span)
        {
            var ok = (ch >= '0' && ch <= '9')
                  || (ch >= 'a' && ch <= 'z')
                  || (ch >= 'A' && ch <= 'Z')
                  || ch == '-' || ch == '_';
            if (ok) buf[j++] = ch;
        }
        return j == 0 ? "_" : new string(buf, 0, j).ToLowerInvariant();
    }

    private static string ResolveImageContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png"  => "image/png",
            ".jpg"  => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif"  => "image/gif",
            _       => "application/octet-stream",
        };
    }

    // Wire shapes for FigureSpecJson — match the anonymous JSON written by
    // BagrutPdfIngestionService.ExtractQuestions. Property names are
    // matched case-insensitively (PropertyNameCaseInsensitive = true).
    private sealed record FigureSpecPayload(FigureSpecEntry[]? Figures);

    private sealed record FigureSpecEntry(
        int Page,
        string? Kind,
        string? CroppedPath,
        FigureBboxPayload? Bbox,
        string? AltText);

    private sealed record FigureBboxPayload(double X, double Y, double W, double H);
}
