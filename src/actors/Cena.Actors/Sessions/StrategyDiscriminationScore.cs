// =============================================================================
// Cena Platform — StrategyDiscriminationScore (session-scoped value object)
//
// SESSION-SCOPED ONLY. Do NOT persist to StudentState, any Marten snapshot,
// any outbound DTO, or any ML training corpus. See prr-065, ADR-0003.
//
// This value object is the SINGLE authorised home for the "strategy-
// discrimination" signal in the AdaptiveScheduler bounded context. The score
// expresses, within a single learning session, how consistently the student's
// error patterns map to a distinct problem-solving strategy (e.g. repeated
// sign errors → procedural-algebra strategy vs confused order-of-operations
// → conceptual-precedence strategy). The scheduler consumes it to pick the
// next worked-example bucket; nothing else reads it.
//
// Why session-scoped (ADR-0003):
//   - Error-specific strategy traces, when aggregated across sessions, become
//     a per-student behavioural profile of a minor — the exact class of
//     artefact the FTC v. Edmodo "Affected Work Product" decree flagged,
//     and that ICO v. Reddit £14.47M (Feb 2026) reinforced under GDPR Art. 22.
//   - Mastery signals (BKT P(known)) are aggregate competence measures and
//     are allowed to persist. Strategy-discrimination signals name the
//     specific mistake pattern — they carry the same privacy weight as a
//     misconception tag, so they live under the same ADR-0003 boundary.
//
// Why a value object, not an event:
//   - The score is derived each session from in-session attempt records.
//     It is NOT a business decision to durably record "this student's
//     strategy was X" — that would cross the ADR-0003 boundary.
//   - The scheduler reads the score and decides; the decision is emitted
//     as a session-scoped scheduling event, and the raw score is discarded
//     at session end.
//
// Provenance:
//   - prr-065: Strategy-discrimination scores in AdaptiveScheduler (session-scoped)
//   - ADR-0003: Misconception / session-sensitive data is session-scoped,
//               30-day retention max, never on student profiles or ML training
//   - Sibling seam: SessionRiskAssessment (same pattern, different signal)
//
// The legitimate home for instances of this type is inside the scheduler
// computation owned by LearningSessionActor (post Sprint 1 per ADR-0012).
// Existence of the type unblocks the arch test that prevents any NEW type
// from growing strategy-named fields outside this boundary.
// =============================================================================

namespace Cena.Actors.Sessions;

/// <summary>
/// Session-scoped strategy-discrimination score.
/// <para>
/// Per <c>prr-065</c> and <c>ADR-0003</c>: this value is <b>session-scoped</b>.
/// It lives on the active session actor only. It MUST NOT be persisted to
/// <c>StudentState</c>, to any Marten snapshot, to any projection, or to any
/// outbound DTO. Retention ceiling: end of session. No 30-day buffer, no ML
/// reuse, no cross-session rebuild from event history.
/// </para>
/// <para>
/// The score is a unit-interval confidence that the student's observed
/// answer patterns in the current session consistently map to a single
/// problem-solving strategy label. The scheduler uses it to decide whether
/// to commit to a methodology-aligned next-item bucket (high score) or to
/// offer a diagnostic probe across strategies (low score).
/// </para>
/// </summary>
public sealed record StrategyDiscriminationScore
{
    /// <summary>
    /// Strategy label the score is measured against (e.g. "procedural_algebra",
    /// "conceptual_precedence", "visual_geometry"). Free-string, not an enum,
    /// because the strategy catalog is session-local: the scheduler picks
    /// candidate strategies per-session from the question-bank taxonomy; no
    /// long-term canonical strategy enum exists on the profile side.
    /// </summary>
    public string StrategyLabel { get; }

    /// <summary>
    /// Unit-interval confidence in <c>[0.0, 1.0]</c> that the session's
    /// observed error pattern maps to <see cref="StrategyLabel"/>. A zero
    /// score means "observed errors do not discriminate any strategy"; a
    /// one means "every observed error maps to this strategy". The
    /// scheduler treats values below <see cref="MinReliableThreshold"/>
    /// as noise and falls back to the diagnostic path.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// Number of in-session attempts the score was computed from. Must be
    /// <c>&gt;= MinSampleSize</c> or the score is refused; with too few
    /// attempts the discrimination signal is indistinguishable from noise.
    /// </summary>
    public int SampleSize { get; }

    /// <summary>
    /// Timestamp the score was computed. Used by the session actor to
    /// decide staleness within the session; never compared across sessions.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; }

    /// <summary>
    /// The confidence floor below which the scheduler MUST NOT commit to a
    /// strategy-aligned bucket. Below this, the signal is noise.
    /// </summary>
    public const double MinReliableThreshold = 0.55;

    /// <summary>
    /// Minimum in-session attempt count required to compute a score. Below
    /// this, the sample is statistically indistinguishable from random and
    /// the score constructor throws.
    /// </summary>
    public const int MinSampleSize = 3;

    /// <summary>Constructs and validates the four invariants. Fails fast.</summary>
    public StrategyDiscriminationScore(
        string strategyLabel,
        double confidence,
        int sampleSize,
        DateTimeOffset generatedAt)
    {
        if (string.IsNullOrWhiteSpace(strategyLabel))
            throw new ArgumentException(
                "StrategyLabel must not be empty (prr-065: a discriminated strategy without a label is meaningless).",
                nameof(strategyLabel));

        if (double.IsNaN(confidence) || confidence < 0.0 || confidence > 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Confidence must be in [0.0, 1.0] (prr-065: unbounded/NaN scores are banned).");

        if (sampleSize < MinSampleSize)
            throw new ArgumentOutOfRangeException(
                nameof(sampleSize),
                sampleSize,
                $"SampleSize must be >= {MinSampleSize} (prr-065: below this, the discrimination signal is noise).");

        StrategyLabel = strategyLabel;
        Confidence = confidence;
        SampleSize = sampleSize;
        GeneratedAt = generatedAt;
    }

    /// <summary>
    /// True when the score is above the reliability floor; callers MUST
    /// gate strategy-aligned scheduler decisions on this.
    /// </summary>
    public bool IsReliable => Confidence >= MinReliableThreshold;
}
