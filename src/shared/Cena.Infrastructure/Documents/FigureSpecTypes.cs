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

/// <summary>
/// FBD-001: Diagram interaction mode.
/// </summary>
public enum DiagramMode
{
    /// <summary>Complete diagram with all forces rendered (default).</summary>
    Display,

    /// <summary>Scene only — student drags force arrows onto the body.</summary>
    Construct
}

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
