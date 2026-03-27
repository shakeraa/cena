// =============================================================================
// Cena Platform -- Quality Gate Service
// Orchestrates Stage 1 (structural) scoring across all dimensions.
// Stage 2-3 (LLM-based) to be added in later iterations.
// =============================================================================

namespace Cena.Admin.Api.QualityGate;

public interface IQualityGateService
{
    /// <summary>
    /// Evaluate a question through the quality gate pipeline.
    /// Currently implements Stage 1 (structural validation) only.
    /// </summary>
    QualityGateResult Evaluate(QualityGateInput input);
}

public sealed class QualityGateService : IQualityGateService
{
    private readonly QualityGateThresholds _thresholds;

    public QualityGateService(QualityGateThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? QualityGateThresholds.Default;
    }

    public QualityGateResult Evaluate(QualityGateInput input)
    {
        var allViolations = new List<QualityViolation>();

        // Stage 1: Structural Validation
        var (structuralScore, structuralViolations) = StructuralValidator.Validate(input);
        allViolations.AddRange(structuralViolations);

        // Stage 1: Stem Clarity
        var (stemScore, stemViolations) = StemClarityScorer.Score(input);
        allViolations.AddRange(stemViolations);

        // Stage 1: Distractor Quality
        var (distractorScore, distractorViolations) = DistractorQualityScorer.Score(input);
        allViolations.AddRange(distractorViolations);

        // Stage 1: Bloom's Alignment
        var (bloomScore, bloomViolations) = BloomAlignmentScorer.Score(input);
        allViolations.AddRange(bloomViolations);

        // Stage 2-3 stubs (LLM-based — future iterations)
        int factualAccuracy = 80;       // Default to "needs review" range until LLM stage
        int languageQuality = 80;       // Default until LLM stage
        int pedagogicalQuality = 75;    // Default until LLM stage
        int culturalSensitivity = 80;   // Default until LLM stage

        var scores = new DimensionScores(
            FactualAccuracy: factualAccuracy,
            LanguageQuality: languageQuality,
            PedagogicalQuality: pedagogicalQuality,
            DistractorQuality: distractorScore,
            StemClarity: stemScore,
            BloomAlignment: bloomScore,
            StructuralValidity: structuralScore,
            CulturalSensitivity: culturalSensitivity);

        float composite = ComputeComposite(scores);
        var decision = DetermineDecision(scores, composite);

        return new QualityGateResult(
            QuestionId: input.QuestionId,
            Scores: scores,
            CompositeScore: composite,
            Decision: decision,
            Violations: allViolations,
            EvaluatedAt: DateTimeOffset.UtcNow);
    }

    private float ComputeComposite(DimensionScores s)
    {
        // Weighted composite from research report
        return 0.15f * s.FactualAccuracy
             + 0.10f * s.LanguageQuality
             + 0.10f * s.PedagogicalQuality
             + 0.15f * s.DistractorQuality
             + 0.15f * s.StemClarity
             + 0.10f * s.BloomAlignment
             + 0.20f * s.StructuralValidity
             + 0.05f * s.CulturalSensitivity;
    }

    private GateDecision DetermineDecision(DimensionScores s, float composite)
    {
        // Hard gates: any critical dimension below reject threshold → auto-reject
        if (s.StructuralValidity < _thresholds.StructuralValidityReject) return GateDecision.AutoRejected;
        if (s.FactualAccuracy < _thresholds.FactualAccuracyReject) return GateDecision.AutoRejected;
        if (s.CulturalSensitivity < _thresholds.CulturalSensitivityHardGate) return GateDecision.AutoRejected;
        if (s.DistractorQuality < _thresholds.DistractorQualityReject) return GateDecision.AutoRejected;
        if (s.StemClarity < _thresholds.StemClarityReject) return GateDecision.AutoRejected;
        if (s.BloomAlignment < _thresholds.BloomAlignmentReject) return GateDecision.AutoRejected;

        // Composite reject
        if (composite < _thresholds.CompositeReject) return GateDecision.AutoRejected;

        // All dimensions above approve thresholds + composite above approve → auto-approve
        if (s.StructuralValidity >= _thresholds.StructuralValidityApprove
            && s.FactualAccuracy >= _thresholds.FactualAccuracyApprove
            && s.LanguageQuality >= _thresholds.LanguageQualityApprove
            && s.PedagogicalQuality >= _thresholds.PedagogicalQualityApprove
            && s.DistractorQuality >= _thresholds.DistractorQualityApprove
            && s.StemClarity >= _thresholds.StemClarityApprove
            && s.BloomAlignment >= _thresholds.BloomAlignmentApprove
            && composite >= _thresholds.CompositeApprove)
        {
            return GateDecision.AutoApproved;
        }

        // Everything else → human review
        return GateDecision.NeedsReview;
    }
}
