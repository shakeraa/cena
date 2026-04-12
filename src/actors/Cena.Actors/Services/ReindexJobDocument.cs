// =============================================================================
// Cena Platform -- Reindex Job Document
// Tracks embedding reindex jobs initiated via Admin API.
// =============================================================================

using Marten.Schema;

namespace Cena.Actors.Services;

public enum ReindexJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class ReindexJobDocument
{
    [Identity]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Scope { get; set; } = "all";           // all, subject, concept
    public string? Filter { get; set; }                  // subject name or concept id
    public int EstimatedBlocks { get; set; }
    public int ProcessedBlocks { get; set; }
    public int FailedBlocks { get; set; }

    public ReindexJobStatus Status { get; set; } = ReindexJobStatus.Pending;
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string RequestedBy { get; set; } = "";

    public double ProgressPercentage => EstimatedBlocks > 0
        ? Math.Min(100.0, (ProcessedBlocks * 100.0) / EstimatedBlocks)
        : 0;

    public bool IsComplete => Status is ReindexJobStatus.Completed or ReindexJobStatus.Failed or ReindexJobStatus.Cancelled;
}

// NATS command payload for reindex requests
public sealed record ReindexCommand(
    string JobId,
    string Scope,
    string? Filter,
    int EstimatedBlocks,
    string RequestedBy,
    DateTimeOffset RequestedAt);

// NATS event payload for reindex progress updates
public sealed record ReindexProgressEvent(
    string JobId,
    int ProcessedBlocks,
    int FailedBlocks,
    int TotalBlocks,
    ReindexJobStatus Status,
    string? ErrorMessage = null);
