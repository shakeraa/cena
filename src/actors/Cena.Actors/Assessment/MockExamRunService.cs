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
using Cena.Actors.Content;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment;

public sealed class MockExamRunService : IMockExamRunService
{
    private readonly IDocumentStore _store;
    private readonly ICasRouterService _cas;
    private readonly IBagrutPaperStructureCatalog _structureCatalog;
    private readonly IItemDeliveryGate _deliveryGate;
    private readonly ILogger<MockExamRunService> _logger;
    private readonly TimeProvider _clock;
    /// <summary>
    /// PRR-289 — optional dependency that propagates per-question correctness
    /// from a submitted mock exam into the skill-keyed mastery store via the
    /// canonical BKT update path. Null means the host hasn't wired BKT (e.g.
    /// historical test fixtures); the runner stays callable without it but
    /// throws away the strongest adaptive signal we have.
    /// </summary>
    private readonly IBktStateTracker? _bktTracker;
    private readonly MockExamGrader _grader;
    private readonly MockExamPaperDraw _paperDraw;
    private readonly MockExamBktPropagator _bktPropagator;

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
        IBagrutPaperStructureCatalog structureCatalog,
        IItemDeliveryGate deliveryGate,
        ILogger<MockExamRunService> logger,
        TimeProvider? clock = null,
        IBktStateTracker? bktTracker = null)
    {
        _store = store;
        _cas = cas;
        _structureCatalog = structureCatalog;
        _deliveryGate = deliveryGate;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _bktTracker = bktTracker;
        _grader = new MockExamGrader(structureCatalog, cas, _clock);
        _paperDraw = new MockExamPaperDraw(logger, _clock);
        _bktPropagator = new MockExamBktPropagator(bktTracker, store, logger);
    }

    private static string SubjectForExamCode(string examCode) => examCode switch
    {
        "806" or "807" => "math",
        "036"          => "physics",
        "016"          => "english",
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

            return MockExamWireResponses.BuildStarted(existing, request.PaperCode);
        }

        // ── Slot-aware question draw delegated to MockExamPaperDraw ──
        var subject = SubjectForExamCode(request.ExamCode);
        var draw = await _paperDraw.DrawAsync(
            session, structure, request.PaperCode, subject, studentId, request.ExamCode, ct);
        var partAIds = new List<string>(draw.PartAIds);
        var partBIds = new List<string>(draw.PartBIds);

        var simulationId = Guid.NewGuid().ToString("N")[..16];
        var startedAt = _clock.GetUtcNow();
        var variantSeed = draw.VariantSeed;

        // Phase 2B: clamp accommodation to [0, 100]% silently. Storing
        // the resolved minute count (not the percent) keeps the state
        // auditable: a future policy change to the accommodation cap
        // doesn't retroactively alter past runs.
        var clampedPercent = Math.Clamp(request.ExtraTimePercent, 0, 100);
        var extraTimeMinutes = (int)Math.Round(legacyFormat.TimeLimitMinutes * (clampedPercent / 100.0));

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
            ExtraTimeMinutes = extraTimeMinutes,
            CalculatorPolicy = structure.CalculatorPolicy.ToString(),
            FormulaSheetMode = structure.FormulaSheetMode.ToString(),
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
            ExtraTimeMinutes: extraTimeMinutes,
            PartAQuestionCount: legacyFormat.PartAQuestionCount,
            PartBQuestionCount: legacyFormat.PartBQuestionCount,
            PartBRequiredCount: legacyFormat.PartBRequiredCount,
            PartAQuestionIds: partAIds,
            PartBQuestionIds: partBIds,
            StartedAt: startedAt,
            Deadline: state.Deadline,
            CalculatorPolicy: structure.CalculatorPolicy.ToString(),
            FormulaSheetMode: structure.FormulaSheetMode.ToString());
    }

    public async Task<MockExamRunStateResponse?> GetStateAsync(
        string studentId, string runId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId) return null;
        return MockExamWireResponses.BuildState(state);
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

        return MockExamWireResponses.BuildState(state);
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

        // Phase 2A: multi-part questions store per-subpart answers under
        // a composite key "{qid}:{subpartId}" inside the same Answers
        // dictionary. Single-cell questions keep the bare qid key — the
        // existing wire shape and on-disk format are preserved for
        // legacy single-cell rows.
        var answerKey = string.IsNullOrWhiteSpace(request.SubpartId)
            ? request.QuestionId
            : $"{request.QuestionId}:{request.SubpartId}";

        var answerValue = request.Answer ?? "";
        state.Answers[answerKey] = answerValue;
        // PRR-299: stamp pacing timestamps. TryAdd preserves first-engaged;
        // direct assignment keeps last-engaged fresh on every edit.
        var nowAns = _clock.GetUtcNow();
        state.AnswerTimestamps.TryAdd(answerKey, nowAns);
        state.AnswerLastTimestamps[answerKey] = nowAns;

        session.Store(state);

        // PRR-283 — per-answer audit event.
        session.Events.Append(studentId, new ExamSimulationAnswerSubmitted_V1(
            StudentId: studentId,
            SimulationId: state.SimulationId,
            ItemId: request.QuestionId,
            SubpartId: string.IsNullOrWhiteSpace(request.SubpartId) ? null : request.SubpartId,
            HadContent: !string.IsNullOrWhiteSpace(answerValue),
            SubmittedAt: _clock.GetUtcNow()));

        await session.SaveChangesAsync(ct);

        AnswersSubmitted.Add(1);
        return MockExamWireResponses.BuildState(state);
    }

    public async Task<MockExamRunStateResponse> SubmitAnswersBulkAsync(
        string studentId, string runId, SubmitAnswersBulkRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Answers is null || request.Answers.Count == 0)
            throw new ArgumentException("answers list must be non-empty.", nameof(request));

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

        // Validate the whole batch FIRST so we never partially apply.
        foreach (var a in request.Answers)
        {
            if (!validIds.Contains(a.QuestionId))
                throw new ArgumentException(
                    $"questionId {a.QuestionId} is not part of this run.");
        }

        // PRR-283 + PRR-299: shared timestamp for the whole bulk batch.
        // PRR-283 audit event timestamp + PRR-299 pacing-engaged stamp
        // both attribute every entry to the same `now` — the student
        // engaged the entire submission window from the server's
        // perspective.
        var ts = _clock.GetUtcNow();
        foreach (var a in request.Answers)
        {
            var key = string.IsNullOrWhiteSpace(a.SubpartId)
                ? a.QuestionId
                : $"{a.QuestionId}:{a.SubpartId}";
            var value = a.Answer ?? "";
            state.Answers[key] = value;
            // PRR-299 — pacing timestamps for every entry in the batch.
            state.AnswerTimestamps.TryAdd(key, ts);
            state.AnswerLastTimestamps[key] = ts;

            // PRR-283 — emit one per-answer event per submission, so the
            // stream resolution matches the single-answer path. Bulk
            // doesn't dedup; the audit semantics are "every answer write
            // produces one event".
            session.Events.Append(studentId, new ExamSimulationAnswerSubmitted_V1(
                StudentId: studentId,
                SimulationId: state.SimulationId,
                ItemId: a.QuestionId,
                SubpartId: string.IsNullOrWhiteSpace(a.SubpartId) ? null : a.SubpartId,
                HadContent: !string.IsNullOrWhiteSpace(value),
                SubmittedAt: ts));
        }

        session.Store(state);
        await session.SaveChangesAsync(ct);

        AnswersSubmitted.Add(request.Answers.Count,
            new KeyValuePair<string, object?>("path", "bulk"));
        return MockExamWireResponses.BuildState(state);
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

        // PRR-289 — feed per-question correctness into the BKT mastery store
        // AFTER the run has been durably submitted. Best-effort: a BKT failure
        // here must NOT roll back the submit — the student deserves their mark
        // sheet even if the adaptive-signal pipeline is briefly down.
        await _bktPropagator.RecordAsync(state, result, now, ct).ConfigureAwait(false);

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

    public async Task<MockExamRunStateResponse> PauseAsync(
        string studentId, string runId, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");
        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");
        if (state.IsSubmitted)
            throw new InvalidOperationException("Run already submitted; nothing to pause.");
        if (state.IsPaused) return MockExamWireResponses.BuildState(state); // idempotent

        state.PausedAt = _clock.GetUtcNow();
        session.Store(state);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[MOCK-EXAM] Paused runId={RunId} studentId={StudentId} totalPausedMs={Total}",
            runId, studentId, state.TotalPausedMs);
        return MockExamWireResponses.BuildState(state);
    }

    public async Task<MockExamRunStateResponse> ResumeAsync(
        string studentId, string runId, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");
        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");
        if (state.IsSubmitted)
            throw new InvalidOperationException("Run already submitted; nothing to resume.");
        if (!state.IsPaused) return MockExamWireResponses.BuildState(state); // idempotent

        var pausedDuration = _clock.GetUtcNow() - state.PausedAt!.Value;
        state.TotalPausedMs += (long)Math.Max(0, pausedDuration.TotalMilliseconds);
        state.PausedAt = null;
        session.Store(state);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[MOCK-EXAM] Resumed runId={RunId} studentId={StudentId} pausedThisRoundMs={Round} totalPausedMs={Total}",
            runId, studentId, (long)pausedDuration.TotalMilliseconds, state.TotalPausedMs);
        return MockExamWireResponses.BuildState(state);
    }

    public async Task<IReadOnlyList<MockExamRunSummary>> GetRecentRunsAsync(
        string studentId, string? examCode, string? paperCode, int limit, CancellationToken ct)
    {
        // Cap limit defensively — bounded response shape.
        var capped = Math.Clamp(limit, 1, 10);

        await using var session = _store.QuerySession();
        var query = session.Query<ExamSimulationState>()
            .Where(s => s.StudentId == studentId && s.SubmittedAt != null);
        if (!string.IsNullOrWhiteSpace(examCode))
            query = query.Where(s => s.ExamCode == examCode);
        // PaperCode is NOT stored on ExamSimulationState today (it's
        // request-time display only). Filter is best-effort: returns
        // all runs for the examCode and the SPA can filter further.
        // PRR-294 follow-up: persist PaperCode on state for stricter
        // filtering. For now examCode is the granularity.

        var states = await query
            .OrderByDescending(s => s.SubmittedAt!.Value)
            .Take(capped)
            .ToListAsync(ct);

        // We don't store the graded score on state — re-derive from
        // submitted answers. For the trend card this is acceptable;
        // grading is deterministic so re-derive yields the same value
        // shown on the original result page (assuming canonicals
        // unchanged; PRR-298 re-grade case will reflect the latest).
        var summaries = new List<MockExamRunSummary>(states.Count);
        foreach (var state in states)
        {
            // Lightweight re-grade: count answers + canonical hits
            // without invoking CAS for every Q (perf-bounded). We use
            // the persisted Answers dict and count which keys had
            // non-empty values; for points we use the structure +
            // section weighting as the upper bound.
            var attempted = state.Answers.Count(a => !string.IsNullOrWhiteSpace(a.Value));
            var totalPts = await ComputeTotalPointsAsync(session, state.ExamCode, ct);
            // For trend purposes we re-grade fully (small N; bounded by
            // cap=10 calls × ~9 questions = 90 CAS calls worst case).
            var graded = await GradeAsync(session, state, ct);
            summaries.Add(new MockExamRunSummary(
                RunId: state.SimulationId,
                ExamCode: state.ExamCode,
                PaperCode: paperCode,
                StartedAt: state.StartedAt,
                SubmittedAt: state.SubmittedAt!.Value,
                PointsAwarded: graded.PointsAwarded,
                TotalPoints: graded.TotalPoints,
                ScorePercent: graded.ScorePercent));
            _ = attempted; _ = totalPts; // suppress unused warnings while keeping local for debugging
        }
        return summaries;
    }

    private async Task<int> ComputeTotalPointsAsync(IQuerySession session, string examCode, CancellationToken ct)
    {
        var structure = await _structureCatalog.GetAsync(examCode, paperCode: null, ct);
        return structure.Sections.Sum(s => s.Slots.Take(s.RequiredAnswers).Sum(slot => slot.Points));
    }

    public async Task<MockExamResultResponse> RegradeAsync(
        string studentId, string runId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");
        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");
        if (!state.IsSubmitted)
            throw new InvalidOperationException("Run not yet submitted; cannot re-grade.");

        // Re-run the grader. GradeAsync reads the CURRENT canonical
        // answers from Marten — so a corrected QuestionDocument /
        // BagrutMultipartQuestion gets picked up automatically.
        // Original Submitted_V2 event stays untouched on the stream.
        return await GradeAsync(session, state, ct);
    }

    public async Task<MockExamRunStateResponse> ReportVisibilityEventAsync(
        string studentId, string runId, VisibilityEventReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);

        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");
        if (state.StudentId != studentId)
            throw new UnauthorizedAccessException("Run does not belong to this student.");
        // Continue accepting reports even if the run is submitted/expired —
        // a delayed visibility report after submit is still useful audit.

        var ts = _clock.GetUtcNow();
        var dur = TimeSpan.FromMilliseconds(Math.Max(0, report.DurationAwayMs));
        state.VisibilityEvents.Add(new VisibilityEvent(ts, report.State, dur));

        session.Store(state);
        session.Events.Append(studentId, new ExamVisibilityWarning_V1(
            StudentId: studentId,
            SimulationId: runId,
            VisibilityState: report.State,
            DurationAway: dur,
            DetectedAt: ts));

        await session.SaveChangesAsync(ct);
        return MockExamWireResponses.BuildState(state);
    }

    public async Task<MockExamQuestionPreview?> GetQuestionPreviewAsync(
        string studentId, string runId, string questionId, CancellationToken ct)
    {
        // Phase-4 #2: this is a delivery seam — every served preview gets
        // a corresponding ExamSimulationItemDelivered_V1 event so the
        // audit trail can answer "did we serve item X to student Y" by
        // event-stream replay.
        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId) return null;

        var validIds = state.PartAQuestionIds.Concat(state.PartBQuestionIds).ToHashSet();
        if (!validIds.Contains(questionId)) return null;

        // Multi-part Q's first; the runner needs subpart shape to render
        // multiple input rows.
        var multipart = await session.LoadAsync<BagrutMultipartQuestion>(questionId, ct);
        if (multipart is not null)
        {
            // ADR-0043 chokepoint — derive provenance from the doc and
            // throw on MinistryBagrut. Today TeacherAuthoredOriginal +
            // AiRecreated are the only legitimate writers; the gate is
            // belt-and-braces against a future writer slipping a Ministry
            // doc into the multi-part pool. Throw escapes to the endpoint
            // as 5xx (P0 SIEM alarm), which is correct.
            AssertDeliverable(multipart.SourceType, questionId, runId, studentId);

            await EmitItemDeliveredAsync(session, studentId, runId, questionId, multipart.SourceType, ct);

            return new MockExamQuestionPreview(
                QuestionId: questionId,
                Prompt: multipart.Stem,
                Topic: multipart.Topic,
                BloomsLevel: multipart.BloomsLevel,
                Subparts: multipart.Subparts
                    .Select(s => new MockExamSubpartPreview(s.PartId, s.Prompt, s.Points))
                    .ToList());
        }

        var rm = await session.LoadAsync<QuestionReadModel>(questionId, ct);
        var doc = await session.LoadAsync<QuestionDocument>(questionId, ct);
        if (doc is null) return null;

        // For single-cell Q's the derivation is "every active doc is
        // AiRecreated" by the same convention DiagnosticEndpoints uses
        // (see DeriveProvenanceKind). Belt-and-braces.
        var sourceType = rm?.SourceType ?? "AiRecreated";
        AssertDeliverable(sourceType, questionId, runId, studentId);
        await EmitItemDeliveredAsync(session, studentId, runId, questionId, sourceType, ct);

        return new MockExamQuestionPreview(
            QuestionId: questionId,
            Prompt: doc.Prompt,
            Topic: doc.Topic ?? rm?.Topic,
            BloomsLevel: rm?.BloomsLevel ?? 0,
            Subparts: null);
    }

    /// <summary>Phase-4 #2 — append an ExamSimulationItemDelivered_V1
    /// event so the per-student stream has an auditable record of every
    /// item served. Idempotency: this fires on every preview read, so
    /// the same item served twice = two events; the projection layer
    /// can dedup by (SimulationId, ItemId) when needed.</summary>
    private async Task EmitItemDeliveredAsync(
        IDocumentSession session, string studentId, string runId, string itemId,
        string sourceType, CancellationToken ct)
    {
        var kind = sourceType switch
        {
            "TeacherAuthoredOriginal" => ProvenanceKind.TeacherAuthoredOriginal,
            "MinistryBagrut"          => ProvenanceKind.MinistryBagrut, // unreachable — gate threw
            _                          => ProvenanceKind.AiRecreated,
        };
        session.Events.Append(studentId, new ExamSimulationItemDelivered_V1(
            StudentId: studentId,
            SimulationId: runId,
            ItemId: itemId,
            Provenance: kind,
            DeliveredAt: _clock.GetUtcNow()));
        await session.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Maps the doc's stored SourceType label to a Provenance + invokes
    /// the central gate. Throws on MinistryBagrut.
    /// </summary>
    private void AssertDeliverable(string sourceType, string itemId, string sessionId, string actorId)
    {
        var kind = sourceType switch
        {
            "TeacherAuthoredOriginal" => ProvenanceKind.TeacherAuthoredOriginal,
            "AiRecreated"             => ProvenanceKind.AiRecreated,
            "MinistryBagrut"          => ProvenanceKind.MinistryBagrut,
            _                          => ProvenanceKind.AiRecreated, // pragmatic default
        };
        // Best-effort tenantId — the runner state doesn't carry one
        // today (the run is keyed on studentId only). Gate logs both
        // sessionId + actorId, so trace+SIEM remain correlatable.
        _deliveryGate.AssertDeliverable(
            provenance: new Provenance(kind, _clock.GetUtcNow(), sourceType),
            itemId: itemId,
            sessionId: sessionId,
            tenantId: "(exam-prep-runner)",
            actorId: actorId);
    }

    // ── Grading delegated to MockExamGrader (single-responsibility extract).
    // The grader owns the Stopwatch + grade-duration metric; runner stays
    // focused on state-machine orchestration. All 5 internal callers
    // (StartAsync continuation / SubmitAsync / GetRecentRunsAsync / RegradeAsync)
    // keep calling GradeAsync unchanged.
    private async Task<MockExamResultResponse> GradeAsync(
        IQuerySession session, ExamSimulationState state, CancellationToken ct) =>
        await _grader.GradeAsync(session, state, ct);

}
