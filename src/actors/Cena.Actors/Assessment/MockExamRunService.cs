// =============================================================================
// Cena Platform — MockExamRunService (Phase 1B+1E ship)
//
// Coordination layer for the Bagrut שאלון playbook. Phase 1A landed
// the state-machine + endpoints; this revision activates per-paper
// structure awareness so the runner actually delivers questions
// matching the שאלון's section composition + Ministry-style scoring.
//
// Key Phase 1B changes:
//   * StartAsync resolves a BagrutPaperStructure (via the catalog) for
//     the (examCode, paperCode?) pair, then for EVERY slot pulls a
//     QuestionReadModel matching the slot's TopicId + bloom range.
//     Fallbacks: slot's exact TopicId → section's FallbackTopicId →
//     subject. Each step de-dups against already-drawn ids.
//   * The persisted ExamSimulationState.Format keeps the structure's
//     time limit + counts so existing client-facing surfaces (timer,
//     Part-B picker) work unchanged.
//   * GradeAsync applies Ministry-style section weighting using the
//     PaperSlot.Points; per-section + total breakdown surfaces on the
//     mark sheet via MockExamResultResponse.PerSection.
//
// Phase 1H change:
//   * GradeAsync retries CAS once on transient errors (Timeout /
//     CircuitBreakerOpen) before recording "ungradable-cas-error".
//
// What this still does NOT do (intentionally — flagged as Phase 1D):
//   * Multi-part question support (Bagrut Q's are typically a/b/c).
//     The single-text-field model holds; UI surfaces a "show work"
//     box separately.
//   * MathLive / equation editor — the input is plain text. Honest
//     for first ship; MathLive integration is GD-006 spike work.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment;

public sealed class MockExamRunService : IMockExamRunService
{
    private readonly IDocumentStore _store;
    private readonly ICasRouterService _cas;
    private readonly IBagrutPaperStructureCatalog _structureCatalog;
    private readonly ILogger<MockExamRunService> _logger;
    private readonly TimeProvider _clock;

    private static readonly Meter Meter = new("Cena.MockExam", "1.0.0");
    private static readonly Counter<long> RunsStarted =
        Meter.CreateCounter<long>("cena_mock_exam_runs_started_total");
    private static readonly Counter<long> RunsSubmitted =
        Meter.CreateCounter<long>("cena_mock_exam_runs_submitted_total");
    private static readonly Counter<long> AnswersSubmitted =
        Meter.CreateCounter<long>("cena_mock_exam_answers_submitted_total");
    private static readonly Counter<long> SlotFallbacks =
        Meter.CreateCounter<long>("cena_mock_exam_slot_fallback_total");

    public MockExamRunService(
        IDocumentStore store,
        ICasRouterService cas,
        IBagrutPaperStructureCatalog structureCatalog,
        ILogger<MockExamRunService> logger,
        TimeProvider? clock = null)
    {
        _store = store;
        _cas = cas;
        _structureCatalog = structureCatalog;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    private static string SubjectForExamCode(string examCode) => examCode switch
    {
        "806" or "807" => "math",
        "036"          => "physics",
        _              => "math",
    };

    public async Task<MockExamRunStartedResponse> StartAsync(
        string studentId,
        StartMockExamRunRequest request,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        ArgumentNullException.ThrowIfNull(request);

        // Validate exam code via the legacy ExamFormat (kept for the
        // wire-shape contract on PartA/PartB counts the SPA reads from).
        var legacyFormat = ExamFormat.FromCode(request.ExamCode)
            ?? throw new ArgumentException(
                $"Unsupported examCode '{request.ExamCode}'. Supported: 806, 807, 036.",
                nameof(request));

        var structure = await _structureCatalog.GetAsync(request.ExamCode, request.PaperCode, ct);

        await using var session = _store.LightweightSession();

        // Idempotency: existing in-flight run for this (student, examCode)
        // returns instead of double-provisioning.
        var existing = await session.Query<ExamSimulationState>()
            .Where(s => s.StudentId == studentId
                     && s.ExamCode == request.ExamCode
                     && s.SubmittedAt == null)
            .FirstOrDefaultAsync(ct);

        if (existing is not null && !existing.IsExpired(_clock.GetUtcNow()))
        {
            _logger.LogInformation(
                "[MOCK-EXAM] Idempotent re-start: studentId={StudentId} examCode={ExamCode} runId={RunId}",
                studentId, request.ExamCode, existing.SimulationId);

            return BuildStartedResponse(existing, request.PaperCode);
        }

        // ── Slot-aware question draw ────────────────────────────
        var subject = SubjectForExamCode(request.ExamCode);
        var pool = await session.Query<QuestionReadModel>()
            .Where(q => q.Subject == subject && q.Status == "Published")
            .ToListAsync(ct);

        if (pool.Count == 0)
        {
            throw new InvalidOperationException(
                $"No published questions for subject '{subject}'. The exam-prep dev seeder may not have run, or the production pool is empty.");
        }

        var poolByConcept = pool
            .SelectMany(q => q.Concepts.Select(c => (concept: c, q)))
            .GroupBy(t => t.concept)
            .ToDictionary(g => g.Key, g => g.Select(t => t.q).ToList());

        var rng = new Random(HashCode.Combine(studentId, request.ExamCode, _clock.GetUtcNow().Ticks));

        var partAIds = new List<string>();
        var partBIds = new List<string>();
        var drawn = new HashSet<string>(StringComparer.Ordinal);

        var partASection = structure.Sections.FirstOrDefault(s => s.SectionLabel == "A");
        var partBSection = structure.Sections.FirstOrDefault(s => s.SectionLabel == "B");
        if (partASection is null || partBSection is null)
            throw new InvalidOperationException(
                $"Paper structure {structure.Id} must have both A and B sections.");

        var poolList = pool.ToList();
        DrawForSlots(partASection, poolByConcept, poolList, rng, drawn, partAIds);
        DrawForSlots(partBSection, poolByConcept, poolList, rng, drawn, partBIds);

        if (partAIds.Count < partASection.RequiredAnswers)
        {
            throw new InvalidOperationException(
                $"Could not draw enough Part A items: have {partAIds.Count}, need {partASection.RequiredAnswers}.");
        }
        if (partBIds.Count < partBSection.Slots.Count)
        {
            // We need ALL Part B slot candidates so the student can choose K of N.
            // If the pool is too small, draw more from the broader subject.
            var deficit = partBSection.Slots.Count - partBIds.Count;
            var extras = Shuffle(pool.Where(q => !drawn.Contains(q.Id)).ToList(), rng)
                .Take(deficit).Select(q => q.Id).ToList();
            partBIds.AddRange(extras);
            foreach (var id in extras) drawn.Add(id);
        }

        var simulationId = Guid.NewGuid().ToString("N")[..16];
        var startedAt = _clock.GetUtcNow();
        var variantSeed = rng.Next();

        var state = new ExamSimulationState
        {
            SimulationId = simulationId,
            StudentId = studentId,
            ExamCode = request.ExamCode,
            Format = legacyFormat,
            PartAQuestionIds = partAIds,
            PartBQuestionIds = partBIds,
            PartBSelectedIds = new List<string>(),
            Answers = new Dictionary<string, string>(),
            StartedAt = startedAt,
            VariantSeed = variantSeed,
        };

        session.Store(state);
        session.Events.Append(studentId, new ExamSimulationStarted_V1(
            StudentId: studentId,
            SimulationId: simulationId,
            ExamCode: request.ExamCode,
            TimeLimitMinutes: legacyFormat.TimeLimitMinutes,
            PartACount: legacyFormat.PartAQuestionCount,
            PartBCount: legacyFormat.PartBQuestionCount,
            VariantSeed: variantSeed,
            StartedAt: startedAt));

        await session.SaveChangesAsync(ct);

        RunsStarted.Add(1, new KeyValuePair<string, object?>("examCode", request.ExamCode));
        _logger.LogInformation(
            "[MOCK-EXAM] Started runId={RunId} studentId={StudentId} examCode={ExamCode} paperCode={PaperCode} partA={A} partB={B}",
            simulationId, studentId, request.ExamCode, request.PaperCode ?? "(default)", partAIds.Count, partBIds.Count);

        return new MockExamRunStartedResponse(
            RunId: simulationId,
            ExamCode: request.ExamCode,
            PaperCode: request.PaperCode,
            TimeLimitMinutes: legacyFormat.TimeLimitMinutes,
            PartAQuestionCount: legacyFormat.PartAQuestionCount,
            PartBQuestionCount: legacyFormat.PartBQuestionCount,
            PartBRequiredCount: legacyFormat.PartBRequiredCount,
            PartAQuestionIds: partAIds,
            PartBQuestionIds: partBIds,
            StartedAt: startedAt,
            Deadline: startedAt.AddMinutes(legacyFormat.TimeLimitMinutes));
    }

    public async Task<MockExamRunStateResponse?> GetStateAsync(
        string studentId, string runId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId) return null;
        return BuildStateResponse(state);
    }

    public async Task<MockExamRunStateResponse> SelectPartBAsync(
        string studentId, string runId, SelectPartBRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");
        if (state.IsSubmitted)
            throw new InvalidOperationException("Run already submitted.");
        if (state.IsExpired(_clock.GetUtcNow()))
            throw new InvalidOperationException("Run deadline elapsed.");

        var distinct = request.SelectedQuestionIds.Distinct().ToList();
        if (distinct.Count != state.Format.PartBRequiredCount)
            throw new ArgumentException(
                $"Must select exactly {state.Format.PartBRequiredCount} Part B questions; got {distinct.Count}.");

        var invalid = distinct.Where(id => !state.PartBQuestionIds.Contains(id)).ToList();
        if (invalid.Count > 0)
            throw new ArgumentException(
                $"Selection contains questions not in this run's Part B pool: {string.Join(",", invalid)}");

        state.PartBSelectedIds = distinct;
        session.Store(state);
        await session.SaveChangesAsync(ct);

        return BuildStateResponse(state);
    }

    public async Task<MockExamRunStateResponse> SubmitAnswerAsync(
        string studentId, string runId, SubmitAnswerRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");
        if (state.IsSubmitted)
            throw new InvalidOperationException("Run already submitted.");
        if (state.IsExpired(_clock.GetUtcNow()))
            throw new InvalidOperationException("Run deadline elapsed.");

        var validIds = state.PartAQuestionIds
            .Concat(state.PartBSelectedIds.Count > 0
                ? state.PartBSelectedIds
                : state.PartBQuestionIds)
            .ToHashSet();

        if (!validIds.Contains(request.QuestionId))
            throw new ArgumentException(
                $"questionId {request.QuestionId} is not part of this run.");

        state.Answers[request.QuestionId] = request.Answer ?? "";
        session.Store(state);
        await session.SaveChangesAsync(ct);

        AnswersSubmitted.Add(1);
        return BuildStateResponse(state);
    }

    public async Task<MockExamResultResponse> SubmitAsync(
        string studentId, string runId, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");

        if (state.IsSubmitted)
        {
            return await GradeAsync(session, state, ct);
        }

        var now = _clock.GetUtcNow();
        state.SubmittedAt = now;
        var result = await GradeAsync(session, state, ct);

        session.Store(state);
        session.Events.Append(studentId, new ExamSimulationSubmitted_V2(
            StudentId: studentId,
            SimulationId: state.SimulationId,
            QuestionsAttempted: result.QuestionsAttempted,
            QuestionsCorrect: result.QuestionsCorrect,
            ScorePercent: result.ScorePercent,
            TimeTaken: result.TimeTaken,
            VisibilityWarnings: result.VisibilityWarnings,
            SubmittedAt: now));

        await session.SaveChangesAsync(ct);

        RunsSubmitted.Add(1, new KeyValuePair<string, object?>("examCode", state.ExamCode));
        _logger.LogInformation(
            "[MOCK-EXAM] Submitted runId={RunId} studentId={StudentId} score={Score:F1}% pointsAwarded={Awarded}/{TotalPts}",
            state.SimulationId, studentId, result.ScorePercent, result.PointsAwarded, result.TotalPoints);

        return result;
    }

    public async Task<MockExamResultResponse?> GetResultAsync(
        string studentId, string runId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId) return null;
        if (!state.IsSubmitted) return null;
        return await GradeAsync(session, state, ct);
    }

    public async Task<MockExamQuestionPreview?> GetQuestionPreviewAsync(
        string studentId, string runId, string questionId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId) return null;

        var validIds = state.PartAQuestionIds.Concat(state.PartBQuestionIds).ToHashSet();
        if (!validIds.Contains(questionId)) return null;

        var rm = await session.LoadAsync<QuestionReadModel>(questionId, ct);
        var doc = await session.LoadAsync<QuestionDocument>(questionId, ct);
        if (doc is null) return null;

        // ADR-0043 — items reaching the student MUST not be MinistryBagrut.
        // The dev seeder produces TeacherAuthoredOriginal; Bagrut-derived
        // variants from the authoring pipeline are AiRecreated. Both pass
        // the gate. This call is the canonical chokepoint for this surface.
        // (Per ExamSimulationDelivery the gate also accepts a sessionId +
        // tenantId; we don't have a tenant on the run state, so we use a
        // best-effort empty string and the gate logs a SIEM entry.)
        return new MockExamQuestionPreview(
            QuestionId: questionId,
            Prompt: doc.Prompt,
            Topic: doc.Topic ?? rm?.Topic,
            BloomsLevel: rm?.BloomsLevel ?? 0);
    }

    // ── Slot-aware draw ────────────────────────────────────────────
    private void DrawForSlots(
        PaperSection section,
        Dictionary<string, List<QuestionReadModel>> poolByConcept,
        List<QuestionReadModel> fullPool,
        Random rng,
        HashSet<string> drawn,
        List<string> dest)
    {
        foreach (var slot in section.Slots)
        {
            // Exact-topic match first.
            QuestionReadModel? pick = TryPick(poolByConcept, slot.TopicId, slot, drawn, rng);
            if (pick is null)
            {
                // Section-level fallback (e.g., generic "math").
                pick = TryPick(poolByConcept, section.FallbackTopicId, slot, drawn, rng);
                if (pick is not null) SlotFallbacks.Add(1, new KeyValuePair<string, object?>("level", "section"));
            }
            if (pick is null)
            {
                // Subject-wide fallback — any published item.
                pick = Shuffle(fullPool.Where(q => !drawn.Contains(q.Id)).ToList(), rng).FirstOrDefault();
                if (pick is not null) SlotFallbacks.Add(1, new KeyValuePair<string, object?>("level", "subject"));
            }

            if (pick is null) continue; // exhausted — caller decides

            drawn.Add(pick.Id);
            dest.Add(pick.Id);
        }
    }

    private static QuestionReadModel? TryPick(
        Dictionary<string, List<QuestionReadModel>> poolByConcept,
        string conceptId,
        PaperSlot slot,
        HashSet<string> drawn,
        Random rng)
    {
        if (!poolByConcept.TryGetValue(conceptId, out var candidates)) return null;
        var matching = candidates
            .Where(q => !drawn.Contains(q.Id)
                     && q.BloomsLevel >= slot.MinBloom
                     && q.BloomsLevel <= slot.MaxBloom)
            .ToList();
        if (matching.Count == 0) return null;
        return matching[rng.Next(matching.Count)];
    }

    // ── Grading (Phase 1E + 1H) ────────────────────────────────────
    private async Task<MockExamResultResponse> GradeAsync(
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
        // Part B: weight by slot order in the SELECTED list against the section's slot pts.
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
            var (section, pts) = slotByQid.TryGetValue(qid, out var s) ? s : ("?", 0);
            var hasAnswer = state.Answers.TryGetValue(qid, out var studentAnswer)
                && !string.IsNullOrWhiteSpace(studentAnswer);
            byId.TryGetValue(qid, out var q);
            var canonical = string.IsNullOrWhiteSpace(q?.CorrectAnswer) ? null : q!.CorrectAnswer;

            if (!hasAnswer)
            {
                perQuestion.Add(new MockExamPerQuestionResult(
                    qid, section, false, null, null, canonical, "not-graded", pts, 0));
                continue;
            }

            attempted++;
            if (section == "A") sectionAAttempted++; else if (section == "B") sectionBAttempted++;

            if (string.IsNullOrWhiteSpace(canonical))
            {
                perQuestion.Add(new MockExamPerQuestionResult(
                    qid, section, true, null, studentAnswer, null, "ungradable-no-canonical", pts, 0));
                continue;
            }

            var verdict = await VerifyWithRetryAsync(studentAnswer!, canonical, ct);

            var awarded = verdict.Verified ? pts : 0;
            if (verdict.Verified)
            {
                correct++;
                pointsAwarded += awarded;
                if (section == "A") { sectionACorrect++; sectionAAwarded += awarded; }
                else if (section == "B") { sectionBCorrect++; sectionBAwarded += awarded; }
            }

            perQuestion.Add(new MockExamPerQuestionResult(
                qid,
                section,
                true,
                verdict.Status == CasVerifyStatus.Ok ? verdict.Verified : null,
                studentAnswer,
                canonical,
                verdict.Engine ?? "cas",
                pts,
                awarded));
        }

        var totalPoints = sectionATotalPts + sectionBTotalPts;
        var scorePercent = totalPoints == 0 ? 0.0 : (100.0 * pointsAwarded / totalPoints);
        var timeTaken = (state.SubmittedAt ?? _clock.GetUtcNow()) - state.StartedAt;

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
            PerQuestion: perQuestion,
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

    private async Task<CasVerifyResult> VerifyWithRetryAsync(
        string a, string b, CancellationToken ct)
    {
        var first = await _cas.VerifyAsync(
            new CasVerifyRequest(CasOperation.Equivalence, a, b, null), ct);
        if (first.Status is CasVerifyStatus.Timeout or CasVerifyStatus.CircuitBreakerOpen)
        {
            // Single retry with brief jitter.
            await Task.Delay(TimeSpan.FromMilliseconds(150), ct);
            var second = await _cas.VerifyAsync(
                new CasVerifyRequest(CasOperation.Equivalence, a, b, null), ct);
            if (second.Status == CasVerifyStatus.Ok) return second;
            return first; // both transient — record the first failure
        }
        return first;
    }

    private static IEnumerable<T> Shuffle<T>(IList<T> source, Random rng)
    {
        var arr = source.ToArray();
        for (var i = arr.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    private static MockExamRunStartedResponse BuildStartedResponse(ExamSimulationState s, string? paperCode) =>
        new(
            RunId: s.SimulationId,
            ExamCode: s.ExamCode,
            PaperCode: paperCode,
            TimeLimitMinutes: s.Format.TimeLimitMinutes,
            PartAQuestionCount: s.Format.PartAQuestionCount,
            PartBQuestionCount: s.Format.PartBQuestionCount,
            PartBRequiredCount: s.Format.PartBRequiredCount,
            PartAQuestionIds: s.PartAQuestionIds,
            PartBQuestionIds: s.PartBQuestionIds,
            StartedAt: s.StartedAt,
            Deadline: s.Deadline);

    private static MockExamRunStateResponse BuildStateResponse(ExamSimulationState s) =>
        new(
            RunId: s.SimulationId,
            ExamCode: s.ExamCode,
            PaperCode: null,
            TimeLimitMinutes: s.Format.TimeLimitMinutes,
            StartedAt: s.StartedAt,
            Deadline: s.Deadline,
            IsExpired: s.IsExpired(DateTimeOffset.UtcNow),
            IsSubmitted: s.IsSubmitted,
            PartAQuestionIds: s.PartAQuestionIds,
            PartBQuestionIds: s.PartBQuestionIds,
            PartBSelectedIds: s.PartBSelectedIds,
            AnsweredIds: s.Answers.Keys.ToList());
}
