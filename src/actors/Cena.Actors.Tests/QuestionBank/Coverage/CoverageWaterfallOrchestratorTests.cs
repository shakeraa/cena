// =============================================================================
// Cena Platform — Coverage Waterfall Orchestrator Tests (prr-201)
//
// Exercises the cascade:
//   1. Stage 1 sufficient → stage 2 never called, no curator task.
//   2. Stage 1 partial → stage 2 fills the rest; all CAS-verified.
//   3. CAS failure on stage 2 drops the candidate.
//   4. Ministry similarity too high drops the candidate (ADR-0043).
//   5. Budget cap short-circuits stage 2 to stage 3.
//   6. Dedupe across strategies.
//   7. All three stages cascade → curator enqueued.
//   8. Curator enqueue is idempotent on re-run.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.QuestionBank.Coverage;
using Cena.Actors.QuestionBank.Templates;
using Cena.Actors.Tests.QuestionBank.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

public sealed class CoverageWaterfallOrchestratorTests
{
    private static ParametricTemplate LinearTemplate(int slotCount = 10) => new()
    {
        Id = "t_cov_linear",
        Version = 1,
        Subject = "math",
        Topic = "algebra.linear_equations",
        Track = TemplateTrack.FourUnit,
        Difficulty = TemplateDifficulty.Medium,
        Methodology = TemplateMethodology.Halabi,
        StemTemplate = "Solve for x: {a}x + {b} = {c}",
        SolutionExpr = "(c - b) / a",
        VariableName = "x",
        AcceptShapes = AcceptShape.Integer,
        Slots = new[]
        {
            new ParametricSlot
            {
                Name = "a", Kind = ParametricSlotKind.Integer,
                IntegerMin = 2, IntegerMax = 9, IntegerExclude = new[] { 0 }
            },
            new ParametricSlot
            {
                Name = "b", Kind = ParametricSlotKind.Integer,
                IntegerMin = -20, IntegerMax = 20
            },
            new ParametricSlot
            {
                Name = "c", Kind = ParametricSlotKind.Integer,
                IntegerMin = -50, IntegerMax = 50
            },
        }
    };

    private static CoverageCell Cell(TemplateDifficulty d = TemplateDifficulty.Medium) => new()
    {
        Track = TemplateTrack.FourUnit,
        Subject = "math",
        Topic = "algebra.linear_equations",
        Difficulty = d,
        Methodology = TemplateMethodology.Halabi,
        QuestionType = "multiple-choice",
        Language = "en"
    };

    private static CoverageWaterfallOrchestrator NewOrchestrator(
        IIsomorphGenerator? iso = null,
        ICasVerificationGate? gate = null,
        MinistrySimilarityChecker? similarity = null,
        InMemoryCuratorQueue? queue = null,
        CoverageWaterfallOptions? opts = null)
    {
        var compiler = new ParametricCompiler(
            new FakeParametricRenderer(),
            NullLogger<ParametricCompiler>.Instance);

        return new CoverageWaterfallOrchestrator(
            compiler,
            iso ?? new FakeIsomorphGenerator(),
            similarity ?? new MinistrySimilarityChecker(new EmptyMinistryReferenceCorpus()),
            gate ?? new StubCasVerificationGate(),
            queue ?? new InMemoryCuratorQueue(),
            Options.Create(opts ?? new CoverageWaterfallOptions()),
            NullLogger<CoverageWaterfallOrchestrator>.Instance,
            budgetService: null);
    }

    // ── 1. Strategy 1 sufficient ────────────────────────────────────────

    [Fact]
    public async Task Strategy1_Sufficient_NoLlmCalls_NoCurator()
    {
        var iso = new FakeIsomorphGenerator();
        var queue = new InMemoryCuratorQueue();
        var orch = NewOrchestrator(iso: iso, queue: queue);

        var result = await orch.FillRungAsync(
            Cell(), targetCount: 5, templateOrNull: LinearTemplate(), instituteId: "inst1");

        Assert.True(result.IsFullyCovered);
        Assert.Equal(5, result.Filled);
        Assert.Equal(0, iso.CallCount); // stage 2 never called
        Assert.Null(result.CuratorTaskId);
        Assert.Empty(queue.Items);
        Assert.False(result.UsedLlm);
        Assert.False(result.UsedCurator);
    }

    // ── 2. Strategy 1 partial → Strategy 2 fills the rest ──────────────

    [Fact]
    public async Task Strategy1_Partial_TriggersStrategy2_CasVerified()
    {
        // A template with a tiny slot space (only a=2, b∈{0}, c∈{2,4,6})
        // yields ≤ 3 distinct canonical forms. Request 5 → stage 1 partial.
        var template = new ParametricTemplate
        {
            Id = "t_tiny",
            Version = 1,
            Subject = "math",
            Topic = "algebra.linear_equations",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "Solve for x: {a}x + {b} = {c}",
            SolutionExpr = "(c - b) / a",
            AcceptShapes = AcceptShape.Integer,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 2, IntegerMax = 2 },
                new ParametricSlot { Name = "b", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 0, IntegerMax = 0 },
                new ParametricSlot { Name = "c", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 2, IntegerMax = 6, IntegerExclude = new[] { 3, 5 } },
            }
        };

        var iso = new FakeIsomorphGenerator()
            .EnqueueCandidates(
                new IsomorphCandidate("LLM isomorph A: 3x+1=10", "3",
                    Array.Empty<IsomorphDistractor>(), null),
                new IsomorphCandidate("LLM isomorph B: 5x-2=13", "3",
                    Array.Empty<IsomorphDistractor>(), null));

        var orch = NewOrchestrator(iso: iso);

        var result = await orch.FillRungAsync(
            Cell(TemplateDifficulty.Easy), targetCount: 5, templateOrNull: template, instituteId: "inst1");

        Assert.True(result.UsedLlm);
        // Stage 1 admitted ≤3, stage 2 filled remainder.
        var s1 = result.Stages.First(s => s.Stage == WaterfallStage.Parametric);
        var s2 = result.Stages.First(s => s.Stage == WaterfallStage.LlmIsomorph);
        Assert.True(s1.Executed);
        Assert.True(s2.Executed);
        Assert.True(s1.AcceptedCount > 0 && s1.AcceptedCount < 5);
        Assert.True(s2.AcceptedCount >= 1);
        Assert.Equal(s1.AcceptedCount + s2.AcceptedCount, result.Filled);
    }

    // ── 3. CAS failure on Strategy 2 drops candidate ───────────────────

    [Fact]
    public async Task Strategy2_CasRejected_DropsCandidate()
    {
        // Stage 1 returns no template — cascade straight to stage 2
        // which produces one candidate that CAS rejects.
        var iso = new FakeIsomorphGenerator()
            .EnqueueCandidates(
                new IsomorphCandidate("Broken stem 2+2=5", "5",
                    Array.Empty<IsomorphDistractor>(), null));
        var gate = new StubCasVerificationGate
        {
            ShouldVerify = (_, ans) => ans == "5" ? CasGateOutcome.Failed : CasGateOutcome.Verified
        };

        var orch = NewOrchestrator(iso: iso, gate: gate);

        var result = await orch.FillRungAsync(
            Cell(), targetCount: 3, templateOrNull: null, instituteId: "inst1");

        var s2 = result.Stages.First(s => s.Stage == WaterfallStage.LlmIsomorph);
        Assert.Equal(0, s2.AcceptedCount);
        Assert.Contains(s2.Drops, d => d.Kind == WaterfallDropKind.CasRejected);
        Assert.NotNull(result.CuratorTaskId);
    }

    // ── 4. Ministry similarity rejection (ADR-0043) ────────────────────

    [Fact]
    public async Task Strategy2_MinistrySimilarityTooHigh_DropsCandidate()
    {
        var ministryStem = "Solve the equation 5x plus 7 equals 42 and verify your answer.";
        var iso = new FakeIsomorphGenerator()
            .EnqueueCandidates(
                new IsomorphCandidate(
                    // near-identical to ministry reference
                    "Solve the equation 5x plus 7 equals 42 and verify your answer.",
                    "7",
                    Array.Empty<IsomorphDistractor>(), null));
        var corpus = new StubMinistryReferenceCorpus(("bagrut-2021-q1", ministryStem));
        var similarity = new MinistrySimilarityChecker(corpus, threshold: 0.7);

        var orch = NewOrchestrator(iso: iso, similarity: similarity);

        var result = await orch.FillRungAsync(
            Cell(), targetCount: 3, templateOrNull: null, instituteId: "inst1");

        var s2 = result.Stages.First(s => s.Stage == WaterfallStage.LlmIsomorph);
        Assert.Contains(s2.Drops, d => d.Kind == WaterfallDropKind.MinistrySimilarity);
    }

    // ── 5. Budget cap skips stage 2 ────────────────────────────────────

    [Fact]
    public async Task Strategy2_BudgetCapExceeded_SkipsToCurator()
    {
        // Budget service that always refuses — emulates institute over cap.
        var iso = new FakeIsomorphGenerator()
            .EnqueueCandidates(new IsomorphCandidate("Should never be used", "1",
                Array.Empty<IsomorphDistractor>(), null));

        var compiler = new ParametricCompiler(
            new FakeParametricRenderer(), NullLogger<ParametricCompiler>.Instance);
        var queue = new InMemoryCuratorQueue();

        var orch = new CoverageWaterfallOrchestrator(
            compiler,
            iso,
            new MinistrySimilarityChecker(new EmptyMinistryReferenceCorpus()),
            new StubCasVerificationGate(),
            queue,
            Options.Create(new CoverageWaterfallOptions()),
            NullLogger<CoverageWaterfallOrchestrator>.Instance,
            budgetService: new BudgetRefusingService());

        var result = await orch.FillRungAsync(
            Cell(), targetCount: 3, templateOrNull: null, instituteId: "inst1");

        Assert.Equal(0, iso.CallCount); // generator never called
        var s2 = result.Stages.First(s => s.Stage == WaterfallStage.LlmIsomorph);
        Assert.Contains(s2.Drops, d => d.Kind == WaterfallDropKind.BudgetCap);
        Assert.NotNull(result.CuratorTaskId);
        Assert.Single(queue.Items);
    }

    // ── 6. Cross-strategy dedupe ───────────────────────────────────────

    [Fact]
    public async Task DedupeAcrossStrategies_RejectsDuplicateFromStage2()
    {
        // Stage 1 runs with a tiny slot space and emits a known canonical
        // answer; stage 2 returns the exact same stem+answer → dropped as
        // duplicate.
        var template = new ParametricTemplate
        {
            Id = "t_dup",
            Version = 1,
            Subject = "math",
            Topic = "algebra.linear_equations",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Medium,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "Solve: {a}x = {c}",
            SolutionExpr = "c / a",
            AcceptShapes = AcceptShape.Integer,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 2, IntegerMax = 2 },
                new ParametricSlot { Name = "c", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 4, IntegerMax = 4 },
            }
        };
        // After stage 1 we'll have exactly one canonical variant: stem
        // "Solve: 2x = 4", answer "2".
        var iso = new FakeIsomorphGenerator()
            .EnqueueCandidates(
                new IsomorphCandidate("Solve: 2x = 4", "2",
                    Array.Empty<IsomorphDistractor>(), null));

        var orch = NewOrchestrator(iso: iso);
        var result = await orch.FillRungAsync(
            Cell(), targetCount: 3, templateOrNull: template, instituteId: "inst1");

        var s2 = result.Stages.First(s => s.Stage == WaterfallStage.LlmIsomorph);
        Assert.Contains(s2.Drops, d => d.Kind == WaterfallDropKind.Duplicate);
    }

    // ── 7. All three stages cascade ────────────────────────────────────

    [Fact]
    public async Task AllThreeStages_Cascade_WhenStage1And2Undersupply()
    {
        // No template + isomorph returns nothing → stage 3 curator enqueue.
        var iso = new FakeIsomorphGenerator()
            .EnqueueCandidates(); // explicitly empty
        var queue = new InMemoryCuratorQueue();

        var orch = NewOrchestrator(iso: iso, queue: queue);
        var result = await orch.FillRungAsync(
            Cell(), targetCount: 5, templateOrNull: null, instituteId: "inst1");

        Assert.False(result.IsFullyCovered);
        Assert.Equal(5, result.Gap);
        Assert.NotNull(result.CuratorTaskId);
        Assert.Single(queue.Items);
        var item = queue.Items.First();
        Assert.Equal(5, item.Gap);
    }

    // ── 8. Curator enqueue idempotency ─────────────────────────────────

    [Fact]
    public async Task CuratorEnqueue_IsIdempotent_OnReRun()
    {
        var iso = new FakeIsomorphGenerator()
            .EnqueueCandidates()
            .EnqueueCandidates();
        var queue = new InMemoryCuratorQueue();

        var releaseDate = new DateTimeOffset(2026, 06, 01, 0, 0, 0, TimeSpan.Zero);
        var orch = NewOrchestrator(iso: iso, queue: queue);

        var r1 = await orch.FillRungAsync(Cell(), 5, null, "inst1", releaseDate);
        var r2 = await orch.FillRungAsync(Cell(), 5, null, "inst1", releaseDate);

        Assert.Equal(r1.CuratorTaskId, r2.CuratorTaskId);
        // Second call still invokes EnqueueAsync (it's the queue's job to
        // deduplicate). One stored item in the queue state.
        Assert.Single(queue.Items);
    }

    // ── 9. Strategy 1 zero LLM calls ───────────────────────────────────

    [Fact]
    public async Task Strategy1ZeroLlmCalls_WhenTemplateExceedsTarget()
    {
        var iso = new FakeIsomorphGenerator();
        var queue = new InMemoryCuratorQueue();
        var orch = NewOrchestrator(iso: iso, queue: queue);

        // Ask for 2 variants from a template whose space is ≥ 8*6*26 — stage 1
        // produces both; isomorph generator never runs.
        var result = await orch.FillRungAsync(
            Cell(), targetCount: 2, templateOrNull: LinearTemplate(), instituteId: "inst1");

        Assert.True(result.IsFullyCovered);
        Assert.Equal(0, iso.CallCount);
        Assert.False(result.UsedCurator);
        Assert.False(result.UsedLlm);
    }

    // ── Helper: budget service that always refuses ─────────────────────

    private sealed class BudgetRefusingService : Cena.Actors.RateLimit.ICostBudgetService
    {
        public Task<bool> TryChargeTenantAsync(string tenantId, double estimatedCostUsd, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<(double Used, double Limit)> GetTenantUsageAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult((100.0, 50.0));
        public Task<(double Used, double Limit)> GetGlobalUsageAsync(CancellationToken ct = default)
            => Task.FromResult((1000.0, 1000.0));
    }
}
