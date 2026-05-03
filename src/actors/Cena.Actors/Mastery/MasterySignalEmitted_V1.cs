// =============================================================================
// Cena Platform — MasterySignalEmitted_V1 (EPIC-PRR-J PRR-381, EPIC-PRR-A
// mastery-engine link)
//
// Why this file exists
// --------------------
// PRR-381 implements the post-reflection-gate "productive failure" path. When
// a student fails a step, walks through the reflection gate, retries, and the
// CAS verifies the new step as correct, we want to record that as a small
// positive mastery nudge — a one-shot signal the downstream BKT projection
// and readiness calculators can ingest.
//
// What this is NOT
// ----------------
// This is NOT a streak counter. It is NOT a variable-ratio reward. It is NOT
// a "comeback bonus" or any other dark-pattern engagement mechanic — those
// are banned per memory "Ship-gate banned terms" (GD-004), ADR-0048, and the
// shipgate UX scanner. The event carries a single magnitude field
// (MasteryDelta) and a TriggerSource string identifying the pedagogical path
// that produced it. There is no running count, no multiplier, no
// loss-aversion framing anywhere in the payload.
//
// The delta is intentionally SMALL (default 0.05 per
// MasterySignalOptions.DefaultDelta). Large deltas distort the BKT posterior
// and produce the very "slot machine" feeling the shipgate bans.
//
// Design link
// -----------
// Persona-#4 (education research, productive-failure pattern) in
// docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md — post-
// reflection success is a high-information learning moment; logging it as a
// separate stream (distinct from the raw CAS attempt log) keeps the mastery
// engine's source-of-truth clear without collapsing into the BKT update path
// that already runs off of ConceptAttempted_V1.
//
// The TriggerSource is a string (not an enum) by design: future post-
// reflection surfaces (e.g. tutor-guided retry, whiteboard retry) will add
// their own trigger strings without requiring a schema bump. The canonical
// trigger for THIS task's path is "post_reflection_retry_success" — see
// MasterySignalTrigger below.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Mastery;

/// <summary>
/// Canonical TriggerSource values for <see cref="MasterySignalEmitted_V1"/>.
/// New call sites are encouraged (not forced) to add their identifier here
/// so the set of origin surfaces stays discoverable from one place.
/// </summary>
public static class MasterySignalTrigger
{
    /// <summary>
    /// Student failed a step, walked through the reflection gate, retried,
    /// and the CAS verified the new step as correct. Emitted exactly once
    /// per successful retry — no multiplier, no streak.
    /// </summary>
    public const string PostReflectionRetrySuccess = "post_reflection_retry_success";
}

/// <summary>
/// Mastery-signal event emitted when a pedagogical surface produces a
/// positive, one-shot nudge to a student's mastery posterior. The first
/// producer is the post-reflection retry-success path (PRR-381), but the
/// shape is reusable — other post-reflection success surfaces can emit it
/// with a different <see cref="TriggerSource"/>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id (same encoding as the BKT store).</param>
/// <param name="ExamTargetCode">Catalog code for the exam context. String to match event serialization elsewhere; parse to <see cref="Cena.Actors.ExamTargets.ExamTargetCode"/> at the projection boundary.</param>
/// <param name="SkillCode">Skill whose posterior is being nudged. String to match event serialization elsewhere; parse to <see cref="SkillCode"/> at the projection boundary.</param>
/// <param name="MasteryDelta">
/// Positive nudge magnitude, typically small (default 0.05 per
/// <see cref="MasterySignalOptions.DefaultDelta"/>). Consumers clamp into
/// the BKT P(L) range — this field is the RAW signal, not the post-clamp
/// posterior.
/// </param>
/// <param name="TriggerSource">
/// Which pedagogical surface produced the signal. Use the constants in
/// <see cref="MasterySignalTrigger"/> when available; otherwise a stable
/// snake_case string the producer owns.
/// </param>
/// <param name="EmittedAt">Wall-clock of emission.</param>
public sealed record MasterySignalEmitted_V1(
    string StudentAnonId,
    string ExamTargetCode,
    string SkillCode,
    double MasteryDelta,
    string TriggerSource,
    DateTimeOffset EmittedAt
) : IDelegatedEvent;

/// <summary>
/// Tunables for the post-reflection mastery-signal pipeline. Default values
/// reflect the PRR-381 design review: a small positive nudge (0.05) that
/// intentionally sits well below the BKT posterior's sensitivity threshold,
/// so the signal is additive — it cannot by itself cross a mastery
/// threshold.
/// </summary>
public sealed class MasterySignalOptions
{
    /// <summary>Default magnitude when no explicit override is supplied.</summary>
    public const double DefaultDelta = 0.05d;

    /// <summary>
    /// Positive-only magnitude applied to the mastery posterior. Values
    /// outside (0, 1) are rejected by the service at emission time so the
    /// downstream BKT projection never sees a signal that would collapse or
    /// saturate the posterior in a single step.
    /// </summary>
    public double MasteryDelta { get; set; } = DefaultDelta;
}
