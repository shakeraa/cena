// =============================================================================
// Cena Platform — Curriculum Track Document (TENANCY-P1a + DATA-READY-001)
// Marten document for curriculum track definitions.
// DATA-READY-001: Added ContentReadiness lifecycle + quality gate fields.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Content readiness lifecycle for a curriculum track (DATA-READY-001).
/// Only <see cref="Ready"/> tracks are enrollable by students.
/// Admins can see all statuses; students only see Ready tracks.
/// </summary>
public enum CurriculumTrackStatus
{
    /// <summary>Initial authoring — not visible to students.</summary>
    Draft = 0,

    /// <summary>Content being seeded/imported — not enrollable yet.</summary>
    Seeding = 1,

    /// <summary>Under editorial/QA review before go-live.</summary>
    InReview = 2,

    /// <summary>Passed quality gate — enrollable by students.</summary>
    Ready = 3,

    /// <summary>Retired — read-only for existing enrollments, hidden from new enrollment.</summary>
    Archived = 4
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

    // ── DATA-READY-001: Quality gate fields ──

    /// <summary>
    /// Number of questions with IRT parameters available for this track.
    /// Updated by the question ingestion pipeline. Track must have >= 50
    /// before it can transition to Ready.
    /// </summary>
    public int QuestionsWithIrtCount { get; set; }

    /// <summary>
    /// Minimum questions with IRT parameters required to move to Ready status.
    /// Default 50 per the quality gate specification.
    /// </summary>
    public int MinQuestionsForReady { get; set; } = 50;

    /// <summary>
    /// Whether this track passes the quality gate (QuestionsWithIrtCount >= MinQuestionsForReady).
    /// </summary>
    public bool PassesQualityGate => QuestionsWithIrtCount >= MinQuestionsForReady;

    /// <summary>
    /// When the readiness status was last changed. Null for tracks that haven't changed since creation.
    /// </summary>
    public DateTimeOffset? ReadinessChangedAt { get; set; }

    /// <summary>
    /// Who changed the readiness status last (admin user ID).
    /// </summary>
    public string? ReadinessChangedBy { get; set; }
}
