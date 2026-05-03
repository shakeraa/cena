// =============================================================================
// Cena Platform — MasteryUpdated_V1 event (OBSOLETE, kept for replay) (prr-222)
//
// Pre-multi-target shape: StudentId + SkillCode only. ALL existing events
// on disk before the wave1c cut-over assume this shape.
//
// Replay / upcast:
//   Events of this type on replay are passed through
//   MasteryUpcaster.ToV2, which synthesises a V2 event with
//   ExamTargetCode = ExamTargetCode.V1UpcastDefault ("bagrut-math-5yu")
//   and Source = "v1-upcast-default-5yu" so the projection can audit
//   which rows came from the upcaster vs. the native V2 path.
//
// DO NOT emit V1 for new work. The [Obsolete] attribute is an error-level
// warning to keep fresh writes from appending to the legacy shape.
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Mastery.Events;

/// <summary>
/// Legacy (pre-multi-target) mastery-updated event. Emit paths are
/// compile-error forbidden; only the replay / upcast path still reads
/// this type. Prefer <see cref="MasteryUpdated_V2"/> for new code.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="SkillCode">Skill whose mastery posterior changed.</param>
/// <param name="MasteryProbability">BKT P(L) after the update; [0.001, 0.999].</param>
/// <param name="UpdatedAt">Wall-clock of the update.</param>
[Obsolete(
    "V1 is pre-multi-target. Emit MasteryUpdated_V2 instead. This record "
    + "exists only so event streams written before the wave1c cut-over "
    + "still replay via MasteryUpcaster.ToV2. New writes that reference "
    + "this type must localise the reference inside a pragma-suppressed "
    + "block (search MasteryUpcaster.cs for the pattern).",
    error: false)]
public sealed record MasteryUpdated_V1(
    string StudentAnonId,
    SkillCode SkillCode,
    float MasteryProbability,
    DateTimeOffset UpdatedAt);
