// =============================================================================
// Cena Platform -- Consent Management REST Endpoints (SEC-006)
// Student-facing consent management with granular ProcessingPurpose control.
// Extends GDPR self-service with per-purpose consent tracking.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Compliance;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Cena.Infrastructure.Errors;

namespace Cena.Api.Host.Endpoints;

public static class ConsentEndpoints
{
    private sealed class ConsentLoggerMarker { }

    public static IEndpointRouteBuilder MapConsentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/consent")
            .WithTags("Consent Management")
            .RequireAuthorization();

        // GET /api/me/consent - Get current student's consent status for all purposes
        group.MapGet("", GetConsentStatus).WithName("GetMyConsentStatus")
    .Produces<ConsentOverviewDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/me/consent - Grant consent for a specific purpose
        group.MapPost("", GrantConsent).WithName("GrantMyConsent");

        // POST /api/me/consent/bulk - Bulk update consent for multiple purposes
        group.MapPost("/bulk", BulkUpdateConsent).WithName("BulkUpdateMyConsent")
    .Produces<BulkConsentResultDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/me/consent/defaults - Get default consent values (adult vs minor)
        group.MapGet("/defaults", GetConsentDefaults).WithName("GetConsentDefaults")
    .Produces<ConsentDefaultsDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // DELETE /api/me/consent/{purpose} - Revoke consent for a specific purpose
        group.MapDelete("/{purpose}", RevokeConsent).WithName("RevokeMyConsent")
    .Produces<ConsentGrantResultDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // =======================================================================
    // GET /api/me/consent
    // =======================================================================
    private static async Task<IResult> GetConsentStatus(
        HttpContext ctx,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<ConsentLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        var consents = await consentManager.GetConsentsAsync(studentId);

        // Build comprehensive consent status for all ProcessingPurposes
        var consentStatuses = Enum.GetValues<ProcessingPurpose>()
            .Select(purpose =>
            {
                var hasExplicitConsent = consents.Any(c => 
                    c.Purpose == purpose && c.Granted);
                
                return new ConsentStatusDto(
                    Purpose: purpose.ToString(),
                    Description: purpose.GetDescription(),
                    Granted: hasExplicitConsent || purpose.IsAlwaysRequired(),
                    IsRequired: purpose.IsAlwaysRequired(),
                    LawfulBasis: GetLawfulBasis(purpose).ToString(),
                    CanBeRevoked: !purpose.IsAlwaysRequired()
                );
            })
            .ToList();

        logger.LogInformation(
            "SEC-006: Student {StudentId} retrieved consent status, purposes={PurposeCount}",
            studentId, consentStatuses.Count);

        return Results.Ok(new ConsentOverviewDto(
            StudentId: studentId,
            Purposes: consentStatuses,
            LastUpdated: consents.Where(c => c.Granted)
                .Max(c => (DateTimeOffset?)c.GrantedAt) ?? DateTimeOffset.UtcNow
        ));
    }

    // =======================================================================
    // POST /api/me/consent
    // =======================================================================
    private static async Task<IResult> GrantConsent(
        HttpContext ctx,
        [FromBody] GrantConsentRequest request,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<ConsentLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (!Enum.TryParse<ProcessingPurpose>(request.Purpose, true, out var purpose))
        {
            return Results.BadRequest(new 
            { 
                error = "Invalid purpose", 
                validPurposes = Enum.GetNames<ProcessingPurpose>() 
            });
        }

        // Cannot grant consent for always-required purposes (they're automatic)
        if (purpose.IsAlwaysRequired())
        {
            return Results.BadRequest(new 
            { 
                error = "Cannot grant consent for required purpose", 
                message = $"{purpose} is always required and cannot be changed." 
            });
        }

        await consentManager.RecordConsentAsync(studentId, purpose);

        logger.LogInformation(
            "SEC-006: Student {StudentId} granted consent for {Purpose}",
            studentId, purpose);

        return Results.Ok(new ConsentGrantResultDto(
            StudentId: studentId,
            Purpose: purpose.ToString(),
            Granted: true,
            GrantedAt: DateTimeOffset.UtcNow
        ));
    }

    // =======================================================================
    // POST /api/me/consent/bulk
    // =======================================================================
    private static async Task<IResult> BulkUpdateConsent(
        HttpContext ctx,
        [FromBody] BulkConsentRequest request,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<ConsentLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        var results = new List<ConsentGrantResultDto>();
        var errors = new List<string>();

        foreach (var purposeUpdate in request.Purposes)
        {
            if (!Enum.TryParse<ProcessingPurpose>(purposeUpdate.Purpose, true, out var purpose))
            {
                errors.Add($"Invalid purpose: {purposeUpdate.Purpose}");
                continue;
            }

            // Skip always-required purposes
            if (purpose.IsAlwaysRequired())
            {
                results.Add(new ConsentGrantResultDto(
                    StudentId: studentId,
                    Purpose: purpose.ToString(),
                    Granted: true,
                    GrantedAt: DateTimeOffset.UtcNow,
                    Note: "Always required - cannot be changed"
                ));
                continue;
            }

            if (purposeUpdate.Granted)
            {
                await consentManager.RecordConsentAsync(studentId, purpose);
                results.Add(new ConsentGrantResultDto(
                    StudentId: studentId,
                    Purpose: purpose.ToString(),
                    Granted: true,
                    GrantedAt: DateTimeOffset.UtcNow
                ));
            }
            else
            {
                await consentManager.RevokeConsentAsync(studentId, purpose);
                results.Add(new ConsentGrantResultDto(
                    StudentId: studentId,
                    Purpose: purpose.ToString(),
                    Granted: false,
                    GrantedAt: DateTimeOffset.UtcNow,
                    Note: "Consent revoked"
                ));
            }
        }

        logger.LogInformation(
            "SEC-006: Student {StudentId} bulk updated consent, updated={UpdatedCount}, errors={ErrorCount}",
            studentId, results.Count, errors.Count);

        return Results.Ok(new BulkConsentResultDto(
            StudentId: studentId,
            Results: results,
            Errors: errors.Count > 0 ? errors : null
        ));
    }

    // =======================================================================
    // GET /api/me/consent/defaults
    // =======================================================================
    private static IResult GetConsentDefaults(
        HttpContext ctx,
        [FromServices] ILogger<ConsentLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        var adultDefaults = Enum.GetValues<ProcessingPurpose>()
            .Select(p => new PurposeDefaultDto(
                Purpose: p.ToString(),
                Description: p.GetDescription(),
                AdultDefault: p.GetDefaultConsent(isMinor: false),
                MinorDefault: p.GetDefaultConsent(isMinor: true),
                IsRequired: p.IsAlwaysRequired()
            ))
            .ToList();

        logger.LogInformation(
            "SEC-006: Student {StudentId} retrieved consent defaults",
            studentId);

        return Results.Ok(new ConsentDefaultsDto(
            AdultAgeThreshold: 16,
            Purposes: adultDefaults
        ));
    }

    // =======================================================================
    // DELETE /api/me/consent/{purpose}
    // =======================================================================
    private static async Task<IResult> RevokeConsent(
        HttpContext ctx,
        string purpose,
        [FromServices] IGdprConsentManager consentManager,
        [FromServices] ILogger<ConsentLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (!Enum.TryParse<ProcessingPurpose>(purpose, true, out var processingPurpose))
        {
            return Results.BadRequest(new 
            { 
                error = "Invalid purpose", 
                validPurposes = Enum.GetNames<ProcessingPurpose>() 
            });
        }

        // Cannot revoke always-required purposes
        if (processingPurpose.IsAlwaysRequired())
        {
            return Results.BadRequest(new 
            { 
                error = "Cannot revoke required purpose", 
                message = $"{processingPurpose} is required for account operation and cannot be revoked." 
            });
        }

        await consentManager.RevokeConsentAsync(studentId, processingPurpose);

        logger.LogInformation(
            "SEC-006: Student {StudentId} revoked consent for {Purpose}",
            studentId, processingPurpose);

        // Log structured consent denial for audit trail
        logger.LogWarning(
            "[SIEM] ConsentRevoked: Student {StudentId} revoked consent for {Purpose}",
            studentId, processingPurpose);

        return Results.Ok(new ConsentGrantResultDto(
            StudentId: studentId,
            Purpose: processingPurpose.ToString(),
            Granted: false,
            GrantedAt: DateTimeOffset.UtcNow
        ));
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }



    private static LawfulBasis GetLawfulBasis(ProcessingPurpose purpose)
    {
        var fieldInfo = purpose.GetType().GetField(purpose.ToString());
        if (fieldInfo is null) return LawfulBasis.Consent;

        var attr = fieldInfo.GetCustomAttributes(typeof(LawfulBasisAttribute), false)
            .Cast<LawfulBasisAttribute>()
            .FirstOrDefault();

        return attr?.Basis ?? LawfulBasis.Consent;
    }
}

// =============================================================================
// DTOs
// =============================================================================

public record ConsentStatusDto(
    string Purpose,
    string Description,
    bool Granted,
    bool IsRequired,
    string LawfulBasis,
    bool CanBeRevoked);

public record ConsentOverviewDto(
    string StudentId,
    IReadOnlyList<ConsentStatusDto> Purposes,
    DateTimeOffset LastUpdated);

public record GrantConsentRequest(string Purpose);

public record ConsentGrantResultDto(
    string StudentId,
    string Purpose,
    bool Granted,
    DateTimeOffset GrantedAt,
    string? Note = null);

public record BulkConsentPurposeDto(string Purpose, bool Granted);

public record BulkConsentRequest(IReadOnlyList<BulkConsentPurposeDto> Purposes);

public record BulkConsentResultDto(
    string StudentId,
    IReadOnlyList<ConsentGrantResultDto> Results,
    IReadOnlyList<string>? Errors);

public record PurposeDefaultDto(
    string Purpose,
    string Description,
    bool AdultDefault,
    bool MinorDefault,
    bool IsRequired);

public record ConsentDefaultsDto(
    int AdultAgeThreshold,
    IReadOnlyList<PurposeDefaultDto> Purposes);
