// =============================================================================
// Cena Platform — CoverageCellVariantCounter tests (prr-210)
//
// The counter is a thin but load-bearing bridge between the prr-201
// orchestrator and the prr-210 ship-gate. These tests exercise:
//
//   1. Record overwrites prior count for the same cell.
//   2. Record + MarkBelowSlo compose (the two labels share a cell).
//   3. Snapshot returns every recorded cell.
//   4. Record rejects negative counts.
//   5. Orchestrator calls both Record and MarkBelowSlo(true) on the
//      curator-enqueued path.
//   6. Orchestrator clears below-SLO flag when stage 1 fills the cell.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.QuestionBank.Coverage;
using Cena.Actors.QuestionBank.Templates;
using Cena.Actors.Tests.QuestionBank.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

public sealed class CoverageCellVariantCounterTests
{
    private static CoverageCell Cell(string topic = "algebra.linear_equations") => new()
    {
        Track = TemplateTrack.FourUnit,
        Subject = "math",
        Topic = topic,
        Difficulty = TemplateDifficulty.Medium,
        Methodology = TemplateMethodology.Halabi,
        QuestionType = "multiple-choice",
        Language = "en"
    };

    [Fact]
    public void Record_OverwritesPriorCount()
    {
        var counter = new CoverageCellVariantCounter();
        var cell = Cell();

        counter.Record(cell, 3);
        counter.Record(cell, 8);

        var snap = counter.Snapshot().Single(s => s.Cell.Address == cell.Address);
        Assert.Equal(8, snap.VariantCount);
    }

    [Fact]
    public void MarkBelowSlo_ComposesWithRecord()
    {
        var counter = new CoverageCellVariantCounter();
        var cell = Cell();

        counter.Record(cell, 4);
        counter.MarkBelowSlo(cell, true);

        var snap = counter.Snapshot().Single(s => s.Cell.Address == cell.Address);
        Assert.Equal(4, snap.VariantCount);
        Assert.True(snap.BelowSlo);

        counter.MarkBelowSlo(cell, false);
        snap = counter.Snapshot().Single(s => s.Cell.Address == cell.Address);
        Assert.Equal(4, snap.VariantCount);
        Assert.False(snap.BelowSlo);
    }

    [Fact]
    public void Record_RejectsNegativeCount()
    {
        var counter = new CoverageCellVariantCounter();
        Assert.Throws<ArgumentOutOfRangeException>(() => counter.Record(Cell(), -1));
    }

    [Fact]
    public void Snapshot_ReturnsEveryRecordedCell()
    {
        var counter = new CoverageCellVariantCounter();
        counter.Record(Cell("algebra.linear_equations"), 10);
        counter.Record(Cell("algebra.quadratic_equations"), 5);
        counter.Record(Cell("calculus.differentiation"), 7);

        var snap = counter.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Contains(snap, s => s.Cell.Topic == "algebra.linear_equations" && s.VariantCount == 10);
        Assert.Contains(snap, s => s.Cell.Topic == "algebra.quadratic_equations" && s.VariantCount == 5);
        Assert.Contains(snap, s => s.Cell.Topic == "calculus.differentiation" && s.VariantCount == 7);
    }

    // ── Orchestrator integration ───────────────────────────────────────

    private static ParametricTemplate LinearTemplate() => new()
    {
        Id = "t_cov_prr210",
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
            new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 2, IntegerMax = 9, IntegerExclude = new[] { 0 } },
            new ParametricSlot { Name = "b", Kind = ParametricSlotKind.Integer, IntegerMin = -20, IntegerMax = 20 },
            new ParametricSlot { Name = "c", Kind = ParametricSlotKind.Integer, IntegerMin = -50, IntegerMax = 50 },
        }
    };

    private static CoverageWaterfallOrchestrator NewOrchestrator(
        ICoverageCellVariantCounter counter,
        IIsomorphGenerator iso,
        InMemoryCuratorQueue queue)
    {
        var compiler = new ParametricCompiler(
            new FakeParametricRenderer(),
            NullLogger<ParametricCompiler>.Instance);

        return new CoverageWaterfallOrchestrator(
            compiler,
            iso,
            new MinistrySimilarityChecker(new EmptyMinistryReferenceCorpus()),
            new StubCasVerificationGate(),
            queue,
            Options.Create(new CoverageWaterfallOptions()),
            NullLogger<CoverageWaterfallOrchestrator>.Instance,
            budgetService: null,
            variantCounter: counter);
    }

    [Fact]
    public async Task Orchestrator_RecordsBelowSlo_OnCuratorEnqueuedPath()
    {
        var counter = new CoverageCellVariantCounter();
        var iso = new FakeIsomorphGenerator().EnqueueCandidates();  // empty → cascades to stage 3
        var queue = new InMemoryCuratorQueue();
        var orch = NewOrchestrator(counter, iso, queue);

        var cell = Cell();
        var result = await orch.FillRungAsync(cell, targetCount: 5, templateOrNull: null, instituteId: "inst-prr210");

        Assert.NotNull(result.CuratorTaskId);
        var snap = counter.Snapshot().Single(s => s.Cell.Address == cell.Address);
        Assert.True(snap.BelowSlo, "curator-enqueued path must mark the cell below SLO");
        Assert.Equal(0, snap.VariantCount);
    }

    [Fact]
    public async Task Orchestrator_ClearsBelowSlo_OnStage1FillsCompletely()
    {
        var counter = new CoverageCellVariantCounter();
        var iso = new FakeIsomorphGenerator();
        var queue = new InMemoryCuratorQueue();
        var orch = NewOrchestrator(counter, iso, queue);

        var cell = Cell();

        // First: force a below-SLO state via an empty cascade.
        var empty = new FakeIsomorphGenerator().EnqueueCandidates();
        var orchEmpty = NewOrchestrator(counter, empty, queue);
        await orchEmpty.FillRungAsync(cell, 5, null, "inst-prr210");
        Assert.True(counter.Snapshot().Single(s => s.Cell.Address == cell.Address).BelowSlo);

        // Then fill the cell via stage 1 with a healthy template.
        var result = await orch.FillRungAsync(cell, targetCount: 3, templateOrNull: LinearTemplate(), instituteId: "inst-prr210");
        Assert.True(result.IsFullyCovered);

        var snap = counter.Snapshot().Single(s => s.Cell.Address == cell.Address);
        Assert.False(snap.BelowSlo, "stage-1-fully-covered path must clear below-SLO");
        Assert.Equal(3, snap.VariantCount);
    }
}
