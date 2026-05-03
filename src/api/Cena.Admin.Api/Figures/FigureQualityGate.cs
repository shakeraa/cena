// =============================================================================
// Cena Platform — Figure Quality Gate (FIGURE-007)
// Automated checks that must pass before a question with a figure can enter
// the question bank. No figure reaches students without passing all rules.
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Figures;

/// <summary>
/// Quality gate result for a figure specification.
/// </summary>
public sealed record FigureQualityResult
{
    public bool Passed { get; init; }
    public IReadOnlyList<FigureQualityViolation> Violations { get; init; } = Array.Empty<FigureQualityViolation>();
}

public sealed record FigureQualityViolation(
    string RuleId,
    string Severity,
    string Message);

/// <summary>
/// Runs quality gate checks on figure specifications.
/// </summary>
public static class FigureQualityGate
{
    /// <summary>
    /// Validates a FigureSpec against all quality rules.
    /// </summary>
    public static FigureQualityResult Validate(FigureSpec spec)
    {
        var violations = new List<FigureQualityViolation>();

        // Rule FIG-001: AriaLabel is mandatory and non-trivial
        CheckAriaLabel(spec, violations);

        // Rule FIG-002: Type-specific validation
        switch (spec)
        {
            case FunctionPlotSpec fp:
                CheckFunctionPlot(fp, violations);
                break;
            case PhysicsDiagramSpec pd:
                CheckPhysicsDiagram(pd, violations);
                break;
            case GeometryConstructionSpec gc:
                CheckGeometryConstruction(gc, violations);
                break;
            case RasterFigureSpec rf:
                CheckRasterFigure(rf, violations);
                break;
        }

        return new FigureQualityResult
        {
            Passed = violations.Count == 0,
            Violations = violations
        };
    }

    private static void CheckAriaLabel(FigureSpec spec, List<FigureQualityViolation> violations)
    {
        var ariaLabel = spec switch
        {
            FunctionPlotSpec fp => fp.AriaLabel,
            PhysicsDiagramSpec pd => pd.AriaLabel,
            GeometryConstructionSpec gc => gc.AriaLabel,
            RasterFigureSpec rf => rf.AriaLabel,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(ariaLabel))
        {
            violations.Add(new("FIG-001", "critical",
                "AriaLabel is mandatory for accessibility. Every figure must have a descriptive text alternative."));
        }
        else if (ariaLabel.Length < 20)
        {
            violations.Add(new("FIG-001b", "warning",
                $"AriaLabel is too short ({ariaLabel.Length} chars). Should describe the figure for screen reader users (min 20 chars)."));
        }
    }

    private static void CheckFunctionPlot(FunctionPlotSpec spec, List<FigureQualityViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(spec.Expression))
            violations.Add(new("FIG-FP-001", "critical", "Function plot expression is empty."));

        if (spec.XMax <= spec.XMin)
            violations.Add(new("FIG-FP-002", "critical", "XMax must be greater than XMin."));

        if (spec.YMax <= spec.YMin)
            violations.Add(new("FIG-FP-003", "critical", "YMax must be greater than YMin."));

        foreach (var marker in spec.Markers)
        {
            if (marker.X < spec.XMin || marker.X > spec.XMax || marker.Y < spec.YMin || marker.Y > spec.YMax)
                violations.Add(new("FIG-FP-004", "warning",
                    $"Marker at ({marker.X}, {marker.Y}) is outside the plot bounds."));
        }
    }

    private static void CheckPhysicsDiagram(PhysicsDiagramSpec spec, List<FigureQualityViolation> violations)
    {
        if (spec.Forces.Count == 0)
            violations.Add(new("FIG-PD-001", "warning", "Physics diagram has no forces. Consider adding at least gravity."));

        // Equilibrium check: if parameters claim equilibrium, verify forces sum to zero
        if (spec.Parameters.TryGetValue("is_equilibrium", out var eq) && eq == 1.0)
        {
            double fx = 0, fy = 0;
            foreach (var force in spec.Forces)
            {
                var rad = force.AngleDeg * Math.PI / 180;
                fx += force.Magnitude * Math.Cos(rad);
                fy += force.Magnitude * Math.Sin(rad);
            }

            if (Math.Abs(fx) > 0.01 || Math.Abs(fy) > 0.01)
            {
                violations.Add(new("FIG-PD-002", "critical",
                    $"Diagram claims equilibrium but forces don't sum to zero (Fx={fx:F3}, Fy={fy:F3})."));
            }
        }

        // Check for duplicate force labels
        var labels = spec.Forces.Select(f => f.Label).ToList();
        var dupes = labels.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var dupe in dupes)
        {
            violations.Add(new("FIG-PD-003", "warning", $"Duplicate force label: '{dupe}'."));
        }
    }

    private static void CheckGeometryConstruction(GeometryConstructionSpec spec, List<FigureQualityViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(spec.JsxGraphJson))
            violations.Add(new("FIG-GC-001", "critical", "JSXGraph JSON specification is empty."));
    }

    private static void CheckRasterFigure(RasterFigureSpec spec, List<FigureQualityViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(spec.CdnUrl))
            violations.Add(new("FIG-RF-001", "critical", "CDN URL is empty."));

        if (spec.WidthPx <= 0 || spec.HeightPx <= 0)
            violations.Add(new("FIG-RF-002", "critical", "Width and height must be positive."));

        if (string.IsNullOrWhiteSpace(spec.SourceAttribution))
            violations.Add(new("FIG-RF-003", "warning", "Source attribution is missing. Required for Bagrut exam figures."));
    }

    /// <summary>
    /// PP-012: Validate a raw JSON figure spec string (from AI generation).
    /// Parses the JSON, constructs the appropriate FigureSpec, and runs the full gate.
    /// </summary>
    public static FigureQualityResult ValidateJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            var ariaLabel = root.TryGetProperty("ariaLabel", out var alProp) ? alProp.GetString() ?? "" : "";

            FigureSpec? spec = type switch
            {
                "functionPlot" => ParseFunctionPlotFromJson(root, ariaLabel),
                "physics" => ParsePhysicsDiagramFromJson(root, ariaLabel),
                "geometry" => new GeometryConstructionSpec(
                    root.TryGetProperty("jsxGraphConfig", out var jsx) ? jsx.GetRawText() : "",
                    root.TryGetProperty("caption", out var gc) ? gc.GetString() : null,
                    ariaLabel),
                "raster" => new RasterFigureSpec(
                    root.TryGetProperty("imageUrl", out var url) ? url.GetString() ?? "" : "",
                    root.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                    root.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
                    root.TryGetProperty("caption", out var rc) ? rc.GetString() : null,
                    ariaLabel,
                    root.TryGetProperty("sourceAttribution", out var sa) ? sa.GetString() : null),
                _ => null
            };

            if (spec is null)
            {
                return new FigureQualityResult
                {
                    Passed = false,
                    Violations = [new("FIG-PARSE", "critical", $"Unknown figure type: {type}")]
                };
            }

            return Validate(spec);
        }
        catch (JsonException ex)
        {
            return new FigureQualityResult
            {
                Passed = false,
                Violations = [new("FIG-JSON", "critical", $"Invalid JSON: {ex.Message}")]
            };
        }
    }

    private static FunctionPlotSpec ParseFunctionPlotFromJson(JsonElement root, string ariaLabel)
    {
        var config = root.TryGetProperty("functionPlotConfig", out var cfg) ? cfg : root;
        var markers = new List<FigureMarker>();
        if (config.TryGetProperty("markers", out var mArr))
        {
            foreach (var m in mArr.EnumerateArray())
            {
                markers.Add(new FigureMarker(
                    m.TryGetProperty("x", out var mx) ? mx.GetDouble() : 0,
                    m.TryGetProperty("y", out var my) ? my.GetDouble() : 0,
                    m.TryGetProperty("label", out var ml) ? ml.GetString() : null,
                    m.TryGetProperty("type", out var mt) ? mt.GetString() ?? "point" : "point"));
            }
        }

        return new FunctionPlotSpec(
            config.TryGetProperty("expression", out var expr) ? expr.GetString() ?? "" : "",
            config.TryGetProperty("xMin", out var xn) ? xn.GetDouble() : -10,
            config.TryGetProperty("xMax", out var xx) ? xx.GetDouble() : 10,
            config.TryGetProperty("yMin", out var yn) ? yn.GetDouble() : -10,
            config.TryGetProperty("yMax", out var yx) ? yx.GetDouble() : 10,
            markers,
            root.TryGetProperty("caption", out var cap) ? cap.GetString() : null,
            ariaLabel);
    }

    private static PhysicsDiagramSpec ParsePhysicsDiagramFromJson(JsonElement root, string ariaLabel)
    {
        var pdSpec = root.TryGetProperty("physicsDiagramSpec", out var pds) ? pds : root;
        var forces = new List<PhysicsForce>();
        if (pdSpec.TryGetProperty("forces", out var fArr))
        {
            foreach (var f in fArr.EnumerateArray())
            {
                forces.Add(new PhysicsForce(
                    f.TryGetProperty("label", out var fl) ? fl.GetString() ?? "" : "",
                    f.TryGetProperty("magnitude", out var fm) ? fm.GetDouble() : 0,
                    f.TryGetProperty("angleDeg", out var fa) ? fa.GetDouble() : 0,
                    f.TryGetProperty("color", out var fc) ? fc.GetString() : null));
            }
        }

        var parameters = new Dictionary<string, double>();
        if (pdSpec.TryGetProperty("parameters", out var pars))
        {
            foreach (var p in pars.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number)
                    parameters[p.Name] = p.Value.GetDouble();
            }
        }

        var body = PhysicsBodyType.FreeBody;
        if (pdSpec.TryGetProperty("body", out var bodyProp))
            Enum.TryParse<PhysicsBodyType>(bodyProp.GetString(), true, out body);

        return new PhysicsDiagramSpec(body, forces, parameters,
            CoordinateFrame.Inertial,
            root.TryGetProperty("caption", out var cap) ? cap.GetString() : null,
            ariaLabel);
    }
}
