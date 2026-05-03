// =============================================================================
// Cena Platform — SessionRiskAssessment (session-scoped value object)
//
// SESSION-SCOPED ONLY. Do NOT persist to StudentState, any snapshot, or any
// outbound DTO. See prr-013 and RDY-080.
//
// This value object is the SINGLE authorised home for the
// risk/readiness/prediction naming pattern. Every other surface — persistence
// snapshots, outbound adapter payloads, admin API DTOs — is banned from
// carrying a field that matches the `risk*` / `atRisk*` / `bagrutRisk*` /
// `predictedScore*` / `readiness*` pattern. That ban is enforced by
// NoAtRiskPersistenceTest and NoAtRiskExternalEmissionTest.
//
// Provenance:
//   - prr-013: Redesign "At-Risk Student Alert" under three hard constraints
//   - ADR-0003: Misconception / session-sensitive data is session-scoped,
//               30-day retention max, never on student profiles or ML training
//   - RDY-080: In-surface only — no external emission
//
// The legitimate home for instances of this type is inside a future
// LearningSessionActor-owned computation (post Sprint 1 per ADR-0012). Until
// that wiring lands, the type is declared but not yet referenced from
// session-actor hot paths — existence alone is enough to unblock the
// architecture tests that prevent any NEW type from growing risk-named
// fields outside this boundary.
// =============================================================================

namespace Cena.Actors.Sessions;

/// <summary>
/// Session-scoped risk/readiness assessment. Carries a point estimate, a
/// confidence-interval half-width, the sample size it was computed from,
/// and the instant it was generated.
/// <para>
/// Per <c>prr-013</c>: every student-facing risk number ships with its
/// uncertainty (CI + N). Naked point estimates are dishonest and banned.
/// </para>
/// <para>
/// Per <c>ADR-0003</c>: this value is <b>session-scoped</b>. It lives on the
/// active session actor only. It MUST NOT be persisted to
/// <c>StudentState</c>, to any Marten snapshot, or to any projection.
/// Retention ceiling: end of session. No 30-day buffer, no ML reuse, no
/// cross-session rebuild from event history.
/// </para>
/// <para>
/// Per <c>RDY-080</c>: this value is <b>in-surface only</b>. It MUST NOT be
/// emitted to parent SMS/WhatsApp, passed back to Google Classroom / Mashov /
/// any SIS, or loaded into a tomorrow-dashboard. The student and the teacher
/// see it during the session. Nobody else, ever.
/// </para>
/// </summary>
public sealed record SessionRiskAssessment
{
    /// <summary>
    /// Unit-interval fraction in <c>[0.0, 1.0]</c>. For example, <c>0.40</c>
    /// means "40% of mastery-threshold problems answered correctly this session".
    /// </summary>
    public double PointEstimate { get; }

    /// <summary>
    /// Unit-interval half-width in <c>[0.0, 1.0]</c>. For example, <c>0.12</c>
    /// renders as <c>±12%</c>. The UI MUST display this alongside the point
    /// estimate — a naked number is banned.
    /// </summary>
    public double ConfidenceIntervalHalfWidth { get; }

    /// <summary>
    /// Number of observations the estimate was computed from (e.g. problems
    /// attempted this session). Must be <c>&gt; 0</c>; a zero-sample
    /// "prediction" is not an assessment, it is a guess, and shipping it to
    /// a student would violate the honesty contract.
    /// </summary>
    public int SampleSize { get; }

    /// <summary>
    /// Timestamp the assessment was computed. Used by the session actor to
    /// decide staleness within the session; never compared across sessions.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; }

    /// <summary>Constructs and validates the four invariants. Fails fast.</summary>
    public SessionRiskAssessment(
        double pointEstimate,
        double confidenceIntervalHalfWidth,
        int sampleSize,
        DateTimeOffset generatedAt)
    {
        if (double.IsNaN(pointEstimate) || pointEstimate < 0.0 || pointEstimate > 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(pointEstimate),
                pointEstimate,
                "PointEstimate must be in [0.0, 1.0] (prr-013: naked/unbounded numbers are banned).");

        if (double.IsNaN(confidenceIntervalHalfWidth)
            || confidenceIntervalHalfWidth < 0.0
            || confidenceIntervalHalfWidth > 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(confidenceIntervalHalfWidth),
                confidenceIntervalHalfWidth,
                "ConfidenceIntervalHalfWidth must be in [0.0, 1.0] (prr-013: CI must be present and finite).");

        if (sampleSize <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(sampleSize),
                sampleSize,
                "SampleSize must be > 0 (prr-013: a zero-sample 'assessment' is a guess, not an assessment).");

        PointEstimate = pointEstimate;
        ConfidenceIntervalHalfWidth = confidenceIntervalHalfWidth;
        SampleSize = sampleSize;
        GeneratedAt = generatedAt;
    }

    /// <summary>Lower bound of the confidence interval, clipped to zero.</summary>
    public double LowerBound => Math.Max(0.0, PointEstimate - ConfidenceIntervalHalfWidth);

    /// <summary>Upper bound of the confidence interval, clipped to one.</summary>
    public double UpperBound => Math.Min(1.0, PointEstimate + ConfidenceIntervalHalfWidth);
}
