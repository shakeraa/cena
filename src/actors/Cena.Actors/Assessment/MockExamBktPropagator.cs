// =============================================================================
// Cena Platform — Mock-exam BKT propagator (extracted from MockExamRunService)
//
// Single-responsibility: take a graded MockExamResultResponse and update the
// BKT mastery store. Best-effort by design — a BKT outage cannot fail the
// student's submit (their mark sheet is already durable; we just lose
// adaptive signal until the next submit).
//
// Maps:
//   * exam-code → ExamTargetCode  (806 → bagrut-math-5yu, etc.)
//   * QuestionId → SkillCode      (via Topic field on QuestionDocument /
//                                   BagrutMultipartQuestion, parsed by
//                                   SkillCode.Parse).
//
// Multi-part Q's: each subpart with a graded verdict is an independent
// observation at the parent skill (per ADR-0048 / PRR-289 design — sub-parts
// are independent skill exercises in Bagrut convention).
//
// ADR alignment:
//   * ADR-0002 — BKT update fires only when the grader has produced a
//     verdict. Ungraded / unattempted / Part-B-unselected questions are
//     skipped, not silently zero-credited.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment;

public sealed class MockExamBktPropagator
{
    private readonly IBktStateTracker? _bktTracker;
    private readonly IDocumentStore _store;
    private readonly ILogger _logger;

    private static readonly Meter Meter = new("Cena.MockExam.Bkt", "1.0.0");
    private static readonly Counter<long> BktObservations =
        Meter.CreateCounter<long>("cena_mock_exam_bkt_observations_total");
    private static readonly Counter<long> BktSkipped =
        Meter.CreateCounter<long>("cena_mock_exam_bkt_observations_skipped_total");

    public MockExamBktPropagator(
        IBktStateTracker? bktTracker,
        IDocumentStore store,
        ILogger logger)
    {
        _bktTracker = bktTracker;
        _store = store;
        _logger = logger;
    }

    public async Task RecordAsync(
        ExamSimulationState state,
        MockExamResultResponse result,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        if (_bktTracker is null) return;
        if (!TryMapExamCodeToTargetCode(state.ExamCode, out var examTargetCode))
        {
            _logger.LogInformation(
                "[MOCK-EXAM-BKT] skip runId={RunId} reason=unmapped_exam_code examCode={ExamCode}",
                state.SimulationId, state.ExamCode);
            return;
        }

        try
        {
            // Batched-load topic mapping for every gradable question id in
            // the result. Doing this once here (rather than re-deriving from
            // grader internals) keeps the BKT path independent of grading —
            // if the grade shape evolves, BKT keeps working as long as
            // PerQuestion.QuestionId remains stable.
            var questionIds = result.PerQuestion.Select(pq => pq.QuestionId).ToList();
            var topicById = await LoadQuestionTopicsAsync(questionIds, ct).ConfigureAwait(false);

            int observed = 0;
            int skipped = 0;
            foreach (var pq in result.PerQuestion)
            {
                if (!topicById.TryGetValue(pq.QuestionId, out var topic)
                    || string.IsNullOrWhiteSpace(topic))
                {
                    if (PerQuestionHasObservation(pq))
                    {
                        BktSkipped.Add(1,
                            new KeyValuePair<string, object?>("reason", "missing_topic"));
                        skipped++;
                    }
                    continue;
                }

                SkillCode skillCode;
                try
                {
                    skillCode = SkillCode.Parse(topic);
                }
                catch (ArgumentException)
                {
                    if (PerQuestionHasObservation(pq))
                    {
                        BktSkipped.Add(1,
                            new KeyValuePair<string, object?>("reason", "invalid_topic_format"));
                        skipped++;
                    }
                    continue;
                }

                if (pq.Subparts is { Count: > 0 })
                {
                    foreach (var sp in pq.Subparts)
                    {
                        if (!sp.Correct.HasValue)
                        {
                            BktSkipped.Add(1,
                                new KeyValuePair<string, object?>("reason", "ungraded_subpart"));
                            skipped++;
                            continue;
                        }
                        await _bktTracker.UpdateAsync(
                            studentAnonId: state.StudentId,
                            examTargetCode: examTargetCode,
                            skillCode: skillCode,
                            isCorrect: sp.Correct.Value,
                            occurredAt: occurredAt,
                            ct: ct).ConfigureAwait(false);
                        observed++;
                    }
                }
                else if (pq.Correct.HasValue)
                {
                    await _bktTracker.UpdateAsync(
                        studentAnonId: state.StudentId,
                        examTargetCode: examTargetCode,
                        skillCode: skillCode,
                        isCorrect: pq.Correct.Value,
                        occurredAt: occurredAt,
                        ct: ct).ConfigureAwait(false);
                    observed++;
                }
                else
                {
                    BktSkipped.Add(1,
                        new KeyValuePair<string, object?>("reason", "ungraded_question"));
                    skipped++;
                }
            }

            BktObservations.Add(observed,
                new KeyValuePair<string, object?>("examCode", state.ExamCode));
            _logger.LogInformation(
                "[MOCK-EXAM-BKT] runId={RunId} studentId={StudentId} examTarget={Target} observations={Observed} skipped={Skipped}",
                state.SimulationId, state.StudentId, examTargetCode.Value, observed, skipped);
        }
        catch (Exception ex)
        {
            // Best-effort: BKT outage MUST NOT fail the submit. The student's
            // mark sheet is already durable; we lose adaptive signal until
            // the next submit, which is acceptable.
            _logger.LogWarning(ex,
                "[MOCK-EXAM-BKT] BKT propagation failed for runId={RunId} studentId={StudentId} — submit succeeds, signal dropped.",
                state.SimulationId, state.StudentId);
        }
    }

    /// <summary>
    /// Per-question observation predicate: does this row produce ANY graded
    /// verdict (top-level or via at least one graded subpart)? Used to
    /// distinguish "skipped because grading is null" from "skipped because
    /// taxonomy is missing" in the metrics.
    /// </summary>
    private static bool PerQuestionHasObservation(MockExamPerQuestionResult pq)
    {
        if (pq.Subparts is { Count: > 0 })
            return pq.Subparts.Any(s => s.Correct.HasValue);
        return pq.Correct.HasValue;
    }

    /// <summary>
    /// Map a Bagrut exam-code (numeric שאלון) to the canonical
    /// <see cref="ExamTargetCode"/> the mastery store keys on. Returns false
    /// for unmapped codes so the caller can log + no-op rather than throw.
    ///
    ///   806 → bagrut-math-5yu
    ///   807 → bagrut-math-4yu
    ///   036 → bagrut-physics-5yu
    ///
    /// New codes added to MockExamRunService.SubjectForExamCode MUST be
    /// added here too — the parity is enforced by MockExamBktPropagationTests.
    /// </summary>
    public static bool TryMapExamCodeToTargetCode(string examCode, out ExamTargetCode code)
    {
        var raw = examCode switch
        {
            "806" => "bagrut-math-5yu",
            "807" => "bagrut-math-4yu",
            "036" => "bagrut-physics-5yu",
            _     => null,
        };
        if (raw is null)
        {
            code = default;
            return false;
        }
        return ExamTargetCode.TryParse(raw, out code);
    }

    /// <summary>
    /// Single batched query that returns Topic for every question id in
    /// <paramref name="questionIds"/>. Handles both Marten doc types
    /// (<see cref="QuestionDocument"/> + <see cref="BagrutMultipartQuestion"/>)
    /// since either may carry a graded item.
    /// </summary>
    private async Task<Dictionary<string, string?>> LoadQuestionTopicsAsync(
        IReadOnlyList<string> questionIds, CancellationToken ct)
    {
        var topics = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (questionIds.Count == 0) return topics;

        await using var session = _store.QuerySession();

        var docs = await session.Query<QuestionDocument>()
            .Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.Topic })
            .ToListAsync(ct);
        foreach (var d in docs) topics[d.Id] = d.Topic;

        var multipart = await session.Query<BagrutMultipartQuestion>()
            .Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.Topic })
            .ToListAsync(ct);
        foreach (var d in multipart)
        {
            // Multipart wins when it provides a non-empty Topic — either
            // store carries the same logical topic, but multipart's Topic
            // field is non-nullable while QuestionDocument's is nullable.
            if (!string.IsNullOrWhiteSpace(d.Topic)) topics[d.Id] = d.Topic;
        }

        return topics;
    }
}
