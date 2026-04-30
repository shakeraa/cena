// =============================================================================
// Cena Platform — Mock-exam grader (extracted from MockExamRunService)
//
// Single-responsibility: turn an ExamSimulationState plus the canonical
// QuestionDocument / BagrutMultipartQuestion store into a Ministry-style
// MockExamResultResponse. CAS retry, multi-part subpart envelope-share
// distribution, section-weighted scoring, and per-question pacing overlay
// all live here so the runner orchestrator (start / answer / submit /
// pause / resume) stays focused on state-machine and event-sourcing
// concerns.
//
// ADR alignment:
//   * ADR-0002 — grading uses ICasRouterService. The grader is the only
//     correctness oracle; LLM never decides correctness.
//   * Phase 3 #3 — multi-part envelope shares use floor-then-remainder
//     so envelope == sum-of-shares is invariant (the 25-pt slot with
//     10/15/25 sub-share split rounding bug is closed here).
//   * PRR-297 — multi-attempt CAS retry with exponential backoff (3
//     attempts at 150ms / 400ms / 1200ms jittered).
//   * PRR-299 — per-question pacing overlay; pure function so the
//     existing OverlayPerQuestionPacing tests continue to exercise it
//     in isolation (no I/O).
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Actors.Assessment;

public sealed class MockExamGrader
{
    private readonly IBagrutPaperStructureCatalog _structureCatalog;
    private readonly ICasRouterService _cas;
    private readonly TimeProvider _clock;

    private static readonly Meter Meter = new("Cena.MockExam.Grader", "1.0.0");
    private static readonly Histogram<double> GradeDurationMs =
        Meter.CreateHistogram<double>("cena_mock_exam_grade_duration_ms");

    // PRR-322: ambient counter the grader bumps each time it invokes the
    // CAS router (including PRR-297 retries). Stored as AsyncLocal of a
    // *reference type* so child-task mutations propagate back to the
    // parent that opened the scope. The shape is a single-element int
    // array because AsyncLocal<int?> would lose mutations from the child
    // (AsyncLocal flows DOWN across awaits but writes in the child do
    // NOT flow back UP — caught by PRR-322f-integration-test 2026-04-30
    // which exercised the SubmitAsync path against live cena-postgres
    // and observed CasCallsCount=0 instead of the expected 7).
    private static readonly AsyncLocal<int[]?> _casAttemptCounter = new();

    public MockExamGrader(
        IBagrutPaperStructureCatalog structureCatalog,
        ICasRouterService cas,
        TimeProvider? clock = null)
    {
        _structureCatalog = structureCatalog;
        _cas = cas;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Compute the Ministry-style mark sheet for a submitted run. Reads
    /// canonical answers from <paramref name="session"/> (single-cell via
    /// <see cref="QuestionDocument"/>; multi-part via
    /// <see cref="BagrutMultipartQuestion"/>) and student answers from
    /// <paramref name="state"/>.
    /// </summary>
    public async Task<MockExamResultResponse> GradeAsync(
        IQuerySession session, ExamSimulationState state, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await GradeInternalAsync(session, state, ct);
        }
        finally
        {
            sw.Stop();
            GradeDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("examCode", state.ExamCode));
        }
    }

    /// <summary>
    /// PRR-322 — grade variant that records every CAS-call attempt
    /// (including PRR-297 retries) into <paramref name="casAttempts"/> so
    /// MockExamRunService.SubmitAsync can persist a MockExamRunCost doc.
    /// SubmitAsync uses this; StartAsync auto-grade and GetResultAsync use
    /// the bare GradeAsync to avoid double-counting on idempotent re-grades.
    /// </summary>
    public async Task<(MockExamResultResponse Result, int CasAttempts)>
        GradeAndMeasureAsync(IQuerySession session, ExamSimulationState state, CancellationToken ct)
    {
        // Enter a counter scope. The shared int[] reference flows down
        // to VerifyWithRetryAsync via AsyncLocal; mutations to the array
        // contents are visible here on return because both sides hold
        // the same object reference. Re-assigning _casAttemptCounter.Value
        // inside the child would NOT propagate back — see field comment.
        // Out-of-scope grades see _casAttemptCounter.Value == null and
        // skip the bump path.
        var counter = new int[1];
        _casAttemptCounter.Value = counter;
        try
        {
            var result = await GradeAsync(session, state, ct);
            return (result, counter[0]);
        }
        finally
        {
            _casAttemptCounter.Value = null;
        }
    }

    private async Task<MockExamResultResponse> GradeInternalAsync(
        IQuerySession session, ExamSimulationState state, CancellationToken ct)
    {
        var structure = await _structureCatalog.GetAsync(state.ExamCode, paperCode: null, ct);
        var partASection = structure.Sections.First(s => s.SectionLabel == "A");
        var partBSection = structure.Sections.First(s => s.SectionLabel == "B");

        // For Part B, only the SELECTED ids count toward the mark sheet
        // (real Bagrut: unselected long-form Q's are not graded). If the
        // student never confirmed a Part-B subset, default to the first
        // RequiredAnswers slots for grading purposes (auto-selected).
        var partBSelected = state.PartBSelectedIds.Count > 0
            ? state.PartBSelectedIds
            : state.PartBQuestionIds.Take(state.Format.PartBRequiredCount).ToList();

        var gradableIds = state.PartAQuestionIds.Concat(partBSelected).ToList();

        // Multi-part Q's first (preferred for Part B); whatever ids are
        // multi-part get graded per-subpart. Remaining ids fall through
        // to QuestionDocument single-cell grading.
        var multipartHits = await session.Query<BagrutMultipartQuestion>()
            .Where(q => gradableIds.Contains(q.Id))
            .ToListAsync(ct);
        var multipartById = multipartHits.ToDictionary(q => q.Id);

        var canonicalDocs = await session.Query<QuestionDocument>()
            .Where(q => gradableIds.Contains(q.Id))
            .ToListAsync(ct);
        var byId = canonicalDocs.ToDictionary(q => q.Id);

        // Map qid → slot (so we know its point weight + section).
        var slotByQid = new Dictionary<string, (string section, int points)>();
        for (var i = 0; i < state.PartAQuestionIds.Count; i++)
        {
            var pts = i < partASection.Slots.Count ? partASection.Slots[i].Points : 0;
            slotByQid[state.PartAQuestionIds[i]] = ("A", pts);
        }
        for (var i = 0; i < partBSelected.Count; i++)
        {
            var pts = i < partBSection.Slots.Count ? partBSection.Slots[i].Points : 0;
            slotByQid[partBSelected[i]] = ("B", pts);
        }

        var perQuestion = new List<MockExamPerQuestionResult>(gradableIds.Count);
        var attempted = 0;
        var correct = 0;
        var pointsAwarded = 0;
        var sectionATotalPts = partASection.Slots.Take(partASection.RequiredAnswers).Sum(s => s.Points);
        var sectionBTotalPts = partBSection.Slots.Take(partBSection.RequiredAnswers).Sum(s => s.Points);
        var sectionAAwarded = 0;
        var sectionBAwarded = 0;
        var sectionAAttempted = 0;
        var sectionBAttempted = 0;
        var sectionACorrect = 0;
        var sectionBCorrect = 0;

        foreach (var qid in gradableIds)
        {
            var (section, slotPts) = slotByQid.TryGetValue(qid, out var s) ? s : ("?", 0);

            // ── Multi-part path ──
            if (multipartById.TryGetValue(qid, out var multipart))
            {
                // Distribute the slot's envelope-points across subparts
                // proportionally to the subpart's declared Points (so a
                // 25-pt slot with 10/15 subpart split awards 10/15 of the
                // 25-pt envelope, not the raw subpart points).
                //
                // Phase 3 #3 — Math.Round per subpart can leak rounding
                // (25 split as 33/33/34 → 8.25 + 8.25 + 8.5 → 8/8/9 = 25 ✓
                // but 30/35/35 of 100 → 30/35/35 = 100 ✓; 10/15/25 of 25 →
                // 5/7.5/12.5 → 5/8/13 = 26 ✗). Compute floor for all
                // subparts then assign the remainder to the LAST subpart
                // so envelope == sum-of-shares is invariant.
                var subpartTotal = multipart.TotalPoints;
                var subpartShares = new int[multipart.Subparts.Count];
                if (subpartTotal > 0)
                {
                    for (var i = 0; i < multipart.Subparts.Count; i++)
                        subpartShares[i] = (int)((long)slotPts * multipart.Subparts[i].Points / subpartTotal);
                    var assigned = subpartShares.Sum();
                    if (subpartShares.Length > 0)
                        subpartShares[^1] += slotPts - assigned;
                }

                var qAttempted = false;
                var qPointsAwarded = 0;
                var subpartLines = new List<MockExamSubpartResult>();

                for (var i = 0; i < multipart.Subparts.Count; i++)
                {
                    var subpart = multipart.Subparts[i];
                    var key = $"{qid}:{subpart.PartId}";
                    var hasSubAnswer = state.Answers.TryGetValue(key, out var studentSubAnswer)
                        && !string.IsNullOrWhiteSpace(studentSubAnswer);

                    var envelopeShare = subpartShares[i];

                    if (!hasSubAnswer)
                    {
                        subpartLines.Add(new MockExamSubpartResult(
                            subpart.PartId, false, null, null, subpart.CorrectAnswer,
                            "not-graded", envelopeShare, 0));
                        continue;
                    }

                    qAttempted = true;
                    var verdict = await VerifyWithRetryAsync(studentSubAnswer!, subpart.CorrectAnswer, ct);
                    var awarded = verdict.Verified ? envelopeShare : 0;
                    qPointsAwarded += awarded;
                    subpartLines.Add(new MockExamSubpartResult(
                        subpart.PartId,
                        true,
                        verdict.Status == CasVerifyStatus.Ok ? verdict.Verified : null,
                        studentSubAnswer,
                        subpart.CorrectAnswer,
                        verdict.Engine ?? "cas",
                        envelopeShare,
                        awarded));
                }

                if (qAttempted)
                {
                    attempted++;
                    if (section == "A") sectionAAttempted++; else if (section == "B") sectionBAttempted++;
                }
                if (qPointsAwarded > 0)
                {
                    correct++;
                    pointsAwarded += qPointsAwarded;
                    if (section == "A") { sectionACorrect++; sectionAAwarded += qPointsAwarded; }
                    else if (section == "B") { sectionBCorrect++; sectionBAwarded += qPointsAwarded; }
                }

                perQuestion.Add(new MockExamPerQuestionResult(
                    qid, section, qAttempted,
                    qAttempted ? qPointsAwarded == slotPts : (bool?)null,
                    null, null, "multipart-cas",
                    slotPts, qPointsAwarded, subpartLines));
                continue;
            }

            // ── Single-cell legacy path ──
            var hasAnswer = state.Answers.TryGetValue(qid, out var studentAnswer)
                && !string.IsNullOrWhiteSpace(studentAnswer);
            byId.TryGetValue(qid, out var q);
            var canonical = string.IsNullOrWhiteSpace(q?.CorrectAnswer) ? null : q!.CorrectAnswer;

            if (!hasAnswer)
            {
                perQuestion.Add(new MockExamPerQuestionResult(
                    qid, section, false, null, null, canonical, "not-graded", slotPts, 0));
                continue;
            }

            attempted++;
            if (section == "A") sectionAAttempted++; else if (section == "B") sectionBAttempted++;

            if (string.IsNullOrWhiteSpace(canonical))
            {
                perQuestion.Add(new MockExamPerQuestionResult(
                    qid, section, true, null, studentAnswer, null, "ungradable-no-canonical", slotPts, 0));
                continue;
            }

            var singleVerdict = await VerifyWithRetryAsync(studentAnswer!, canonical, ct);
            var singleAwarded = singleVerdict.Verified ? slotPts : 0;
            if (singleVerdict.Verified)
            {
                correct++;
                pointsAwarded += singleAwarded;
                if (section == "A") { sectionACorrect++; sectionAAwarded += singleAwarded; }
                else if (section == "B") { sectionBCorrect++; sectionBAwarded += singleAwarded; }
            }

            perQuestion.Add(new MockExamPerQuestionResult(
                qid, section, true,
                singleVerdict.Status == CasVerifyStatus.Ok ? singleVerdict.Verified : null,
                studentAnswer, canonical,
                singleVerdict.Engine ?? "cas",
                slotPts, singleAwarded));
        }

        var totalPoints = sectionATotalPts + sectionBTotalPts;
        var scorePercent = totalPoints == 0 ? 0.0 : (100.0 * pointsAwarded / totalPoints);
        var timeTaken = (state.SubmittedAt ?? _clock.GetUtcNow()) - state.StartedAt;

        var perQuestionWithPacing = OverlayPerQuestionPacing(perQuestion, state);

        return new MockExamResultResponse(
            RunId: state.SimulationId,
            ExamCode: state.ExamCode,
            PaperCode: null,
            TotalQuestions: gradableIds.Count,
            QuestionsAttempted: attempted,
            QuestionsCorrect: correct,
            ScorePercent: scorePercent,
            TimeTaken: timeTaken,
            TimeLimit: TimeSpan.FromMinutes(state.Format.TimeLimitMinutes),
            VisibilityWarnings: state.VisibilityEvents.Count,
            PerQuestion: perQuestionWithPacing,
            PointsAwarded: pointsAwarded,
            TotalPoints: totalPoints,
            PerSection: new[]
            {
                new MockExamSectionResult("A", sectionAAttempted, sectionACorrect,
                    sectionAAwarded, sectionATotalPts),
                new MockExamSectionResult("B", sectionBAttempted, sectionBCorrect,
                    sectionBAwarded, sectionBTotalPts),
            });
    }

    /// <summary>
    /// PRR-299 — derive per-question pacing from the timestamp dictionaries.
    ///
    /// Algorithm:
    ///   1. For each question id present in <paramref name="perQuestion"/>,
    ///      collect its answer-keys (single-cell: just the qid; multi-part:
    ///      "{qid}:{subpartId}" for each subpart). Compute:
    ///        firstAnsweredAt(q) = min(state.AnswerTimestamps[k]      ∀ k of q)
    ///        lastAnsweredAt(q)  = max(state.AnswerLastTimestamps[k]  ∀ k of q)
    ///   2. Sort answered questions by firstAnsweredAt ascending.
    ///   3. Walk in sorted order, tracking prevLast (initially StartedAt):
    ///        TimeSpent(q) = firstAnsweredAt(q) − prevLast
    ///        prevLast     = lastAnsweredAt(q)
    ///      Negative deltas are clamped to TimeSpan.Zero (defensive — a
    ///      clock skew or out-of-order answer should not yield "−5 min on Q3").
    ///   4. Rebuild perQuestion preserving the original slot order; un-
    ///      answered rows keep null FirstAnsweredAt + TimeSpent.
    ///
    /// Pure function (no I/O) so unit tests can exercise it directly.
    /// </summary>
    public static IReadOnlyList<MockExamPerQuestionResult> OverlayPerQuestionPacing(
        IReadOnlyList<MockExamPerQuestionResult> perQuestion,
        ExamSimulationState state)
    {
        var firstByQ = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var lastByQ = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var pq in perQuestion)
        {
            var keys = new List<string>(1);
            if (pq.Subparts is { Count: > 0 })
            {
                foreach (var sp in pq.Subparts)
                    keys.Add($"{pq.QuestionId}:{sp.SubpartId}");
            }
            else
            {
                keys.Add(pq.QuestionId);
            }

            DateTimeOffset? earliest = null, latest = null;
            foreach (var k in keys)
            {
                if (state.AnswerTimestamps.TryGetValue(k, out var first))
                {
                    earliest = earliest is null || first < earliest ? first : earliest;
                }
                if (state.AnswerLastTimestamps.TryGetValue(k, out var last))
                {
                    latest = latest is null || last > latest ? last : latest;
                }
            }

            if (earliest.HasValue) firstByQ[pq.QuestionId] = earliest.Value;
            if (latest.HasValue) lastByQ[pq.QuestionId] = latest.Value;
        }

        var timeSpentByQ = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var answeredOrder = firstByQ
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        var prevLast = state.StartedAt;
        foreach (var qid in answeredOrder)
        {
            var first = firstByQ[qid];
            var delta = first - prevLast;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            timeSpentByQ[qid] = delta;
            prevLast = lastByQ.TryGetValue(qid, out var l) ? l : first;
        }

        var rebuilt = new List<MockExamPerQuestionResult>(perQuestion.Count);
        foreach (var pq in perQuestion)
        {
            firstByQ.TryGetValue(pq.QuestionId, out var firstAt);
            timeSpentByQ.TryGetValue(pq.QuestionId, out var timeSpent);
            rebuilt.Add(pq with
            {
                FirstAnsweredAt = firstByQ.ContainsKey(pq.QuestionId) ? firstAt : null,
                TimeSpent = timeSpentByQ.ContainsKey(pq.QuestionId) ? timeSpent : null,
            });
        }
        return rebuilt;
    }

    private async Task<CasVerifyResult> VerifyWithRetryAsync(
        string a, string b, CancellationToken ct)
    {
        // PRR-297 — multi-attempt CAS retry with exponential backoff.
        // Single-retry-at-150ms left persistent SymPy flake (>300ms outage)
        // silently degrading the grade. Now: 3 attempts at 150ms / 400ms /
        // 1200ms (jittered). Worst-case ~1.75s, still under typical
        // grade-time budget. Retry-rate >5% is a SymPy SLO burn signal.
        var rng = new Random(HashCode.Combine(a, b));
        int[] backoffsMs = { 150, 400, 1200 };

        CasVerifyResult last = default!;
        for (var attempt = 0; attempt <= backoffsMs.Length; attempt++)
        {
            // PRR-322: bump the ambient cost counter for every attempt,
            // not just the winning one — each VerifyAsync round-trip is
            // real CPU on the SymPy sidecar regardless of outcome. The
            // counter is a shared int[1] reference scoped by
            // GradeAndMeasureAsync; mutating the array in-place is what
            // makes the increment visible to the parent on await return.
            if (_casAttemptCounter.Value is int[] counter)
                counter[0]++;

            var result = await _cas.VerifyAsync(
                new CasVerifyRequest(CasOperation.Equivalence, a, b, null), ct);
            if (result.Status == CasVerifyStatus.Ok) return result;
            last = result;
            if (result.Status is not (CasVerifyStatus.Timeout or CasVerifyStatus.CircuitBreakerOpen))
                return result;
            if (attempt == backoffsMs.Length) break;

            var jitter = rng.Next(50);
            await Task.Delay(TimeSpan.FromMilliseconds(backoffsMs[attempt] + jitter), ct);
        }
        return last;
    }
}
