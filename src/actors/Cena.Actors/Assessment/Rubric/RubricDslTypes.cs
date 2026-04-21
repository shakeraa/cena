// =============================================================================
// Cena Platform — Ministry Bagrut Rubric DSL domain types (prr-033, ADR-0052)
//
// Immutable value-objects the IRubricVersionPinning service exposes to
// callers. Every field on a rubric is a deliberate part of the v1 DSL;
// adding a field is a minor-bump, removing or reshaping one is a major
// bump and requires a superseding rubric entry per ADR-0052 §3.
//
// Sign-off metadata (ApprovedByUserId + ApprovedAtUtc + MinistryCircularRef)
// is REQUIRED on every track. The loader rejects rubrics where any of the
// three are empty — this is the audit trail a regulator or a night-shift
// on-call can read at 03:00 on a Bagrut exam morning.
// =============================================================================

namespace Cena.Actors.Assessment.Rubric;

/// <summary>
/// A pinned rubric for a single exam track at a single version.
/// Aggregate root for the rubric bounded context.
/// </summary>
public sealed record BagrutRubric(
    string ExamCode,
    string RubricVersion,
    RubricSignOff SignOff,
    IReadOnlyList<RubricGradeBand> GradeBands,
    IReadOnlyList<RubricCriterion> ScoringCriteria)
{
    public string RubricId => $"{ExamCode}@{RubricVersion}";
}

/// <summary>
/// Sign-off triple — who approved, when, and under what Ministry
/// circular. Empty / missing values are rejected at load.
/// </summary>
public sealed record RubricSignOff(
    string ApprovedByUserId,
    DateTimeOffset ApprovedAtUtc,
    string MinistryCircularRef);

/// <summary>A score-band with bounds (0..100 partition) and localized descriptor.</summary>
public sealed record RubricGradeBand(
    string Band,
    int MinScore,
    int MaxScore,
    RubricLocalizedText Descriptor);

/// <summary>Weighted scoring criterion composed of checkpoints.</summary>
public sealed record RubricCriterion(
    string CriterionId,
    double Weight,
    RubricLocalizedText Display,
    IReadOnlyList<RubricCheckpoint> Checkpoints);

/// <summary>Atomic scorable checkpoint within a criterion.</summary>
public sealed record RubricCheckpoint(
    string Id,
    int Points,
    string DescriptionEn);

/// <summary>en / he / ar localized text triple (other locales fall back to en).</summary>
public sealed record RubricLocalizedText(
    string En,
    string? He,
    string? Ar)
{
    public string Resolve(string locale) => locale switch
    {
        "he" when !string.IsNullOrEmpty(He) => He,
        "ar" when !string.IsNullOrEmpty(Ar) => Ar,
        _ => En,
    };
}

/// <summary>
/// Immutable snapshot of all loaded rubrics indexed by exam code.
/// One snapshot per process; reload replaces atomically.
/// </summary>
public sealed record RubricSnapshot(
    IReadOnlyDictionary<string, BagrutRubric> ByExamCode,
    IReadOnlyList<BagrutRubric> All,
    DateTimeOffset LoadedAtUtc);

public sealed class RubricLoadException : Exception
{
    public RubricLoadException(string message, Exception? inner = null) : base(message, inner) { }
}
