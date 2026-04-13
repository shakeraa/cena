// =============================================================================
// Cena Platform — Figure Quality Gate (FIGURE-007)
// Automated checks that must pass before a question with a figure can enter
// the question bank. No figure reaches students without passing all rules.
// =============================================================================

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
}
