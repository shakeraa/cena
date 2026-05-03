// =============================================================================
// Cena Platform — MasteryKey VO (prr-222)
//
// Composite projection key for the skill-keyed mastery state.
//
// Invariant enforced by SkillKeyedMasteryProjection:
//   (StudentId, ExamTargetCode, SkillCode) is unique.
//
// Two students: separate rows.
// One student with targets Bagrut-Math-4yu AND Bagrut-Math-5yu: separate
// rows per unit even for the SAME skill (e.g. "math.algebra.quadratic-equations").
// One student with one target having the same skill attempted many times:
// ONE row, updated in-place via BKT posterior.
//
// The VO is intentionally a readonly record struct so it is free to pass
// around as a projection key and hashes cheaply.
// =============================================================================

using Cena.Actors.ExamTargets;

namespace Cena.Actors.Mastery;

/// <summary>
/// Composite key for the skill-keyed mastery projection.
/// </summary>
public readonly record struct MasteryKey(
    string StudentAnonId,
    ExamTargetCode ExamTargetCode,
    SkillCode SkillCode)
{
    /// <summary>
    /// Canonical string form used for audit logs and for the dedup check
    /// in integration tests. Do NOT log the raw student id in compliance
    /// events — prefer the hash form via the compliance logger.
    /// </summary>
    public override string ToString()
        => $"{StudentAnonId}|{ExamTargetCode.Value}|{SkillCode.Value}";

    /// <summary>
    /// Construct a key from raw strings. Throws <see cref="ArgumentException"/>
    /// if any component fails VO validation.
    /// </summary>
    public static MasteryKey From(
        string studentAnonId,
        string examTargetCode,
        string skillCode)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "StudentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        return new MasteryKey(
            studentAnonId,
            ExamTargetCode.Parse(examTargetCode),
            SkillCode.Parse(skillCode));
    }
}
