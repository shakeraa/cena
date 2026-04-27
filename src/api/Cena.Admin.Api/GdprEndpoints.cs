// =============================================================================
// Cena Platform -- GDPR Admin Endpoints (SEC-005)
// Consent management, data export, and right-to-erasure for GDPR compliance.
//
// FIND-arch-006: DI-injected services are declared with [FromServices] so
// minimal-API route inference doesn't mistake them for body parameters. The
// authorization policy name matches a real CenaAuthPolicies entry so the
// endpoints are reachable at runtime.
// =============================================================================

using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Marten;
using EventErasureManifest = Cena.Actors.Events.ErasureManifest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class GdprEndpoints
{
    // Marker for ILogger<T>. The non-generic ILogger isn't registered
    // by AddLogging(), so [FromServices] ILogger params 500'd at
    // dispatch — surfaced by EPIC-G-08. Mirrors MeGdprEndpoints.GdprLoggerMarker.
    private sealed class GdprAdminLogMarker { }

    private static readonly TimeSpan CoolingPeriod = TimeSpan.FromDays(30);

    public static RouteGroupBuilder MapGdprEndpoints(this IEndpointRouteBuilder app)
    {
        // FIND-arch-006: the original policy name "AdminPolicy" did not match
        // any policy registered in CenaAuthPolicies. Use the canonical constant
        // so the authorization middleware can resolve it at runtime.
        var group = app.MapGroup("/api/admin/gdpr")
            .WithTags("GDPR")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        // ── Consent Management ──

        group.MapGet("/consents/{studentId}", async (
            string studentId,
            [FromServices] IGdprConsentManager consentManager,
            [FromServices] IDocumentStore store,
            HttpContext ctx,
            [FromServices] ILogger<GdprAdminLogMarker> logger) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            var consents = await consentManager.GetConsentsAsync(studentId);

            logger.LogInformation(
                "[SIEM] ConsentsQueried: StudentId={StudentId}, Count={Count}, QueriedBy={QueriedBy}",
                studentId, consents.Count, ctx.User.Identity?.Name ?? "unknown");

            return Results.Ok(new { studentId, consents });
        })
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/consents", async (
            [FromBody] ConsentRequest request,
            [FromServices] IGdprConsentManager consentManager,
            [FromServices] IDocumentStore store,
            HttpContext ctx,
            [FromServices] ILogger<GdprAdminLogMarker> logger) =>
        {
            if (!Enum.TryParse<ProcessingPurpose>(request.Purpose, true, out var purpose))
                return Results.BadRequest(new { error = $"Invalid consent purpose: {request.Purpose}" });

            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(request.StudentId, ctx.User, store);

            await consentManager.RecordConsentAsync(request.StudentId, purpose);

            logger.LogInformation(
                "[SIEM] ConsentRecorded: StudentId={StudentId}, Purpose={Purpose}, RecordedBy={RecordedBy}",
                request.StudentId, purpose, ctx.User.Identity?.Name ?? "unknown");

            return Results.Ok(new { request.StudentId, purpose = purpose.ToString(), granted = true });
        })
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapDelete("/consents/{studentId}/{consentType}", async (
            string studentId,
            string consentType,
            [FromServices] IGdprConsentManager consentManager,
            [FromServices] IDocumentStore store,
            HttpContext ctx,
            [FromServices] ILogger<GdprAdminLogMarker> logger) =>
        {
            if (!Enum.TryParse<ProcessingPurpose>(consentType, true, out var purpose))
                return Results.BadRequest(new { error = $"Invalid consent purpose: {consentType}" });

            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            await consentManager.RevokeConsentAsync(studentId, purpose);

            logger.LogInformation(
                "[SIEM] ConsentRevoked: StudentId={StudentId}, Purpose={Purpose}, RevokedBy={RevokedBy}",
                studentId, purpose, ctx.User.Identity?.Name ?? "unknown");

            return Results.Ok(new { studentId, consentType = purpose.ToString(), granted = false });
        })
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // ── Data Export (Article 20) ──

        group.MapPost("/export/{studentId}", async (
            string studentId,
            [FromServices] IDocumentStore store,
            HttpContext ctx,
            [FromServices] ILogger<GdprAdminLogMarker> logger) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            await using var session = store.QuerySession();
            var snapshot = await session.Query<StudentProfileSnapshot>()
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (snapshot is null)
            {
                logger.LogWarning(
                    "[SIEM] DataExportFailed: StudentId={StudentId}, Reason=NotFound, RequestedBy={RequestedBy}",
                    studentId, ctx.User.Identity?.Name ?? "unknown");
                return Results.NotFound(new { error = $"Student {studentId} not found" });
            }

            var export = StudentDataExporter.Export(studentId, snapshot);

            logger.LogInformation(
                "[SIEM] DataExportGenerated: StudentId={StudentId}, FieldCount={FieldCount}, GeneratedBy={GeneratedBy}",
                studentId, export.Profile.Count, ctx.User.Identity?.Name ?? "unknown");

            return Results.Ok(export);
        })
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // ── Right to Erasure (Article 17) ──

        group.MapPost("/erasure/{studentId}", async (
            string studentId,
            [FromServices] IRightToErasureService erasureService,
            [FromServices] IDocumentStore store,
            HttpContext httpContext,
            [FromServices] ILogger<GdprAdminLogMarker> logger) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school (CRITICAL - destructive operation)
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, httpContext.User, store);

            var requestedBy = httpContext.User.Identity?.Name ?? "admin";
            var request = await erasureService.RequestErasureAsync(studentId, requestedBy);

            // Emit StudentErasureRequested_V1 event to student's event stream
            await using (var session = store.LightweightSession())
            {
                var scheduledProcessingAt = request.RequestedAt.Add(CoolingPeriod);
                var erasureEvent = new StudentErasureRequested_V1(
                    StudentId: studentId,
                    RequestId: request.Id,
                    RequestedAt: request.RequestedAt,
                    RequestedBy: $"admin:{requestedBy}",
                    ScheduledProcessingAt: scheduledProcessingAt
                );

                session.Events.Append(studentId, erasureEvent);
                await session.SaveChangesAsync();
            }

            logger.LogInformation(
                "[SIEM] ErasureRequested: StudentId={StudentId}, RequestId={RequestId}, RequestedBy={RequestedBy}, ScheduledProcessingAt={ScheduledProcessingAt}",
                studentId, request.Id, requestedBy, request.RequestedAt.Add(CoolingPeriod));

            return Results.Ok(new
            {
                request.StudentId,
                requestId = request.Id,
                request.Status,
                request.RequestedAt,
                coolingPeriodEnds = request.RequestedAt.Add(CoolingPeriod)
            });
        })
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/erasure/{studentId}/status", async (
            string studentId,
            [FromServices] IRightToErasureService erasureService,
            [FromServices] IDocumentStore store,
            HttpContext ctx,
            [FromServices] ILogger<GdprAdminLogMarker> logger) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            var request = await erasureService.GetErasureStatusAsync(studentId);

            if (request is null)
            {
                logger.LogInformation(
                    "[SIEM] ErasureStatusQueried: StudentId={StudentId}, Result=NotFound, QueriedBy={QueriedBy}",
                    studentId, ctx.User.Identity?.Name ?? "unknown");
                return Results.NotFound(new { error = $"No erasure request for {studentId}" });
            }

            var now = DateTimeOffset.UtcNow;
            var scheduledProcessingAt = request.RequestedAt.Add(CoolingPeriod);
            var coolingPeriodPassed = now >= scheduledProcessingAt;

            // Check for completed manifest if status is Completed
            EventErasureManifest? manifest = null;
            if (request.Status == ErasureStatus.Completed)
            {
                await using var session = store.QuerySession();
                manifest = await session.Query<EventErasureManifest>()
                    .FirstOrDefaultAsync(m => m.RequestId == request.Id.ToString());
            }

            logger.LogInformation(
                "[SIEM] ErasureStatusQueried: StudentId={StudentId}, RequestId={RequestId}, Status={Status}, CoolingPeriodPassed={CoolingPeriodPassed}, QueriedBy={QueriedBy}",
                studentId, request.Id, request.Status, coolingPeriodPassed, ctx.User.Identity?.Name ?? "unknown");

            return Results.Ok(new
            {
                request.StudentId,
                requestId = request.Id,
                status = request.Status.ToString(),
                request.RequestedAt,
                request.ProcessedAt,
                coolingPeriodEnds = scheduledProcessingAt,
                coolingPeriodPassed,
                scheduledProcessingAt,
                manifestLink = request.Status == ErasureStatus.Completed
                    ? $"/api/admin/gdpr/erasure/{studentId}/manifest"
                    : null,
                manifestSummary = manifest is not null
                    ? new
                    {
                        manifest.RequestId,
                        manifest.TotalRowsAffected,
                        manifest.IsComplete,
                        storeActions = manifest.StoreActions.Select(a => new { a.StoreName, a.ActionTaken, a.RowsAffected })
                    }
                    : null
            });
        })
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /erasure/{studentId}/manifest - Returns the erasure manifest if completed
        group.MapGet("/erasure/{studentId}/manifest", async (
            string studentId,
            [FromServices] IRightToErasureService erasureService,
            [FromServices] IDocumentStore store,
            HttpContext ctx,
            [FromServices] ILogger<GdprAdminLogMarker> logger) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            var request = await erasureService.GetErasureStatusAsync(studentId);

            if (request is null)
            {
                logger.LogInformation(
                    "[SIEM] ErasureManifestQueried: StudentId={StudentId}, Result=RequestNotFound, QueriedBy={QueriedBy}",
                    studentId, ctx.User.Identity?.Name ?? "unknown");
                return Results.NotFound(new { error = $"No erasure request for {studentId}" });
            }

            if (request.Status != ErasureStatus.Completed)
            {
                logger.LogInformation(
                    "[SIEM] ErasureManifestQueried: StudentId={StudentId}, RequestId={RequestId}, Result=NotCompleted, Status={Status}, QueriedBy={QueriedBy}",
                    studentId, request.Id, request.Status, ctx.User.Identity?.Name ?? "unknown");
                return Results.BadRequest(new
                {
                    error = "Erasure not yet completed",
                    status = request.Status.ToString(),
                    message = "Manifest is only available after erasure is fully processed."
                });
            }

            await using var session = store.QuerySession();
            var manifest = await session.Query<EventErasureManifest>()
                .FirstOrDefaultAsync(m => m.RequestId == request.Id.ToString());

            if (manifest is null)
            {
                logger.LogWarning(
                    "[SIEM] ErasureManifestQueried: StudentId={StudentId}, RequestId={RequestId}, Result=ManifestNotFound, QueriedBy={QueriedBy}",
                    studentId, request.Id, ctx.User.Identity?.Name ?? "unknown");
                return Results.NotFound(new { error = "Erasure manifest not found" });
            }

            logger.LogInformation(
                "[SIEM] ErasureManifestQueried: StudentId={StudentId}, RequestId={RequestId}, TotalRowsAffected={TotalRowsAffected}, IsComplete={IsComplete}, QueriedBy={QueriedBy}",
                studentId, manifest.RequestId, manifest.TotalRowsAffected, manifest.IsComplete, ctx.User.Identity?.Name ?? "unknown");

            return Results.Ok(new
            {
                manifest.RequestId,
                manifest.StudentId,
                manifest.StartedAt,
                manifest.CompletedAt,
                manifest.TotalRowsAffected,
                manifest.IsComplete,
                storeActions = manifest.StoreActions.Select(a => new
                {
                    a.StoreName,
                    a.ActionTaken,
                    a.RowsAffected,
                    a.Details
                })
            });
        });

        return group;
    }
}

public sealed record ConsentRequest(string StudentId, string Purpose);
