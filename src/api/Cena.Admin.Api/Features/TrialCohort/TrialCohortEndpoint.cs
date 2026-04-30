// =============================================================================
// Cena Platform — Admin trial-cohort dashboard endpoint (Phase 4).
//
//   GET /api/admin/cohorts/trial?from=YYYY-MM-DD&to=YYYY-MM-DD
//
// Returns trial-funnel metrics for the admin dashboard's trial-cohort card.
// Default window: trailing 30 days from now if from/to are omitted.
//
// AuthZ: ModeratorOrAbove. Tenant-agnostic for now (the trial-funnel is
// platform-wide; per-institute cohort split comes later when ADR-0001
// multi-institute lands fully).
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Features.TrialCohort;

public static class TrialCohortEndpoint
{
    public const string Route = "/api/admin/cohorts/trial";

    public static IEndpointRouteBuilder MapTrialCohortEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetTrialCohortMetrics")
            .WithTags("Admin", "Trial Cohort")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);
        return app;
    }

    internal static async Task<IResult> HandleAsync(
        [FromServices] ITrialCohortReader reader,
        [FromServices] TimeProvider clock,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        DateTimeOffset windowEnd, windowStart;

        // Defaults: trailing 30 days. Both 'from' and 'to' must be valid
        // ISO-8601 if supplied; partial supplies are rejected so the UI
        // stays explicit about what window it's asking for.
        if (string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to))
        {
            windowEnd = now;
            windowStart = now.AddDays(-30);
        }
        else
        {
            if (!TryParseDate(from, out windowStart))
                return Results.BadRequest(new { error = "invalid_from", message = "from must be ISO-8601 date or datetime." });
            if (!TryParseDate(to, out windowEnd))
                return Results.BadRequest(new { error = "invalid_to", message = "to must be ISO-8601 date or datetime." });
        }

        if (windowEnd <= windowStart)
        {
            return Results.BadRequest(new
            {
                error = "invalid_window",
                message = "to must be strictly after from."
            });
        }

        var dto = await reader.GetMetricsAsync(windowStart, windowEnd, ct);
        return Results.Ok(dto);
    }

    private static bool TryParseDate(string? input, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input)) return false;
        return DateTimeOffset.TryParse(
            input,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out value);
    }
}
