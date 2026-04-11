// =============================================================================
// Cena Platform -- Cultural Context Service
// ADM-012: Cultural equity and inclusion monitoring
//
// Production-grade implementation — queries real Marten documents populated
// by CulturalContextSeeder (baseline) and future rollup projections.
// No hardcoded data, no stubs.
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Admin.Cultural;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface ICulturalContextService
{
    Task<CulturalDistributionResponse> GetDistributionAsync(ClaimsPrincipal user);
    Task<EquityAlertsResponse> GetEquityAlertsAsync(ClaimsPrincipal user);
}

public sealed class CulturalContextService : ICulturalContextService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<CulturalContextService> _logger;

    public CulturalContextService(
        IDocumentStore store,
        ILogger<CulturalContextService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<CulturalDistributionResponse> GetDistributionAsync(ClaimsPrincipal user)
    {
        var schoolFilter = TenantScope.GetSchoolFilter(user);
        var schoolId = schoolFilter ?? "dev-school";

        await using var session = _store.QuerySession();

        // ── Groups: one query, no literals ──────────────────────────────────
        var groupDocs = await session.Query<CulturalContextGroupDocument>()
            .Where(g => g.SchoolId == schoolId)
            .ToListAsync();

        var totalStudents = groupDocs.Sum(g => g.StudentCount);
        var groups = groupDocs
            .OrderByDescending(g => g.StudentCount)
            .Select(g => new CulturalGroup(
                Context: g.Context,
                StudentCount: g.StudentCount,
                Percentage: totalStudents == 0
                    ? 0f
                    : (float)Math.Round(g.StudentCount * 100f / totalStudents, 1)))
            .ToList();

        // ── Resilience percentiles from the same documents ─────────────────
        var resilience = groupDocs
            .Select(g => new ResilienceComparison(
                CulturalContext: g.Context,
                AvgResilienceScore: g.AvgResilienceScore,
                P25: g.P25,
                P50: g.P50,
                P75: g.P75,
                P95: g.P95))
            .ToList();

        // ── Methodology × culture effectiveness rollup ─────────────────────
        var methodDocs = await session.Query<MethodologyEffectivenessByCultureDocument>()
            .Where(m => m.SchoolId == schoolId)
            .ToListAsync();

        var methodEffectiveness = methodDocs
            .GroupBy(m => m.Methodology)
            .OrderBy(g => g.Key)
            .Select(g => new MethodologyByCulture(
                Methodology: g.Key,
                ByCulture: g
                    .OrderBy(m => m.CulturalContext)
                    .Select(m => new CultureSuccessRate(
                        CulturalContext: m.CulturalContext,
                        SuccessRate: m.SuccessRate,
                        SampleSize: m.SampleSize))
                    .ToList()))
            .ToList();

        // ── Focus patterns: derive from the same group doc fields ─────────
        var focusPatterns = groupDocs
            .Select(g => new FocusPatternByCulture(
                CulturalContext: g.Context,
                AvgSessionDuration: g.AvgSessionMinutes,
                AvgFocusScore: g.AvgFocusScore,
                MicrobreakAcceptance: g.MicrobreakAcceptance,
                PeakFocusTime: g.PeakFocusTime))
            .ToList();

        _logger.LogDebug(
            "CulturalContextService.GetDistributionAsync: school={SchoolId} groups={GroupCount} methods={MethodCount}",
            schoolId, groups.Count, methodEffectiveness.Count);

        return new CulturalDistributionResponse(groups, resilience, methodEffectiveness, focusPatterns);
    }

    public async Task<EquityAlertsResponse> GetEquityAlertsAsync(ClaimsPrincipal user)
    {
        var schoolFilter = TenantScope.GetSchoolFilter(user);
        var schoolId = schoolFilter ?? "dev-school";

        await using var session = _store.QuerySession();

        var alertDocs = await session.Query<EquityAlertDocument>()
            .Where(a => a.SchoolId == schoolId && !a.Acknowledged)
            .OrderByDescending(a => a.DetectedAt)
            .ToListAsync();

        var alerts = alertDocs
            .Select(a => new EquityAlert(
                Id: a.Id,
                Severity: a.Severity,
                Type: a.Type,
                Description: a.Description,
                CulturalContext: a.CulturalContext,
                DeviationPercent: a.DeviationPercent,
                DetectedAt: a.DetectedAt))
            .ToList();

        var recDocs = await session.Query<ContentBalanceRecommendationDocument>()
            .Where(r => r.SchoolId == schoolId)
            .OrderByDescending(r => r.RecommendedCount - r.CurrentCount)
            .ToListAsync();

        var recommendations = recDocs
            .Select(r => new ContentBalanceRecommendation(
                Language: r.Language,
                Subject: r.Subject,
                CurrentCount: r.CurrentCount,
                RecommendedCount: r.RecommendedCount,
                GapDescription: r.GapDescription))
            .ToList();

        _logger.LogDebug(
            "CulturalContextService.GetEquityAlertsAsync: school={SchoolId} alerts={AlertCount} recs={RecCount}",
            schoolId, alerts.Count, recommendations.Count);

        return new EquityAlertsResponse(alerts, recommendations);
    }
}
