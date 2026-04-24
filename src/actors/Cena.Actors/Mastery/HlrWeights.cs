// =============================================================================
// Cena Platform -- HLR Weights
// MST-003: Half-Life Regression weight vector (loaded from config)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// HLR weight vector: 6 feature weights + bias.
/// At launch these are hand-tuned; at scale, trained offline by MST-016.
/// </summary>
public readonly record struct HlrWeights
{
    private const int WeightCount = 6;

    public readonly float[] Weights;
    public readonly float Bias;

    public HlrWeights(float[] weights, float Bias)
    {
        if (weights.Length != WeightCount)
            throw new ArgumentException($"Weights must have exactly {WeightCount} elements");
        this.Weights = weights;
        this.Bias = Bias;
    }

    /// <summary>
    /// Default hand-tuned weights. h approx 8 days for a fresh concept.
    /// Positive weights: more attempts/correct/bloom -> longer half-life.
    /// Negative weights: harder concept/deeper prereqs -> shorter half-life.
    /// </summary>
    public static readonly HlrWeights Default = new(
        new float[] { 0.3f, 0.5f, -0.2f, -0.1f, 0.1f, 0.05f },
        Bias: 3.0f);

    /// <summary>
    /// Dot product of weights and feature vector. Zero allocation with stack span.
    /// </summary>
    public float DotProduct(HlrFeatures features)
    {
        Span<float> featureVector = stackalloc float[WeightCount];
        features.FillVector(featureVector);

        float sum = Bias;
        for (int i = 0; i < WeightCount; i++)
            sum += Weights[i] * featureVector[i];

        return sum;
    }
}
