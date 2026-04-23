// =============================================================================
// Cena Platform — DisputeMetricsEndpoints tests (EPIC-PRR-J PRR-393)
//
// Locks the helper surfaces that drive the /api/admin/dispute-metrics
// endpoint — window parse, DTO projection, slice-dimensions honesty.
//
// Pure unit tests only. We deliberately do NOT spin up a WebApplicationFactory
// here: the Admin.Api.Host auth graph depends on Firebase JWTs and the
// integration fixtures would triple the test-suite runtime for very little
// additional coverage above the aggregator + service tests. The route
// wiring itself is exercised at the swagger-gen gate.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Admin.Api.Host.Endpoints;
using Xunit;

namespace Cena.Admin.Api.Tests.Observability;

public class DisputeMetricsEndpointsTests
{
    [Theory]
    [InlineData(null, AggregationWindow.SevenDay)]      // default
    [InlineData("", AggregationWindow.SevenDay)]        // empty → default
    [InlineData("   ", AggregationWindow.SevenDay)]     // whitespace → default
    [InlineData("7d", AggregationWindow.SevenDay)]
    [InlineData("7D", AggregationWindow.SevenDay)]
    [InlineData("7days", AggregationWindow.SevenDay)]
    [InlineData("30d", AggregationWindow.ThirtyDay)]
    [InlineData("30D", AggregationWindow.ThirtyDay)]
    [InlineData("30days", AggregationWindow.ThirtyDay)]
    public void ParseWindow_accepts_documented_aliases(string? raw, AggregationWindow expected)
    {
        Assert.Equal(expected, DisputeMetricsEndpoints.ParseWindow(raw));
    }

    [Theory]
    [InlineData("14d")]
    [InlineData("month")]
    [InlineData("junk")]
    [InlineData("-1d")]
    public void ParseWindow_rejects_undocumented_aliases(string raw)
    {
        Assert.Null(DisputeMetricsEndpoints.ParseWindow(raw));
    }

    [Fact]
    public void ToDto_projects_snapshot_fields_one_for_one()
    {
        var perReason = new Dictionary<DisputeReason, int>
        {
            [DisputeReason.WrongNarration] = 3,
            [DisputeReason.WrongStepIdentified] = 1,
            [DisputeReason.OcrMisread] = 0,
            [DisputeReason.Other] = 0,
        };
        var perReasonRates = new Dictionary<DisputeReason, double>
        {
            [DisputeReason.WrongNarration] = 1.0,
            [DisputeReason.WrongStepIdentified] = 0.0,
            [DisputeReason.OcrMisread] = 0.0,
            [DisputeReason.Other] = 0.0,
        };
        var snap = new DisputeMetricsSnapshot(
            WindowDays: 7,
            TotalDisputes: 4,
            UpheldCount: 3,
            RejectedCount: 1,
            InReviewCount: 0,
            NewCount: 0,
            WithdrawnCount: 0,
            UpheldRate: 0.75,
            PerReasonCounts: perReason,
            PerReasonUpheldRate: perReasonRates,
            AlertThreshold: 0.05,
            IsAboveAlertThreshold: true);

        var dto = DisputeMetricsEndpoints.ToDto(snap);

        Assert.Equal(7, dto.WindowDays);
        Assert.Equal(4, dto.TotalDisputes);
        Assert.Equal(3, dto.UpheldCount);
        Assert.Equal(0.75, dto.UpheldRate, 9);
        Assert.True(dto.IsAboveAlertThreshold);
        Assert.Equal(3, dto.PerReasonCounts["WrongNarration"]);
        Assert.Equal(1, dto.PerReasonCounts["WrongStepIdentified"]);
        Assert.Equal(1.0, dto.PerReasonUpheldRate["WrongNarration"], 9);

        // Honest scope marker — template/item/locale NOT promised in v1.
        Assert.Contains("reason", dto.SliceDimensionsAvailable);
        Assert.Contains("status", dto.SliceDimensionsAvailable);
        Assert.DoesNotContain("template", dto.SliceDimensionsAvailable);
        Assert.DoesNotContain("item", dto.SliceDimensionsAvailable);
        Assert.DoesNotContain("locale", dto.SliceDimensionsAvailable);
    }
}
