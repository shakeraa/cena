// =============================================================================
// Cena Platform — IHintStuckShadowService (RDY-063 Phase 2a)
//
// "Shadow mode" integration into the live hint endpoint. Runs the
// stuck-type classifier alongside the hint request — never changes the
// hint response, only logs + persists the diagnosis for observability.
//
// Once we have ≥2 weeks of real distribution data, Phase 2b flips the
// classifier output into hint selection. Shadow mode keeps the rollout
// risk-free.
// =============================================================================

using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Diagnosis;

public interface IHintStuckShadowService
{
    /// <summary>
    /// Build a StuckContext from the live session queue + question state
    /// and run the classifier. Never throws — all failures are swallowed
    /// and metricised. Intended to be called from the hint endpoint as
    /// fire-and-forget (though the method returns Task for testability).
    /// </summary>
    Task RecordShadowDiagnosisAsync(
        string studentId,
        string sessionId,
        string questionId,
        LearningSessionQueueProjection queue,
        QuestionDocument question,
        int hintLevel,
        string locale,
        CancellationToken ct = default);
}
