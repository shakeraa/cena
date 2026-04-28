// =============================================================================
// Cena Platform — MockExamRunService
//
// Coordination layer that activates the orphan ExamSimulation scaffolding
// (ExamFormat / ExamSimulationState / ExamSimulationStarted_V1 /
// ExamSimulationSubmitted_V2) into a working playbook for students.
//
// Lifecycle:
//   1. StartAsync   — resolve format, draw N+M questions from the pool,
//                     persist ExamSimulationState, emit Started_V1.
//   2. SelectPartB  — student picks K of M Part-B; validated server-side.
//   3. SubmitAnswer — per-question save; rejected post-submit / post-expire.
//   4. Submit       — CAS-graded mark sheet; emits Submitted_V2.
//   5. GetResult    — read mark sheet (re-graded only on the submit pass).
//
// Question source: Marten QuestionReadModel filtered by Subject derived
// from ExamCode (Phase 1A). Topic-precise selection per the שאלון's
// section structure is a Phase 1B follow-up — see PaperStructure design
// notes. Today the contract is bounded: right COUNT, right TIME, right
// section structure, best-effort topic mix.
//
// Grading:
//   * Multiple choice / numeric → MathNet (in-process) via ICasRouterService.
//   * Symbolic / calculus → SymPy sidecar via the same router.
//   * If a question has no CanonicalAnswer (legacy item), grader records
//     "ungradable" and counts toward attempted-but-ungraded; final
//     percentage is over GRADABLE attempted only.
//
// Privacy / safety:
//   * No misconception data on persisted state (ADR-0003).
//   * No streak / loss-aversion copy in any returned shape (GD-004).
//   * Raw Ministry text never leaves the server — questions come from the
//     pool of CAS-gated items, which are AiRecreated or
//     TeacherAuthoredOriginal by construction. Defense-in-depth via
//     ExamSimulationDelivery.AssertDeliverable when the SPA loads each
//     question body for display.
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
    private readonly ILogger<MockExamRunService> _logger;
    private readonly TimeProvider _clock;

    private static readonly Meter Meter = new("Cena.MockExam", "1.0.0");
    private static readonly Counter<long> RunsStarted =
        Meter.CreateCounter<long>("cena_mock_exam_runs_started_total");
    private static readonly Counter<long> RunsSubmitted =
        Meter.CreateCounter<long>("cena_mock_exam_runs_submitted_total");
    private static readonly Counter<long> AnswersSubmitted =
        Meter.CreateCounter<long>("cena_mock_exam_answers_submitted_total");

    public MockExamRunService(
        IDocumentStore store,
        ICasRouterService cas,
        ILogger<MockExamRunService> logger,
        TimeProvider? clock = null)
    {
        _store = store;
        _cas = cas;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    // ── Subject mapping ────────────────────────────────────────────────
    // ExamCode → student-question-pool Subject. Bagrut math 4U/5U share
    // the same subject pool today; physics is its own bucket.
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

        var format = ExamFormat.FromCode(request.ExamCode)
            ?? throw new ArgumentException(
                $"Unsupported examCode '{request.ExamCode}'. Supported: 806, 807, 036.",
                nameof(request));

        await using var session = _store.LightweightSession();

        // Idempotency: existing in-flight run for this (student, examCode)
        // returns instead of double-provisioning. We scope by examCode so a
        // student can have parallel-but-distinct prep runs across subjects.
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

            return BuildStartedResponse(existing);
        }

        // Draw questions from the pool. Subject filter; Status=Published only.
        var subject = SubjectForExamCode(request.ExamCode);
        var pool = await session.Query<QuestionReadModel>()
            .Where(q => q.Subject == subject && q.Status == "Published")
            .ToListAsync(ct);

        var totalNeeded = format.PartAQuestionCount + format.PartBQuestionCount;
        if (pool.Count < totalNeeded)
        {
            throw new InvalidOperationException(
                $"Insufficient published questions for subject '{subject}': have {pool.Count}, need {totalNeeded}.");
        }

        // Stratify: lower bloom → Part A (short), higher bloom → Part B (long).
        // Stable sort by (BloomsLevel asc, Difficulty asc) gives a deterministic
        // baseline; per-run shuffle randomizes within strata so each run hands
        // a different subset.
        var sorted = pool
            .OrderBy(q => q.BloomsLevel)
            .ThenBy(q => q.Difficulty)
            .ToList();

        var partAPool = sorted.Take(sorted.Count / 2).ToList();
        var partBPool = sorted.Skip(sorted.Count / 2).ToList();

        var rng = new Random(HashCode.Combine(studentId, request.ExamCode, _clock.GetUtcNow().Ticks));

        var partA = Shuffle(partAPool, rng).Take(format.PartAQuestionCount).Select(q => q.Id).ToList();
        var partB = Shuffle(partBPool, rng).Take(format.PartBQuestionCount).Select(q => q.Id).ToList();

        // Edge case: if half the pool is < requested, take from the full pool.
        if (partA.Count < format.PartAQuestionCount || partB.Count < format.PartBQuestionCount)
        {
            partA = Shuffle(sorted, rng).Take(format.PartAQuestionCount).Select(q => q.Id).ToList();
            partB = Shuffle(sorted.Except(sorted.Where(q => partA.Contains(q.Id))).ToList(), rng)
                .Take(format.PartBQuestionCount).Select(q => q.Id).ToList();
        }

        var simulationId = Guid.NewGuid().ToString("N")[..16];
        var startedAt = _clock.GetUtcNow();
        var variantSeed = rng.Next();

        var state = new ExamSimulationState
        {
            SimulationId = simulationId,
            StudentId = studentId,
            ExamCode = request.ExamCode,
            Format = format,
            PartAQuestionIds = partA,
            PartBQuestionIds = partB,
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
            TimeLimitMinutes: format.TimeLimitMinutes,
            PartACount: format.PartAQuestionCount,
            PartBCount: format.PartBQuestionCount,
            VariantSeed: variantSeed,
            StartedAt: startedAt));

        await session.SaveChangesAsync(ct);

        RunsStarted.Add(1, new KeyValuePair<string, object?>("examCode", request.ExamCode));
        _logger.LogInformation(
            "[MOCK-EXAM] Started runId={RunId} studentId={StudentId} examCode={ExamCode} partA={A} partB={B}",
            simulationId, studentId, request.ExamCode, partA.Count, partB.Count);

        return new MockExamRunStartedResponse(
            RunId: simulationId,
            ExamCode: request.ExamCode,
            PaperCode: request.PaperCode,
            TimeLimitMinutes: format.TimeLimitMinutes,
            PartAQuestionCount: format.PartAQuestionCount,
            PartBQuestionCount: format.PartBQuestionCount,
            PartBRequiredCount: format.PartBRequiredCount,
            PartAQuestionIds: partA,
            PartBQuestionIds: partB,
            StartedAt: startedAt,
            Deadline: startedAt.AddMinutes(format.TimeLimitMinutes));
    }

    public async Task<MockExamRunStateResponse?> GetStateAsync(
        string studentId,
        string runId,
        CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId)
            return null;

        return BuildStateResponse(state);
    }

    public async Task<MockExamRunStateResponse> SelectPartBAsync(
        string studentId,
        string runId,
        SelectPartBRequest request,
        CancellationToken ct)
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
        string studentId,
        string runId,
        SubmitAnswerRequest request,
        CancellationToken ct)
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
        string studentId,
        string runId,
        CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");
        if (state.IsSubmitted)
        {
            // Idempotent re-submit returns the persisted result.
            var existing = await GradeAsync(session, state, ct);
            return existing;
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
            "[MOCK-EXAM] Submitted runId={RunId} studentId={StudentId} score={Score:F1}% attempted={A}/{T}",
            state.SimulationId, studentId, result.ScorePercent,
            result.QuestionsAttempted, result.TotalQuestions);

        return result;
    }

    public async Task<MockExamResultResponse?> GetResultAsync(
        string studentId,
        string runId,
        CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId)
            return null;
        if (!state.IsSubmitted)
            return null;

        // Read-only re-grade. Cheap because CAS results for the same answer
        // are deterministic and the run is small (≤ 9 items).
        return await GradeAsync(session, state, ct);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private async Task<MockExamResultResponse> GradeAsync(
        IQuerySession session, ExamSimulationState state, CancellationToken ct)
    {
        var gradableIds = state.PartAQuestionIds
            .Concat(state.PartBSelectedIds.Count > 0
                ? state.PartBSelectedIds
                : state.PartBQuestionIds.Take(state.Format.PartBRequiredCount))
            .ToList();

        // CorrectAnswer lives on QuestionDocument (the canonical doc).
        // QuestionReadModel — used at draw time for Status/Bloom — does
        // not carry the answer key. Load both views by id list.
        var canonicalDocs = await session.Query<QuestionDocument>()
            .Where(q => gradableIds.Contains(q.Id))
            .ToListAsync(ct);
        var byId = canonicalDocs.ToDictionary(q => q.Id);

        var perQuestion = new List<MockExamPerQuestionResult>(gradableIds.Count);
        var attempted = 0;
        var correct = 0;

        foreach (var qid in gradableIds)
        {
            var section = state.PartAQuestionIds.Contains(qid) ? "A" : "B";
            var hasAnswer = state.Answers.TryGetValue(qid, out var studentAnswer)
                && !string.IsNullOrWhiteSpace(studentAnswer);
            byId.TryGetValue(qid, out var q);
            var canonical = string.IsNullOrWhiteSpace(q?.CorrectAnswer) ? null : q!.CorrectAnswer;

            if (!hasAnswer)
            {
                perQuestion.Add(new MockExamPerQuestionResult(
                    QuestionId: qid,
                    Section: section,
                    Attempted: false,
                    Correct: null,
                    StudentAnswer: null,
                    CanonicalAnswer: canonical,
                    GradingEngine: "not-graded"));
                continue;
            }

            attempted++;
            if (string.IsNullOrWhiteSpace(canonical))
            {
                perQuestion.Add(new MockExamPerQuestionResult(
                    QuestionId: qid,
                    Section: section,
                    Attempted: true,
                    Correct: null,
                    StudentAnswer: studentAnswer,
                    CanonicalAnswer: null,
                    GradingEngine: "ungradable-no-canonical"));
                continue;
            }

            var verifyResult = await _cas.VerifyAsync(
                new CasVerifyRequest(
                    Operation: CasOperation.Equivalence,
                    ExpressionA: studentAnswer!,
                    ExpressionB: canonical,
                    Variable: null),
                ct);

            var verified = verifyResult.Status == CasVerifyStatus.Ok && verifyResult.Verified;
            if (verified) correct++;

            perQuestion.Add(new MockExamPerQuestionResult(
                QuestionId: qid,
                Section: section,
                Attempted: true,
                Correct: verifyResult.Status == CasVerifyStatus.Ok ? verified : null,
                StudentAnswer: studentAnswer,
                CanonicalAnswer: canonical,
                GradingEngine: verifyResult.Engine ?? "cas"));
        }

        var totalQuestions = gradableIds.Count;
        var scorePercent = attempted == 0 ? 0.0 : (100.0 * correct / attempted);
        var timeTaken = (state.SubmittedAt ?? _clock.GetUtcNow()) - state.StartedAt;

        return new MockExamResultResponse(
            RunId: state.SimulationId,
            ExamCode: state.ExamCode,
            PaperCode: null,
            TotalQuestions: totalQuestions,
            QuestionsAttempted: attempted,
            QuestionsCorrect: correct,
            ScorePercent: scorePercent,
            TimeTaken: timeTaken,
            TimeLimit: TimeSpan.FromMinutes(state.Format.TimeLimitMinutes),
            VisibilityWarnings: state.VisibilityEvents.Count,
            PerQuestion: perQuestion);
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

    private static MockExamRunStartedResponse BuildStartedResponse(ExamSimulationState s) =>
        new(
            RunId: s.SimulationId,
            ExamCode: s.ExamCode,
            PaperCode: null,
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
