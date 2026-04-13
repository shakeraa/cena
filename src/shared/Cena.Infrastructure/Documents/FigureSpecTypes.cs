// =============================================================================
// Cena Platform — Figure Spec DTOs (FIGURE-002)
// Discriminated union for structured, parametric figure specifications.
// Crosses the API boundary — used by admin authoring, student rendering,
// and AI figure generation.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Base type for all figure specifications. Discriminated on <see cref="Type"/>.
/// JSON serialization uses System.Text.Json polymorphism via [JsonDerivedType].
/// </summary>
[JsonDerivedType(typeof(FunctionPlotSpec), "function_plot")]
[JsonDerivedType(typeof(PhysicsDiagramSpec), "physics_diagram")]
[JsonDerivedType(typeof(GeometryConstructionSpec), "geometry")]
[JsonDerivedType(typeof(RasterFigureSpec), "raster")]
public abstract record FigureSpec(
    [property: JsonPropertyName("type")] string Type);

/// <summary>
/// Function plot rendered by function-plot.js (2D math graphs).
/// </summary>
public sealed record FunctionPlotSpec(
    string Expression,
    double XMin,
    double XMax,
    double YMin,
    double YMax,
    IReadOnlyList<FigureMarker> Markers,
    string? Caption,
    string AriaLabel
) : FigureSpec("function_plot");

/// <summary>
/// Physics diagram rendered by programmatic SVG (inclined planes, pulleys, etc.).
/// </summary>
public sealed record PhysicsDiagramSpec(
    PhysicsBodyType Body,
    IReadOnlyList<PhysicsForce> Forces,
    IReadOnlyDictionary<string, double> Parameters,
    CoordinateFrame Frame,
    string? Caption,
    string AriaLabel
) : FigureSpec("physics_diagram");

/// <summary>
/// Geometry construction rendered by JSXGraph.
/// </summary>
public sealed record GeometryConstructionSpec(
    string JsxGraphJson,
    string? Caption,
    string AriaLabel
) : FigureSpec("geometry");

/// <summary>
/// Raster image (pre-rendered, CDN-hosted). Fallback for legacy or scanned figures.
/// </summary>
public sealed record RasterFigureSpec(
    string CdnUrl,
    int WidthPx,
    int HeightPx,
    string? Caption,
    string AriaLabel,
    string? SourceAttribution
) : FigureSpec("raster");

// ── Supporting types ──

/// <summary>
/// A marker/annotation on a function plot (e.g., a labeled point or intercept).
/// </summary>
public sealed record FigureMarker(
    double X,
    double Y,
    string? Label,
    string MarkerType);

// ── FIG-RTL-001: Bidi script property for diagram text ──

/// <summary>
/// Script direction for text elements within diagrams.
/// Math content is always LTR; labels may be RTL (Arabic/Hebrew).
/// </summary>
public enum TextScript
{
    /// <summary>Left-to-right (default for math, English).</summary>
    Ltr,
    /// <summary>Right-to-left (Arabic, Hebrew prose labels).</summary>
    Rtl,
    /// <summary>Auto-detect from content.</summary>
    Auto
}

/// <summary>
/// A text element in a physics/geometry diagram with explicit script direction.
/// Ensures bidi rendering works correctly in RTL pages (FIG-RTL-001).
/// </summary>
public sealed record DiagramTextElement(
    string Text,
    double X,
    double Y,
    TextScript Script = TextScript.Auto,
    string? FontSize = null,
    string? Color = null);

// ── FIG-VIS-001: Visibility per scaffolding level ──

/// <summary>
/// Controls which diagram elements are visible at each scaffolding level.
/// Novice learners see simplified diagrams; experts see full detail.
/// </summary>
public sealed record VisibilityRule(
    /// <summary>Element ID within the diagram spec.</summary>
    string ElementId,
    /// <summary>Minimum scaffolding level at which this element becomes visible.</summary>
    string VisibleAtLevel,
    /// <summary>Optional fade-in opacity for transitional levels.</summary>
    double Opacity = 1.0);

// ── FIG-QUAL-001: Figure quality gate metadata ──

/// <summary>
/// Quality gate metadata for figure consistency checks.
/// Ensures figure complexity matches question difficulty.
/// </summary>
public sealed record FigureQualityMetadata(
    /// <summary>Estimated information density (1-5 scale).</summary>
    int InfoLevel,
    /// <summary>Whether all forces in a physics diagram sum to equilibrium (or expected net force).</summary>
    bool? EquilibriumVerified,
    /// <summary>Whether all text elements have aria-labels.</summary>
    bool AriaLabelsComplete,
    /// <summary>Whether marker labels match the question's variable names.</summary>
    bool MarkerConsistencyVerified);

/// <summary>
/// Physics body type for programmatic SVG generation.
/// </summary>
public enum PhysicsBodyType
{
    InclinedPlane,
    Pulley,
    FreeBody,
    CircularMotion,
    CircuitSchematic
}

/// <summary>
/// A force vector in a physics diagram.
/// </summary>
public sealed record PhysicsForce(
    string Label,
    double Magnitude,
    double AngleDeg,
    string? Color);

/// <summary>
/// Coordinate frame for physics diagrams.
/// </summary>
public enum CoordinateFrame
{
    Inertial,
    NonInertial
}
