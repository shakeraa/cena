// =============================================================================
// Cena Platform — Academic Integrity Review Service (prr-144 retirement seam)
//
// This interface is the architectural SEAM for the "cheating-alert" family
// of features which the 2026-04-20 pre-release review retired (task
// prr-144, absorbed into EPIC-PRR-D D3 copy-semantic cluster). Any code
// path that emits a potential-integrity signal — answer-copying, session
// sharing, plagiarism-score escalation of authored content, overlap between
// sibling-session submissions — MUST route through this interface. It is
// the arch-test-gated replacement for ad-hoc "cheating detected" alerts.
//
// WHY this seam exists (senior-architect protocol):
//
//   WHY does prr-144 retire the cheating-alert family?
//     Because "cheating detected" framing:
//       1. addresses the student in an accusation register, which is
//          developmentally inappropriate and shames rather than supports;
//       2. presumes the adjudication the platform cannot make
//          (collaboration with a sibling is routinely allowed, session
//          overlap between study partners is common, and a confident
//          automated verdict in either direction invites harm);
//       3. crosses into educator-adjudication territory without educator
//          oversight, which is a role-boundary violation.
//
//   WHY a service interface rather than a hard ban?
//     Because SOME integrity signals ARE real and need to reach an
//     educator. Content-ingestion plagiarism scoring (admin side,
//     checking AUTHORED content for originality) is a legitimate signal,
//     just not a student-facing one. Session-overlap detection for
//     high-stakes assessments needs a path to teacher review. A service
//     interface lets us keep the signal while controlling the
//     presentation.
//
//   WHY a stub interface at landing time?
//     Because the production implementation is out of scope for the
//     prr-144 landing (the ship-gate scanner is the first deliverable;
//     the review workflow itself is a separate track — tasks still in
//     triage, see pre-release-review backlog). The stub lets the
//     scanner's fixture-time assertion pass, the architectural ratchet
//     pass on the full Cena.Actors.sln build, and future implementation
//     PRs land without renegotiating the seam boundary.
//
//   WHAT an implementation must do when it lands:
//     1. Accept a neutral request (NeverAccusationStyle) identifying the
//        session and the nature of the signal.
//     2. Route to an educator review queue — NEVER to a student-facing
//        alert.
//     3. Treat the signal as an invitation to review, not a verdict.
//     4. Never persist a "cheating" label on a student profile
//        (misconception data is session-scoped per ADR-0003; integrity
//        signals follow the same posture — session-scoped, educator-
//        reviewed, no profile-level stigma).
//
// Related:
//   - scripts/shipgate/cheating-alert-framing.yml — the ship-gate rule pack
//     enforcing the copy-ban this seam counterpoints.
//   - tests/shipgate/cheating-alert-framing.spec.mjs — asserts this file
//     exists at its canonical path.
//   - src/actors/Cena.Actors.Tests/Architecture/AcademicIntegrityRoutingTest.cs
//     — architectural ratchet asserting that any code path naming an
//     integrity signal references this interface.
//   - pre-release-review/reviews/retired.md — retirement log for the
//     cheating-alert family.
// =============================================================================

namespace Cena.Actors.AcademicIntegrity;

/// <summary>
/// Routes potential academic-integrity signals (answer copying, session
/// sharing, content-ingestion plagiarism escalation) to an educator
/// review queue. NEVER emits student-facing alerts. See file header for
/// the full architectural rationale and the retirement context (prr-144).
/// </summary>
/// <remarks>
/// <para>
/// This is a SEAM interface: every code path that previously wanted to
/// emit a "cheating detected" signal now records a
/// <see cref="PotentialCollaborationSignal"/> via this interface. The
/// interface has no production implementation at prr-144 landing time;
/// the implementation lands in a follow-up task once the educator
/// review-queue UX has been designed and reviewed.
/// </para>
/// <para>
/// The naming is deliberate: <c>PotentialCollaborationSignal</c>, not
/// <c>CheatingAlert</c>. The scanner's banned-vocabulary enforcement
/// extends to this interface — implementations may not name a method or
/// a property using the banned terms (cheating, plagiarism-alert, honor-
/// code-violation, academic-dishonesty). Neutral framing is a contract,
/// not a preference.
/// </para>
/// </remarks>
public interface IAcademicIntegrityReviewService
{
    /// <summary>
    /// Records a potential-collaboration signal for educator review.
    /// Returns a review token the caller can use to correlate follow-up
    /// outcomes. The signal is invisible to the student by contract.
    /// </summary>
    /// <param name="signal">
    /// Neutral signal describing the observation (session IDs involved,
    /// nature of the overlap, confidence band). NEVER includes student
    /// names in accusation framing; the educator review interface pairs
    /// the session IDs to students at render time.
    /// </param>
    /// <param name="cancellationToken">Standard cancellation.</param>
    /// <returns>
    /// A review token (GUID) the caller logs alongside its local trace so
    /// downstream educator decisions can be correlated back to the source
    /// signal.
    /// </returns>
    Task<Guid> RecordSignalAsync(
        PotentialCollaborationSignal signal,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Neutral descriptor of a potential-collaboration observation. Names
/// sessions, not students; names observations, not verdicts.
/// </summary>
/// <param name="TenantId">Multi-tenant isolation key (ADR-0001).</param>
/// <param name="PrimarySessionId">Session that produced the observation.</param>
/// <param name="RelatedSessionIds">
/// Other sessions the observation touches (e.g. the other half of a
/// session-overlap signal). Empty for single-session observations such
/// as a content-ingestion plagiarism escalation.
/// </param>
/// <param name="ObservationKind">
/// Coarse category: AnswerOverlap, SessionOverlap, ContentPlagiarism,
/// Other. Avoid finer-grained verdict vocabulary; the educator makes the
/// call.
/// </param>
/// <param name="ConfidenceBand">
/// Low / Medium / High — the strength of the observation, NOT of any
/// verdict. "High" does not mean "the student cheated"; it means "the
/// observation is well-supported and worth an educator looking at".
/// </param>
/// <param name="DetectedAt">UTC timestamp of detection.</param>
public sealed record PotentialCollaborationSignal(
    Guid TenantId,
    Guid PrimarySessionId,
    IReadOnlyList<Guid> RelatedSessionIds,
    ObservationKind ObservationKind,
    SignalConfidenceBand ConfidenceBand,
    DateTimeOffset DetectedAt);

/// <summary>
/// Coarse category of a potential-collaboration observation. Deliberately
/// avoids accusation vocabulary (no "Cheating" enum value).
/// </summary>
public enum ObservationKind
{
    /// <summary>Two sessions converged on matching answer sequences.</summary>
    AnswerOverlap = 1,

    /// <summary>Two student sessions overlapped in time on the same item set.</summary>
    SessionOverlap = 2,

    /// <summary>Admin-ingested content tripped a plagiarism-score escalation (content-moderation side, not student-side).</summary>
    ContentPlagiarism = 3,

    /// <summary>Unclassified observation requiring human review.</summary>
    Other = 99,
}

/// <summary>
/// Strength of the observation. Not a verdict about the student — the
/// educator adjudicates; the service only surfaces the signal.
/// </summary>
public enum SignalConfidenceBand
{
    Low = 1,
    Medium = 2,
    High = 3,
}
