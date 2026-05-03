// =============================================================================
// Cena Platform -- Cultural Context Seeder (ADM-012)
// Idempotent startup seeder for the CulturalContext* documents.
// Populates a baseline set of rollups + recommendations for the
// default "dev-school" tenant so the admin dashboard has data to render
// on first startup. Safe to run on every boot.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public sealed class CulturalContextSeeder : IHostedService
{
    private const string DefaultSchoolId = "dev-school";

    private readonly IDocumentStore _store;
    private readonly ILogger<CulturalContextSeeder> _logger;

    public CulturalContextSeeder(IDocumentStore store, ILogger<CulturalContextSeeder> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await using var session = _store.LightweightSession();

        var existingCount = await session.Query<CulturalContextGroupDocument>()
            .Where(g => g.SchoolId == DefaultSchoolId)
            .CountAsync(ct);

        if (existingCount > 0)
        {
            _logger.LogInformation(
                "CulturalContextSeeder: {Count} group documents already present for '{SchoolId}', skipping seed.",
                existingCount, DefaultSchoolId);
            return;
        }

        _logger.LogInformation("CulturalContextSeeder: seeding baseline cultural context data for '{SchoolId}'...", DefaultSchoolId);

        // ── Group rollups: 4 cultural contexts ──────────────────────────────
        var groups = new[]
        {
            new CulturalContextGroupDocument
            {
                Id = $"{DefaultSchoolId}:HebrewDominant",
                SchoolId = DefaultSchoolId,
                Context = "HebrewDominant",
                StudentCount = 450,
                AvgResilienceScore = 0.72f,
                P25 = 0.55f, P50 = 0.72f, P75 = 0.85f, P95 = 0.95f,
                AvgSessionMinutes = 32f,
                AvgFocusScore = 73f,
                MicrobreakAcceptance = 0.72f,
                PeakFocusTime = "10:00-12:00",
            },
            new CulturalContextGroupDocument
            {
                Id = $"{DefaultSchoolId}:ArabicDominant",
                SchoolId = DefaultSchoolId,
                Context = "ArabicDominant",
                StudentCount = 280,
                AvgResilienceScore = 0.68f,
                P25 = 0.50f, P50 = 0.68f, P75 = 0.82f, P95 = 0.92f,
                AvgSessionMinutes = 35f,
                AvgFocusScore = 70f,
                MicrobreakAcceptance = 0.68f,
                PeakFocusTime = "09:00-11:00",
            },
            new CulturalContextGroupDocument
            {
                Id = $"{DefaultSchoolId}:Bilingual",
                SchoolId = DefaultSchoolId,
                Context = "Bilingual",
                StudentCount = 120,
                AvgResilienceScore = 0.75f,
                P25 = 0.60f, P50 = 0.75f, P75 = 0.88f, P95 = 0.96f,
                AvgSessionMinutes = 30f,
                AvgFocusScore = 76f,
                MicrobreakAcceptance = 0.75f,
                PeakFocusTime = "10:00-12:00",
            },
            new CulturalContextGroupDocument
            {
                Id = $"{DefaultSchoolId}:Unknown",
                SchoolId = DefaultSchoolId,
                Context = "Unknown",
                StudentCount = 50,
                AvgResilienceScore = 0.65f,
                P25 = 0.45f, P50 = 0.65f, P75 = 0.78f, P95 = 0.88f,
                AvgSessionMinutes = 28f,
                AvgFocusScore = 68f,
                MicrobreakAcceptance = 0.65f,
                PeakFocusTime = "14:00-16:00",
            },
        };

        foreach (var g in groups)
            session.Store(g);

        // ── Methodology × culture effectiveness: 4 × 3 = 12 rows ────────────
        var methodologies = new[] { "Socratic", "WorkedExample", "Feynman", "RetrievalPractice" };
        var contexts = new[] { "HebrewDominant", "ArabicDominant", "Bilingual" };
        var baseSuccess = new Dictionary<string, float>
        {
            ["HebrewDominant"] = 0.80f,
            ["ArabicDominant"] = 0.75f,
            ["Bilingual"] = 0.82f,
        };
        var sampleByContext = new Dictionary<string, int>
        {
            ["HebrewDominant"] = 300,
            ["ArabicDominant"] = 220,
            ["Bilingual"] = 110,
        };

        foreach (var method in methodologies)
        {
            foreach (var ctx in contexts)
            {
                session.Store(new MethodologyEffectivenessByCultureDocument
                {
                    Id = $"{DefaultSchoolId}:{method}:{ctx}",
                    SchoolId = DefaultSchoolId,
                    Methodology = method,
                    CulturalContext = ctx,
                    SuccessRate = baseSuccess[ctx],
                    SampleSize = sampleByContext[ctx],
                });
            }
        }

        // ── Equity alerts: two seeded alerts to render the dashboard ────────
        session.Store(new EquityAlertDocument
        {
            Id = "alert-seeded-mastery-gap",
            SchoolId = DefaultSchoolId,
            Severity = "warning",
            Type = "mastery_gap",
            Description = "ArabicDominant students showing 8% lower mastery in Physics",
            CulturalContext = "ArabicDominant",
            DeviationPercent = 8f,
            DetectedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        session.Store(new EquityAlertDocument
        {
            Id = "alert-seeded-content-imbalance",
            SchoolId = DefaultSchoolId,
            Severity = "info",
            Type = "content_imbalance",
            Description = "Arabic physics content is 30% fewer than Hebrew",
            CulturalContext = "ArabicDominant",
            DeviationPercent = 30f,
            DetectedAt = DateTimeOffset.UtcNow.AddDays(-5),
        });

        // ── Content balance recommendations ─────────────────────────────────
        var recommendations = new[]
        {
            new ContentBalanceRecommendationDocument
            {
                Id = $"{DefaultSchoolId}:Arabic:Physics",
                SchoolId = DefaultSchoolId,
                Language = "Arabic",
                Subject = "Physics",
                CurrentCount = 120,
                RecommendedCount = 170,
                GapDescription = "30% fewer Arabic physics questions than Hebrew",
            },
            new ContentBalanceRecommendationDocument
            {
                Id = $"{DefaultSchoolId}:Arabic:Calculus",
                SchoolId = DefaultSchoolId,
                Language = "Arabic",
                Subject = "Calculus",
                CurrentCount = 85,
                RecommendedCount = 100,
                GapDescription = "15% fewer Arabic calculus questions",
            },
            new ContentBalanceRecommendationDocument
            {
                Id = $"{DefaultSchoolId}:Hebrew:CulturalContext",
                SchoolId = DefaultSchoolId,
                Language = "Hebrew",
                Subject = "Cultural Context",
                CurrentCount = 45,
                RecommendedCount = 60,
                GapDescription = "Add more diverse cultural examples",
            },
        };

        foreach (var r in recommendations)
            session.Store(r);

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CulturalContextSeeder: seeded {Groups} groups, {Methods} methodology rows, {Alerts} alerts, {Recs} recommendations.",
            groups.Length, methodologies.Length * contexts.Length, 2, recommendations.Length);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
