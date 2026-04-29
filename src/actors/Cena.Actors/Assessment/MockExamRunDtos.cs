// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) DTOs
//
// Wire-shape contracts between the student SPA and the
// MockExamRunEndpoints. Aggregate counts, IDs, timer fields, and
// (Phase 1E) section-weighted Ministry-style scoring breakdown.
//
// What is NOT here: raw question bodies. Those are loaded into the
// runner's session view via the existing question read endpoint
// (separate route) so the delivery-gate chokepoint
// (ExamSimulationDelivery.AssertDeliverable) stays the single point
// of enforcement for ADR-0043.
// =============================================================================

namespace Cena.Actors.Assessment;

/// <summary>Request to begin a mock-exam run.</summary>
public sealed record StartMockExamRunRequest(
    /// <summary>Bagrut format code: "806" (math 5U), "807" (math 4U),
    /// "036" (physics). Maps to <see cref="ExamFormat.FromCode"/>.</summary>
    string ExamCode,

    /// <summary>Optional Ministry שאלון code (e.g., "035582"). Phase 1B
    /// uses this to look up a per-paper <see cref="BagrutPaperStructure"/>
    /// and drive topic-aware question selection. If null/unknown, the
    /// canonical default for the exam code is used.</summary>
    string? PaperCode = null,

    /// <summary>Phase 2B — extra-time accommodation as a percentage of
    /// the canonical exam time. Real Ministry exam-day accommodations
    /// (dyslexia / learning differences) typically grant +25% or +50%.
    /// Server clamps to [0, 100]; values outside the band are coerced
    /// silently. Default 0 = standard time. The deadline shown to the
    /// student honors this value end-to-end.</summary>
    int ExtraTimePercent = 0
);

/// <summary>Response after starting a run.</summary>
public sealed record MockExamRunStartedResponse(
    string RunId,
    string ExamCode,
    string? PaperCode,
    int TimeLimitMinutes,
    /// <summary>Phase 2B — extra minutes granted via accommodation
    /// (e.g., 45 for +25% of a 180-min exam). 0 = standard time. The
    /// SPA can render "180 min + 45 accommodation" when this is &gt; 0.</summary>
    int ExtraTimeMinutes,
    int PartAQuestionCount,
    int PartBQuestionCount,
    int PartBRequiredCount,
    IReadOnlyList<string> PartAQuestionIds,
    IReadOnlyList<string> PartBQuestionIds,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline);

/// <summary>Snapshot of run state for the runner UI.</summary>
public sealed record MockExamRunStateResponse(
    string RunId,
    string ExamCode,
    string? PaperCode,
    int TimeLimitMinutes,
    int ExtraTimeMinutes,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    bool IsExpired,
    bool IsSubmitted,
    IReadOnlyList<string> PartAQuestionIds,
    IReadOnlyList<string> PartBQuestionIds,
    IReadOnlyList<string> PartBSelectedIds,
    /// <summary>Question IDs the student has submitted an answer for.
    /// Multi-part subparts use composite "{qid}:{subpartId}" keys here
    /// so the SPA can drive per-subpart "answered" indicators.</summary>
    IReadOnlyList<string> AnsweredIds);

/// <summary>Student picks the Part-B subset they will answer.</summary>
public sealed record SelectPartBRequest(IReadOnlyList<string> SelectedQuestionIds);

/// <summary>
/// Per-question answer submission during the run. <see cref="SubpartId"/>
/// is non-null only when the question is multi-part (a/b/c). Single-cell
/// questions omit it; multi-part submissions specify which subpart this
/// answer is for.
/// </summary>
public sealed record SubmitAnswerRequest(
    string QuestionId,
    string Answer,
    string? SubpartId = null);

/// <summary>
/// Phase 3 #8 — bulk-answer submission. The runner uses this on submit-
/// flush so a multi-part exam (3 subparts × 7 Q's = 21 surfaces)
/// collapses to one round-trip instead of 21. Server applies all
/// entries atomically; whole batch rolls back on validation failure.
/// </summary>
public sealed record SubmitAnswersBulkRequest(IReadOnlyList<SubmitAnswerRequest> Answers);

/// <summary>Per-section breakdown on the mark sheet (Ministry-style).</summary>
public sealed record MockExamSectionResult(
    string SectionLabel,    // "A" / "B"
    int Attempted,
    int Correct,
    int PointsAwarded,
    int TotalPoints);

/// <summary>Final mark sheet returned after submission.</summary>
public sealed record MockExamResultResponse(
    string RunId,
    string ExamCode,
    string? PaperCode,
    int TotalQuestions,
    int QuestionsAttempted,
    int QuestionsCorrect,
    /// <summary>Phase 1E: percentage based on Ministry-weighted points
    /// (PointsAwarded / TotalPoints), not the raw correct/attempted ratio.</summary>
    double ScorePercent,
    TimeSpan TimeTaken,
    TimeSpan TimeLimit,
    int VisibilityWarnings,
    IReadOnlyList<MockExamPerQuestionResult> PerQuestion,
    int PointsAwarded,
    int TotalPoints,
    IReadOnlyList<MockExamSectionResult> PerSection);

/// <summary>Per-question grading line item (Phase 1E adds Points/PointsAwarded;
/// Phase 2A adds Subparts when the question is multi-part).</summary>
public sealed record MockExamPerQuestionResult(
    string QuestionId,
    string Section, // "A" | "B"
    bool Attempted,
    bool? Correct,        // null = ungraded (e.g., not selected from Part B)
    string? StudentAnswer,
    string? CanonicalAnswer,
    /// <summary>Engine that decided the verdict (mathnet / sympy /
    /// multipart-cas / not-graded).</summary>
    string GradingEngine,
    int Points,
    int PointsAwarded,
    /// <summary>Phase 2A — when non-null, the question is multi-part and
    /// each subpart has its own line. <see cref="StudentAnswer"/> +
    /// <see cref="CanonicalAnswer"/> are null on the parent in this case
    /// — the answers live on the subpart records.</summary>
    IReadOnlyList<MockExamSubpartResult>? Subparts = null);

/// <summary>Per-subpart grading detail (a/b/c).</summary>
public sealed record MockExamSubpartResult(
    string SubpartId,
    bool Attempted,
    bool? Correct,
    string? StudentAnswer,
    string? CanonicalAnswer,
    string GradingEngine,
    int Points,
    int PointsAwarded);
