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
