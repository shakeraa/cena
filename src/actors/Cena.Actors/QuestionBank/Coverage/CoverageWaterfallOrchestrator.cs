// =============================================================================
// Cena Platform — Coverage Waterfall Orchestrator (prr-201)
//
// Strategy 1 (parametric) → Strategy 2 (LLM isomorph) → Strategy 3 (curator
// queue), cascading only on undersupply. The cost gradient is
// $0 → cents → human-hours, so escalating only when the previous stage
// cannot fulfil the rung is the cheapest policy that still hits the
// coverage SLO. Design doc: docs/design/coverage-waterfall.md.
//
// ADR-0002: every stage-2 candidate passes through ICasVerificationGate
// before being admitted. Stage 1 variants are verified inside the
// ParametricCompiler's renderer (CAS gate is the prr-200 contract).
// ADR-0026: stage-2 LLM calls go through IIsomorphGenerator whose
// production impl is tier3 with task name question_generation.
// ADR-0043: stage-2 outputs run through MinistrySimilarityChecker before
// CAS — dropped if too close to Ministry text.
// =============================================================================

using System.Diagnostics;
using Cena.Actors.Cas;
using Cena.Actors.QuestionBank.Templates;
using Cena.Actors.RateLimit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.QuestionBank.Coverage;

public interface ICoverageWaterfallOrchestrator
{
    /// <summary>
    /// Cascade fill: run stage 1 (parametric), then if undersupplied stage 2
    /// (LLM isomorph), then if still undersupplied stage 3 (curator queue).
    /// Always returns — a gap is a data point, not an exception.
    /// </summary>
    Task<WaterfallResult> FillRungAsync(
        CoverageCell cell,
        int targetCount,
        ParametricTemplate? templateOrNull,
        string instituteId,
        DateTimeOffset? releaseDate = null,
        CancellationToken ct = default);
}

public sealed class CoverageWaterfallOrchestrator : ICoverageWaterfallOrchestrator
{
    private readonly ParametricCompiler _parametricCompiler;
    private readonly IIsomorphGenerator _isomorphGenerator;
    private readonly MinistrySimilarityChecker _similarityChecker;
    private readonly ICasVerificationGate _casGate;
    private readonly ICostBudgetService? _budgetService;
    private readonly ICuratorQueueEmitter _curatorQueue;
    private readonly CoverageWaterfallOptions _options;
    private readonly ILogger<CoverageWaterfallOrchestrator> _logger;

    public CoverageWaterfallOrchestrator(
        ParametricCompiler parametricCompiler,
        IIsomorphGenerator isomorphGenerator,
        MinistrySimilarityChecker similarityChecker,
        ICasVerificationGate casGate,
        ICuratorQueueEmitter curatorQueue,
        IOptions<CoverageWaterfallOptions> options,
        ILogger<CoverageWaterfallOrchestrator> logger,
        ICostBudgetService? budgetService = null)
    {
        _parametricCompiler = parametricCompiler ?? throw new ArgumentNullException(nameof(parametricCompiler));
        _isomorphGenerator = isomorphGenerator ?? throw new ArgumentNullException(nameof(isomorphGenerator));
        _similarityChecker = similarityChecker ?? throw new ArgumentNullException(nameof(similarityChecker));
        _casGate = casGate ?? throw new ArgumentNullException(nameof(casGate));
        _curatorQueue = curatorQueue ?? throw new ArgumentNullException(nameof(curatorQueue));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _budgetService = budgetService; // optional — null in tests / offline mode
    }

    public async Task<WaterfallResult> FillRungAsync(
        CoverageCell cell,
        int targetCount,
        ParametricTemplate? templateOrNull,
        string instituteId,
        DateTimeOffset? releaseDate = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (targetCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
        if (string.IsNullOrWhiteSpace(instituteId))
            throw new ArgumentException("instituteId required", nameof(instituteId));

        var deduper = new ParametricVariantDeduper();
        var stageOutcomes = new List<StageOutcome>();
        var allAccepted = new List<ParametricVariant>();
        var effectiveRelease = releaseDate ?? _options.DefaultReleaseDate;

        // ── Stage 1: parametric compile ──
        var s1 = await RunStage1Async(templateOrNull, cell, targetCount, deduper, ct);
        stageOutcomes.Add(s1);
        allAccepted.AddRange(s1.AcceptedVariants);

        if (allAccepted.Count >= targetCount)
        {
            RecordOutcome(cell, "strategy1_sufficient");
            return new WaterfallResult(cell, targetCount, allAccepted.Count, stageOutcomes, null, allAccepted);
        }

        // ── Stage 2: LLM isomorph ──
        var s2 = await RunStage2Async(cell, targetCount, allAccepted, deduper, instituteId, ct);
        stageOutcomes.Add(s2);
        allAccepted.AddRange(s2.AcceptedVariants);

        if (allAccepted.Count >= targetCount)
        {
            RecordOutcome(cell, "strategy2_closed_gap");
            return new WaterfallResult(cell, targetCount, allAccepted.Count, stageOutcomes, null, allAccepted);
        }

        // ── Stage 3: curator queue ──
        var gap = targetCount - allAccepted.Count;
        var (curatorId, s3) = await RunStage3Async(cell, gap, stageOutcomes, effectiveRelease, ct);
        stageOutcomes.Add(s3);
        RecordOutcome(cell, "curator_enqueued");
        RecordGap(cell, gap);

        return new WaterfallResult(cell, targetCount, allAccepted.Count, stageOutcomes, curatorId, allAccepted);
    }

    // ─── Stage 1 ───────────────────────────────────────────────────────

    private async Task<StageOutcome> RunStage1Async(
        ParametricTemplate? template,
        CoverageCell cell,
        int targetCount,
        ParametricVariantDeduper deduper,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (template is null)
        {
            // No template means Strategy 1 is unavailable for this cell —
            // not a failure, just a no-op. The cascade falls through to
            // stage 2 without a drop reason (stage 1 simply didn't run).
            sw.Stop();
            return new StageOutcome(
                WaterfallStage.Parametric, Executed: false,
                AcceptedCount: 0,
                AcceptedVariants: Array.Empty<ParametricVariant>(),
                Drops: Array.Empty<WaterfallDrop>(),
                ElapsedMs: sw.Elapsed.TotalMilliseconds);
        }

        // The baseSeed is derived from the cell address so the call is
        // reproducible across runs (debugging + ship-gate determinism).
        long baseSeed = ComputeCellSeed(cell);

        ParametricCompileReport report;
        try
        {
            report = await _parametricCompiler.CompileAsync(template, baseSeed, targetCount, ct);
        }
        catch (InsufficientSlotSpaceException ex)
        {
            // Slot space too small to satisfy targetCount. Compile with the
            // count the template can actually produce so stage 1 is "best
            // effort" and stage 2 picks up the gap. Never silently drops
            // variants — if even the reduced count fails we fall through
            // with zero.
            _logger.LogInformation(ex,
                "[COVERAGE_WATERFALL] Stage1 slot-space limited for {Cell} ({Produced}/{Req}); retrying at {Produced}",
                cell.Address, ex.Produced, ex.Requested, ex.Produced);

            if (ex.Produced <= 0)
            {
                sw.Stop();
                return new StageOutcome(
                    WaterfallStage.Parametric, Executed: true,
                    0, Array.Empty<ParametricVariant>(),
                    Array.Empty<WaterfallDrop>(),
                    sw.Elapsed.TotalMilliseconds);
            }

            try
            {
                report = await _parametricCompiler.CompileAsync(template, baseSeed, ex.Produced, ct);
            }
            catch (InsufficientSlotSpaceException ex2)
            {
                _logger.LogWarning(ex2,
                    "[COVERAGE_WATERFALL] Stage1 retry still undersupplied for {Cell}", cell.Address);
                sw.Stop();
                return new StageOutcome(
                    WaterfallStage.Parametric, Executed: true,
                    0, Array.Empty<ParametricVariant>(),
                    Array.Empty<WaterfallDrop>(),
                    sw.Elapsed.TotalMilliseconds);
            }
        }

        var admitted = new List<ParametricVariant>(report.Variants.Count);
        foreach (var v in report.Variants)
        {
            if (deduper.TryAdmit(v)) admitted.Add(v);
        }

        sw.Stop();
        CoverageWaterfallMetrics.WaterfallDuration.Record(
            sw.Elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("stage", "parametric"));

        return new StageOutcome(
            WaterfallStage.Parametric, Executed: true,
            AcceptedCount: admitted.Count,
            AcceptedVariants: admitted,
            Drops: Array.Empty<WaterfallDrop>(),
            ElapsedMs: sw.Elapsed.TotalMilliseconds);
    }

    private static long ComputeCellSeed(CoverageCell cell)
    {
        // Stable seed derived from the cell address. Same cell → same seed →
        // deterministic stage-1 output across runs.
        var bytes = System.Text.Encoding.UTF8.GetBytes(cell.Address);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return BitConverter.ToInt64(hash, 0);
    }

    // ─── Stage 2 ───────────────────────────────────────────────────────

    private async Task<StageOutcome> RunStage2Async(
        CoverageCell cell,
        int targetCount,
        IReadOnlyList<ParametricVariant> seedVariants,
        ParametricVariantDeduper deduper,
        string instituteId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var drops = new List<WaterfallDrop>();
        var admitted = new List<ParametricVariant>();

        var need = targetCount - seedVariants.Count;
        if (need <= 0)
        {
            sw.Stop();
            return new StageOutcome(WaterfallStage.LlmIsomorph, Executed: false,
                0, Array.Empty<ParametricVariant>(),
                Array.Empty<WaterfallDrop>(), sw.Elapsed.TotalMilliseconds);
        }

        // Budget gate: a pre-authorised daily spend cap per institute. We
        // reserve a conservative upper bound on cost before calling the
        // generator so over-budget institutes fall straight through to
        // stage 3 without incurring a single call.
        if (!await TryChargeBudgetAsync(instituteId, need, ct))
        {
            drops.Add(new WaterfallDrop(
                WaterfallStage.LlmIsomorph, WaterfallDropKind.BudgetCap,
                CandidatePreview: null,
                Detail: $"institute {instituteId} exceeded daily cap ${_options.InstituteDailyBudgetUsd:F2}"));
            CoverageWaterfallMetrics.DropTotal.Add(1,
                new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                new KeyValuePair<string, object?>("kind", "budget_cap"));
            sw.Stop();
            return new StageOutcome(WaterfallStage.LlmIsomorph, Executed: true,
                0, Array.Empty<ParametricVariant>(), drops, sw.Elapsed.TotalMilliseconds);
        }

        for (var attempt = 0; attempt < _options.MaxIsomorphAttempts && admitted.Count < need; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var remaining = need - admitted.Count;
            var req = new IsomorphRequest(cell, seedVariants, remaining, instituteId);
            IsomorphResult genResult;
            try
            {
                genResult = await _isomorphGenerator.GenerateAsync(req, ct);
            }
            catch (Exception ex)
            {
                drops.Add(new WaterfallDrop(
                    WaterfallStage.LlmIsomorph, WaterfallDropKind.IsomorphGeneratorError,
                    null, ex.Message));
                CoverageWaterfallMetrics.DropTotal.Add(1,
                    new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                    new KeyValuePair<string, object?>("kind", "generator_error"));
                break;
            }

            if (genResult.Verdict == IsomorphVerdict.CircuitOpen)
            {
                drops.Add(new WaterfallDrop(
                    WaterfallStage.LlmIsomorph, WaterfallDropKind.CircuitOpen,
                    null, genResult.ErrorDetail));
                CoverageWaterfallMetrics.DropTotal.Add(1,
                    new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                    new KeyValuePair<string, object?>("kind", "circuit_open"));
                break;
            }
            if (genResult.Verdict == IsomorphVerdict.GeneratorError)
            {
                drops.Add(new WaterfallDrop(
                    WaterfallStage.LlmIsomorph, WaterfallDropKind.IsomorphGeneratorError,
                    null, genResult.ErrorDetail));
                CoverageWaterfallMetrics.DropTotal.Add(1,
                    new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                    new KeyValuePair<string, object?>("kind", "generator_error"));
                break;
            }

            CoverageWaterfallMetrics.LlmCostUsdTotal.Add(genResult.EstimatedCostUsd,
                new KeyValuePair<string, object?>("institute", instituteId));

            foreach (var cand in genResult.Candidates)
            {
                if (admitted.Count >= need) break;

                var preview = Truncate(cand.Stem, 120);

                // ── Ministry-similarity gate (ADR-0043) ──
                var sim = _similarityChecker.Score(cand.Stem, cell.Subject, cell.Track.ToString().ToLowerInvariant());
                if (sim.IsTooClose)
                {
                    drops.Add(new WaterfallDrop(
                        WaterfallStage.LlmIsomorph, WaterfallDropKind.MinistrySimilarity,
                        preview,
                        $"cosine={sim.Score:F2} >= threshold={_similarityChecker.Threshold:F2} near={sim.NearestReferenceId}",
                        sim.Score));
                    CoverageWaterfallMetrics.DropTotal.Add(1,
                        new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                        new KeyValuePair<string, object?>("kind", "ministry_similarity"));
                    continue;
                }

                // ── CAS gate (ADR-0002) ──
                var questionId = $"wf/{cell.Address}/{admitted.Count + drops.Count}";
                CasGateResult gate;
                try
                {
                    gate = await _casGate.VerifyForCreateAsync(
                        questionId, cell.Subject, cand.Stem, cand.AnswerExpr,
                        variable: null, ct: ct);
                }
                catch (Exception ex)
                {
                    drops.Add(new WaterfallDrop(
                        WaterfallStage.LlmIsomorph, WaterfallDropKind.CasRejected,
                        preview, $"gate-error: {ex.Message}"));
                    CoverageWaterfallMetrics.DropTotal.Add(1,
                        new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                        new KeyValuePair<string, object?>("kind", "cas_rejected"));
                    continue;
                }

                if (gate.Outcome != CasGateOutcome.Verified)
                {
                    drops.Add(new WaterfallDrop(
                        WaterfallStage.LlmIsomorph, WaterfallDropKind.CasRejected,
                        preview,
                        $"gate={gate.Outcome} engine={gate.Engine} reason={gate.FailureReason}"));
                    CoverageWaterfallMetrics.DropTotal.Add(1,
                        new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                        new KeyValuePair<string, object?>("kind", "cas_rejected"));
                    continue;
                }

                // Wrap the accepted candidate into a ParametricVariant-shaped record
                // so downstream dedupe + caller API is uniform across strategies.
                var variant = new ParametricVariant(
                    TemplateId: $"llm:{cell.Address}",
                    TemplateVersion: 1,
                    Seed: admitted.Count + 1,
                    SlotValues: Array.Empty<ParametricSlotValue>(),
                    RenderedStem: cand.Stem,
                    CanonicalAnswer: gate.CanonicalAnswer,
                    Distractors: cand.Distractors
                        .Select(d => new ParametricDistractor(d.MisconceptionId, d.Text, null))
                        .ToArray());

                if (!deduper.TryAdmit(variant))
                {
                    drops.Add(new WaterfallDrop(
                        WaterfallStage.LlmIsomorph, WaterfallDropKind.Duplicate,
                        preview, "canonical-form already admitted"));
                    CoverageWaterfallMetrics.DropTotal.Add(1,
                        new KeyValuePair<string, object?>("stage", "llm_isomorph"),
                        new KeyValuePair<string, object?>("kind", "duplicate"));
                    continue;
                }

                admitted.Add(variant);
            }
        }

        sw.Stop();
        CoverageWaterfallMetrics.WaterfallDuration.Record(
            sw.Elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("stage", "llm_isomorph"));

        return new StageOutcome(WaterfallStage.LlmIsomorph, Executed: true,
            admitted.Count, admitted, drops, sw.Elapsed.TotalMilliseconds);
    }

    private async Task<bool> TryChargeBudgetAsync(string instituteId, int needed, CancellationToken ct)
    {
        if (_budgetService is null)
        {
            // No budget service wired — accept. Tests and offline mode.
            return true;
        }
        // Worst-case cost estimate: one tier-3 call per needed variant at
        // the tier3 ceiling of $0.015. This is deliberately a lower-bound
        // *charge* used as a gate, not an accounting record — actual cost
        // is counted via LlmCostUsdTotal when the generator returns.
        var reserve = Math.Max(0.015 * needed, 0.015);
        try
        {
            return await _budgetService.TryChargeTenantAsync(instituteId, reserve, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[COVERAGE_WATERFALL] budget-service error for {Institute}; failing closed",
                instituteId);
            return false; // fail closed — do not spend if we cannot charge
        }
    }

    // ─── Stage 3 ───────────────────────────────────────────────────────

    private async Task<(string TaskId, StageOutcome Outcome)> RunStage3Async(
        CoverageCell cell,
        int gap,
        IReadOnlyList<StageOutcome> priorOutcomes,
        DateTimeOffset releaseDate,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var priorDrops = priorOutcomes
            .SelectMany(o => o.Drops)
            .Select(d => new CuratorDropSummary(d.Stage, d.Kind, d.Detail))
            .ToList();

        // Idempotent id: same cell + same release date → same task id.
        var idInput = $"{cell.Address}|{releaseDate:O}";
        var idHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(idInput));
        var id = "curator/" + Convert.ToHexString(idHash).AsSpan(0, 16).ToString().ToLowerInvariant();

        var deadline = releaseDate - _options.CuratorLeadTime;
        var item = new CuratorQueueItem
        {
            Id = id,
            Cell = cell,
            Gap = gap,
            Deadline = deadline,
            PriorDrops = priorDrops,
            MinistryReferenceId = priorOutcomes
                .SelectMany(o => o.Drops)
                .Select(d => d.Detail)
                .FirstOrDefault(s => s?.Contains("near=") == true)
        };

        var taskId = await _curatorQueue.EnqueueAsync(item, ct);

        CoverageWaterfallMetrics.CuratorEnqueuedTotal.Add(1,
            new KeyValuePair<string, object?>("cell", cell.Address));

        sw.Stop();
        CoverageWaterfallMetrics.WaterfallDuration.Record(
            sw.Elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("stage", "curator_queue"));

        return (taskId, new StageOutcome(
            WaterfallStage.CuratorQueue, Executed: true,
            0, Array.Empty<ParametricVariant>(),
            Array.Empty<WaterfallDrop>(),
            sw.Elapsed.TotalMilliseconds));
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static void RecordOutcome(CoverageCell cell, string result) =>
        CoverageWaterfallMetrics.RungFilledTotal.Add(1,
            new KeyValuePair<string, object?>("cell", cell.Address),
            new KeyValuePair<string, object?>("result", result));

    private static void RecordGap(CoverageCell cell, int gap)
    {
        if (gap <= 0) return;
        CoverageWaterfallMetrics.RungGapTotal.Add(gap,
            new KeyValuePair<string, object?>("cell", cell.Address));
    }

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
}
