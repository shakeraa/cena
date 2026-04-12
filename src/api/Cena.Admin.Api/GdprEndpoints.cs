// =============================================================================
// Cena Platform -- GDPR Admin Endpoints (SEC-005)
// Consent management, data export, and right-to-erasure for GDPR compliance.
//
// FIND-arch-006: DI-injected services are declared with [FromServices] so
// minimal-API route inference doesn't mistake them for body parameters. The
// authorization policy name matches a real CenaAuthPolicies entry so the
// endpoints are reachable at runtime.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class GdprEndpoints
{
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
            HttpContext ctx) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);
            
            var consents = await consentManager.GetConsentsAsync(studentId);
            return Results.Ok(new { studentId, consents });
        });

        group.MapPost("/consents", async (
            [FromBody] ConsentRequest request,
            [FromServices] IGdprConsentManager consentManager,
            [FromServices] IDocumentStore store,
            HttpContext ctx) =>
        {
            if (!Enum.TryParse<ConsentType>(request.ConsentType, true, out var type))
                return Results.BadRequest(new { error = $"Invalid consent type: {request.ConsentType}" });

            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(request.StudentId, ctx.User, store);

            await consentManager.RecordConsentAsync(request.StudentId, type);
            return Results.Ok(new { request.StudentId, request.ConsentType, granted = true });
        });

        group.MapDelete("/consents/{studentId}/{consentType}", async (
            string studentId,
            string consentType,
            [FromServices] IGdprConsentManager consentManager,
            [FromServices] IDocumentStore store,
            HttpContext ctx) =>
        {
            if (!Enum.TryParse<ConsentType>(consentType, true, out var type))
                return Results.BadRequest(new { error = $"Invalid consent type: {consentType}" });

            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            await consentManager.RevokeConsentAsync(studentId, type);
            return Results.Ok(new { studentId, consentType, granted = false });
        });

        // ── Data Export (Article 20) ──

        group.MapPost("/export/{studentId}", async (
            string studentId,
            [FromServices] IDocumentStore store,
            HttpContext ctx) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            await using var session = store.QuerySession();
            var snapshot = await session.Query<Cena.Actors.Events.StudentProfileSnapshot>()
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (snapshot is null)
                return Results.NotFound(new { error = $"Student {studentId} not found" });

            var export = StudentDataExporter.Export(studentId, snapshot);
            return Results.Ok(export);
        });

        // ── Right to Erasure (Article 17) ──

        group.MapPost("/erasure/{studentId}", async (
            string studentId,
            [FromServices] IRightToErasureService erasureService,
            [FromServices] IDocumentStore store,
            HttpContext httpContext) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school (CRITICAL - destructive operation)
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, httpContext.User, store);

            var requestedBy = httpContext.User.Identity?.Name ?? "admin";
            var request = await erasureService.RequestErasureAsync(studentId, requestedBy);
            return Results.Ok(new
            {
                request.StudentId,
                request.Status,
                request.RequestedAt,
                coolingPeriodEnds = request.RequestedAt.AddDays(30)
            });
        });

        group.MapGet("/erasure/{studentId}/status", async (
            string studentId,
            [FromServices] IRightToErasureService erasureService,
            [FromServices] IDocumentStore store,
            HttpContext ctx) =>
        {
            // FIND-sec-011: Verify student belongs to caller's school
            await GdprResourceGuard.VerifyStudentBelongsToCallerSchoolAsync(studentId, ctx.User, store);

            var request = await erasureService.GetErasureStatusAsync(studentId);
            return request is null
                ? Results.NotFound(new { error = $"No erasure request for {studentId}" })
                : Results.Ok(request);
        });

        return group;
    }
}

public sealed record ConsentRequest(string StudentId, string ConsentType);
