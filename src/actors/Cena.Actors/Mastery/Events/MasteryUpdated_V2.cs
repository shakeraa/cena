// =============================================================================
// Cena Platform — MasteryUpdated_V2 event (prr-222)
//
// Post-multi-target shape: carries the ExamTargetCode so the projection
// key (StudentId, ExamTargetCode, SkillCode) is complete.
//
// Emitted by IBktStateTracker.Update on every learning-session attempt.
// The projection (SkillKeyedMasteryProjection) folds these into the
// skill-keyed mastery table with the dedup invariant enforced on apply.
// =============================================================================

using Cena.Actors.ExamTargets;

namespace Cena.Actors.Mastery.Events;

/// <summary>
/// Source of a V2 event — distinguishes native writes from V1-upcast
/// events so downstream analytics / migrations can audit the blast radius
/// of the default assumption.
/// </summary>
public static class MasteryEventSource
{
    /// <summary>Emitted natively by post-wave1c write paths.</summary>
    public const string Native = "native";

    /// <summary>Synthesised from a V1 event by MasteryUpcaster.ToV2.</summary>
    public const string V1UpcastDefault5Yu = "v1-upcast-default-5yu";
}

/// <summary>
/// Mastery-updated event, post-multi-target. The tuple
/// (<see cref="StudentAnonId"/>, <see cref="ExamTargetCode"/>,
/// <see cref="SkillCode"/>) is the primary key in the skill-keyed
/// projection and is enforced unique on apply.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="ExamTargetCode">Catalog code for the exam context.</param>
/// <param name="SkillCode">Skill whose posterior changed.</param>
/// <param name="MasteryProbability">BKT P(L) after the update; [0.001, 0.999].</param>
/// <param name="UpdatedAt">Wall-clock of the update.</param>
/// <param name="Source">
/// <see cref="MasteryEventSource.Native"/> for natively-written events,
/// <see cref="MasteryEventSource.V1UpcastDefault5Yu"/> for events
/// synthesised by the V1→V2 upcaster.
/// </param>
public sealed record MasteryUpdated_V2(
    string StudentAnonId,
    ExamTargetCode ExamTargetCode,
    SkillCode SkillCode,
    float MasteryProbability,
    DateTimeOffset UpdatedAt,
    string Source);
