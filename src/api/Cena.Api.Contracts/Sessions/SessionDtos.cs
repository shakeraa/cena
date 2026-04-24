// =============================================================================
// Cena Platform -- Session API Contracts (DB-05)
// Shared DTOs for session lifecycle endpoints.
// =============================================================================

namespace Cena.Api.Contracts.Sessions;

public sealed record SessionListResponse(
    IReadOnlyList<SessionSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record SessionSummaryDto(
    string Id,
    string SessionId,
    string Subject,
    string ConceptId,
    string Methodology,
    string Status,
    int TurnCount,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

public sealed record ActiveSessionResponse(
    bool HasActive,
    string? SessionId,
    string? Subject,
    DateTimeOffset? StartedAt);

public sealed record SessionDetailDto(
    string Id,
    string SessionId,
    string Subject,
    string ConceptId,
    string Methodology,
    string Status,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double Accuracy,
    double FatigueScore,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyDictionary<string, double> MasteryDeltas,
    // RDY-034 slice 2: flow-state assessment computed from session signals.
    // Null only if the caller explicitly opts out (no such opt-out exists
    // yet) — always populated on the main session read, including for
    // brand-new sessions (state == "warming").
    FlowStateAssessmentResponse? FlowState = null);

public sealed record SessionReplayDto(
    string SessionId,
    string Subject,
    string Methodology,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<QuestionAttemptDto> Attempts);

public sealed record QuestionAttemptDto(
    int Sequence,
    string QuestionId,
    string ConceptId,
    string QuestionType,
    bool IsCorrect,
    int ResponseTimeMs,
    int HintCountUsed,
    bool WasSkipped,
    double PriorMastery,
    double PosteriorMastery,
    DateTimeOffset Timestamp);

// =============================================================================
// STB-01: Session Start + Active Session DTOs
// =============================================================================

public sealed record SessionStartRequest(
    string[] Subjects,
    int DurationMinutes,     // 5 | 10 | 15 | 30 | 45 | 60
    string Mode);            // 'practice' | 'challenge' | 'review' | 'diagnostic'

public sealed record SessionStartResponse(
    string SessionId,
    string HubGroupName,     // for SignalR subscription: "session-{sessionId}"
    string? FirstQuestionId); // FIND-pedagogy-016: seeded by AdaptiveQuestionPool on session start

public sealed record ActiveSessionDto(
    string SessionId,
    string[] Subjects,
    string Mode,
    DateTime StartedAt,
    int DurationMinutes,
    int ProgressPercent,
    string? CurrentQuestionId);

// =============================================================================
// STB-01b: Session Question + Answer DTOs
// =============================================================================

/// <summary>
/// In-session question DTO served to the Vue student app.
///
/// FIND-pedagogy-006 — Scaffolding fields. The Vue REST path now computes
/// <see cref="ScaffoldingLevel"/> from the student's current mastery on
/// this question's concept so novices see a worked example, intermediate
/// students get a hint budget, and experts get independent practice.
///
/// The level is a plain string to keep the shared contract assembly free
/// of a reference to <c>Cena.Actors</c>. The server converts the enum via
/// ToString() and the TypeScript client types it as
/// <c>'Full' | 'Partial' | 'HintsOnly' | 'None'</c>.
///
/// Citations:
///   - Sweller, van Merriënboer &amp; Paas (1998), DOI 10.1023/A:1022193728205
///     (worked example effect for novice learners).
///   - Renkl &amp; Atkinson (2003), DOI 10.1207/S15326985EP3801_3
///     (faded examples as transition to independent problem solving).
///   - Kalyuga, Ayres, Chandler &amp; Sweller (2003), DOI 10.1207/S15326985EP3801_4
///     (expertise reversal effect — fading scaffolds for experts).
/// </summary>
public sealed record SessionQuestionDto(
    string QuestionId,
    int QuestionIndex,
    int TotalQuestions,
    string Prompt,
    string QuestionType,  // 'multiple-choice' | 'short-answer' | 'numeric'
    string[] Choices,     // Empty for non-multiple-choice
    string Subject,
    int ExpectedTimeSeconds,
    // FIND-pedagogy-006 — scaffolding derived from real BKT mastery at request time.
    string? ScaffoldingLevel = null,   // "Full" | "Partial" | "HintsOnly" | "None"
    string? WorkedExample = null,      // Authored worked example, only when level == Full
    int HintsAvailable = 0,            // Total hint budget for this scaffolding level
    int HintsRemaining = 0,            // Same as HintsAvailable on first load
    // =========================================================================
    // PRR-151 R-22 accommodation-render flags (compliance fix).
    //
    // The parent-console endpoint persists AccommodationProfileAssignedV1
    // events; until these flags landed, no session-rendering code
    // consulted them. With these fields, the student frontend can
    // activate TTS / distraction-reduced layout / graph-paper overlay
    // based on the folded profile. Default values (off / 1.0) match the
    // no-accommodation baseline so existing clients that haven't added
    // the fields to their render path keep working unchanged.
    // =========================================================================
    /// <summary>
    /// PRR-151 R-22 — true when the student has a parent-consented TTS
    /// accommodation (<c>AccommodationDimension.TtsForProblemStatements</c>).
    /// Frontend activates the TTS button / autoplay path only when set.
    /// </summary>
    bool TtsEnabled = false,
    /// <summary>
    /// PRR-151 R-22 — multiplier applied to <see cref="ExpectedTimeSeconds"/>.
    /// 1.0 = no accommodation, 1.5 = Ministry hatama-1 extended-time
    /// (<c>AccommodationDimension.ExtendedTime</c>). Server pre-multiplies
    /// <see cref="ExpectedTimeSeconds"/> before sending; this field is
    /// reported for client-side telemetry / audit rendering so the
    /// effective multiplier is visible end-to-end.
    /// </summary>
    double ExtendedTimeMultiplier = 1.0,
    /// <summary>
    /// PRR-151 R-22 — true when the student has consented to the
    /// distraction-reduced layout accommodation
    /// (<c>AccommodationDimension.DistractionReducedLayout</c>). Frontend
    /// renders one problem per page, hides sidebar / mastery widgets /
    /// ambient progress bars. Graph-paper overlay for drawing-heavy
    /// items is gated by the same flag because it's the same
    /// accessibility class (reduce incidental visual load).
    /// </summary>
    bool GraphPaperRequired = false,
    /// <summary>
    /// PRR-151 R-22 — true when the student has consented to suppression
    /// of peer-comparative stats (<c>AccommodationDimension.NoComparativeStats</c>).
    /// Surfaced on every question DTO so summary / footer widgets can
    /// consistently hide cohort comparisons without re-fetching the
    /// profile.
    /// </summary>
    bool NoComparativeStats = false,
    // =========================================================================
    // prr-050 — Dyscalculia accommodation pack. Sibling of the PRR-151 R-22
    // render flags above. When ShowNumberLineStrip is true, the frontend
    // renders a 0–20 number-line strip beneath the problem so the student
    // can count/point visually. SessionTimeMultiplier propagates the max
    // of ExtendedTime and Dyscalculia multipliers; clients that want to
    // telemeter the effective pacing budget read this field instead of
    // computing a max themselves (the server has already done so).
    //
    // Defaults match the no-accommodation baseline (off / 1.0) so existing
    // clients that have not added the field to their render path keep
    // working unchanged. Research citations live on
    // AccommodationDimension.Dyscalculia.
    // =========================================================================
    /// <summary>
    /// prr-050 — true when the student has the dyscalculia accommodation
    /// pack enabled (<c>AccommodationDimension.Dyscalculia</c>). Frontend
    /// renders a 0–20 number-line strip beneath the problem.
    /// </summary>
    bool ShowNumberLineStrip = false,
    /// <summary>
    /// prr-050 — effective pacing multiplier combining ExtendedTime and
    /// Dyscalculia accommodations (max of the two; no stacking). 1.0 when
    /// neither is enabled, 1.5 when either is. Reported so client-side
    /// audit / telemetry can log the effective budget the student saw.
    /// </summary>
    double SessionTimeMultiplier = 1.0);

/// <summary>
/// FIND-pedagogy-006 — Response for POST /api/sessions/{id}/question/{qid}/hint.
/// A single progressive hint produced by the HintGenerator service.
/// Returns 404 when the student has no remaining hints at the current
/// scaffolding level or when the question is not the one in flight.
/// </summary>
public sealed record SessionHintResponseDto(
    int HintLevel,
    string HintText,
    bool HasMoreHints,
    int HintsRemaining);

/// <summary>
/// FIND-pedagogy-006 — In-session hint usage counter stored on the
/// <c>LearningSessionQueueProjection</c>. Per-question so the budget
/// resets for each new question (matching the actor-side hint budget
/// semantics in <c>LearningSessionActor.HandleHintRequest</c>).
/// </summary>
public sealed record SessionHintUsageDto(string QuestionId, int HintsUsed);

/// <summary>
/// Request body for POST /api/sessions/{sessionId}/question/{questionId}/hint.
/// HintLevel must be between 1 and 3 (inclusive).
/// </summary>
public sealed record SessionHintRequest(int HintLevel);

/// <summary>
/// prr-203 — Response for POST /api/sessions/{sid}/question/{qid}/hint/next.
/// The server decides the next rung based on per-(session, question) state
/// held on <c>LearningSessionQueueProjection.LadderRungByQuestion</c>, so
/// clients CANNOT skip rungs by requesting a level (there is no request
/// body at all). ADR-0045 §3 pins L1=template (no LLM), L2=Haiku, L3=Sonnet.
///
/// Field meaning:
///   - Rung: the rung actually served on this call (1, 2, or 3).
///   - Body: the hint text; for L1 this is the deterministic template
///     (optionally rewritten by the LD-anxious governor), for L2/L3 this
///     is the LLM-produced copy passed through the ship-gate scrubber.
///   - RungSource: one of "template" | "haiku" | "sonnet" | "template-fallback".
///     The UI uses this to choose the right aria-live announcement and the
///     admin dashboard uses it to track rung-source distribution.
///   - MaxRungReached: the highest rung the student has been served for
///     this question so far — equal to Rung on first exposure.
///   - NextRungAvailable: true when the student still has budget and has
///     not yet exhausted L3; false after L3 is served or when degraded.
/// </summary>
public sealed record HintLadderResponseDto(
    int Rung,
    string Body,
    string RungSource,
    int MaxRungReached,
    bool NextRungAvailable);

public sealed record SessionAnswerRequest(
    string QuestionId,
    string Answer,
    int TimeSpentMs);

/// <summary>
/// Response for POST /api/sessions/{id}/answer.
///
/// FIND-pedagogy-001 / FIND-pedagogy-017 — feedback is no longer binary.
/// The <see cref="Feedback"/> field is DEPRECATED: the server ships an empty
/// string since FIND-pedagogy-017. The UI renders its own translated heading
/// via i18n keys. This field is kept for one release for backwards-compat;
/// callers should stop reading it.
///
/// The response carries:
/// - <see cref="Explanation"/>: the authored per-question worked explanation
///   (<c>QuestionDocument.Explanation</c>). Shown on every answer when present.
/// - <see cref="DistractorRationale"/>: the authored rationale for the SPECIFIC
///   wrong option the student chose (<c>QuestionDocument.DistractorRationales</c>).
///   Null on correct answers and null when no per-option rationale exists.
///
/// Both fields are nullable — the UI renders them only when present and falls
/// back gracefully when the question has no authored explanation yet.
///
/// Citations: Hattie &amp; Timperley (2007) "The Power of Feedback";
/// Black &amp; Wiliam (1998) "Assessment and Classroom Learning".
/// </summary>
public sealed record SessionAnswerResponseDto(
    bool Correct,
    [property: Obsolete("FIND-pedagogy-017: UI uses i18n keys for the heading. This field ships empty and will be removed next release.")]
    string Feedback,
    int XpAwarded,
    decimal MasteryDelta,
    string? NextQuestionId,
    string? Explanation = null,
    string? DistractorRationale = null);

public sealed record SessionCompletedDto(
    string SessionId,
    int TotalCorrect,
    int TotalWrong,
    int TotalXpAwarded,
    int AccuracyPercent,
    int DurationSeconds);

// =============================================================================
// STB-01c: Session History DTOs
// =============================================================================

public sealed record SessionHistoryDto(
    string SessionId,
    DateTime StartedAt,
    DateTime? EndedAt,
    string Mode,
    string[] Subjects,
    int TotalQuestionsAttempted,
    int CorrectAnswers,
    double Accuracy,
    int CurrentStreak,
    IReadOnlyList<QuestionHistoryItemDto> QuestionHistory,
    int RemainingInQueue);

public sealed record QuestionHistoryItemDto(
    string QuestionId,
    DateTime AnsweredAt,
    bool IsCorrect,
    int TimeSpentSeconds,
    string? SelectedOption);

// =============================================================================
// PWA-BE-001: Session Snapshot DTO for SignalR reconnect
// =============================================================================

public sealed record SessionSnapshotDto(
    string SessionId,
    Cena.Api.Contracts.Content.QuestionDto? CurrentQuestion,
    int CurrentStepNumber,
    Dictionary<string, Cena.Actors.Sessions.SkillMasteryDto> BktSnapshot,
    string ScaffoldingLevel,
    List<Cena.Actors.Sessions.StepResultDto> CompletedSteps,
    DateTimeOffset SessionStartedAt,
    int SessionDurationSeconds);

// =============================================================================
// prr-204: Tutor Context DTOs
// Session-scoped snapshot served to the Sidekick drawer + hint-ladder
// consumers. The DTO stays strictly flat so downstream TypeScript clients
// can type it as a plain interface. See ADR-0003 for the scope boundary:
// the snapshot lives in Redis with session-TTL and is rebuilt from session
// projections on cache miss — it is never persisted on a student profile.
// =============================================================================

/// <summary>
/// prr-204 — Response for GET /api/v1/sessions/{sid}/tutor-context.
///
/// Strictly session-scoped (ADR-0003). The
/// <see cref="LastMisconceptionTag"/> field is the session's most recent
/// buggy-rule id and NEVER joins onto a long-lived student profile.
/// </summary>
/// <param name="SessionId">Session stream key.</param>
/// <param name="CurrentQuestionId">The question the student is currently on, or null between questions.</param>
/// <param name="AnsweredCount">Questions answered in this session.</param>
/// <param name="CorrectCount">Questions answered correctly in this session.</param>
/// <param name="CurrentRung">Highest hint-ladder rung (0..3) reached for the current question.</param>
/// <param name="LastMisconceptionTag">Session-scoped misconception tag. Null when none detected.</param>
/// <param name="AttemptPhase">"first_try" | "retry" | "post_solution".</param>
/// <param name="ElapsedMinutes">Minutes since session start at snapshot time.</param>
/// <param name="DailyMinutesRemaining">Minutes left in today's tutor-time budget.</param>
/// <param name="BktMasteryBucket">"low" | "mid" | "high" | "unknown" — coarse per-ADR-0003.</param>
/// <param name="AccommodationFlags">Accommodation flags relevant to the tutor/hint copy path.</param>
/// <param name="BuiltAtUtc">When the snapshot was assembled (for staleness telemetry).</param>
public sealed record TutorContextResponseDto(
    string SessionId,
    string? CurrentQuestionId,
    int AnsweredCount,
    int CorrectCount,
    int CurrentRung,
    string? LastMisconceptionTag,
    string AttemptPhase,
    int ElapsedMinutes,
    int DailyMinutesRemaining,
    string BktMasteryBucket,
    TutorContextAccommodationDto AccommodationFlags,
    DateTimeOffset BuiltAtUtc);

/// <summary>
/// prr-204 — Accommodation flags forwarded with the tutor context so the
/// Sidekick + hint consumers don't need a round-trip to the accommodations
/// service mid-turn. Defaults match "no accommodations" baseline.
/// </summary>
public sealed record TutorContextAccommodationDto(
    bool LdAnxiousFriendly,
    double ExtendedTimeMultiplier,
    bool DistractionReducedLayout,
    bool TtsForProblemStatements);
