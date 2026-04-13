// =============================================================================
// Cena Platform — Curriculum Track Document (TENANCY-P1a)
// Marten document for curriculum track definitions
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Content readiness status for a curriculum track.
/// Only <see cref="Ready"/> tracks are enrollable by students.
/// </summary>
public enum CurriculumTrackStatus
{
    Draft,
    Seeding,
    Ready
}

/// <summary>
/// Curriculum track document representing a structured learning pathway.
/// Tracks are bound directly to enrollments in Phase 1 (no Program indirection).
/// </summary>
public class CurriculumTrackDocument
{
    /// <summary>
    /// Stable Marten document identity. Format: <c>track-{slug}</c>.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Domain identity alias. Always equal to <see cref="Id"/>.
    /// </summary>
    public string TrackId { get; set; } = "";

    /// <summary>
    /// Human-readable curriculum-facing code.
    /// Example: <c>MATH-BAGRUT-5UNIT</c>.
    /// </summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// Student-facing title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Subject slug that scopes the track.
    /// </summary>
    public string Subject { get; set; } = "";

    /// <summary>
    /// Optional target exam or certification this track prepares for.
    /// </summary>
    public string? TargetExam { get; set; }

    /// <summary>
    /// Learning objective IDs covered by this track.
    /// </summary>
    public string[] LearningObjectiveIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// External standards alignment codes.
    /// </summary>
    public string[] StandardMappings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Content readiness status. Only <see cref="CurriculumTrackStatus.Ready"/> tracks are enrollable.
    /// </summary>
    public CurriculumTrackStatus Status { get; set; } = CurriculumTrackStatus.Draft;
}
