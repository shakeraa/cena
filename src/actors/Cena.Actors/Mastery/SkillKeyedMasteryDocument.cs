// =============================================================================
// Cena Platform — SkillKeyedMasteryDocument (prr-222 production binding)
//
// Marten-persisted document shape for the skill-keyed mastery projection.
// Mirrors SkillKeyedMasteryRow (the domain VO) but adds a string Id
// derived from MasteryKey.ToString() so Marten can index + query rows by
// that key directly. Domain code keeps consuming SkillKeyedMasteryRow;
// MartenSkillKeyedMasteryStore does the round-trip conversion on
// every read/write.
//
// Why a separate type:
//   - Marten's default Id strategy is a single-value property; composite
//     keys aren't first-class without a wrapper.
//   - SkillKeyedMasteryRow holds MasteryKey + SkillCode/ExamTargetCode as
//     VOs (readonly record structs). Marten's JSON serialiser can
//     round-trip these, but flattening the primary-key strings onto the
//     document makes the `Where` queries trivially indexable.
//   - Provenance (Source) is kept as a plain string to match the domain
//     row's contract (see MasteryEventSource for legal values).
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Marten document representation of <see cref="SkillKeyedMasteryRow"/>.
/// Id is the canonical <c>student|target|skill</c> form produced by
/// <see cref="MasteryKey.ToString"/>; the other fields are flattened so
/// per-student and per-target queries run against indexed columns.
/// </summary>
public sealed record SkillKeyedMasteryDocument
{
    /// <summary>
    /// Canonical <c>studentAnonId|examTargetCode|skillCode</c> form.
    /// Primary key for Marten; composed from <see cref="MasteryKey"/>
    /// to enforce the prr-222 dedup invariant.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>Pseudonymous student id (ADR-0038).</summary>
    public string StudentAnonId { get; init; } = "";

    /// <summary>Catalog-level exam target code (e.g. "bagrut-math-5yu").</summary>
    public string ExamTargetCode { get; init; } = "";

    /// <summary>Canonical skill id (e.g. "math.algebra.quadratic-equations").</summary>
    public string SkillCode { get; init; } = "";

    /// <summary>Current BKT P(L) in [0.001, 0.999].</summary>
    public float MasteryProbability { get; init; }

    /// <summary>Monotone count of attempts folded into the row.</summary>
    public int AttemptCount { get; init; }

    /// <summary>Wall-clock of the most recent fold.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Provenance tag (see <see cref="Events.MasteryEventSource"/>).</summary>
    public string Source { get; init; } = "";
}
