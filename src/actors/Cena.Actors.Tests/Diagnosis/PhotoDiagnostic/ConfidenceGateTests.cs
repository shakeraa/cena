// =============================================================================
// Cena Platform — ConfidenceGate tests (EPIC-PRR-J PRR-351)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class ConfidenceGateTests
{
    [Fact]
    public void High_confidence_sequence_passes_to_chain()
    {
        var steps = new[]
        {
            Step(0, 0.98),
            Step(1, 0.95),
            Step(2, 0.92),
        };

        var decision = ConfidenceGate.Evaluate(steps);

        Assert.Equal(ConfidenceRouting.PassToChain, decision.Routing);
        Assert.Null(decision.ReasonCode);
        Assert.True(decision.MinPerStepConfidence >= 0.80);
        Assert.True(decision.AggregateConfidence >= 0.85);
    }

    [Fact]
    public void Any_step_below_per_step_threshold_routes_to_preview()
    {
        // 2 high + 1 low step below 0.80 default — per-step signal fires.
        var steps = new[]
        {
            Step(0, 0.99),
            Step(1, 0.70),   // below 0.80
            Step(2, 0.99),
        };

        var decision = ConfidenceGate.Evaluate(steps);

        Assert.Equal(ConfidenceRouting.RouteToPreview, decision.Routing);
        Assert.Equal("per_step_low", decision.ReasonCode);
        Assert.Equal(0.70, decision.MinPerStepConfidence);
    }

    [Fact]
    public void All_steps_above_per_step_but_geometric_below_aggregate_routes_to_preview()
    {
        // Every step passes the per-step 0.80 bar, but geometric mean
        // falls under the 0.85 aggregate bar. Aggregate signal fires.
        var steps = new[]
        {
            Step(0, 0.82),
            Step(1, 0.82),
            Step(2, 0.82),
        };

        var decision = ConfidenceGate.Evaluate(steps);

        // Geometric mean of (0.82, 0.82, 0.82) = 0.82 which is < 0.85.
        Assert.Equal(ConfidenceRouting.RouteToPreview, decision.Routing);
        Assert.Equal("aggregate_low", decision.ReasonCode);
        Assert.Equal(0.82, decision.MinPerStepConfidence);
        Assert.InRange(decision.AggregateConfidence, 0.81, 0.83);
    }

    [Fact]
    public void Empty_sequence_routes_to_preview()
    {
        var decision = ConfidenceGate.Evaluate(Array.Empty<ExtractedStep>());

        Assert.Equal(ConfidenceRouting.RouteToPreview, decision.Routing);
        Assert.Equal("per_step_low", decision.ReasonCode);
        Assert.Equal(0.0, decision.AggregateConfidence);
    }

    [Fact]
    public void Thresholds_are_configurable()
    {
        // Set per-step threshold to 0.95 — the previously-passing
        // high-confidence sequence now trips the gate.
        var strict = new ConfidenceGateOptions(
            PerStepThreshold: 0.95,
            AggregateThreshold: 0.85);

        var steps = new[]
        {
            Step(0, 0.94),   // previously passing, now below
            Step(1, 0.99),
        };
        var decision = ConfidenceGate.Evaluate(steps, strict);

        Assert.Equal(ConfidenceRouting.RouteToPreview, decision.Routing);
        Assert.Equal("per_step_low", decision.ReasonCode);
    }

    [Fact]
    public void Null_steps_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ConfidenceGate.Evaluate(null!));
    }

    // ── ComputeGeometricMean ────────────────────────────────────────────────

    [Fact]
    public void Geometric_mean_matches_expected()
    {
        // GM(0.5, 0.5) = 0.5; GM(1.0, 0.25) = 0.5.
        Assert.InRange(
            ConfidenceGate.ComputeGeometricMean(new[] { 0.5, 0.5 }),
            0.499, 0.501);
        Assert.InRange(
            ConfidenceGate.ComputeGeometricMean(new[] { 1.0, 0.25 }),
            0.499, 0.501);
    }

    [Fact]
    public void Geometric_mean_with_zero_is_zero()
    {
        // Zero poisons the set — an UTTERLY uncertain step pulls the
        // aggregate to 0 regardless of the other steps' confidence.
        Assert.Equal(0.0,
            ConfidenceGate.ComputeGeometricMean(new[] { 0.99, 0.0, 0.99 }));
    }

    [Fact]
    public void Geometric_mean_of_empty_is_zero()
    {
        Assert.Equal(0.0,
            ConfidenceGate.ComputeGeometricMean(Array.Empty<double>()));
    }

    [Fact]
    public void Geometric_mean_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ConfidenceGate.ComputeGeometricMean(null!));
    }

    [Fact]
    public void Geometric_mean_sensitive_to_single_low_step()
    {
        // Demonstrates WHY we chose geometric over arithmetic.
        // Arithmetic mean(0.99, 0.99, 0.40) = 0.793 (would pass 0.80)
        // Geometric mean(0.99, 0.99, 0.40) ≈ 0.735 (would fail 0.80)
        var gm = ConfidenceGate.ComputeGeometricMean(new[] { 0.99, 0.99, 0.40 });
        Assert.InRange(gm, 0.72, 0.74);
    }

    private static ExtractedStep Step(int index, double confidence) =>
        new(Index: index, Latex: "x", Canonical: "x", Confidence: confidence);
}
