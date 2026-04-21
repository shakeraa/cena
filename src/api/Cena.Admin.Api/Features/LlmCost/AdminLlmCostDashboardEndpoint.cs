// =============================================================================
// Cena Platform — Admin LLM cost dashboard endpoint (prr-112).
//
// GET /api/admin/llm-cost/per-cohort?cohort={cohortId}&from={iso}&to={iso}
//
// Returns per-feature cost rollup for a cohort (today: institute_id) over a
// time window. Delegates to ILlmCostRollupService which is production-wired
// to Prometheus; test + local-dev get the NullLlmCostRollupService which
// returns zero-cost slices (the dashboard still renders, operators see
// "no data yet" vs. a 500).
//
// AuthZ:
//   - ADMIN or SUPER_ADMIN (CenaAuthPolicies.AdminOnly).
//   - Tenant scope: ADMIN callers can only query their own institute_id
//     (enforced in the handler); SUPER_ADMIN may query any institute.
//
// Window constraints:
//   - Required: `from` and `to` ISO-8601 timestamps.
//   - Max range: 90 days (LLM cost metrics retain 90d in Prometheus;
//     anything older is aggregated away and the query would 404).
//   - Future timestamps: rejected (400). Prevents silly / malicious
//     queries that would return "0 cost across Q3 2038".
// =============================================================================

using System.Globalization;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Llm;
using Cena.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.LlmCost;

public static class AdminLlmCostDashboardEndpoint
{
    /// <summary>Canonical route.</summary>
    public const string Route = "/api/admin/llm-cost/per-cohort";

    /// <summary>Max window length (days) allowed by the endpoint.</summary>
    public const int MaxWindowDays = 90;

    internal sealed class AdminLlmCostDashboardMarker { }

    public static IEndpointRouteBuilder MapAdminLlmCostDashboardEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetAdminLlmCostPerCohort")
            .WithTags("Admin", "LLM Cost", "Cost Dashboard")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);
        return app;
    }

    internal static async Task<IResult> HandleAsync(
        string? cohort,
        string? from,
        string? to,
        HttpContext http,
        ILlmCostRollupService rollupService,
        ILogger<AdminLlmCostDashboardMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cohort))
            return Results.BadRequest(new { error = "missing-cohort" });
        if (string.IsNullOrWhiteSpace(from))
            return Results.BadRequest(new { error = "missing-from" });
        if (string.IsNullOrWhiteSpace(to))
            return Results.BadRequest(new { error = "missing-to" });

        if (!DateTimeOffset.TryParse(from,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var fromUtc))
        {
            return Results.BadRequest(new { error = "invalid-from", from });
        }
        if (!DateTimeOffset.TryParse(to,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var toUtc))
        {
            return Results.BadRequest(new { error = "invalid-to", to });
        }

        if (fromUtc >= toUtc)
            return Results.BadRequest(new { error = "from-must-precede-to" });

        var now = DateTimeOffset.UtcNow;
        // Allow a small (1h) buffer for clock skew but reject clearly-future
        // `to` values; they imply a client bug or a probing request.
        if (toUtc > now.AddHours(1))
            return Results.BadRequest(new { error = "to-is-in-the-future" });

        var window = toUtc - fromUtc;
        if (window.TotalDays > MaxWindowDays)
        {
            return Results.BadRequest(new
            {
                error = "window-too-large",
                maxDays = MaxWindowDays,
                requestedDays = Math.Round(window.TotalDays, 1),
            });
        }

        // Tenant-scope: ADMIN pinned to own institute; SUPER_ADMIN any.
        if (!IsTenantAllowed(http, cohort))
        {
            logger.LogWarning(
                "[prr-112] per-cohort refused cross-tenant: requested={Cohort}",
                cohort);
            return Results.Forbid();
        }

        var rollup = await rollupService
            .GetCohortRollupAsync(cohort, fromUtc, toUtc, ct)
            .ConfigureAwait(false);

        logger.LogInformation(
            "[prr-112] per-cohort rollup: cohort={Cohort} from={From} to={To} "
            + "features={FeatureCount} total_usd={Total}",
            cohort,
            fromUtc.ToString("O", CultureInfo.InvariantCulture),
            toUtc.ToString("O", CultureInfo.InvariantCulture),
            rollup.FeatureSlices.Count,
            rollup.TotalCostUsd);

        return Results.Ok(rollup);
    }

    internal static bool IsTenantAllowed(HttpContext http, string cohort)
    {
        // SUPER_ADMIN sees everything.
        if (http.User.IsInRole("SUPER_ADMIN"))
            return true;

        // ADMIN must match institute_id to the cohort (Phase 1B: cohort = institute_id).
        var allowed = TenantScope.GetInstituteFilter(http.User, defaultInstituteId: null);
        if (allowed.Count == 0) return false;
        return allowed.Any(a => string.Equals(a, cohort, StringComparison.Ordinal));
    }
}
