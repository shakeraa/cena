// =============================================================================
// Cena Platform — HybridStuckClassifier composition tests (RDY-063)
//
// Exercises HybridStuckClassifier.Compose against the full matrix of
// (heuristic, LLM) outcomes. The composer is a pure function — no DI
// needed beyond an Options instance.
// =============================================================================

using Cena.Actors.Diagnosis;

namespace Cena.Actors.Tests.Diagnosis;

public class HybridStuckClassifierComposeTests
{
    private static readonly StuckClassifierOptions Opts = new()
    {
        Enabled = true,
        ClassifierVersion = "v1.0.0-test",
        HeuristicSkipLlmThreshold = 0.7f,
        MinActionableConfidence = 0.6f,
        DisagreementDampening = 0.5f,
    };

    private static StuckDiagnosis MakeDiag(
        StuckType primary, float conf,
        StuckDiagnosisSource source = StuckDiagnosisSource.Heuristic) =>
        new(
            Primary: primary,
            PrimaryConfidence: conf,
            Secondary: StuckType.Unknown,
            SecondaryConfidence: 0f,
            SuggestedStrategy: StuckScaffoldStrategy.Unspecified,
            FocusChapterId: "ch-1",
            ShouldInvolveTeacher: false,
            Source: source,
            ClassifierVersion: Opts.ClassifierVersion,
            DiagnosedAt: DateTimeOffset.UtcNow,
            LatencyMs: 10,
            SourceReasonCode: "test");

    [Fact]
    public void BothUnknown_ReturnsNoneSource()
    {
        var h = StuckDiagnosis.Unknown(Opts.ClassifierVersion, StuckDiagnosisSource.Heuristic, 5);
        var l = StuckDiagnosis.Unknown(Opts.ClassifierVersion, StuckDiagnosisSource.Llm, 200);

        var result = HybridStuckClassifier.Compose(h, l, Opts);

        Assert.Equal(StuckType.Unknown, result.Primary);
        Assert.Equal(StuckDiagnosisSource.None, result.Source);
    }

    [Fact]
    public void LlmUnknown_HeuristicFired_PrefersHeuristic()
    {
        var h = MakeDiag(StuckType.Procedural, 0.5f);
        var l = StuckDiagnosis.Unknown(Opts.ClassifierVersion, StuckDiagnosisSource.LlmError, 100);

        var result = HybridStuckClassifier.Compose(h, l, Opts);

        Assert.Equal(StuckType.Procedural, result.Primary);
        Assert.Equal(StuckDiagnosisSource.Heuristic, result.Source);
    }

    [Fact]
    public void HeuristicUnknown_LlmDecided_PrefersLlm()
    {
        var h = StuckDiagnosis.Unknown(Opts.ClassifierVersion, StuckDiagnosisSource.Heuristic, 5);
        var l = MakeDiag(StuckType.Strategic, 0.8f, StuckDiagnosisSource.Llm);

        var result = HybridStuckClassifier.Compose(h, l, Opts);

        Assert.Equal(StuckType.Strategic, result.Primary);
        Assert.Equal(StuckDiagnosisSource.Llm, result.Source);
    }

    [Fact]
    public void Agreement_EscalatesConfidence_WithCap()
    {
        var h = MakeDiag(StuckType.Misconception, 0.8f);
        var l = MakeDiag(StuckType.Misconception, 0.7f, StuckDiagnosisSource.Llm);

        var result = HybridStuckClassifier.Compose(h, l, Opts);

        Assert.Equal(StuckType.Misconception, result.Primary);
        Assert.Equal(StuckDiagnosisSource.HybridAgreement, result.Source);
        Assert.True(result.PrimaryConfidence >= 0.8f);
        Assert.True(result.PrimaryConfidence <= 0.95f);  // cap
    }

    [Fact]
    public void Disagreement_DampensConfidence_SwapsSecondary()
    {
        var h = MakeDiag(StuckType.Misconception, 0.65f);
        var l = MakeDiag(StuckType.Strategic, 0.8f, StuckDiagnosisSource.Llm);

        var result = HybridStuckClassifier.Compose(h, l, Opts);

        Assert.Equal(StuckType.Strategic, result.Primary);
        Assert.Equal(StuckType.Misconception, result.Secondary);
        Assert.Equal(StuckDiagnosisSource.HybridDisagreement, result.Source);
        // Dampened: 0.8 * 0.5 = 0.4
        Assert.Equal(0.4f, result.PrimaryConfidence, precision: 3);
    }

    [Fact]
    public void Disagreement_BelowActionable_FlagsNonActionable()
    {
        var h = MakeDiag(StuckType.Recall, 0.6f);
        var l = MakeDiag(StuckType.Encoding, 0.7f, StuckDiagnosisSource.Llm);

        var result = HybridStuckClassifier.Compose(h, l, Opts);

        Assert.False(result.IsActionable(Opts.MinActionableConfidence));
    }

    [Fact]
    public void Agreement_AboveActionable_IsActionable()
    {
        var h = MakeDiag(StuckType.Procedural, 0.78f);
        var l = MakeDiag(StuckType.Procedural, 0.75f, StuckDiagnosisSource.Llm);

        var result = HybridStuckClassifier.Compose(h, l, Opts);

        Assert.True(result.IsActionable(Opts.MinActionableConfidence));
    }
}
