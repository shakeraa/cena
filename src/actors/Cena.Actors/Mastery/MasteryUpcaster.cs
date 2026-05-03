// =============================================================================
// Cena Platform — MasteryUpdated V1 → V2 upcaster (prr-222)
//
// V1 events were pre-multi-target; they carry only (StudentId, SkillCode).
// V2 requires ExamTargetCode as the third axis of the dedup invariant.
//
// The upcaster fills in the missing field with a HISTORICAL DEFAULT,
// "bagrut-math-5yu" — chosen because Cena's entire pre-wave1c user base
// was Bagrut-Math-5yu students (the only exam track shipped before
// wave1c). Upcast events carry Source = "v1-upcast-default-5yu" so the
// projection can audit which rows came from this assumption vs. native
// V2 writes.
//
// Contract:
//   - Pure function; no I/O, no clock, no mutation.
//   - Round-tripping V1 → V2 → V1 is NOT supported (V2 is strictly richer).
//   - Deterministic: same V1 input always yields same V2 output.
//   - Idempotent: calling ToV2 on an already-V2 event returns the event
//     unchanged (the caller's replay loop may hand both types to one
//     upcaster entry-point).
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery.Events;

namespace Cena.Actors.Mastery;

/// <summary>
/// Stateless upcaster for MasteryUpdated events.
/// </summary>
public static class MasteryUpcaster
{
    /// <summary>
    /// Pass-through identity for already-V2 events. Exists so the replay
    /// loop can invoke the upcaster on every event regardless of version.
    /// </summary>
    public static MasteryUpdated_V2 ToV2(MasteryUpdated_V2 v2) => v2;

    /// <summary>
    /// Upcast a V1 event into V2 by filling in the historical default
    /// ExamTargetCode ("bagrut-math-5yu") and tagging the Source so
    /// downstream consumers can audit which rows are upcast-synthesised.
    /// </summary>
#pragma warning disable CS0618, CS0619 // V1 is obsolete-as-error for EMIT paths; replay path is the sanctioned exception
    public static MasteryUpdated_V2 ToV2(MasteryUpdated_V1 v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        return new MasteryUpdated_V2(
            StudentAnonId: v1.StudentAnonId,
            ExamTargetCode: ExamTargetCode.Default,
            SkillCode: v1.SkillCode,
            MasteryProbability: v1.MasteryProbability,
            UpdatedAt: v1.UpdatedAt,
            Source: MasteryEventSource.V1UpcastDefault5Yu);
    }
#pragma warning restore CS0618, CS0619

    /// <summary>
    /// Object-typed dispatch: route any known mastery event to V2. Throws
    /// <see cref="NotSupportedException"/> for anything unrecognised —
    /// the replay loop should filter by type first.
    /// </summary>
    public static MasteryUpdated_V2 ToV2Dynamic(object @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

#pragma warning disable CS0618, CS0619
        return @event switch
        {
            MasteryUpdated_V2 v2 => v2,
            MasteryUpdated_V1 v1 => ToV2(v1),
            _ => throw new NotSupportedException(
                $"MasteryUpcaster cannot upcast event of type {@event.GetType().FullName}."),
        };
#pragma warning restore CS0618, CS0619
    }
}
