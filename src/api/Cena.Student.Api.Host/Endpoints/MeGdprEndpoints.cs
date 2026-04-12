// =============================================================================
// Cena Platform -- Student Self-Service GDPR Endpoints (FIND-privacy-003)
//
// GDPR Art 12-22, COPPA 312.6, Israel PPL 13: Students and parents must be
// able to exercise their data rights without contacting a school admin.
// These endpoints are scoped to the authenticated student's own data via JWT
// claim -- no cross-tenant / cross-student risk.
//
// Rate limits:
//   - Export:  1 per hour per student
//   - Erasure: 1 per day per student
//   - DSAR:    1 per day per student
//   - Consent: standard API rate limit
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public static class MeGdprEndpoints
{
    private sealed class GdprLoggerMarker { }

    public static IEndpointRouteBuilder MapMeGdprEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/gdpr")
            .WithTags("GDPR Self-Service")
            .RequireAuthorization();

        // ---- Consent Management (GDPR Art 7) ----
        group.MapGet("/consents", GetConsents).WithName("GetMyConsents");
        group.MapPost("/consents", RecordConsent).WithName("RecordMyConsent");
        group.MapDelete("/consents/{purpose}", RevokeConsent).WithName("RevokeMyConsent");

        // ---- Data Export (GDPR Art 20 -- Portability) ----
        group.MapPost("/export", RequestExport)
            .WithName("RequestMyDataExport")
            .RequireRateLimiting("gdpr-export");

        // ---- Right to Erasure (GDPR Art 17) ----
        group.MapPost("/erasure", RequestErasure)
            .WithName("RequestMyErasure")
            .RequireRateLimiting("gdpr-erasure");

        group.MapGet("/erasure/status", GetErasureStatus).WithName("GetMyErasureStatus");

        // ---- DSAR (GDPR Art 12, Israel PPL 13) ----
        var dsarGroup = app.MapGroup("/api/me")
            .WithTags("GDPR Self-Service")
            .RequireAuthorization();

        dsarGroup.MapPost("/dsar", SubmitDsar)
            .WithName("SubmitMyDsar")
            .RequireRateLimiting("gdpr-erasure");

        return app;
    }

    // ========================================================================
    // GET /api/me/gdpr/consents
    // ========================================================================
    private static async Task<IResult> GetConsents(
        HttpContext ctx,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        var consents = await consentManager.GetConsentsAsync(studentId);

        logger.LogInformation(
            "FIND-privacy-003: Student {StudentId} retrieved their consent records, count={Count}",
            studentId, consents.Count);

        return Results.Ok(new
        {
            studentId,
            consents = consents.Select(c => new
            {
                type = c.ConsentType.ToString(),
                granted = c.Granted,
                grantedAt = c.GrantedAt,
                revokedAt = c.RevokedAt,
            }),
        });
    }

    // ========================================================================
    // POST /api/me/gdpr/consents
    // ========================================================================
    private static async Task<IResult> RecordConsent(
        HttpContext ctx,
        [FromBody] SelfConsentRequest request,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (!Enum.TryParse<ConsentType>(request.ConsentType, true, out var type))
            return Results.BadRequest(new { error = $"Invalid consent type: {request.ConsentType}. Valid types: Analytics, Marketing, ThirdParty" });

        await consentManager.RecordConsentAsync(studentId, type);

        logger.LogInformation(
            "FIND-privacy-003: Student {StudentId} granted consent for {ConsentType}",
            studentId, type);

        return Results.Ok(new { studentId, consentType = type.ToString(), granted = true });
    }

    // ========================================================================
    // DELETE /api/me/gdpr/consents/{purpose}
    // ========================================================================
    private static async Task<IResult> RevokeConsent(
        HttpContext ctx,
        string purpose,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (!Enum.TryParse<ConsentType>(purpose, true, out var type))
            return Results.BadRequest(new { error = $"Invalid consent type: {purpose}. Valid types: Analytics, Marketing, ThirdParty" });

        await consentManager.RevokeConsentAsync(studentId, type);

        logger.LogInformation(
            "FIND-privacy-003: Student {StudentId} revoked consent for {ConsentType}",
            studentId, type);

        return Results.Ok(new { studentId, consentType = type.ToString(), granted = false });
    }

    // ========================================================================
    // POST /api/me/gdpr/export
    // ========================================================================
    private static async Task<IResult> RequestExport(
        HttpContext ctx,
        [FromServices] IDocumentStore store,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        await using var session = store.QuerySession();
        var snapshot = await session.Query<StudentProfileSnapshot>()
            .FirstOrDefaultAsync(s => s.StudentId == studentId);

        if (snapshot is null)
        {
            logger.LogWarning(
                "FIND-privacy-003: Data export requested by student {StudentId} but no profile found",
                studentId);
            return Results.NotFound(new { error = "Student profile not found" });
        }

        var export = StudentDataExporter.Export(studentId, snapshot, logger);

        logger.LogInformation(
            "FIND-privacy-003: Data export generated for student {StudentId}, fields={FieldCount}",
            studentId, export.Profile.Count);

        return Results.Json(export, contentType: "application/json", statusCode: 200);
    }

    // ========================================================================
    // POST /api/me/gdpr/erasure
    // ========================================================================
    private static async Task<IResult> RequestErasure(
        HttpContext ctx,
        [FromServices] IRightToErasureService erasureService,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        var request = await erasureService.RequestErasureAsync(studentId, $"self-service:{studentId}");

        logger.LogInformation(
            "FIND-privacy-003: Erasure requested by student {StudentId}, status={Status}, coolingPeriodEnds={CoolingEnd}",
            studentId, request.Status, request.RequestedAt.AddDays(30));

        return Results.Ok(new
        {
            request.StudentId,
            status = request.Status.ToString(),
            request.RequestedAt,
            coolingPeriodEnds = request.RequestedAt.AddDays(30),
            message = "Your data deletion request has been received. There is a 30-day cooling period during which you can cancel. After 30 days, your data will be permanently deleted.",
        });
    }

    // ========================================================================
    // GET /api/me/gdpr/erasure/status
    // ========================================================================
    private static async Task<IResult> GetErasureStatus(
        HttpContext ctx,
        [FromServices] IRightToErasureService erasureService,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        var request = await erasureService.GetErasureStatusAsync(studentId);

        if (request is null)
        {
            logger.LogInformation(
                "FIND-privacy-003: Erasure status queried by student {StudentId} -- no request found",
                studentId);
            return Results.Ok(new { studentId, hasActiveRequest = false });
        }

        return Results.Ok(new
        {
            request.StudentId,
            hasActiveRequest = request.Status != ErasureStatus.Completed && request.Status != ErasureStatus.Cancelled,
            status = request.Status.ToString(),
            request.RequestedAt,
            request.ProcessedAt,
            coolingPeriodEnds = request.RequestedAt.AddDays(30),
        });
    }

    // ========================================================================
    // POST /api/me/dsar
    // ========================================================================
    private static async Task<IResult> SubmitDsar(
        HttpContext ctx,
        [FromBody] DsarSubmitRequest request,
        [FromServices] IDocumentStore store,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "Message is required for a DSAR request" });

        var trackingId = $"DSAR-{studentId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        var slaDeadline = DateTimeOffset.UtcNow.AddDays(30);

        await using var session = store.LightweightSession();

        var dsarRecord = new DsarRecord
        {
            Id = trackingId,
            StudentId = studentId,
            Message = request.Message,
            ContactEmail = request.ContactEmail ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value,
            Status = "Submitted",
            SubmittedAt = DateTimeOffset.UtcNow,
            SlaDeadline = slaDeadline,
        };

        session.Store(dsarRecord);
        await session.SaveChangesAsync();

        logger.LogInformation(
            "FIND-privacy-003: DSAR submitted by student {StudentId}, trackingId={TrackingId}, slaDeadline={SlaDeadline}",
            studentId, trackingId, slaDeadline);

        return Results.Ok(new
        {
            trackingId,
            status = "Submitted",
            slaDeadline,
            message = "Your Data Subject Access Request has been submitted. Our Data Protection Officer will respond within 30 days.",
        });
    }

    // ---- Helpers ----

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}

// ---- Request/Response DTOs ----

public sealed record SelfConsentRequest(string ConsentType);

public sealed record DsarSubmitRequest(string Message, string? ContactEmail = null);

/// <summary>
/// Marten document for persisting DSAR (Data Subject Access Request) records.
/// GDPR Art 12 + Israel PPL 13: the platform must track and respond within 30 days.
/// </summary>
public sealed class DsarRecord
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ContactEmail { get; set; }
    public string Status { get; set; } = "Submitted";
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset SlaDeadline { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
