// =============================================================================
// Cena Platform — Parent dashboard endpoint (EPIC-PRR-I PRR-320/321/322/324)
//
// GET /api/me/parent-dashboard — Premium-tier only (feature-fenced at
// StudentEntitlementView.Features.ParentDashboard). Returns aggregated
// per-student + household rollups.
//
// Feature-gate discipline: if the caller's tier doesn't include the parent
// dashboard, return 403 with error=tier_required. Frontend handles the
// upsell to Premium.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Minimal-API endpoint for the parent dashboard.</summary>
public static class ParentDashboardEndpoints
{
    /// <summary>Register <c>GET /api/me/parent-dashboard</c>.</summary>
    public static IEndpointRouteBuilder MapParentDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/parent-dashboard", GetDashboard)
            .WithTags("Subscriptions")
            .RequireAuthorization()
            .WithName("GetParentDashboard");
        return app;
    }

    private static async Task<IResult> GetDashboard(
        HttpContext http,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] IStudentEntitlementResolver entitlementResolver,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? http.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return Results.Unauthorized();
        }

        var aggregate = await store.LoadAsync(parentId, ct);
        if (aggregate.State.Status != SubscriptionStatus.Active)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Feature-fence: only tiers with ParentDashboard = true may access.
        var tierDef = TierCatalog.Get(aggregate.State.CurrentTier);
        if (!tierDef.Features.ParentDashboard)
        {
            return Results.Json(
                new { error = "tier_required", requiredTier = "Premium" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var now = clock.GetUtcNow();
        var students = new List<ParentDashboardStudentDto>();
        foreach (var linked in aggregate.State.LinkedStudents)
        {
            var entitlement = await entitlementResolver.ResolveAsync(
                linked.StudentSubjectIdEncrypted, ct);
            // v1 rollup surface: usage metrics come from the per-student
            // StudentMetricsAggregate (ADR-0012) once fan-out wires in.
            // Here we expose tier + placeholder zero counters — the dedicated
            // weekly-rollup worker (PRR-323) will fill these values via the
            // same endpoint once its Marten projection lands.
            students.Add(new ParentDashboardStudentDto(
                StudentId: linked.StudentSubjectIdEncrypted,
                DisplayName: string.Empty,
                ActiveTier: entitlement.EffectiveTier.ToString(),
                WeeklyMinutes: 0,
                MonthlyMinutes: 0,
                TopicsPracticed: 0,
                ReadinessScore: null,
                LastActiveAt: null));
        }

        var response = new ParentDashboardResponseDto(
            Students: students,
            HouseholdMinutesWeekly: students.Sum(s => s.WeeklyMinutes),
            HouseholdMinutesMonthly: students.Sum(s => s.MonthlyMinutes),
            GeneratedAt: now);
        return Results.Ok(response);
    }
}
