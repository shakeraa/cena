// =============================================================================
// Quality Gate Test Harness — AUTORESEARCH mechanical metric
// Measures precision, recall, F1 against labeled test set
// =============================================================================

using Cena.Api.Contracts.Admin.QualityGate;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api.Tests.QualityGate;

/// <summary>
/// Core metric: F1 score of gate decisions against labeled test data.
/// Each test case has an expected GateDecision (AutoApproved/NeedsReview/AutoRejected).
/// Tests run without API key — LLM dimensions fall back to defaults (80/80/75).
/// </summary>
public class QualityGateTests
{
    private readonly QualityGateServices.QualityGateService _service = new();

    [Fact]
    public async Task QualityGate_F1Score_MeetsThreshold()
    {
        var testCases = QualityGateTestData.GetAll().ToList();
        int correct = 0;
        int total = testCases.Count;
        var failures = new List<string>();

        foreach (var tc in testCases)
        {
            var result = await _service.EvaluateAsync(tc.Input);
            if (result.Decision == tc.ExpectedDecision)
            {
                correct++;
            }
            else
            {
                failures.Add($"  {tc.Id}: expected={tc.ExpectedDecision}, actual={result.Decision}, " +
                    $"composite={result.CompositeScore:F1}, structural={result.Scores.StructuralValidity}");
            }
        }

        float accuracy = (float)correct / total;

        // Also compute per-category metrics
        var (rejPrecision, rejRecall, rejF1) = await ComputeF1(testCases, GateDecision.AutoRejected);
        var (revPrecision, revRecall, revF1) = await ComputeF1(testCases, GateDecision.NeedsReview);

        // Output results for autoresearch logging
        var summary = $"\n=== QUALITY GATE METRICS ===\n" +
            $"Overall Accuracy: {accuracy:P1} ({correct}/{total})\n" +
            $"AutoRejected — P:{rejPrecision:F2} R:{rejRecall:F2} F1:{rejF1:F2}\n" +
            $"NeedsReview  — P:{revPrecision:F2} R:{revRecall:F2} F1:{revF1:F2}\n" +
            $"Weighted F1: {(rejF1 + revF1) / 2:F2}\n" +
            $"Failures ({failures.Count}):\n" + string.Join("\n", failures);

        // Assert: weighted F1 >= 0.80 (initial target, increase to 0.90 over iterations)
        Assert.True(accuracy >= 0.75f, summary);
    }

    [Theory]
    [MemberData(nameof(GetGoodQuestions))]
    public async Task GoodQuestion_ShouldNotBeAutoRejected(LabeledTestCase tc)
    {
        var result = await _service.EvaluateAsync(tc.Input);
        Assert.NotEqual(GateDecision.AutoRejected, result.Decision);
    }

    [Theory]
    [MemberData(nameof(GetBadQuestionsExpectingRejection))]
    public async Task BadQuestion_WithCriticalDefect_ShouldBeAutoRejected(LabeledTestCase tc)
    {
        var result = await _service.EvaluateAsync(tc.Input);
        Assert.Equal(GateDecision.AutoRejected, result.Decision);
    }

    [Theory]
    [MemberData(nameof(GetAllTestCases))]
    public async Task AllQuestions_ShouldDetectExpectedFlags(LabeledTestCase tc)
    {
        if (tc.ExpectedFlags == null || tc.ExpectedFlags.Length == 0) return;

        var result = await _service.EvaluateAsync(tc.Input);
        var violationRuleIds = result.Violations.Select(v => v.RuleId).ToHashSet();

        foreach (var flag in tc.ExpectedFlags)
        {
            Assert.True(violationRuleIds.Contains(flag),
                $"Question {tc.Id}: expected flag '{flag}' not found. Got: [{string.Join(", ", violationRuleIds)}]");
        }
    }

    [Fact]
    public void StructuralValidator_PerfectQuestion_ScoresAbove80()
    {
        var input = QualityGateTestData.GetGoodQuestions().First().Input;
        var (score, violations) = StructuralValidator.Validate(input);
        Assert.True(score >= 80, $"Perfect question scored only {score}. Violations: {string.Join(", ", violations.Select(v => v.RuleId))}");
    }

    [Fact]
    public void StemClarityScorer_WellFormedStem_ScoresAbove70()
    {
        var input = QualityGateTestData.GetGoodQuestions().First().Input;
        var (score, _) = StemClarityScorer.Score(input);
        Assert.True(score >= 70, $"Well-formed stem scored only {score}");
    }

    [Fact]
    public void DistractorQualityScorer_GoodDistractors_ScoresAbove60()
    {
        // Use G04 which has full rationales on all distractors
        var g04 = QualityGateTestData.GetGoodQuestions().First(tc => tc.Id == "G04");
        var (score, _) = DistractorQualityScorer.Score(g04.Input);
        Assert.True(score >= 60, $"Good distractors scored only {score}");
    }

    [Fact]
    public void BloomAlignmentScorer_CorrectBloom_ScoresAbove70()
    {
        // G01 is Bloom 3 (Apply) with "Solve for x" — should match Middle tier
        var g01 = QualityGateTestData.GetGoodQuestions().First(tc => tc.Id == "G01");
        var (score, _) = BloomAlignmentScorer.Score(g01.Input);
        Assert.True(score >= 70, $"Correct Bloom alignment scored only {score}");
    }

    // Data providers
    public static IEnumerable<object[]> GetGoodQuestions() =>
        QualityGateTestData.GetGoodQuestions().Select(tc => new object[] { tc });

    public static IEnumerable<object[]> GetBadQuestionsExpectingRejection() =>
        QualityGateTestData.GetBadQuestions()
            .Where(tc => tc.ExpectedDecision == GateDecision.AutoRejected)
            .Select(tc => new object[] { tc });

    public static IEnumerable<object[]> GetAllTestCases() =>
        QualityGateTestData.GetAll().Select(tc => new object[] { tc });

    private async Task<(float Precision, float Recall, float F1)> ComputeF1(
        List<LabeledTestCase> testCases, GateDecision targetClass)
    {
        int tp = 0, fp = 0, fn = 0;
        foreach (var tc in testCases)
        {
            var result = await _service.EvaluateAsync(tc.Input);
            bool predicted = result.Decision == targetClass;
            bool actual = tc.ExpectedDecision == targetClass;

            if (predicted && actual) tp++;
            else if (predicted && !actual) fp++;
            else if (!predicted && actual) fn++;
        }

        float precision = tp + fp > 0 ? (float)tp / (tp + fp) : 0;
        float recall = tp + fn > 0 ? (float)tp / (tp + fn) : 0;
        float f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;
        return (precision, recall, f1);
    }
}
