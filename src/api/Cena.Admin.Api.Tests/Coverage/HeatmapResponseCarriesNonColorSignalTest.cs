// =============================================================================
// Cena Platform — Architecture ratchet: heatmap DTOs carry a non-color signal
// (prr-209 DoD, prr-052 color-alone ban, WCAG 1.4.1).
//
// Every cell-ish heatmap DTO MUST carry:
//   - a "Status"      property (green|amber|red semantic state), AND
//   - a "PatternKey"  property (solid|diagonal|dots colourless signal)
//
// Removing either property — or renaming it away from the contract name —
// fails this test. The front-end renders both so accessibility tooling can
// distinguish the three states without relying on hue alone.
// =============================================================================

using System.Reflection;
using Cena.Admin.Api.Coverage;

namespace Cena.Admin.Api.Tests.Coverage;

public sealed class HeatmapResponseCarriesNonColorSignalTest
{
    /// <summary>
    /// Every record in Cena.Admin.Api.Coverage that *looks* like a cell
    /// carrier (has a Status or PatternKey property, or is the heatmap
    /// response root) must ship BOTH properties — no one-sided exposure of
    /// color without the colourless pattern twin.
    /// </summary>
    [Fact]
    public void EveryCellCarrier_ShipsStatusAndPatternKey()
    {
        // Seed the expectation set with the two public DTOs defined by
        // prr-209. If a new heatmap DTO ships a Status OR PatternKey, the
        // discovery sweep below picks it up and enforces the twin.
        var heatmapTypes = typeof(CoverageHeatmapCellDto).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Cena.Admin.Api.Coverage")
            .Where(t => t.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance) is not null
                     || t.GetProperty("PatternKey", BindingFlags.Public | BindingFlags.Instance) is not null)
            .ToList();

        // Sanity floor: we know at least one DTO carries both.
        Assert.Contains(typeof(CoverageHeatmapCellDto), heatmapTypes);

        foreach (var t in heatmapTypes)
        {
            var status = t.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance);
            var pattern = t.GetProperty("PatternKey", BindingFlags.Public | BindingFlags.Instance);

            Assert.True(
                status is not null,
                $"{t.FullName} declares PatternKey but not Status — color-alone ban violation.");
            Assert.True(
                pattern is not null,
                $"{t.FullName} declares Status but not PatternKey — color-alone ban violation.");

            Assert.Equal(typeof(string), status!.PropertyType);
            Assert.Equal(typeof(string), pattern!.PropertyType);
        }
    }

    /// <summary>
    /// Also lock the rung drilldown response: its inner Cell must ship both
    /// properties so curators drilling into a rung still see the colourless
    /// signal. A contract ratchet on the CoverageHeatmapCellDto embedded in the
    /// CoverageRungDrilldownResponse.
    /// </summary>
    [Fact]
    public void CoverageRungDrilldownResponse_Cell_IsCoverageHeatmapCellDto()
    {
        var cellProp = typeof(CoverageRungDrilldownResponse)
            .GetProperty(nameof(CoverageRungDrilldownResponse.Cell));
        Assert.NotNull(cellProp);
        Assert.Equal(typeof(CoverageHeatmapCellDto), cellProp!.PropertyType);
    }

    /// <summary>
    /// Pattern-key vocabulary lock: the set of accepted pattern keys is
    /// exactly {solid, diagonal, dots}. Widening this vocabulary requires a
    /// deliberate contract change (and a frontend render-layer update).
    /// </summary>
    [Fact]
    public void PatternKeyVocabulary_IsLocked()
    {
        Assert.Equal("solid", CoverageHeatmapPatternKey.Solid);
        Assert.Equal("diagonal", CoverageHeatmapPatternKey.Diagonal);
        Assert.Equal("dots", CoverageHeatmapPatternKey.Dots);
    }

    /// <summary>
    /// Status vocabulary lock: the set of accepted status values is
    /// exactly {green, amber, red}. Matches the task body.
    /// </summary>
    [Fact]
    public void StatusVocabulary_IsLocked()
    {
        Assert.Equal("green", CoverageHeatmapStatus.Green);
        Assert.Equal("amber", CoverageHeatmapStatus.Amber);
        Assert.Equal("red", CoverageHeatmapStatus.Red);
    }
}
