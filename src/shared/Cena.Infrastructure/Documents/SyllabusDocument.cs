// =============================================================================
// Cena Platform — Syllabus + Chapter documents (RDY-061 Phase 1)
//
// The syllabus is a projection layer over the concept / learning-objective
// prereq graph. It groups LOs into named, ordered chapters and maintains a
// chapter-level prereq DAG — matching how Bagrut / Ministry / Yoel-Geva
// actually organise content, and how teachers + students think about
// progress.
//
// Syllabus DEFINITION lives in YAML (config/syllabi/<track>.yaml),
// authored by curriculum experts (Amjad). A DbAdmin tool ingests the
// manifest into these documents. Re-ingestion is idempotent — the
// document identity is deterministic from the manifest, so re-runs patch
// in place rather than duplicating rows.
//
// Syllabus DEFINITION (this file) vs student ADVANCEMENT (separate
// event-sourced aggregate — RDY-061 Phase 2) — the split is intentional
// per Dina's review lens: definition changes (manifest edits) must never
// mutate student history, and student advancement must not be couched
// inside the definition's lifecycle.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Bagrut math/physics unit-level track, replacing the string soup
/// previously stashed in <see cref="QuestionState"/>.Grade
/// (<c>"3 Units"</c>, <c>"4 Units"</c>, <c>"5 Units"</c>).
/// </summary>
public enum BagrutTrack
{
    /// <summary>Not applicable (non-Bagrut content, e.g. SAT).</summary>
    None = 0,
    ThreeUnit = 3,
    FourUnit = 4,
    FiveUnit = 5,
}

/// <summary>
/// Per-track syllabus: an ordered, chapter-grouped view of the track's
/// learning objectives. One per <see cref="CurriculumTrackDocument"/>.
/// </summary>
/// <remarks>
/// Identity: <c>syllabus-{trackSlug}</c>. Re-ingesting the same manifest
/// replaces chapter membership in place.
/// </remarks>
public sealed class SyllabusDocument
{
    /// <summary>Marten identity. Format: <c>syllabus-{trackSlug}</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Foreign key to <see cref="CurriculumTrackDocument.TrackId"/>.</summary>
    public string TrackId { get; set; } = string.Empty;

    /// <summary>
    /// Manifest semver, bumped on each authored revision. Allows the
    /// advancement projection to detect when a student's advancement was
    /// produced against a stale syllabus version.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Ordered chapter ids. Authoritative ordering; the
    /// <see cref="ChapterDocument.Order"/> field is a denormalised copy
    /// for convenience.
    /// </summary>
    public List<string> ChapterIds { get; set; } = new();

    /// <summary>
    /// Bagrut unit-level track (3U/4U/5U). Physics tracks carry 5U only
    /// in the current scope. Non-Bagrut tracks use None.
    /// </summary>
    public BagrutTrack Track { get; set; } = BagrutTrack.None;

    /// <summary>
    /// Total expected weeks across all chapters — sum of
    /// <see cref="ChapterDocument.ExpectedWeeks"/>. Denormalised for
    /// pacing-guide lookups.
    /// </summary>
    public int TotalExpectedWeeks { get; set; }

    /// <summary>
    /// Ministry / textbook reference codes this syllabus aligns to
    /// (e.g. <c>"806"</c>, <c>"807"</c>). Multiple codes allowed when a
    /// track prepares for several exam codes.
    /// </summary>
    public List<string> MinistryCodes { get; set; } = new();

    /// <summary>
    /// Path of the YAML manifest this syllabus was ingested from, so a
    /// re-ingest can verify the source of truth and so audit logs can
    /// point operators at the authored file.
    /// </summary>
    public string? SourceManifestPath { get; set; }

    /// <summary>Author identity on last ingest.</summary>
    public string IngestedBy { get; set; } = "system";

    /// <summary>Last ingest wall-clock.</summary>
    public DateTimeOffset IngestedAt { get; set; }
}

/// <summary>
/// A chapter groups learning objectives into a pedagogically meaningful
/// unit aligned with Ministry / textbook chapter structure.
/// </summary>
/// <remarks>
/// Identity: <c>chapter-{syllabusId}-{order:D2}-{slug}</c>. Re-ingesting
/// a manifest with the same structure replaces the chapter in place.
/// Changing order / slug produces a new identity; the old row is
/// orphaned unless the ingest tool's <c>--prune</c> flag is set.
/// </remarks>
public sealed class ChapterDocument
{
    /// <summary>Marten identity — see class-level remarks.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning syllabus id.</summary>
    public string SyllabusId { get; set; } = string.Empty;

    /// <summary>Chapter sequence, 1-indexed.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Human-readable title per locale. Minimum set: en. he + ar
    /// required before the chapter is considered student-visible
    /// (enforced by the manifest validator, not at the document layer).
    /// </summary>
    public Dictionary<string, string> TitleByLocale { get; set; } = new();

    /// <summary>
    /// Learning objective ids covered by this chapter. Empty chapter is
    /// a validation error at ingest time.
    /// </summary>
    public List<string> LearningObjectiveIds { get; set; } = new();

    /// <summary>
    /// Chapter-level prereq DAG. A chapter is unlocked when all of
    /// <see cref="PrerequisiteChapterIds"/> reach Mastered status on
    /// the student advancement aggregate.
    /// </summary>
    public List<string> PrerequisiteChapterIds { get; set; } = new();

    /// <summary>
    /// Expected weeks to cover under the Ministry pacing guide. Used by
    /// the teacher dashboard's expected-vs-actual pacing delta column.
    /// Never surfaced to students — dark-pattern ban (shipgate).
    /// </summary>
    public int ExpectedWeeks { get; set; }

    /// <summary>
    /// Ministry / textbook reference code (e.g. <c>"806.2"</c>). Empty
    /// for non-Ministry-aligned chapters.
    /// </summary>
    public string? MinistryCode { get; set; }

    /// <summary>
    /// Stable machine slug (kebab-case). Part of the document identity
    /// to keep ids human-readable. Example: <c>"derivatives-of-trig"</c>.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Ingest-time metadata mirrored from the syllabus.</summary>
    public DateTimeOffset IngestedAt { get; set; }
}
