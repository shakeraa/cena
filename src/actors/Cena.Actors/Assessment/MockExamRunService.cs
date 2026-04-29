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

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Cena.Actors.Content;
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
    private readonly IItemDeliveryGate _deliveryGate;
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
    /// <summary>Phase 3 — time-to-grade observability. Histogram so the
    /// SLO dashboard can track p50/p95/p99 grading latency separately
    /// from the broader endpoint latency.</summary>
    private static readonly Histogram<double> GradeDurationMs =
        Meter.CreateHistogram<double>("cena_mock_exam_grade_duration_ms");

    /// <summary>Process-scoped counter mixed into the run-shuffle seed
    /// so two starts within the same tick still produce different draws.</summary>
    private static long s_seedCounter;

    public MockExamRunService(
        IDocumentStore store,
        ICasRouterService cas,
        IBagrutPaperStructureCatalog structureCatalog,
        IItemDeliveryGate deliveryGate,
        ILogger<MockExamRunService> logger,
        TimeProvider? clock = null)
    {
        _store = store;
        _cas = cas;
        _structureCatalog = structureCatalog;
        _deliveryGate = deliveryGate;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
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

            return BuildStartedResponse(existing, request.PaperCode);
        }

        // ── Slot-aware question draw ────────────────────────────
        var subject = SubjectForExamCode(request.ExamCode);

        // Two pools: multi-part (preferred for Part B long-form slots)
        // + single-cell (Part A short-form + multi-part fallback).
        var multipartPool = await session.Query<BagrutMultipartQuestion>()
            .Where(q => q.Subject == subject)
            .ToListAsync(ct);

        var pool = await session.Query<QuestionReadModel>()
            .Where(q => q.Subject == subject && q.Status == "Published")
            .ToListAsync(ct);

        if (pool.Count == 0 && multipartPool.Count == 0)
        {
            throw new InvalidOperationException(
                $"No published questions for subject '{subject}'. The exam-prep dev seeder may not have run, or the production pool is empty.");
        }

        var poolByConcept = pool
            .SelectMany(q => q.Concepts.Select(c => (concept: c, q)))
            .GroupBy(t => t.concept)
            .ToDictionary(g => g.Key, g => g.Select(t => t.q).ToList());

        var multipartByTopic = multipartPool
            .GroupBy(q => q.Topic)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Phase 3 #2 — stronger seed. Guid.NewGuid + per-process atomic
        // counter ensures two starts in the same tick get different
        // shuffles (the old (studentId, examCode, ticks) tuple collided
        // when called twice within a tick). Hash-mix down to int for
        // Random's seed.
        var seedSource = $"{studentId}|{request.ExamCode}|{Guid.NewGuid():N}|{System.Threading.Interlocked.Increment(ref s_seedCounter)}";
        var rng = new Random(seedSource.GetHashCode());

        var partAIds = new List<string>();
        var partBIds = new List<string>();
        var drawn = new HashSet<string>(StringComparer.Ordinal);

        var partASection = structure.Sections.FirstOrDefault(s => s.SectionLabel == "A");
        var partBSection = structure.Sections.FirstOrDefault(s => s.SectionLabel == "B");
        if (partASection is null || partBSection is null)
            throw new InvalidOperationException(
                $"Paper structure {structure.Id} must have both A and B sections.");

        var poolList = pool.ToList();
        // Part A is short-form, single-cell. Part B prefers multi-part
        // candidates (real Bagrut long-form Q's are a/b/c). Both
        // fall back gracefully if their preferred pool is thin.
        DrawForSlots(partASection, poolByConcept, poolList, rng, drawn,
            preferMultipart: false, multipartByTopic, partAIds);
        DrawForSlots(partBSection, poolByConcept, poolList, rng, drawn,
            preferMultipart: true, multipartByTopic, partBIds);

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
        return BuildStateResponse(state);
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

        var ts = _clock.GetUtcNow();
        foreach (var a in request.Answers)
        {
            var key = string.IsNullOrWhiteSpace(a.SubpartId)
                ? a.QuestionId
                : $"{a.QuestionId}:{a.SubpartId}";
            var value = a.Answer ?? "";
            state.Answers[key] = value;

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
        if (state.IsPaused) return BuildStateResponse(state); // idempotent

        state.PausedAt = _clock.GetUtcNow();
        session.Store(state);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[MOCK-EXAM] Paused runId={RunId} studentId={StudentId} totalPausedMs={Total}",
            runId, studentId, state.TotalPausedMs);
        return BuildStateResponse(state);
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
        if (!state.IsPaused) return BuildStateResponse(state); // idempotent

        var pausedDuration = _clock.GetUtcNow() - state.PausedAt!.Value;
        state.TotalPausedMs += (long)Math.Max(0, pausedDuration.TotalMilliseconds);
        state.PausedAt = null;
        session.Store(state);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[MOCK-EXAM] Resumed runId={RunId} studentId={StudentId} pausedThisRoundMs={Round} totalPausedMs={Total}",
            runId, studentId, (long)pausedDuration.TotalMilliseconds, state.TotalPausedMs);
        return BuildStateResponse(state);
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
        return BuildStateResponse(state);
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

    // ── Slot-aware draw ────────────────────────────────────────────
    private void DrawForSlots(
        PaperSection section,
        Dictionary<string, List<QuestionReadModel>> poolByConcept,
        List<QuestionReadModel> fullPool,
        Random rng,
        HashSet<string> drawn,
        bool preferMultipart,
        Dictionary<string, List<BagrutMultipartQuestion>> multipartByTopic,
        List<string> dest)
    {
        foreach (var slot in section.Slots)
        {
            string? pickedId = null;

            if (preferMultipart)
            {
                // Multi-part preferred. Try exact topic, then section fallback.
                pickedId = TryPickMultipart(multipartByTopic, slot.TopicId, slot, drawn, rng)?.Id
                        ?? TryPickMultipart(multipartByTopic, section.FallbackTopicId, slot, drawn, rng)?.Id;
            }

            if (pickedId is null)
            {
                // Single-cell path: exact-topic match first.
                var pick = TryPick(poolByConcept, slot.TopicId, slot, drawn, rng);
                if (pick is null)
                {
                    pick = TryPick(poolByConcept, section.FallbackTopicId, slot, drawn, rng);
                    if (pick is not null)
                    {
                        SlotFallbacks.Add(1, new KeyValuePair<string, object?>("level", "section"));
                        // Phase-4 #3 — fallback honesty. Log the drop so an
                        // operator can correlate the student's "this Q feels
                        // generic" complaint with the actual structure miss.
                        _logger.LogInformation(
                            "[MOCK-EXAM-FALLBACK] slot {Slot} topic={Topic} fell to section topic={Section}",
                            slot.SlotNumber, slot.TopicId, section.FallbackTopicId);
                    }
                }
                if (pick is null)
                {
                    pick = Shuffle(fullPool.Where(q => !drawn.Contains(q.Id)).ToList(), rng).FirstOrDefault();
                    if (pick is not null)
                    {
                        SlotFallbacks.Add(1, new KeyValuePair<string, object?>("level", "subject"));
                        // Loud warning — subject-wide fallback violates the
                        // structure invariant in spirit. Production-grade
                        // ops alarm should fire on this counter > 0.
                        _logger.LogWarning(
                            "[MOCK-EXAM-FALLBACK-SUBJECT] slot {Slot} topic={Topic} could not be matched; falling to ANY subject item. The pool likely needs more items for {Topic}.",
                            slot.SlotNumber, slot.TopicId, slot.TopicId);
                    }
                }
                pickedId = pick?.Id;
            }

            if (pickedId is null) continue;

            drawn.Add(pickedId);
            dest.Add(pickedId);
        }
    }

    private static BagrutMultipartQuestion? TryPickMultipart(
        Dictionary<string, List<BagrutMultipartQuestion>> byTopic,
        string topicId,
        PaperSlot slot,
        HashSet<string> drawn,
        Random rng)
    {
        if (!byTopic.TryGetValue(topicId, out var candidates)) return null;
        var matching = candidates
            .Where(q => !drawn.Contains(q.Id)
                     && q.BloomsLevel >= slot.MinBloom
                     && q.BloomsLevel <= slot.MaxBloom)
            .ToList();
        if (matching.Count == 0) return null;
        return matching[rng.Next(matching.Count)];
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

    // ── Grading (Phase 1E + 1H + 3 grade-timer) ─────────────────────
    private async Task<MockExamResultResponse> GradeAsync(
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
        // PRR-297 — multi-attempt CAS retry with exponential backoff.
        // Old: single retry at 150ms. Persistent SymPy flake (>300ms
        // outage) silently degraded the grade. New: 3 attempts at
        // 150ms / 400ms / 1200ms (jittered). Total worst-case ~1.75s
        // which is still under our typical grade-time budget.
        // Counter so ops can alarm on retry-rate >5% as a SymPy SLO
        // burn signal.
        var rng = new Random(HashCode.Combine(a, b));
        int[] backoffsMs = { 150, 400, 1200 };

        CasVerifyResult last = default!;
        for (var attempt = 0; attempt <= backoffsMs.Length; attempt++)
        {
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
            ExtraTimeMinutes: s.ExtraTimeMinutes,
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
