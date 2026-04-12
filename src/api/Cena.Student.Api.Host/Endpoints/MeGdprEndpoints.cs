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
using Cena.Infrastructure.Documents;
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

        // ---- Consent Management (privacy.vue) ----
        // Student-facing consent endpoints under /api/me/consent
        var consentGroup = app.MapGroup("/api/me/consent")
            .WithTags("Consent")
            .RequireAuthorization(CenaAuthPolicies.StudentOnly);

        consentGroup.MapGet("", GetConsentState).WithName("GetMyConsentState");
        consentGroup.MapPost("", UpdateConsent).WithName("UpdateMyConsent");
        consentGroup.MapPost("/bulk", UpdateBulkConsents).WithName("UpdateMyBulkConsents");
        consentGroup.MapGet("/defaults", GetDefaultConsents).WithName("GetMyDefaultConsents");

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
                purpose = c.Purpose.ToString(),
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

        if (!Enum.TryParse<ProcessingPurpose>(request.Purpose, true, out var purpose))
            return Results.BadRequest(new { error = $"Invalid consent purpose: {request.Purpose}" });

        await consentManager.RecordConsentAsync(studentId, purpose);

        logger.LogInformation(
            "FIND-privacy-003: Student {StudentId} granted consent for {Purpose}",
            studentId, purpose);

        return Results.Ok(new { studentId, consentType = purpose.ToString(), granted = true });
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

        if (!Enum.TryParse<ProcessingPurpose>(purpose, true, out var processingPurpose))
            return Results.BadRequest(new { error = $"Invalid consent purpose: {purpose}" });

        await consentManager.RevokeConsentAsync(studentId, processingPurpose);

        logger.LogInformation(
            "FIND-privacy-003: Student {StudentId} revoked consent for {Purpose}",
            studentId, processingPurpose);

        return Results.Ok(new { studentId, consentType = processingPurpose.ToString(), granted = false });
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

    // ====================================================================================
    // GET /api/me/consent
    // Returns current student's consent settings for all purposes
    // Shows which are granted, which are denied, which are defaults
    // ====================================================================================
    private static async Task<IResult> GetConsentState(
        HttpContext ctx,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        var consents = await consentManager.GetConsentsAsync(studentId);
        var consentDict = consents.ToDictionary(c => c.Purpose, c => c);

        // Build ConsentDto for all known processing purposes
        var allPurposes = Enum.GetValues<ProcessingPurpose>();
        var consentDtos = allPurposes.Select(purpose =>
        {
            if (consentDict.TryGetValue(purpose, out var record))
            {
                return new ConsentDto(
                    Purpose: purpose.ToString().ToLowerInvariant(),
                    Granted: record.Granted,
                    GrantedAt: record.GrantedAt,
                    RevokedAt: record.RevokedAt,
                    IsDefault: false
                );
            }
            else
            {
                // No explicit consent record - this is a default state (denied)
                return new ConsentDto(
                    Purpose: purpose.ToString().ToLowerInvariant(),
                    Granted: false,
                    GrantedAt: null,
                    RevokedAt: null,
                    IsDefault: true
                );
            }
        }).ToList();

        logger.LogInformation(
            "[SIEM] ConsentStateQueried: StudentId={StudentId}, Count={Count}",
            studentId, consentDtos.Count);

        return Results.Ok(new ConsentStateResponse(studentId, consentDtos));
    }

    // ====================================================================================
    // POST /api/me/consent
    // Request body: { "purpose": "analytics", "granted": true/false }
    // Records consent change via GdprConsentManager
    // Returns updated consent state
    // ====================================================================================
    private static async Task<IResult> UpdateConsent(
        HttpContext ctx,
        [FromBody] ConsentUpdateRequest request,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (!Enum.TryParse<ProcessingPurpose>(request.Purpose, true, out var purpose))
        {
            return Results.BadRequest(new
            {
                error = $"Invalid consent purpose: {request.Purpose}",
                validPurposes = Enum.GetNames<ProcessingPurpose>().Select(p => p.ToLowerInvariant())
            });
        }

        // Record or revoke consent based on granted flag
        if (request.Granted)
        {
            await consentManager.RecordConsentAsync(studentId, purpose);
        }
        else
        {
            await consentManager.RevokeConsentAsync(studentId, purpose);
        }

        logger.LogInformation(
            "[SIEM] ConsentUpdated: StudentId={StudentId}, Purpose={Purpose}, Granted={Granted}",
            studentId, purpose, request.Granted);

        // Return updated consent state
        var updatedConsent = new ConsentDto(
            Purpose: purpose.ToString().ToLowerInvariant(),
            Granted: request.Granted,
            GrantedAt: request.Granted ? DateTimeOffset.UtcNow : null,
            RevokedAt: request.Granted ? null : DateTimeOffset.UtcNow,
            IsDefault: false
        );

        return Results.Ok(updatedConsent);
    }

    // ====================================================================================
    // POST /api/me/consent/bulk
    // Request body: { "consents": [{"purpose": "x", "granted": true}, ...] }
    // Records multiple consent changes at once
    // Used by privacy.vue on initial load
    // ====================================================================================
    private static async Task<IResult> UpdateBulkConsents(
        HttpContext ctx,
        [FromBody] BulkConsentRequest request,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (request.Consents == null || request.Consents.Count == 0)
        {
            return Results.BadRequest(new { error = "Consents array is required and cannot be empty" });
        }

        var results = new List<ConsentDto>();
        var errors = new List<string>();

        foreach (var consent in request.Consents)
        {
            if (!Enum.TryParse<ProcessingPurpose>(consent.Purpose, true, out var purpose))
            {
                errors.Add($"Invalid consent purpose: {consent.Purpose}");
                continue;
            }

            try
            {
                if (consent.Granted)
                {
                    await consentManager.RecordConsentAsync(studentId, purpose);
                }
                else
                {
                    await consentManager.RevokeConsentAsync(studentId, purpose);
                }

                results.Add(new ConsentDto(
                    Purpose: purpose.ToString().ToLowerInvariant(),
                    Granted: consent.Granted,
                    GrantedAt: consent.Granted ? DateTimeOffset.UtcNow : null,
                    RevokedAt: consent.Granted ? null : DateTimeOffset.UtcNow,
                    IsDefault: false
                ));
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to update {consent.Purpose}: {ex.Message}");
                logger.LogError(ex,
                    "[SIEM] ConsentUpdateFailed: StudentId={StudentId}, Purpose={Purpose}, Granted={Granted}",
                    studentId, consentType, consent.Granted);
            }
        }

        logger.LogInformation(
            "[SIEM] BulkConsentsUpdated: StudentId={StudentId}, SuccessCount={SuccessCount}, ErrorCount={ErrorCount}",
            studentId, results.Count, errors.Count);

        return Results.Ok(new BulkConsentResponse(studentId, results, errors));
    }

    // ====================================================================================
    // GET /api/me/consent/defaults
    // Returns default consent values based on student's age
    // Uses GetDefaultConsentsAsync logic
    // ====================================================================================
    private static async Task<IResult> GetDefaultConsents(
        HttpContext ctx,
        [FromServices] IDocumentStore store,
        [FromServices] ILogger<GdprLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        // Get student's age from profile if available
        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        int? age = null;
        if (profile?.DateOfBirth.HasValue == true)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dob = DateOnly.FromDateTime(profile.DateOfBirth.Value.Date);
            age = today.Year - dob.Year;
            if (today < dob.AddYears(age.Value))
                age--;
        }

        // Calculate default consents based on age
        // Under 13 (COPPA): all denied
        // 13-15: all denied (opt-in required)
        // 16+ (adult): can have defaults based on policy
        var defaults = GetDefaultConsentsForAge(age);

        logger.LogInformation(
            "[SIEM] DefaultConsentsQueried: StudentId={StudentId}, Age={Age}",
            studentId, age?.ToString() ?? "unknown");

        return Results.Ok(new DefaultConsentsResponse(studentId, age, defaults));
    }

    // ---- Helper methods ----

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    /// <summary>
    /// Determines if the student is a minor (under 16) based on their profile.
    /// </summary>
    private static bool IsMinor(StudentProfileSnapshot? profile)
    {
        if (profile?.DateOfBirth.HasValue != true)
        {
            // If age unknown, default to minor (high-privacy default)
            return true;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dob = DateOnly.FromDateTime(profile.DateOfBirth.Value.Date);
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age))
            age--;

        return age < 16;
    }
}

// ---- Request/Response DTOs ----

public sealed record SelfConsentRequest(string Purpose);

public sealed record DsarSubmitRequest(string Message, string? ContactEmail = null);

/// <summary>
/// Consent DTO returned by all consent endpoints.
/// </summary>
public sealed record ConsentDto(
    string Purpose,
    bool Granted,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? RevokedAt,
    bool IsDefault);

/// <summary>
/// Response for GET /api/me/consent
/// </summary>
public sealed record ConsentStateResponse(string StudentId, IReadOnlyList<ConsentDto> Consents);

/// <summary>
/// Request body for POST /api/me/consent
/// </summary>
public sealed record ConsentUpdateRequest(string Purpose, bool Granted);

/// <summary>
/// Single consent entry for bulk update
/// </summary>
public sealed record ConsentEntry(string Purpose, bool Granted);

/// <summary>
/// Request body for POST /api/me/consent/bulk
/// </summary>
public sealed record BulkConsentRequest(IReadOnlyList<ConsentEntry> Consents);

/// <summary>
/// Response for POST /api/me/consent/bulk
/// </summary>
public sealed record BulkConsentResponse(
    string StudentId,
    IReadOnlyList<ConsentDto> Results,
    IReadOnlyList<string> Errors);

/// <summary>
/// Response for GET /api/me/consent/defaults
/// </summary>
public sealed record DefaultConsentsResponse(
    string StudentId,
    int? Age,
    IReadOnlyList<ConsentDto> Defaults);

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
