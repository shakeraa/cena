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
//
// PRR-320 backfill: the previous version of this endpoint hard-coded zero
// values for WeeklyMinutes / MonthlyMinutes / TopicsPracticed /
// LastActiveAt / ReadinessScore with a "PRR-323 will fill these values"
// TODO. That comment is now closed — the endpoint delegates to
// IParentDashboardCardSource (see ParentDashboardServiceRegistration)
// which composes the scalars from the Marten event log. The Noop
// default remains registered so hosts without a document store still
// resolve the dependency graph (returns zero-scalar cards — honest
// empties).
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Parenting;
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
        [FromServices] IParentDashboardCardSource cardSource,
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

        // PRR-320 backfill: delegate per-student scalars to the card
        // source. The card source owns the read-model composition
        // (Marten event scan in production; honest empties in the Noop
        // default). See IParentDashboardCardSource file banner for the
        // v1 proxy rationale — HintRequested_V1 as engagement signal
        // until a dedicated minutes-on-task projection ships.
        var cards = await cardSource
            .BuildAsync(aggregate.State.LinkedStudents, now, ct)
            .ConfigureAwait(false);

        var students = new List<ParentDashboardStudentDto>(
            aggregate.State.LinkedStudents.Count);
        foreach (var linked in aggregate.State.LinkedStudents)
        {
            var entitlement = await entitlementResolver.ResolveAsync(
                linked.StudentSubjectIdEncrypted, ct);
            var card = cards.GetOrZero(linked.StudentSubjectIdEncrypted);
            students.Add(new ParentDashboardStudentDto(
                StudentId: linked.StudentSubjectIdEncrypted,
                DisplayName: string.Empty,
                ActiveTier: entitlement.EffectiveTier.ToString(),
                WeeklyMinutes: card.WeeklyMinutes,
                MonthlyMinutes: card.MonthlyMinutes,
                TopicsPracticed: card.TopicsPracticed,
                ReadinessScore: card.ReadinessScore,
                LastActiveAt: card.LastActiveAt));
        }

        var response = new ParentDashboardResponseDto(
            Students: students,
            HouseholdMinutesWeekly: students.Sum(s => s.WeeklyMinutes),
            HouseholdMinutesMonthly: students.Sum(s => s.MonthlyMinutes),
            GeneratedAt: now);
        return Results.Ok(response);
    }
}
