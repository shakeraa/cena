// =============================================================================
// Cena Platform — PerTargetDiagnosticEngine integration tests (prr-228)
//
// Pins the critical end-to-end behaviour:
//   - A student with two distinct ExamTargets produces two separate
//     skill-keyed mastery rows (one per target), even for the SAME skill.
//   - Skipped responses contribute to the block cap but do NOT feed the
//     BKT tracker's UpdateAsync (no mastery row change from a skip).
//   - TopicFeeling capture is carried over via the engine's summary
//     (the engine doesn't own the TopicFeelings store — the endpoint layer
//     does — but the summary shape must make it possible for the endpoint
//     to surface per-skill priors keyed by skill so the RDY-057 document
//     can be rebuilt).
// =============================================================================

using Cena.Actors.Diagnosis.PerTarget;
using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PerTarget;

public sealed class PerTargetDiagnosticEngineTests
{
    private const string StudentId = "student-abc-123";

    private static (PerTargetDiagnosticEngine engine, ISkillKeyedMasteryStore store)
        BuildEngine()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var tracker = new BktStateTracker(store, new DefaultBktParameterProvider());
        var engine = new PerTargetDiagnosticEngine(
            tracker,
            NullLogger<PerTargetDiagnosticEngine>.Instance);
        return (engine, store);
    }

    [Fact]
    public async Task ProcessBlockAsync_Rejects_EmptyResponses()
    {
        var (engine, _) = BuildEngine();
        var target = ExamTargetCode.Parse("bagrut-math-5yu");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await engine.ProcessBlockAsync(
                StudentId, target, Array.Empty<DiagnosticBlockResponse>());
        });
    }

    [Fact]
    public async Task ProcessBlockAsync_TwoTargets_ProducesSeparateMasteryRows()
    {
        var (engine, store) = BuildEngine();

        var t1 = ExamTargetCode.Parse("bagrut-math-5yu");
        var t2 = ExamTargetCode.Parse("sat-math");
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");

        // Same skill, same student, two different targets. Each block hits
        // the floor-cap so we get priors for both.
        var responsesT1 = BuildAllCorrect(skill, 6);
        var responsesT2 = BuildAllWrong(skill, 6);

        var summaryT1 = await engine.ProcessBlockAsync(StudentId, t1, responsesT1);
        var summaryT2 = await engine.ProcessBlockAsync(StudentId, t2, responsesT2);

        Assert.Equal(6, summaryT1.ItemsServed);
        Assert.Equal(6, summaryT2.ItemsServed);

        // Two distinct rows, keyed on the (student, target, skill) tuple.
        var rowT1 = await store.TryGetAsync(new MasteryKey(StudentId, t1, skill));
        var rowT2 = await store.TryGetAsync(new MasteryKey(StudentId, t2, skill));

        Assert.NotNull(rowT1);
        Assert.NotNull(rowT2);

        // And — critically — the two posteriors diverge in the expected
        // direction: all-correct target > all-wrong target.
        Assert.True(rowT1!.MasteryProbability > rowT2!.MasteryProbability,
            $"target1 (all-correct) posterior {rowT1.MasteryProbability} should exceed "
            + $"target2 (all-wrong) posterior {rowT2.MasteryProbability}");
    }

    [Fact]
    public async Task ProcessBlockAsync_SkippedResponses_DoNotUpdateMasteryRow()
    {
        var (engine, store) = BuildEngine();

        var target = ExamTargetCode.Parse("bagrut-math-5yu");
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");

        // A block entirely made of skips. The engine still returns a summary
        // but no mastery row should exist — UpdateAsync was never called.
        var skips = Enumerable.Range(0, 6)
            .Select(i => new DiagnosticBlockResponse(
                ItemId: $"q{i}",
                SkillCode: skill,
                Action: DiagnosticResponseAction.Skipped,
                Correct: false,
                DifficultyIrt: -0.5))
            .ToList();

        var summary = await engine.ProcessBlockAsync(StudentId, target, skips);

        Assert.Equal(6, summary.ItemsServed);
        Assert.Equal(0, summary.ItemsAnswered);
        Assert.Equal(6, summary.ItemsSkipped);

        var row = await store.TryGetAsync(new MasteryKey(StudentId, target, skill));
        Assert.Null(row);
    }

    [Fact]
    public async Task ProcessBlockAsync_MixedSkipsAndAnswers_OnlyAnswersDrivePosterior()
    {
        var (engine, store) = BuildEngine();

        var target = ExamTargetCode.Parse("bagrut-math-5yu");
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");

        var responses = new List<DiagnosticBlockResponse>
        {
            // 3 answered-correct
            new("q1", skill, DiagnosticResponseAction.Answered, true, -0.5),
            new("q2", skill, DiagnosticResponseAction.Answered, true, 0.0),
            new("q3", skill, DiagnosticResponseAction.Answered, true, 0.5),
            // 3 skipped
            new("q4", skill, DiagnosticResponseAction.Skipped, false, 0.5),
            new("q5", skill, DiagnosticResponseAction.Skipped, false, 0.8),
            new("q6", skill, DiagnosticResponseAction.Skipped, false, 1.0),
        };

        var summary = await engine.ProcessBlockAsync(StudentId, target, responses);

        Assert.Equal(6, summary.ItemsServed);
        Assert.Equal(3, summary.ItemsAnswered);
        Assert.Equal(3, summary.ItemsSkipped);

        var row = await store.TryGetAsync(new MasteryKey(StudentId, target, skill));
        Assert.NotNull(row);
        // Three correct answers only — attempt count should be 3, not 6.
        Assert.Equal(3, row!.AttemptCount);
    }

    [Fact]
    public async Task ProcessBlockAsync_ReasonTag_ReflectsCeilingCap()
    {
        var (engine, _) = BuildEngine();
        var target = ExamTargetCode.Parse("bagrut-math-5yu");
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");

        var responses = BuildMixed(skill, DiagnosticBlockThresholds.CeilingCap);

        var summary = await engine.ProcessBlockAsync(StudentId, target, responses);
        Assert.Equal(AdaptiveStopDecision.StopCeiling, summary.StopReason);
    }

    [Fact]
    public async Task ProcessBlockAsync_MultipleSkills_OneRowPerSkill()
    {
        var (engine, store) = BuildEngine();
        var target = ExamTargetCode.Parse("bagrut-math-5yu");

        var skillA = SkillCode.Parse("math.algebra.quadratic-equations");
        var skillB = SkillCode.Parse("math.geometry.pythagoras");

        var responses = new List<DiagnosticBlockResponse>
        {
            new("q1", skillA, DiagnosticResponseAction.Answered, true, -0.5),
            new("q2", skillA, DiagnosticResponseAction.Answered, true, 0.0),
            new("q3", skillB, DiagnosticResponseAction.Answered, false, 0.0),
            new("q4", skillB, DiagnosticResponseAction.Answered, false, 0.5),
            new("q5", skillA, DiagnosticResponseAction.Answered, true, 0.5),
            new("q6", skillB, DiagnosticResponseAction.Answered, false, 1.0),
        };

        var summary = await engine.ProcessBlockAsync(StudentId, target, responses);

        Assert.Equal(2, summary.SkillPriors.Count);
        Assert.Contains(skillA, summary.SkillPriors.Keys);
        Assert.Contains(skillB, summary.SkillPriors.Keys);

        var rowA = await store.TryGetAsync(new MasteryKey(StudentId, target, skillA));
        var rowB = await store.TryGetAsync(new MasteryKey(StudentId, target, skillB));

        Assert.NotNull(rowA);
        Assert.NotNull(rowB);
        Assert.True(rowA!.MasteryProbability > rowB!.MasteryProbability,
            "skillA (all-correct) should outscore skillB (all-wrong)");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static List<DiagnosticBlockResponse> BuildAllCorrect(SkillCode skill, int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new DiagnosticBlockResponse(
                ItemId: $"q{i}",
                SkillCode: skill,
                Action: DiagnosticResponseAction.Answered,
                Correct: true,
                DifficultyIrt: -1.0 + 0.3 * i))
            .ToList();
    }

    private static List<DiagnosticBlockResponse> BuildAllWrong(SkillCode skill, int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new DiagnosticBlockResponse(
                ItemId: $"q{i}",
                SkillCode: skill,
                Action: DiagnosticResponseAction.Answered,
                Correct: false,
                DifficultyIrt: -1.0 + 0.3 * i))
            .ToList();
    }

    private static List<DiagnosticBlockResponse> BuildMixed(SkillCode skill, int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new DiagnosticBlockResponse(
                ItemId: $"q{i}",
                SkillCode: skill,
                Action: DiagnosticResponseAction.Answered,
                Correct: i % 2 == 0,
                DifficultyIrt: -1.0 + 0.3 * i))
            .ToList();
    }
}
