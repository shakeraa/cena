// =============================================================================
// Cena Platform — DiagnosticBlockResponse (prr-228)
//
// Student response to a single per-target diagnostic item. Carries the
// `Action` (Answered | Skipped) — a skipped item counts toward the 6-8 cap
// but is NOT scored as wrong; it flips the item's TopicFeeling default to
// `New` and steers the adaptive stop away from "student is struggling".
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Diagnosis.PerTarget;

/// <summary>
/// Whether the student answered the item or opted to skip it.
/// </summary>
public enum DiagnosticResponseAction
{
    /// <summary>Student selected an answer (correct or not).</summary>
    Answered = 0,

    /// <summary>Student pressed "skip this item". The skip counts toward
    /// the 6-8 cap but does NOT decrement mastery estimate.</summary>
    Skipped = 1,
}

/// <summary>
/// Student response to a per-target diagnostic item.
/// </summary>
/// <param name="ItemId">Item identifier from <see cref="DiagnosticBlockItem.ItemId"/>.</param>
/// <param name="SkillCode">Skill this item calibrates.</param>
/// <param name="Action">Answered or Skipped.</param>
/// <param name="Correct">True iff Answered AND the answer was correct.
/// Ignored when Action = Skipped.</param>
/// <param name="DifficultyIrt">The item's difficulty at the time of
/// delivery — snapshot so later item-parameter drift doesn't mis-score
/// historical responses.</param>
public sealed record DiagnosticBlockResponse(
    string ItemId,
    SkillCode SkillCode,
    DiagnosticResponseAction Action,
    bool Correct,
    double DifficultyIrt);
