// =============================================================================
// Cena Platform — Wire-response builders for the mock-exam runner.
//
// Pure static helpers that materialise the SPA-facing DTOs from
// ExamSimulationState. Extracted from MockExamRunService for the
// 500-LOC ratchet (ADR-0012); zero behaviour change.
// =============================================================================

namespace Cena.Actors.Assessment;

internal static class MockExamWireResponses
{
    public static MockExamRunStartedResponse BuildStarted(ExamSimulationState s, string? paperCode) =>
        new(
            RunId: s.SimulationId,
            ExamCode: s.ExamCode,
            PaperCode: paperCode,
            TimeLimitMinutes: s.Format.TimeLimitMinutes,
            ExtraTimeMinutes: s.ExtraTimeMinutes,
            PartAQuestionCount: s.Format.PartAQuestionCount,
            PartBQuestionCount: s.Format.PartBQuestionCount,
            PartBRequiredCount: s.Format.PartBRequiredCount,
            PartAQuestionIds: s.PartAQuestionIds,
            PartBQuestionIds: s.PartBQuestionIds,
            StartedAt: s.StartedAt,
            Deadline: s.Deadline);

    public static MockExamRunStateResponse BuildState(ExamSimulationState s) =>
        new(
            RunId: s.SimulationId,
            ExamCode: s.ExamCode,
            PaperCode: null,
            TimeLimitMinutes: s.Format.TimeLimitMinutes,
            ExtraTimeMinutes: s.ExtraTimeMinutes,
            StartedAt: s.StartedAt,
            Deadline: s.Deadline,
            IsExpired: s.IsExpired(DateTimeOffset.UtcNow),
            IsSubmitted: s.IsSubmitted,
            PartAQuestionIds: s.PartAQuestionIds,
            PartBQuestionIds: s.PartBQuestionIds,
            PartBSelectedIds: s.PartBSelectedIds,
            AnsweredIds: s.Answers.Keys.ToList(),
            CalculatorPolicy: string.IsNullOrEmpty(s.CalculatorPolicy) ? "Allowed" : s.CalculatorPolicy,
            FormulaSheetMode: string.IsNullOrEmpty(s.FormulaSheetMode) ? "None" : s.FormulaSheetMode,
            IsPaused: s.IsPaused,
            TotalPausedMs: s.TotalPausedMs);
}
