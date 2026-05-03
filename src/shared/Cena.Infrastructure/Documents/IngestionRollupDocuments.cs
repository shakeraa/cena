// =============================================================================
// Cena Platform -- Ingestion Rollup Documents (ADM-015)
// Daily ingestion pipeline metrics rollup that powers the ingestion
// dashboard. PipelineItemDocument holds the raw job state; these docs
// are precomputed aggregates so admin queries don't scan the full table.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Daily rollup of ingestion pipeline metrics per school.
/// One row per (schoolId, yyyy-MM-dd).
/// </summary>
public class IngestionMetricsRollupDocument
{
    public string Id { get; set; } = "";              // "{schoolId}:{yyyy-MM-dd}"
    public string SchoolId { get; set; } = "";
    public DateTimeOffset Date { get; set; }

    public int TotalJobs { get; set; }
    public int SucceededJobs { get; set; }
    public int FailedJobs { get; set; }
    public int InProgressJobs { get; set; }
    public int QuestionsExtracted { get; set; }

    // Per-stage counts (populated from PipelineItemDocument.CurrentStage)
    public List<IngestionStageCount> StageCounts { get; set; } = new();

    // Per-stage failure rates
    public List<IngestionStageFailureRate> StageFailureRates { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class IngestionStageCount
{
    public string Stage { get; set; } = "";
    public int Count { get; set; }
}

public class IngestionStageFailureRate
{
    public string Stage { get; set; } = "";
    public int Total { get; set; }
    public int Failed { get; set; }
    public float Rate { get; set; }
}
