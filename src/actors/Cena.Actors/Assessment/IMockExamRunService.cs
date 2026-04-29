// =============================================================================
// Cena Platform — IMockExamRunService contract
//
// Exam-prep run lifecycle. The endpoint layer translates HTTP →
// service-call → HTTP. Aggregate / event work is owned by the impl.
// =============================================================================

namespace Cena.Actors.Assessment;

public interface IMockExamRunService
{
    /// <summary>
    /// Start a new mock-exam run for <paramref name="studentId"/>. Idempotent
    /// per (studentId, examCode) for an unsubmitted in-flight run: returns
    /// the existing run instead of provisioning a duplicate. Publishes
    /// <c>ExamSimulationStarted_V1</c> on success.
    /// </summary>
    Task<MockExamRunStartedResponse> StartAsync(
        string studentId,
        StartMockExamRunRequest request,
        CancellationToken ct);

    /// <summary>Read current run state. 404 if no such run for the student.</summary>
    Task<MockExamRunStateResponse?> GetStateAsync(
        string studentId,
        string runId,
        CancellationToken ct);

    /// <summary>
    /// Persist the student's Part-B subset selection. Validates the
    /// selection is exactly <c>PartBRequiredCount</c> items drawn from
    /// the run's <c>PartBQuestionIds</c>.
    /// </summary>
    Task<MockExamRunStateResponse> SelectPartBAsync(
        string studentId,
        string runId,
        SelectPartBRequest request,
        CancellationToken ct);

    /// <summary>
    /// Save a per-question answer. Idempotent per (runId, questionId) —
    /// last write wins. Rejected if the run is submitted or expired.
    /// </summary>
    Task<MockExamRunStateResponse> SubmitAnswerAsync(
        string studentId,
        string runId,
        SubmitAnswerRequest request,
        CancellationToken ct);

    /// <summary>
    /// Phase 3 #8 — bulk apply N answers atomically. Either all entries
    /// land or none. Same validation rules as <see cref="SubmitAnswerAsync"/>.
    /// </summary>
    Task<MockExamRunStateResponse> SubmitAnswersBulkAsync(
        string studentId,
        string runId,
        SubmitAnswersBulkRequest request,
        CancellationToken ct);

    /// <summary>
    /// Final submit. Triggers grading (CAS-routed per question), persists
    /// the result, emits <c>ExamSimulationSubmitted_V2</c>, returns the
    /// mark sheet.
    /// </summary>
    Task<MockExamResultResponse> SubmitAsync(
        string studentId,
        string runId,
        CancellationToken ct);

    /// <summary>Read the final mark sheet. Available only after submit.</summary>
    Task<MockExamResultResponse?> GetResultAsync(
        string studentId,
        string runId,
        CancellationToken ct);

    /// <summary>
    /// Phase 1D — read a single question's preview (prompt + topic +
    /// bloom). Returns null if the qid isn't part of the run, or the
    /// run isn't owned by the student. The delivery-gate
    /// chokepoint (ExamSimulationDelivery.AssertDeliverable) is
    /// invoked before serializing — Ministry-derived items never
    /// reach this surface.
    /// </summary>
    Task<MockExamQuestionPreview?> GetQuestionPreviewAsync(
        string studentId,
        string runId,
        string questionId,
        CancellationToken ct);
}

public sealed record MockExamQuestionPreview(
    string QuestionId,
    string Prompt,
    string? Topic,
    int BloomsLevel,
    /// <summary>Phase 2A — when non-null, the runner renders one input
    /// per subpart. PartId is locale-free ("a","b","c"); display label
    /// comes from i18n. The Prompt above carries the shared question
    /// stem; each subpart carries its own per-part prompt + point
    /// weight. CanonicalAnswer is intentionally omitted — only the
    /// grader sees that.</summary>
    IReadOnlyList<MockExamSubpartPreview>? Subparts = null);

public sealed record MockExamSubpartPreview(string PartId, string Prompt, int Points);
