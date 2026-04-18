// =============================================================================
// Cena Platform — Stuck-Type Diagnostics Admin Endpoints (RDY-063 Phase 2a+)
//
// Admin-only read surface over StuckDiagnosisDocument. Two views:
//
//   GET /api/admin/stuck-diagnostics/distribution?days=7
//     → overall stuck-type histogram across the pilot
//
//   GET /api/admin/stuck-diagnostics/top-items?days=7&limit=20&stuckType=encoding
//     → items triggering the most stuck-type signals — curriculum-review
//       candidates (e.g. "which questions have the most encoding-stuck
//       signals over the last week?")
//
// Never returns anon ids, never returns student identifiers. The admin
// dashboard consumes this to surface item-quality signals without ever
// touching student-level records.
// =============================================================================

using Cena.Actors.Diagnosis;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Diagnostics;

public static class StuckDiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapStuckDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/stuck-diagnostics")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // GET /distribution?days=7
        group.MapGet("/distribution", async (
            int? days,
            IStuckDiagnosisRepository repo,
            CancellationToken ct) =>
        {
            var window = ClampDays(days);
            var dist = await repo.GetDistributionAsync(window, ct);
            var total = dist.Values.Sum();
            return Results.Ok(new StuckDistributionResponse(
                Days: window,
                Total: total,
                GeneratedAt: DateTimeOffset.UtcNow,
                Counts: dist
                    .OrderBy(kvp => (int)kvp.Key)
                    .Select(kvp => new StuckDistributionBucket(
                        StuckType: kvp.Key.ToString(),
                        Count: kvp.Value,
                        Fraction: total > 0 ? (float)kvp.Value / total : 0f))
                    .ToList()));
        })
        .WithName("GetStuckDistribution")
        .Produces<StuckDistributionResponse>(StatusCodes.Status200OK);

        // GET /top-items?days=7&limit=20&stuckType=encoding
        group.MapGet("/top-items", async (
            int? days,
            int? limit,
            string? stuckType,
            IStuckDiagnosisRepository repo,
            CancellationToken ct) =>
        {
            var window = ClampDays(days);
            var cap = Math.Clamp(limit ?? 20, 1, 200);

            StuckType? filter = null;
            if (!string.IsNullOrWhiteSpace(stuckType))
            {
                if (!Enum.TryParse<StuckType>(stuckType, ignoreCase: true, out var parsed))
                {
                    return Results.BadRequest(new CenaError(
                        ErrorCodes.CENA_INTERNAL_VALIDATION,
                        $"Unknown stuckType '{stuckType}'. Expected one of: " +
                        string.Join(", ", Enum.GetNames<StuckType>()),
                        ErrorCategory.Validation, null, null));
                }
                filter = parsed;
            }

            var items = await repo.GetTopItemsAsync(filter, window, cap, ct);
            return Results.Ok(new StuckTopItemsResponse(
                Days: window,
                Limit: cap,
                Filter: filter?.ToString(),
                GeneratedAt: DateTimeOffset.UtcNow,
                Items: items.Select(a => new StuckItemRow(
                    QuestionId: a.QuestionId,
                    StuckType: a.Primary.ToString(),
                    Count: a.Count,
                    DistinctStudents: a.DistinctStudentsCount,
                    AvgConfidence: a.AvgConfidence,
                    FirstSeenAt: a.FirstSeenAt,
                    LastSeenAt: a.LastSeenAt)).ToList()));
        })
        .WithName("GetStuckTopItems")
        .Produces<StuckTopItemsResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest);

        return app;
    }

    // Retention ceiling (30 days) + sensible default (7 days).
    private static int ClampDays(int? days) => Math.Clamp(days ?? 7, 1, 30);
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

public sealed record StuckDistributionResponse(
    int Days,
    int Total,
    DateTimeOffset GeneratedAt,
    List<StuckDistributionBucket> Counts);

public sealed record StuckDistributionBucket(
    string StuckType,
    int Count,
    float Fraction);

public sealed record StuckTopItemsResponse(
    int Days,
    int Limit,
    string? Filter,
    DateTimeOffset GeneratedAt,
    List<StuckItemRow> Items);

public sealed record StuckItemRow(
    string QuestionId,
    string StuckType,
    int Count,
    int DistinctStudents,
    float AvgConfidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
