// =============================================================================
// Cena Platform — ConfidenceGate (EPIC-PRR-J PRR-351)
//
// Intake-side confidence gate for the photo-diagnostic pipeline. Routes
// low-confidence extracted step sequences to the editable preview UX
// (PRR-352) instead of straight to CAS verification — "never commit to
// a silent mis-OCR", memory "Labels match data". Complements the
// StepChainVerifier's own LowConfidence short-circuit (which only fires
// at chain-walk time for defensive reasons); this gate fires BEFORE the
// chain walker, at the moment of step extraction, so the UI can show
// the preview UX instead of the chain-result UX.
//
// Two signals:
//   1. Per-step minimum: any single step with OCR confidence below
//      <see cref="ConfidenceGateOptions.PerStepThreshold"/> (default
//      0.80) routes the whole sequence to preview.
//   2. Aggregate: the geometric mean across all steps in the sequence.
//      Geometric (not arithmetic) because a single very-low-confidence
//      step should drag the aggregate down more than arithmetic would —
//      a sequence with 0.99, 0.99, 0.40 is NOT effectively 0.79; it's
//      dragged down by the 0.40 step.
//
// Either signal alone triggers RouteToPreview. The default thresholds
// (per-step=0.80, aggregate=0.85) come from the persona review — above
// these the OCR is routinely trustworthy; below, the student either
// wrote in a hand the model can't decode or the photo has a lighting
// problem.
//
// Thresholds are configurable at runtime via the options record so ops
// can retune without a deploy. The options are registered as a
// singleton; hosts can bind them to appsettings.json (ADR-0059 pattern).
// The gate is a pure function; no I/O.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Knobs for <see cref="ConfidenceGate"/>. Runtime-tunable.</summary>
public sealed record ConfidenceGateOptions(
    double PerStepThreshold = 0.80,
    double AggregateThreshold = 0.85)
{
    /// <summary>Default thresholds from the PRR-351 persona review.</summary>
    public static readonly ConfidenceGateOptions Default = new();
}

/// <summary>What the photo-diagnostic pipeline should do with the extracted steps.</summary>
public enum ConfidenceRouting
{
    /// <summary>Confidence is high enough; chain-walk straight through.</summary>
    PassToChain = 0,

    /// <summary>At least one signal fell below threshold; route to preview UX.</summary>
    RouteToPreview = 1,
}

/// <summary>Full decision shape — the reason is what the UI surfaces.</summary>
public sealed record ConfidenceGateDecision(
    ConfidenceRouting Routing,
    string? ReasonCode,              // "per_step_low" | "aggregate_low" | null
    double MinPerStepConfidence,
    double AggregateConfidence);

/// <summary>
/// Pure gate. Takes a step sequence + thresholds, returns a routing
/// decision. Callers (the extraction endpoint) branch on
/// <see cref="ConfidenceRouting"/> before invoking
/// <see cref="IStepChainVerifier"/>.
/// </summary>
public static class ConfidenceGate
{
    /// <summary>
    /// Evaluate the gate. Empty sequence routes to preview (we can't
    /// chain-verify nothing, and the honest UI state is "I didn't read
    /// anything — can you re-upload?").
    /// </summary>
    public static ConfidenceGateDecision Evaluate(
        IReadOnlyList<ExtractedStep> steps,
        ConfidenceGateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        options ??= ConfidenceGateOptions.Default;

        if (steps.Count == 0)
        {
            // Empty = nothing extracted; the "aggregate" is 0.
            return new ConfidenceGateDecision(
                ConfidenceRouting.RouteToPreview,
                ReasonCode: "per_step_low",
                MinPerStepConfidence: 0.0,
                AggregateConfidence: 0.0);
        }

        var minPerStep = steps.Min(s => s.Confidence);
        var aggregate = ComputeGeometricMean(steps.Select(s => s.Confidence));

        if (minPerStep < options.PerStepThreshold)
        {
            return new ConfidenceGateDecision(
                ConfidenceRouting.RouteToPreview,
                ReasonCode: "per_step_low",
                MinPerStepConfidence: minPerStep,
                AggregateConfidence: aggregate);
        }
        if (aggregate < options.AggregateThreshold)
        {
            return new ConfidenceGateDecision(
                ConfidenceRouting.RouteToPreview,
                ReasonCode: "aggregate_low",
                MinPerStepConfidence: minPerStep,
                AggregateConfidence: aggregate);
        }
        return new ConfidenceGateDecision(
            ConfidenceRouting.PassToChain,
            ReasonCode: null,
            MinPerStepConfidence: minPerStep,
            AggregateConfidence: aggregate);
    }

    /// <summary>
    /// Geometric mean of a confidence set. Any zero confidence produces
    /// a zero aggregate (a single uncertain step poisons the set).
    /// </summary>
    public static double ComputeGeometricMean(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var list = values.ToList();
        if (list.Count == 0) return 0.0;
        // Any zero → geometric mean = 0 (short-circuit for clarity).
        if (list.Any(v => v <= 0.0)) return 0.0;

        // Sum of logs / N is numerically stable for small products.
        var logSum = list.Sum(Math.Log);
        return Math.Exp(logSum / list.Count);
    }
}
