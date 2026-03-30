// =============================================================================
// Cena Platform -- GDPR Admin Endpoints (SEC-005)
// Consent management, data export, and right-to-erasure for GDPR compliance.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class GdprEndpoints
{
    public static RouteGroupBuilder MapGdprEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/gdpr")
            .WithTags("GDPR")
            .RequireAuthorization("AdminPolicy");

        // ── Consent Management ──

        group.MapGet("/consents/{studentId}", async (
            string studentId, IGdprConsentManager consentManager) =>
        {
            var consents = await consentManager.GetConsentsAsync(studentId);
            return Results.Ok(new { studentId, consents });
        });

        group.MapPost("/consents", async (
            ConsentRequest request, IGdprConsentManager consentManager) =>
        {
            if (!Enum.TryParse<ConsentType>(request.ConsentType, true, out var type))
                return Results.BadRequest(new { error = $"Invalid consent type: {request.ConsentType}" });

            await consentManager.RecordConsentAsync(request.StudentId, type);
            return Results.Ok(new { request.StudentId, request.ConsentType, granted = true });
        });

        group.MapDelete("/consents/{studentId}/{consentType}", async (
            string studentId, string consentType, IGdprConsentManager consentManager) =>
        {
            if (!Enum.TryParse<ConsentType>(consentType, true, out var type))
                return Results.BadRequest(new { error = $"Invalid consent type: {consentType}" });

            await consentManager.RevokeConsentAsync(studentId, type);
            return Results.Ok(new { studentId, consentType, granted = false });
        });

        // ── Data Export (Article 20) ──

        group.MapPost("/export/{studentId}", async (
            string studentId, Marten.IDocumentStore store) =>
        {
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
            string studentId, IRightToErasureService erasureService, HttpContext httpContext) =>
        {
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
            string studentId, IRightToErasureService erasureService) =>
        {
            var request = await erasureService.GetErasureStatusAsync(studentId);
            return request is null
                ? Results.NotFound(new { error = $"No erasure request for {studentId}" })
                : Results.Ok(request);
        });

        return group;
    }
}

public sealed record ConsentRequest(string StudentId, string ConsentType);
