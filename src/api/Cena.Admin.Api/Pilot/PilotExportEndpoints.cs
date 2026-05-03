// =============================================================================
// Cena Platform — Pilot Data Export Admin Endpoint (RDY-032)
//
// POST /api/admin/pilot/export   (SuperAdminOnly + ai rate limit)
//
// Thin HTTP surface on top of PilotDataExporter. Body carries the
// (fromUtc, toUtc) window, an optional output-dir override, and a dry-run
// flag (default true). All validation + quality checks live in the
// service; the endpoint maps the result codes:
//
//   • ArgumentException             → 400 invalid_request
//   • InvalidOperationException     → 400 missing_salt  (env var unset)
//   • QualityCheckErrors non-empty  → 422 quality_check_failed (with errors)
//   • otherwise                     → 200 with PilotExportResult
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Pilot;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Pilot;

public static class PilotExportEndpoints
{
    public static IEndpointRouteBuilder MapPilotExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/pilot")
            .WithTags("Pilot Export")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("ai");

        group.MapPost("/export", async (
            PilotExportRequest request,
            ClaimsPrincipal user,
            IPilotDataExporter exporter,
            CancellationToken ct) =>
        {
            if (request is null)
                return Results.Json(new CenaError(
                    "invalid_body", "PilotExportRequest body required.",
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);

            // Record who ran the export — structured log only; the exporter
            // already emits its own [PILOT_EXPORT] record with the runId.
            var startedBy = user.FindFirst("user_id")?.Value
                            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? "unknown-super-admin";

            try
            {
                var result = await exporter.ExportAsync(request, ct);

                // 422 if quality checks flagged anything. Caller can still
                // read the metadata to see what went wrong; we don't want
                // to silently return a broken export.
                if (result.QualityCheckErrors.Count > 0)
                {
                    return Results.Json(new
                    {
                        error = "quality_check_failed",
                        message = "Export produced rows but quality checks flagged issues.",
                        result,
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new CenaError(
                    "invalid_request", ex.Message,
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("salt", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new CenaError(
                    "missing_salt", ex.Message,
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("PilotExport")
        .Produces<PilotExportResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
