// =============================================================================
// Cena Platform — StuckContext (RDY-063 Phase 1)
//
// Session-scoped input to the stuck-type classifier. Carries only what
// the classifier legitimately needs to decide a label:
//   - The question (id, text-by-locale, chapter, LOs)
//   - Advancement snapshot (current chapter, status, retention) from RDY-061
//   - Attempts on THIS question (last N, session-scoped)
//   - Aggregate session signals (time-on-question, prior hints, items bailed)
//
// What StuckContext MUST NOT carry (enforced by type shape + arch test):
//   - studentId, studentName, email, schoolId, demographic fields
//   - cross-session history, prior-day attempts, long-term misconception log
//   - any free-text that wasn't already scrubbed by TutorPromptScrubber
//
// Anonymisation: the caller builds StuckContext with `StudentAnonId`,
// an HMAC(studentId, sessionId, salt) truncated to 16 chars. It is stable
// within the session (so we can dedupe diagnoses) and unrecoverable
// outside it (so the classifier cannot join across sessions even if
// the output leaked).
// =============================================================================

namespace Cena.Actors.Diagnosis;

/// <summary>
/// Immutable input record for a single classification call. All string
/// collections are freshly allocated by the caller — the classifier
/// treats StuckContext as a snapshot.
/// </summary>
public sealed record StuckContext(
    string SessionId,
    string StudentAnonId,
    StuckContextQuestion Question,
    StuckContextAdvancement Advancement,
    IReadOnlyList<StuckContextAttempt> Attempts,
    StuckContextSessionSignals SessionSignals,
    string Locale,
    DateTimeOffset AsOf
);

public sealed record StuckContextQuestion(
    string QuestionId,
    string? CanonicalTextByLocaleScrubbed,   // TutorPromptScrubber output; null if absent
    string? ChapterId,                        // may be null before RDY-061 advancement starts
    IReadOnlyList<string> LearningObjectiveIds,
    string? QuestionType,                     // "mcq" | "free_response" | "proof" | ...
    float? QuestionDifficulty                 // 0..1 IRT difficulty estimate if available
);

public sealed record StuckContextAdvancement(
    string? CurrentChapterId,
    string? CurrentChapterStatus,             // "Unlocked" | "InProgress" | "Mastered" | ...
    float CurrentChapterRetention,            // 0..1 from RDY-061 state
    int ChaptersMasteredCount,
    int ChaptersTotalCount
);

/// <summary>
/// One attempt on the current question. Session-scoped; never cross-session.
/// <paramref name="LatexInputScrubbed"/> is already PII-scrubbed; classifier
/// must not re-scrub, but architecture test enforces no raw PII leaks.
/// </summary>
public sealed record StuckContextAttempt(
    DateTimeOffset SubmittedAt,
    string? LatexInputScrubbed,
    bool WasCorrect,
    int TimeSincePrevAttemptSec,
    float InputChangeRatio,                   // character-delta vs prior attempt, 0..1
    string? ErrorType                         // canonical error-class tag, if evaluator set one
);

public sealed record StuckContextSessionSignals(
    int TimeOnQuestionSec,
    int HintsRequestedSoFar,
    int ItemsSolvedInSession,
    int ItemsBailedInSession,
    double RecentAccuracy,                    // 0..1, rolling-5 on session
    double ResponseTimeRatio                  // response / baseline, 0..∞
);
