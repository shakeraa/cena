// =============================================================================
// Cena Platform -- Domain Event Upcasters
// Layer: Configuration | Runtime: .NET 9
//
// DATA-009: Concrete upcasters for event schema evolution.
// Each upcaster transforms V(N) -> V(N+1) during Marten stream reads.
// See Cena.Infrastructure.EventStore.EventUpcaster<TOld,TNew> for the pattern.
// =============================================================================

using Cena.Actors.Events;
using Cena.Infrastructure.EventStore;

namespace Cena.Actors.Configuration;

/// <summary>
/// Upcasts <see cref="ConceptAttempted_V1"/> to <see cref="ConceptAttempted_V2"/>.
/// Added field: <c>Duration</c> (defaults to <see cref="TimeSpan.Zero"/> for old events).
/// </summary>
public sealed class ConceptAttemptedV1ToV2Upcaster
    : EventUpcaster<ConceptAttempted_V1, ConceptAttempted_V2>
{
    /// <summary>Singleton instance used during Marten registration.</summary>
    public static readonly ConceptAttemptedV1ToV2Upcaster Instance = new();

    protected override ConceptAttempted_V2 Upcast(ConceptAttempted_V1 old) => new(
        StudentId: old.StudentId,
        ConceptId: old.ConceptId,
        SessionId: old.SessionId,
        IsCorrect: old.IsCorrect,
        ResponseTimeMs: old.ResponseTimeMs,
        QuestionId: old.QuestionId,
        QuestionType: old.QuestionType,
        MethodologyActive: old.MethodologyActive,
        ErrorType: old.ErrorType,
        PriorMastery: old.PriorMastery,
        PosteriorMastery: old.PosteriorMastery,
        HintCountUsed: old.HintCountUsed,
        WasSkipped: old.WasSkipped,
        AnswerHash: old.AnswerHash,
        BackspaceCount: old.BackspaceCount,
        AnswerChangeCount: old.AnswerChangeCount,
        WasOffline: old.WasOffline,
        Timestamp: old.Timestamp,
        Duration: TimeSpan.Zero, // V1 events lack duration; default to zero
        QuestionDifficulty: old.QuestionDifficulty,
        DifficultyGap: old.DifficultyGap,
        DifficultyFrame: old.DifficultyFrame,
        FocusState: old.FocusState
    );
}

// =============================================================================
// FIND-pedagogy-008 — Question creation V1 → V2 upcasters
//
// V2 adds LearningObjectiveId; V1 events default to null so old streams
// replay cleanly without losing fidelity. See Wiggins &amp; McTighe (2005) and
// Anderson &amp; Krathwohl (2001) for the pedagogical rationale.
// =============================================================================

/// <summary>
/// Upcasts <see cref="QuestionAuthored_V1"/> to <see cref="QuestionAuthored_V2"/>.
/// Adds a null <c>LearningObjectiveId</c>.
/// </summary>
public sealed class QuestionAuthoredV1ToV2Upcaster
    : EventUpcaster<QuestionAuthored_V1, QuestionAuthored_V2>
{
    public static readonly QuestionAuthoredV1ToV2Upcaster Instance = new();

    protected override QuestionAuthored_V2 Upcast(QuestionAuthored_V1 old) => new(
        QuestionId: old.QuestionId,
        Stem: old.Stem,
        StemHtml: old.StemHtml,
        Options: old.Options,
        Subject: old.Subject,
        Topic: old.Topic,
        Grade: old.Grade,
        BloomsLevel: old.BloomsLevel,
        Difficulty: old.Difficulty,
        ConceptIds: old.ConceptIds,
        Language: old.Language,
        AuthorId: old.AuthorId,
        Timestamp: old.Timestamp,
        Explanation: old.Explanation,
        LearningObjectiveId: null
    );
}

/// <summary>
/// Upcasts <see cref="QuestionIngested_V1"/> to <see cref="QuestionIngested_V2"/>.
/// Adds a null <c>LearningObjectiveId</c>.
/// </summary>
public sealed class QuestionIngestedV1ToV2Upcaster
    : EventUpcaster<QuestionIngested_V1, QuestionIngested_V2>
{
    public static readonly QuestionIngestedV1ToV2Upcaster Instance = new();

    protected override QuestionIngested_V2 Upcast(QuestionIngested_V1 old) => new(
        QuestionId: old.QuestionId,
        Stem: old.Stem,
        StemHtml: old.StemHtml,
        Options: old.Options,
        Subject: old.Subject,
        Topic: old.Topic,
        Grade: old.Grade,
        BloomsLevel: old.BloomsLevel,
        Difficulty: old.Difficulty,
        ConceptIds: old.ConceptIds,
        Language: old.Language,
        SourceDocId: old.SourceDocId,
        SourceUrl: old.SourceUrl,
        SourceFilename: old.SourceFilename,
        OriginalText: old.OriginalText,
        ImportedBy: old.ImportedBy,
        Timestamp: old.Timestamp,
        Explanation: old.Explanation,
        LearningObjectiveId: null
    );
}

/// <summary>
/// Upcasts <see cref="QuestionAiGenerated_V1"/> to <see cref="QuestionAiGenerated_V2"/>.
/// Adds a null <c>LearningObjectiveId</c>.
/// </summary>
public sealed class QuestionAiGeneratedV1ToV2Upcaster
    : EventUpcaster<QuestionAiGenerated_V1, QuestionAiGenerated_V2>
{
    public static readonly QuestionAiGeneratedV1ToV2Upcaster Instance = new();

    protected override QuestionAiGenerated_V2 Upcast(QuestionAiGenerated_V1 old) => new(
        QuestionId: old.QuestionId,
        Stem: old.Stem,
        StemHtml: old.StemHtml,
        Options: old.Options,
        Subject: old.Subject,
        Topic: old.Topic,
        Grade: old.Grade,
        BloomsLevel: old.BloomsLevel,
        Difficulty: old.Difficulty,
        ConceptIds: old.ConceptIds,
        Language: old.Language,
        PromptText: old.PromptText,
        ModelId: old.ModelId,
        ModelTemperature: old.ModelTemperature,
        RawModelOutput: old.RawModelOutput,
        RequestedBy: old.RequestedBy,
        Explanation: old.Explanation,
        Timestamp: old.Timestamp,
        LearningObjectiveId: null
    );
}

// =============================================================================
// TENANCY-P2a — Enrollment-scoped mastery upcasters
//
// V2→V3 (ConceptAttempted): adds EnrollmentId, defaults to "default" for
// legacy streams. V1→V2 (ConceptMastered): same pattern.
// =============================================================================

/// <summary>
/// Upcasts <see cref="ConceptAttempted_V2"/> to <see cref="ConceptAttempted_V3"/>.
/// Adds EnrollmentId defaulting to "default" for legacy events.
/// </summary>
public sealed class ConceptAttemptedV2ToV3Upcaster
    : EventUpcaster<ConceptAttempted_V2, ConceptAttempted_V3>
{
    public static readonly ConceptAttemptedV2ToV3Upcaster Instance = new();

    protected override ConceptAttempted_V3 Upcast(ConceptAttempted_V2 old) => new(
        StudentId: old.StudentId,
        ConceptId: old.ConceptId,
        SessionId: old.SessionId,
        IsCorrect: old.IsCorrect,
        ResponseTimeMs: old.ResponseTimeMs,
        QuestionId: old.QuestionId,
        QuestionType: old.QuestionType,
        MethodologyActive: old.MethodologyActive,
        ErrorType: old.ErrorType,
        PriorMastery: old.PriorMastery,
        PosteriorMastery: old.PosteriorMastery,
        HintCountUsed: old.HintCountUsed,
        WasSkipped: old.WasSkipped,
        AnswerHash: old.AnswerHash,
        BackspaceCount: old.BackspaceCount,
        AnswerChangeCount: old.AnswerChangeCount,
        WasOffline: old.WasOffline,
        Timestamp: old.Timestamp,
        Duration: old.Duration,
        EnrollmentId: "default",
        QuestionDifficulty: old.QuestionDifficulty,
        DifficultyGap: old.DifficultyGap,
        DifficultyFrame: old.DifficultyFrame,
        FocusState: old.FocusState
    );
}

/// <summary>
/// Upcasts <see cref="ConceptMastered_V1"/> to <see cref="ConceptMastered_V2"/>.
/// Adds EnrollmentId defaulting to "default" for legacy events.
/// </summary>
public sealed class ConceptMasteredV1ToV2Upcaster
    : EventUpcaster<ConceptMastered_V1, ConceptMastered_V2>
{
    public static readonly ConceptMasteredV1ToV2Upcaster Instance = new();

    protected override ConceptMastered_V2 Upcast(ConceptMastered_V1 old) => new(
        StudentId: old.StudentId,
        ConceptId: old.ConceptId,
        SessionId: old.SessionId,
        MasteryLevel: old.MasteryLevel,
        TotalAttempts: old.TotalAttempts,
        TotalSessions: old.TotalSessions,
        MethodologyAtMastery: old.MethodologyAtMastery,
        InitialHalfLifeHours: old.InitialHalfLifeHours,
        Timestamp: old.Timestamp,
        EnrollmentId: "default"
    );
}
