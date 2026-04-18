// =============================================================================
// Cena Platform — Student Advancement Events (RDY-061 Phase 2)
//
// Event-sourced aggregate per enrollment. Events are emitted by the
// StudentAdvancementReducer in response to ConceptMastered_V1 events
// from the mastery engine, or by teacher override endpoints.
//
// Stream id convention: "advancement-{studentId}-{trackId}".
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Student is now enrolled on a syllabus and the initial chapter-status
/// map has been established (first chapter Unlocked, rest Locked).
/// </summary>
public sealed record AdvancementStarted_V1(
    string AdvancementId,       // "advancement-{studentId}-{trackId}"
    string StudentId,
    string TrackId,
    string SyllabusId,
    string SyllabusVersion,
    string[] ChapterIds,        // full ordered list snapshot at start time
    string FirstChapterId,      // the one we auto-Unlock
    DateTimeOffset StartedAt
) : IDelegatedEvent;

/// <summary>
/// Chapter transitioned from Locked → Unlocked because all its prereq
/// chapters reached Mastered status. Also fires on the first chapter at
/// advancement-start time.
/// </summary>
public sealed record ChapterUnlocked_V1(
    string AdvancementId,
    string ChapterId,
    DateTimeOffset UnlockedAt,
    string Reason               // "prereqs_mastered" | "initial" | "teacher_override"
) : IDelegatedEvent;

/// <summary>
/// Student attempted a question in this chapter for the first time.
/// Emitted once per chapter, idempotent on re-fire.
/// </summary>
public sealed record ChapterStarted_V1(
    string AdvancementId,
    string ChapterId,
    DateTimeOffset FirstAttemptAt
) : IDelegatedEvent;

/// <summary>
/// All learning objectives in the chapter crossed the mastery threshold.
/// </summary>
public sealed record ChapterMastered_V1(
    string AdvancementId,
    string ChapterId,
    float MasteryScore,         // 0..1 averaged across objectives
    int QuestionsAttempted,
    DateTimeOffset MasteredAt
) : IDelegatedEvent;

/// <summary>
/// A previously-mastered chapter's retention decayed below threshold
/// (BKT forgetting curve). Triggers spiral review on the next session.
/// </summary>
public sealed record ChapterDecayDetected_V1(
    string AdvancementId,
    string ChapterId,
    float CurrentRetention,     // 0..1
    DateTimeOffset DetectedAt
) : IDelegatedEvent;

/// <summary>
/// Student finished the spiral-review micro-session; retention score
/// recomputed.
/// </summary>
public sealed record SpiralReviewCompleted_V1(
    string AdvancementId,
    string ChapterId,
    float RetentionAfterReview, // 0..1
    int ReviewQuestionCount,
    DateTimeOffset ReviewedAt
) : IDelegatedEvent;

/// <summary>
/// Teacher / admin manually overrode a chapter status. Audit-logged.
/// </summary>
public sealed record ChapterOverriddenByTeacher_V1(
    string AdvancementId,
    string ChapterId,
    string NewStatus,           // "Locked" | "Unlocked" | "InProgress" | "Mastered" | "NeedsReview"
    string OverriddenBy,        // admin user id
    string Rationale,           // required, audit surface
    DateTimeOffset OverriddenAt
) : IDelegatedEvent;
