// =============================================================================
// RDY-080: ConcordanceMapping scaffolding tests
//
// Proves the guardrails that keep the F8 point-estimate UI honest:
//   * An unapproved mapping refuses to Predict.
//   * A mapping with inadequate diagnostics refuses to Predict even
//     after approval.
//   * F8PointEstimateEnabled is false in every permutation except
//     approved + clears-bar + not-superseded.
// =============================================================================

using Cena.Actors.Services.Calibration;
using Xunit;

namespace Cena.Actors.Tests.Services.Calibration;

public class ConcordanceMappingTests
{
    private static CalibrationAdequacy AdequateResult() => new()
    {
        HeldOutMae = 4.5,
        HeldOutRmse = 5.0,
        HeldOutCoverage68 = 0.66,
        HeldOutCoverage95 = 0.94,
        MappingStandardError = 4.8,
        ResidualsShapiroWilkP = 0.3,
        ResidualsBreuschPaganP = 0.25,
        CrossValidationRmse = 4.9
    };

    private static CalibrationAdequacy InadequateResult() => AdequateResult() with
    {
        MappingStandardError = 6.2 // > 5.0, bar missed
    };

    private static ConcordanceMapping NewMapping(
        CalibrationAdequacy adequacy,
        string? approvedBy = null,
        DateTimeOffset? supersededAtUtc = null)
        => new()
        {
            Version = 1,
            ModelKind = ConcordanceModelKind.LinearV1,
            PreRegistrationId = "prereg-rdy080-v1",
            CoefficientsJson = """{"beta0":50.0,"beta1":10.0,"sigma":8.0}""",
            TrainingCohortSize = 300,
            ValidationCohortSize = 100,
            TrainingCohortHash = new string('a', 64),
            Adequacy = adequacy,
            ApprovedBy = approvedBy,
            ApprovedAtUtc = approvedBy is null ? null : DateTimeOffset.UtcNow,
            SupersededAtUtc = supersededAtUtc
        };

    [Fact]
    public void Unapproved_mapping_refuses_to_predict()
    {
        var m = NewMapping(AdequateResult(), approvedBy: null);
        Assert.False(m.F8PointEstimateEnabled);
        var ex = Assert.Throws<InvalidOperationException>(() => m.Predict(0.5));
        Assert.Contains("not approved", ex.Message);
    }

    [Fact]
    public void Approved_but_inadequate_mapping_refuses_to_predict()
    {
        var m = NewMapping(InadequateResult(), approvedBy: "dr.yael");
        // Dr. Yael approved, but adequacy failed the SE bar.
        Assert.False(m.F8PointEstimateEnabled);
        Assert.Throws<InvalidOperationException>(() => m.Predict(0.5));
    }

    [Fact]
    public void Superseded_mapping_refuses_to_predict()
    {
        var m = NewMapping(
            AdequateResult(),
            approvedBy: "dr.yael",
            supersededAtUtc: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.False(m.F8PointEstimateEnabled);
    }

    [Fact]
    public void Approved_and_adequate_mapping_enables_prediction_flag()
    {
        var m = NewMapping(AdequateResult(), approvedBy: "dr.yael");
        Assert.True(m.F8PointEstimateEnabled);
        // Predict() throws NotImplementedException — that's the scaffold
        // state, we're checking the scaffold wiring, not the math.
        Assert.Throws<NotImplementedException>(() => m.Predict(0.5));
    }

    [Theory]
    [InlineData(5.1, false)] // SE just over the 5-point bar
    [InlineData(6.1, false)] // MAE over bar
    public void Adequacy_rejects_when_primary_SE_misses(double seValue, bool expected)
    {
        var adequacy = AdequateResult() with { MappingStandardError = seValue };
        Assert.Equal(expected, adequacy.ClearsBar);
    }

    [Fact]
    public void Adequacy_rejects_when_normality_test_fails()
    {
        var adequacy = AdequateResult() with { ResidualsShapiroWilkP = 0.01 };
        Assert.False(adequacy.ClearsBar);
    }

    [Fact]
    public void Adequacy_rejects_when_coverage_below_target()
    {
        var adequacy = AdequateResult() with { HeldOutCoverage68 = 0.55 };
        Assert.False(adequacy.ClearsBar);
    }
}

public class CalibrationEvidenceTests
{
    [Fact]
    public void Required_roles_for_production_lists_three_roles()
    {
        Assert.Equal(3, ReviewSignoff.RequiredRolesForProduction.Count);
        Assert.Contains("psychometrics-lead", ReviewSignoff.RequiredRolesForProduction);
        Assert.Contains("pedagogy-lead", ReviewSignoff.RequiredRolesForProduction);
        Assert.Contains("legal-dpo", ReviewSignoff.RequiredRolesForProduction);
    }
}
