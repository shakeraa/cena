// =============================================================================
// PRR-322 — Mock-exam run cost telemetry admin endpoints
//
// Three GETs under /api/admin/mock-exam-runs/cost:
//
//   GET /runs[?from=&to=&examCode=&limit=]    — recent per-run rows
//   GET /daily[?days=N]                       — daily rollup over last N days (default 30)
//   GET /projection                            — 30-day forward projection at current rates
//
// Auth: ModeratorOrAbove. Rate-limited under "api". The doc is keyed on
// runId + indexed on ExamCode + ComputedAt (PRR-322 Marten registration).
// =============================================================================

using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Assessment;

public static class MockExamRunCostEndpoints
{
    private const int DefaultDailyDays    = 30;
    private const int MaxDailyDays        = 365;
    private const int DefaultRunListLimit = 100;
    private const int MaxRunListLimit     = 500;

    public static IEndpointRouteBuilder MapMockExamRunCostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/mock-exam-runs/cost")
            .WithTags("Mock-exam Cost Telemetry")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // GET /runs?from=&to=&examCode=&limit=
        group.MapGet("/runs", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? examCode,
            int? limit,
            IDocumentStore store,
            CancellationToken ct) =>
        {
            var safeLimit = Math.Clamp(limit ?? DefaultRunListLimit, 1, MaxRunListLimit);

            await using var session = store.QuerySession();
            var query = session.Query<MockExamRunCost>().AsQueryable();
            if (from is not null)     query = query.Where(c => c.ComputedAt >= from);
            if (to   is not null)     query = query.Where(c => c.ComputedAt <= to);
            if (!string.IsNullOrWhiteSpace(examCode))
                query = query.Where(c => c.ExamCode == examCode);

            var rows = await query
                .OrderByDescending(c => c.ComputedAt)
                .Take(safeLimit)
                .ToListAsync(ct);

            return Results.Ok(new MockExamRunCostListResponse(
                Total: rows.Count,
                Limit: safeLimit,
                Items: rows.Select(ToDto).ToList()));
        })
        .WithName("ListMockExamRunCosts")
        .Produces<MockExamRunCostListResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        // GET /daily?days=N
        group.MapGet("/daily", async (
            int? days,
            IDocumentStore store,
            CancellationToken ct) =>
        {
            var safeDays = Math.Clamp(days ?? DefaultDailyDays, 1, MaxDailyDays);
            var since = DateTimeOffset.UtcNow.Date.AddDays(-(safeDays - 1));

            await using var session = store.QuerySession();
            var rows = await session.Query<MockExamRunCost>()
                .Where(c => c.ComputedAt >= since)
                .ToListAsync(ct);

            // Group server-side after the date-bound query — Marten LINQ
            // can't easily project a date-only key under JSONB, so we
            // materialize then bucket. Volume is bounded by safeDays *
            // run-rate (~200/day in the worst pilot case = ~6000 rows
            // over 30 days, fine for in-memory aggregation).
            var rollup = rows
                .GroupBy(c => c.ComputedAt.UtcDateTime.Date)
                .OrderBy(g => g.Key)
                .Select(g => new MockExamRunCostDailyPoint(
                    Date:            g.Key,
                    RunCount:        g.Count(),
                    CasCallsCount:   g.Sum(c => c.CasCallsCount),
                    LlmTokensInput:  g.Sum(c => c.LlmTokensInput),
                    LlmTokensOutput: g.Sum(c => c.LlmTokensOutput),
                    OcrCallsCount:   g.Sum(c => c.OcrCallsCount),
                    CasCostUsd:      g.Sum(c => c.CasCostUsd),
                    LlmCostUsd:      g.Sum(c => c.LlmCostUsd),
                    OcrCostUsd:      g.Sum(c => c.OcrCostUsd),
                    TotalUsd:        g.Sum(c => c.TotalUsd)))
                .ToList();

            return Results.Ok(new MockExamRunCostDailyResponse(
                Days:   safeDays,
                Since:  since,
                Points: rollup));
        })
        .WithName("DailyMockExamRunCostRollup")
        .Produces<MockExamRunCostDailyResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        // GET /projection — forward-look at current rates × current run-rate.
        group.MapGet("/projection", async (
            IDocumentStore store,
            CancellationToken ct) =>
        {
            // Project from the trailing 14 days of submissions; smaller
            // windows are too noisy on a low-volume product. If <1 row
            // in 14d, projection is "insufficient data" — honest, not
            // a fake zero.
            var window = TimeSpan.FromDays(14);
            var since = DateTimeOffset.UtcNow - window;

            await using var session = store.QuerySession();
            var rows = await session.Query<MockExamRunCost>()
                .Where(c => c.ComputedAt >= since)
                .ToListAsync(ct);

            if (rows.Count == 0)
            {
                return Results.Ok(new MockExamRunCostProjectionResponse(
                    WindowDays:        14,
                    RunsInWindow:      0,
                    AvgUsdPerRun:      null,
                    Projected30DayUsd: null,
                    SufficientData:    false));
            }

            var avgPerRun = rows.Average(c => (double)c.TotalUsd);
            var runsPerDay = rows.Count / 14.0;
            var projected30 = (decimal)Math.Round(avgPerRun * runsPerDay * 30.0, 4);

            return Results.Ok(new MockExamRunCostProjectionResponse(
                WindowDays:        14,
                RunsInWindow:      rows.Count,
                AvgUsdPerRun:      Math.Round((decimal)avgPerRun, 6),
                Projected30DayUsd: projected30,
                SufficientData:    true));
        })
        .WithName("MockExamRunCostProjection")
        .Produces<MockExamRunCostProjectionResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static MockExamRunCostDto ToDto(MockExamRunCost c) =>
        new(
            RunId:           c.Id,
            StudentId:       c.StudentId,
            ExamCode:        c.ExamCode,
            StudentTenant:   c.StudentTenant,
            CasCallsCount:   c.CasCallsCount,
            LlmTokensInput:  c.LlmTokensInput,
            LlmTokensOutput: c.LlmTokensOutput,
            OcrCallsCount:   c.OcrCallsCount,
            CasCostUsd:      c.CasCostUsd,
            LlmCostUsd:      c.LlmCostUsd,
            OcrCostUsd:      c.OcrCostUsd,
            TotalUsd:        c.TotalUsd,
            ComputedAt:      c.ComputedAt);
}

// ── Wire DTOs (admin-API-internal; no separate Cena.Api.Contracts entry
//    yet — promote when an external consumer appears) ──

public sealed record MockExamRunCostDto(
    string RunId,
    string StudentId,
    string ExamCode,
    string? StudentTenant,
    int CasCallsCount,
    int LlmTokensInput,
    int LlmTokensOutput,
    int OcrCallsCount,
    decimal CasCostUsd,
    decimal LlmCostUsd,
    decimal OcrCostUsd,
    decimal TotalUsd,
    DateTimeOffset ComputedAt);

public sealed record MockExamRunCostListResponse(
    int Total,
    int Limit,
    IReadOnlyList<MockExamRunCostDto> Items);

public sealed record MockExamRunCostDailyPoint(
    DateTime Date,
    int RunCount,
    int CasCallsCount,
    int LlmTokensInput,
    int LlmTokensOutput,
    int OcrCallsCount,
    decimal CasCostUsd,
    decimal LlmCostUsd,
    decimal OcrCostUsd,
    decimal TotalUsd);

public sealed record MockExamRunCostDailyResponse(
    int Days,
    DateTimeOffset Since,
    IReadOnlyList<MockExamRunCostDailyPoint> Points);

public sealed record MockExamRunCostProjectionResponse(
    int WindowDays,
    int RunsInWindow,
    decimal? AvgUsdPerRun,
    decimal? Projected30DayUsd,
    bool SufficientData);
