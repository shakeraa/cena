// =============================================================================
// Cena Platform -- Ingestion rollup document tests (ADM-015 hardening)
// Exercises shape + invariants of IngestionMetricsRollupDocument, which
// replaces the stub/Random analytics in IngestionPipelineService stats.
// =============================================================================

using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.Admin;

public sealed class IngestionRollupTests
{
    [Fact]
    public void IngestionMetricsRollup_DefaultsAreSafeForAdminDashboards()
    {
        var doc = new IngestionMetricsRollupDocument();
        Assert.Empty(doc.Id);
        Assert.NotNull(doc.StageCounts);
        Assert.NotNull(doc.StageFailureRates);
        Assert.Equal(0, doc.TotalJobs);
    }

    [Fact]
    public void IngestionStageFailureRate_Invariant_RateMatchesFailedOverTotal()
    {
        var doc = new IngestionMetricsRollupDocument
        {
            Id = "dev-school:2026-04-10",
            SchoolId = "dev-school",
            Date = DateTimeOffset.UtcNow.Date,
            TotalJobs = 10,
            SucceededJobs = 7,
            FailedJobs = 2,
            InProgressJobs = 1,
            StageFailureRates = new List<IngestionStageFailureRate>
            {
                new() { Stage = "OcrProcessing", Total = 10, Failed = 2, Rate = 0.2f },
                new() { Stage = "Classified", Total = 8, Failed = 0, Rate = 0f },
            },
        };

        // Totals line up
        Assert.Equal(10, doc.SucceededJobs + doc.FailedJobs + doc.InProgressJobs);
        // Every row's rate matches failed/total
        foreach (var row in doc.StageFailureRates)
        {
            var expected = row.Total > 0 ? (float)row.Failed / row.Total : 0f;
            Assert.Equal(expected, row.Rate, 3);
        }
    }

    [Fact]
    public void IngestionStageCount_PopulatesWithAllCoreStages()
    {
        var stages = new[]
        {
            "Incoming", "OcrProcessing", "Segmented", "Normalized",
            "Classified", "Deduplicated", "ReCreated", "InReview", "Published"
        };

        var doc = new IngestionMetricsRollupDocument
        {
            Id = "dev-school:2026-04-10",
            SchoolId = "dev-school",
            StageCounts = stages.Select(s => new IngestionStageCount { Stage = s, Count = 1 }).ToList(),
        };

        Assert.Equal(9, doc.StageCounts.Count);
        Assert.Contains(doc.StageCounts, s => s.Stage == "Incoming");
        Assert.Contains(doc.StageCounts, s => s.Stage == "Published");
        Assert.All(doc.StageCounts, s => Assert.Equal(1, s.Count));
    }

    [Fact]
    public void IngestionRollup_IdConvention_IsSchoolPlusDate()
    {
        var doc = new IngestionMetricsRollupDocument
        {
            Id = "dev-school:2026-04-10",
            SchoolId = "dev-school",
            Date = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.StartsWith(doc.SchoolId, doc.Id);
        Assert.Contains(doc.Date.ToString("yyyy-MM-dd"), doc.Id);
    }
}
