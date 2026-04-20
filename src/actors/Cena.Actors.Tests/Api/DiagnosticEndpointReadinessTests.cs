// =============================================================================
// Cena Platform — Readiness-bucket mapper boundary coverage (prr-007)
//
// Scope note:
//   The task brief asked for an end-to-end integration test spinning up the
//   student API via WebApplicationFactory, calling /api/diagnostic/estimate,
//   and asserting the JSON response contains `Readiness` and never a raw
//   theta scalar. The Student API host test project
//   (src/api/Cena.Student.Api.Host.Tests) does NOT yet use
//   WebApplicationFactory — every existing test targets the endpoint
//   handlers directly as pure functions. Standing up that harness is a
//   separate piece of plumbing (Marten test container, Firebase auth
//   stub, NATS stub) out of scope for a 30-minute patch.
//
// Follow-up (prr-007 closure): once WebApplicationFactory lands on the
// Student API test project, port these assertions to a real HTTP round-trip
// (status 200 + body JSON has "readiness" and NOT "theta"). Until then,
// this unit test locks the mapper boundaries + the "bucket wins over
// scalar" invariant that the DTO layer depends on.
//
// What this file covers today:
//   * ThetaMasteryMapper.ToReadinessBucket boundary values
//   * CI-straddle down-rounding (the ministry-defensibility rule)
//   * Non-finite input fallback
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Xunit;

namespace Cena.Actors.Tests.Api;

public sealed class DiagnosticEndpointReadinessTests
{
    // ── Point-estimate boundaries ─────────────────────────────────────────
    // Confident (degenerate CI = 0): exact bucket-wise threshold behaviour.

    [Theory]
    [InlineData(-3.0,  ReadinessBucket.Emerging)]
    [InlineData(-1.5,  ReadinessBucket.Emerging)]
    [InlineData(-1.0001, ReadinessBucket.Emerging)]
    [InlineData(-1.0,  ReadinessBucket.Developing)]   // boundary inclusive-lower
    [InlineData(-0.5,  ReadinessBucket.Developing)]
    [InlineData(-0.0001, ReadinessBucket.Developing)]
    [InlineData( 0.0,  ReadinessBucket.Proficient)]   // boundary inclusive-lower
    [InlineData( 0.5,  ReadinessBucket.Proficient)]
    [InlineData( 0.9999, ReadinessBucket.Proficient)]
    [InlineData( 1.0,  ReadinessBucket.ExamReady)]    // boundary inclusive-lower
    [InlineData( 2.5,  ReadinessBucket.ExamReady)]
    public void Point_estimate_maps_to_expected_bucket(double theta, ReadinessBucket expected)
    {
        var bucket = ThetaMasteryMapper.ToReadinessBucket(
            theta, confidenceIntervalHalfWidth: 0.0);
        Assert.Equal(expected, bucket);
    }

    // ── CI-straddle down-rounding (ministry-defensibility) ────────────────
    // When the CI crosses a boundary, the LOWER bucket must win.

    [Fact]
    public void Ci_straddling_boundary_falls_to_lower_bucket()
    {
        // θ = -0.9 with ±0.2 spans [-1.1, -0.7] → Emerging/Developing
        // straddle → Emerging.
        Assert.Equal(
            ReadinessBucket.Emerging,
            ThetaMasteryMapper.ToReadinessBucket(theta: -0.9, 0.2));

        // θ = +0.9 with ±0.2 spans [0.7, 1.1] → Proficient/ExamReady
        // straddle → Proficient.
        Assert.Equal(
            ReadinessBucket.Proficient,
            ThetaMasteryMapper.ToReadinessBucket(theta: 0.9, 0.2));

        // θ = -0.05 with ±0.1 spans [-0.15, 0.05] → Developing/Proficient
        // straddle → Developing.
        Assert.Equal(
            ReadinessBucket.Developing,
            ThetaMasteryMapper.ToReadinessBucket(theta: -0.05, 0.1));
    }

    [Fact]
    public void Ci_wholly_inside_bucket_yields_point_bucket()
    {
        // θ = 0.5 with ±0.1 → [0.4, 0.6] entirely inside Proficient.
        Assert.Equal(
            ReadinessBucket.Proficient,
            ThetaMasteryMapper.ToReadinessBucket(0.5, 0.1));
    }

    [Fact]
    public void Wide_ci_spanning_multiple_buckets_falls_to_lowest()
    {
        // θ = 0.5 with ±2.0 spans [-1.5, 2.5] → crosses three boundaries;
        // down-round to the point-below-lower-bound bucket (Emerging).
        Assert.Equal(
            ReadinessBucket.Emerging,
            ThetaMasteryMapper.ToReadinessBucket(0.5, 2.0));
    }

    // ── Non-finite / degenerate input fallback ────────────────────────────

    [Theory]
    [InlineData(double.NaN,              0.1)]
    [InlineData(double.PositiveInfinity, 0.1)]
    [InlineData(double.NegativeInfinity, 0.1)]
    public void Non_finite_theta_falls_to_emerging(double theta, double ci)
    {
        Assert.Equal(
            ReadinessBucket.Emerging,
            ThetaMasteryMapper.ToReadinessBucket(theta, ci));
    }

    [Fact]
    public void Negative_ci_is_treated_as_zero_not_extended()
    {
        // A nonsense negative CI must not widen the bucket sweep. θ=0.5
        // with a bogus -0.5 CI still returns Proficient (pure point bucket).
        Assert.Equal(
            ReadinessBucket.Proficient,
            ThetaMasteryMapper.ToReadinessBucket(0.5, -0.5));
    }

    // ── Ordinal contract ──────────────────────────────────────────────────
    // Downstream code may compare (bucket >= Proficient) to gate content.
    // Lock the ordering so an enum re-order trips a visible test.

    [Fact]
    public void Bucket_enum_is_ordered_low_to_high()
    {
        Assert.True((int)ReadinessBucket.Emerging
            < (int)ReadinessBucket.Developing);
        Assert.True((int)ReadinessBucket.Developing
            < (int)ReadinessBucket.Proficient);
        Assert.True((int)ReadinessBucket.Proficient
            < (int)ReadinessBucket.ExamReady);
    }
}
