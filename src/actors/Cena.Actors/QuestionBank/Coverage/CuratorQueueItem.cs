// =============================================================================
// Cena Platform — Curator Queue Item (prr-201, stage 3)
//
// The record the orchestrator enqueues when stages 1 and 2 cannot fill a
// rung. Carries the cell address, the gap count, the deadline (computed
// from release date), and a compact summary of prior-stage drops so the
// human curator can understand what the deterministic + LLM paths already
// tried.
//
// ADR-0043: when the cell is tied to a Bagrut reference, the Ministry
// source identifier is included for the curator's reference material
// pane. It is a reference, not a source-of-text — the curator authors a
// brand-new item, never a reworded Ministry string.
// =============================================================================

namespace Cena.Actors.QuestionBank.Coverage;

public sealed record CuratorDropSummary(
    WaterfallStage Stage,
    WaterfallDropKind Kind,
    string? Detail);

public sealed record CuratorQueueItem
{
    /// <summary>
    /// Deterministic id. Re-enqueueing the same cell (unchanged releaseDate)
    /// is a no-op.
    /// </summary>
    public required string Id { get; init; }

    public required CoverageCell Cell { get; init; }
    public required int Gap { get; init; }
    public required DateTimeOffset Deadline { get; init; }
    public required IReadOnlyList<CuratorDropSummary> PriorDrops { get; init; }

    /// <summary>
    /// Opaque string identifying the nearest Ministry reference item, when
    /// the curator should use that as pedagogical inspiration (ADR-0043 —
    /// reference-only, never source-text).
    /// </summary>
    public string? MinistryReferenceId { get; init; }

    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}

public interface ICuratorQueueEmitter
{
    /// <summary>
    /// Enqueue the task if no prior task with the same Id already exists.
    /// Returns the task id (new or existing). Idempotent.
    /// </summary>
    Task<string> EnqueueAsync(CuratorQueueItem item, CancellationToken ct = default);
}
