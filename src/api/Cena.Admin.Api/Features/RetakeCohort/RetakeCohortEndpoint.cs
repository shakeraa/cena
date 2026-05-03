// =============================================================================
// Cena Platform — Retake cohort endpoint (prr-238)
//
//   GET /api/admin/institutes/{instituteId}/cohorts/retake
//
// Returns the list of students in the institute whose active plan
// contains at least one ExamTarget with ReasonTag=Retake. Consumed by
// the educator console and by the admin dashboard's "active retake
// candidates" count.
//
// AuthZ:
//   - AdminOnly — ADMIN sees own institute, SUPER_ADMIN sees any.
//   - Tenant scope enforced in-handler via institute_id claim matching.
//
// Response carries minimal DTOs — no free-text, no grade, no PII other
// than the pseudonymous studentAnonId + the ExamTarget catalog keys (which
// are non-PII per ADR-0050 §2). Educator console expands each row to full
// detail on click via the existing per-student plan read path.
// =============================================================================

using System.Security.Claims;
using System.Text.Json.Serialization;
using Cena.Actors.StudentPlan;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.RetakeCohort;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>One student in the retake cohort.</summary>
public sealed record RetakeCohortStudentDto(
    [property: JsonPropertyName("studentAnonId")]
    string StudentAnonId,
    [property: JsonPropertyName("instituteId")]
    string InstituteId,
    [property: JsonPropertyName("retakeTargetCount")]
    int RetakeTargetCount,
    [property: JsonPropertyName("retakeExamCodes")]
    IReadOnlyList<string> RetakeExamCodes);

/// <summary>Response envelope.</summary>
public sealed record RetakeCohortResponseDto(
    [property: JsonPropertyName("instituteId")]
    string InstituteId,
    [property: JsonPropertyName("retrievalStrengthFraming")]
    bool RetrievalStrengthFraming,
    [property: JsonPropertyName("students")]
    IReadOnlyList<RetakeCohortStudentDto> Students);

// ---- Endpoint ---------------------------------------------------------------

public static class RetakeCohortEndpoint
{
    /// <summary>Canonical GET route.</summary>
    public const string Route = "/api/admin/institutes/{instituteId}/cohorts/retake";

    internal sealed class RetakeCohortMarker { }

    /// <summary>Map the GET route onto the endpoint router.</summary>
    public static IEndpointRouteBuilder MapRetakeCohortEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetInstituteRetakeCohort")
            .WithTags("Admin", "Retake Cohort")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);
        return app;
    }

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string instituteId,
        HttpContext http,
        [FromServices] IRetakeCohortReader reader,
        [FromServices] ILogger<RetakeCohortMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            return Results.BadRequest(new { error = "missing-instituteId" });

        if (!IsTenantAllowed(http, instituteId))
        {
            logger.LogWarning(
                "[prr-238] retake-cohort cross-tenant denied: requested={Institute}",
                instituteId);
            return Results.Forbid();
        }

        var rows = await reader.ListRetakeCohortAsync(instituteId, ct).ConfigureAwait(false);

        var dto = new RetakeCohortResponseDto(
            InstituteId: instituteId,
            // Surface the retrieval-strength framing flag so the admin UI
            // can label the cohort "retrieval-practice prep" rather than
            // "re-teaching" — matches the neutral, honest banner copy per
            // `onboarding.retakeBanner.*` i18n keys and the Honest-not-
            // complimentary memory rule.
            RetrievalStrengthFraming: true,
            Students: rows.Select(r => new RetakeCohortStudentDto(
                StudentAnonId: r.StudentAnonId,
                InstituteId: r.InstituteId,
                RetakeTargetCount: r.RetakeTargets.Count,
                RetakeExamCodes: r.RetakeTargets
                    .Select(t => t.ExamCode.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()))
                .ToArray());

        logger.LogInformation(
            "[prr-238] retake-cohort read: institute={Institute} count={Count}",
            instituteId, dto.Students.Count);

        return Results.Ok(dto);
    }

    internal static bool IsTenantAllowed(HttpContext http, string instituteId)
    {
        if (http.User.IsInRole("SUPER_ADMIN")
            || http.User.HasClaim("role", "SUPER_ADMIN")
            || http.User.HasClaim(ClaimTypes.Role, "SUPER_ADMIN"))
        {
            return true;
        }
        var claim = http.User.FindFirst("institute_id")?.Value;
        return !string.IsNullOrEmpty(claim)
            && string.Equals(claim, instituteId, StringComparison.Ordinal);
    }
}
