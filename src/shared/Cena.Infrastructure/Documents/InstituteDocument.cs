// =============================================================================
// Cena Platform — Institute Document (TENANCY-P1a, prr-030)
// Marten document for multi-institute tenancy.
//
// prr-030 adds institute-scoped pedagogy tuning:
//   - TargetAccuracy (nullable, Bjork-bounded [0.6, 0.9])
//
// WHY institute-scoped instead of global: different IL-Bagrut institutes
// run different cohorts (secular-state, religious-state, private-tutor,
// cram-school). A cram school preparing 5-unit maths students targets a
// tighter confidence window (push difficulty lower, ~0.70) while an NGO
// supporting struggling students runs at the anti-demoralisation floor
// (~0.80 so every session ends on a win). See docs/adr/0051-desirable-
// difficulty-institute-override.md for the full rationale.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Type of educational institute represented by this document.
/// </summary>
public enum InstituteType
{
    Platform,
    School,
    PrivateTutor,
    CramSchool,
    NGO
}

/// <summary>
/// Institute document for multi-institute tenancy.
/// Represents a school, tutoring center, or other educational organization.
/// </summary>
public class InstituteDocument
{
    /// <summary>
    /// Stable Marten document identity. Format: <c>institute-{slug}</c>.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Domain identity alias. Always equal to <see cref="Id"/>.
    /// </summary>
    public string InstituteId { get; set; } = "";

    /// <summary>
    /// Display name of the institute.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Classification of the institute.
    /// </summary>
    public InstituteType Type { get; set; } = InstituteType.School;

    /// <summary>
    /// ISO country code or free-form country name.
    /// </summary>
    public string Country { get; set; } = "";

    /// <summary>
    /// Primary mentor or owner associated with the institute.
    /// </summary>
    public string MentorId { get; set; } = "";

    /// <summary>
    /// Timestamp when the institute record was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// prr-030 per-institute cohort accuracy-target override. When null,
    /// <c>Cena.Actors.Mastery.DifficultyTarget.Default</c> (0.75) applies.
    /// When set, MUST sit in the Bjork-bounded range [0.6, 0.9] — the
    /// scheduler rejects out-of-range values at startup
    /// (ArgumentOutOfRangeException in DifficultyTarget) rather than
    /// silently clamping. Arch-test AccuracyTargetInBjorkRangeTest
    /// scans every seeded InstituteDocument and fails the build if any
    /// TargetAccuracy value drifts outside the range.
    /// </summary>
    public double? TargetAccuracy { get; set; }
}

/// <summary>
/// prr-030 institute-scoped pedagogy configuration. Projected from
/// <see cref="InstituteDocument"/> so the scheduler has a single typed
/// DTO for cohort-default overrides without coupling to the persistence
/// representation.
///
/// WHY a separate record instead of reading InstituteDocument directly:
/// the scheduler and ReadinessBucket selectors run on the hot path and
/// must not allocate an IDocumentSession per question — a cached
/// InstituteConfig lookup keyed by InstituteId is the seam for an
/// in-memory cache (not shipped here; left for a follow-up if the
/// scheduler ever shows up on a profile).
/// </summary>
public sealed record InstituteConfig(string InstituteId, double? TargetAccuracy)
{
    /// <summary>
    /// Default no-override config for institutes without explicit tuning.
    /// </summary>
    public static InstituteConfig DefaultFor(string instituteId)
        => new(instituteId, TargetAccuracy: null);

    /// <summary>
    /// Project from a persisted <see cref="InstituteDocument"/>.
    /// </summary>
    public static InstituteConfig FromDocument(InstituteDocument doc)
        => new(doc.InstituteId, doc.TargetAccuracy);
}
