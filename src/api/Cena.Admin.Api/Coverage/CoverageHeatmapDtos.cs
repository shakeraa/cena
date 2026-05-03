// =============================================================================
// Cena Platform — Admin Coverage Heatmap DTOs (prr-209)
//
// Response shape for the /api/admin/coverage/heatmap endpoint family. The
// DTOs are intentionally flat: the admin front-end renders them into a grid
// per prr-209 DoD (rows = topic × difficulty, columns = methodology × track).
//
// Non-color-alone invariant (prr-052 / WCAG 1.4.1):
//   every heatmap cell ships both:
//     - `Status`      ∈ { green, amber, red }
//     - `PatternKey`  ∈ { solid, diagonal, dots }
// so a front-end that cannot render distinct colors (accessibility, print,
// monochrome) still distinguishes the three states. An architecture ratchet
// test (HeatmapResponseCarriesNonColorSignalTest) asserts both properties
// exist on every heatmap DTO — removing PatternKey by mistake fails CI.
//
// Shape contract:
//   GET /api/admin/coverage/heatmap?track=FourUnit&institute=<id>
//     → HeatmapResponse
//   GET /api/admin/coverage/heatmap/rung?topic=X&difficulty=N&methodology=M
//                                       &track=T&questionType=Q&institute=<id>
//     → RungDrilldownResponse
// =============================================================================

namespace Cena.Admin.Api.Coverage;

/// <summary>
/// Response body for <c>GET /api/admin/coverage/heatmap</c>.
/// </summary>
public sealed record CoverageHeatmapResponse(
    string Track,
    string InstituteId,
    DateTimeOffset GeneratedAt,
    CoverageHeatmapSummary Summary,
    IReadOnlyList<CoverageHeatmapCellDto> Cells);

/// <summary>
/// Roll-up counters so the admin can eyeball "where are we?" at a glance
/// without re-aggregating the cell list client-side.
/// </summary>
public sealed record CoverageHeatmapSummary(
    int TotalCells,
    int GreenCount,
    int AmberCount,
    int RedCount,
    int DeficitTotal,
    int SurplusTotal);

/// <summary>
/// One cell in the heatmap response. Carries both the semantic status and
/// a colourless pattern key so front-ends render accessibly.
/// </summary>
public sealed record CoverageHeatmapCellDto(
    string Topic,
    string Difficulty,
    string Methodology,
    string Track,
    string QuestionType,
    string Language,
    int VariantCount,
    int RequiredCount,
    int Deficit,
    int Surplus,
    bool BelowSlo,
    bool Active,
    bool Declared,
    DateTimeOffset? UpdatedAt,
    string Status,
    string PatternKey,
    string MatchKey,
    string? Notes);

/// <summary>
/// Response body for <c>GET /api/admin/coverage/heatmap/rung</c>.
/// </summary>
public sealed record CoverageRungDrilldownResponse(
    string InstituteId,
    DateTimeOffset GeneratedAt,
    CoverageHeatmapCellDto Cell,
    int CuratorQueuedCount,
    DateTimeOffset? LastUpdated,
    IReadOnlyList<CoverageRungVariantSummary> Variants);

/// <summary>
/// Per-variant summary for the rung drilldown. Keeps the payload small
/// (we don't need the full stem; just enough to identify + link out).
/// </summary>
public sealed record CoverageRungVariantSummary(
    string VariantId,
    string TemplateId,
    int TemplateVersion,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Canonical status values. Centralised so tests can import without
/// drifting string literals.
/// </summary>
public static class CoverageHeatmapStatus
{
    public const string Green = "green";
    public const string Amber = "amber";
    public const string Red = "red";
}

/// <summary>
/// Canonical non-color pattern keys. Centralised so the architecture ratchet
/// test + the front-end render layer share a single vocabulary.
/// </summary>
public static class CoverageHeatmapPatternKey
{
    /// <summary>Cell at or above target — solid fill.</summary>
    public const string Solid = "solid";

    /// <summary>Cell above target — diagonal lines (over-coverage signal).</summary>
    public const string Diagonal = "diagonal";

    /// <summary>Cell below target — dotted fill (deficit signal).</summary>
    public const string Dots = "dots";
}
