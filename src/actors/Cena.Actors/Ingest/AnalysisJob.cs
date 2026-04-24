// =============================================================================
// Cena Platform -- Analysis Job Infrastructure
// Job-based stagnation analysis: submit → queue → actor processes → poll result.
// Rate-limited per user, dedup identical requests, results cached.
// =============================================================================

namespace Cena.Actors.Ingest;

public enum AnalysisJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public enum AnalysisJobType
{
    StagnationInsights,
    StagnationTimeline
}

/// <summary>
/// Marten document tracking an analysis job through its lifecycle.
/// </summary>
public sealed class AnalysisJobDocument
{
    public string Id { get; set; } = "";                    // job-{guid}
    public AnalysisJobType JobType { get; set; }
    public AnalysisJobStatus Status { get; set; } = AnalysisJobStatus.Queued;

    // Request parameters
    public string StudentId { get; set; } = "";
    public string ConceptId { get; set; } = "";
    public string RequestedBy { get; set; } = "";           // Firebase UID of the admin

    // Dedup key: same (type, studentId, conceptId) within TTL = reuse existing job
    public string DedupKey { get; set; } = "";

    // Result (JSON-serialized, populated on completion)
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }

    // Timing
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ProcessingMs { get; set; }

    /// <summary>Builds a dedup key for identical requests.</summary>
    public static string BuildDedupKey(AnalysisJobType type, string studentId, string conceptId)
        => $"{type}:{studentId}:{conceptId}";
}
