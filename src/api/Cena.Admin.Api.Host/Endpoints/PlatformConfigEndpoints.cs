// =============================================================================
// Cena Platform — Platform-config admin endpoints (task t_b89826b8bd60)
//
// Super-admin REST surface under /api/admin/platform-config:
//
//   GET   /trial-allotment    — read current platform-wide trial knobs
//   PATCH /trial-allotment    — overwrite the trial knobs (super-admin only)
//
// Scope: platform-wide configuration that doesn't naturally fit in any one
// bounded context's existing admin surface. Today's only resident is the
// trial-allotment config. As more platform knobs land (per-institute price
// overrides already live in their own InstitutePricingOverride endpoint;
// these are orthogonal), they can join this endpoint group.
//
// Auth: AdminOnly. Both the read and write are AdminOnly because the trial
// settings are visible only to admins (regular users see the *effect* via
// /api/me/entitlement; the raw config is internal). When a higher super-
// admin tier exists in CenaAuthPolicies, the write should be tightened to
// that higher tier — flagged as a follow-up TODO since the policy file is
// shared and the change is cross-cutting.
//
// Audit: every PATCH appends a TrialAllotmentConfigChanged_V1 event to the
// "trial-allotment-config" stream via MartenTrialAllotmentConfigStore. The
// admin's encrypted subject id is captured from the JWT 'sub' claim — the
// same source pattern other admin-write endpoints use.
//
// Validation: ranges enforced server-side via TrialAllotmentValidator. A
// failed validation returns 400 with the structured field+reason so the
// admin UI can surface a precise error.
// =============================================================================

using System.Security.Claims;
using System.Text.Json.Serialization;
using Cena.Actors.Subscriptions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using ErrorCategory = Cena.Infrastructure.Errors.ErrorCategory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>Wire-format response for GET /api/admin/platform-config/trial-allotment.</summary>
public sealed record TrialAllotmentDto(
    [property: JsonPropertyName("trialDurationDays")] int TrialDurationDays,
    [property: JsonPropertyName("trialTutorTurns")] int TrialTutorTurns,
    [property: JsonPropertyName("trialPhotoDiagnostics")] int TrialPhotoDiagnostics,
    [property: JsonPropertyName("trialPracticeSessions")] int TrialPracticeSessions,
    [property: JsonPropertyName("trialEnabled")] bool TrialEnabled,
    [property: JsonPropertyName("lastUpdatedAtUtc")] DateTimeOffset? LastUpdatedAtUtc,
    [property: JsonPropertyName("lastUpdatedByAdminEncrypted")] string? LastUpdatedByAdminEncrypted);

/// <summary>Wire-format request for PATCH /api/admin/platform-config/trial-allotment.</summary>
public sealed record TrialAllotmentUpdateRequestDto(
    [property: JsonPropertyName("trialDurationDays")] int TrialDurationDays,
    [property: JsonPropertyName("trialTutorTurns")] int TrialTutorTurns,
    [property: JsonPropertyName("trialPhotoDiagnostics")] int TrialPhotoDiagnostics,
    [property: JsonPropertyName("trialPracticeSessions")] int TrialPracticeSessions);

/// <summary>
/// Wires the platform-config admin endpoints. All routes are
/// <see cref="CenaAuthPolicies.AdminOnly"/> per the file-header note.
/// </summary>
public static class PlatformConfigEndpoints
{
    public const string TrialAllotmentRoute = "/api/admin/platform-config/trial-allotment";

    /// <summary>Register the platform-config endpoints on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapPlatformConfigEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(TrialAllotmentRoute, HandleGetTrialAllotmentAsync)
            .WithName("GetPlatformTrialAllotment")
            .WithTags("Admin", "PlatformConfig", "Trial")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<TrialAllotmentDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        app.MapPatch(TrialAllotmentRoute, HandlePatchTrialAllotmentAsync)
            .WithName("PatchPlatformTrialAllotment")
            .WithTags("Admin", "PlatformConfig", "Trial")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<TrialAllotmentDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleGetTrialAllotmentAsync(
        [FromServices] ITrialAllotmentConfigStore store,
        CancellationToken ct)
    {
        var config = await store.GetAsync(ct);
        return Results.Ok(ToDto(config));
    }

    private static async Task<IResult> HandlePatchTrialAllotmentAsync(
        HttpContext http,
        [FromBody] TrialAllotmentUpdateRequestDto body,
        [FromServices] ITrialAllotmentConfigStore store,
        CancellationToken ct)
    {
        // Capture the admin's encrypted subject id for the audit event.
        // Same claim source as other admin-write endpoints
        // (BankTransferAdminEndpoints, AlphaMigrationEndpoints).
        var adminId = http.User.FindFirstValue("sub")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(adminId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var updated = await store.UpdateAsync(
                body.TrialDurationDays,
                body.TrialTutorTurns,
                body.TrialPhotoDiagnostics,
                body.TrialPracticeSessions,
                changedByAdminEncrypted: adminId,
                ct);
            return Results.Ok(ToDto(updated));
        }
        catch (TrialAllotmentValidationException ex)
        {
            return Results.Json(
                new CenaError(
                    Code: "trial_allotment_validation_failed",
                    Message: ex.Message,
                    Category: ErrorCategory.Validation,
                    Details: new Dictionary<string, object?>
                    {
                        ["field"] = ex.FailedField,
                        ["reason"] = ex.Reason,
                    },
                    CorrelationId: null),
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static TrialAllotmentDto ToDto(TrialAllotmentConfig config) => new(
        TrialDurationDays: config.TrialDurationDays,
        TrialTutorTurns: config.TrialTutorTurns,
        TrialPhotoDiagnostics: config.TrialPhotoDiagnostics,
        TrialPracticeSessions: config.TrialPracticeSessions,
        TrialEnabled: config.TrialEnabled,
        LastUpdatedAtUtc: config.LastUpdatedAtUtc == DateTimeOffset.MinValue
            ? null
            : config.LastUpdatedAtUtc,
        LastUpdatedByAdminEncrypted: string.IsNullOrEmpty(config.LastUpdatedByAdminEncrypted)
            ? null
            : config.LastUpdatedByAdminEncrypted);
}
