// =============================================================================
// Cena Platform — Physics Diagram Service (FIGURE-005)
// Generates publication-ready SVG diagrams from PhysicsDiagramSpec.
// Pure C# — no external binary, no image-gen model, no headless browser.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Figures;

/// <summary>
/// Generates SVG diagrams for physics problems. Each body type has a dedicated
/// renderer that produces clean, publication-ready SVG with proper force vectors,
/// angle arcs, and math labels.
/// </summary>
public sealed class PhysicsDiagramService
{
    private readonly ILogger<PhysicsDiagramService> _logger;

    // Force type → color mapping (consistent across all diagrams)
    private static readonly Dictionary<string, string> ForceColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gravity"] = "#c0392b",    // red
        ["weight"] = "#c0392b",
        ["mg"] = "#c0392b",
        ["normal"] = "#27ae60",     // green
        ["friction"] = "#e67e22",   // orange
        ["applied"] = "#2980b9",    // blue
        ["tension"] = "#2980b9",
        ["inertial"] = "#7f8c8d",   // grey (pseudo-forces)
    };

    public PhysicsDiagramService(ILogger<PhysicsDiagramService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Renders a PhysicsDiagramSpec to SVG string.
    /// </summary>
    public PhysicsDiagramResult Render(PhysicsDiagramSpec spec)
    {
        var svg = spec.Body switch
        {
            PhysicsBodyType.InclinedPlane => RenderInclinedPlane(spec),
            PhysicsBodyType.FreeBody => RenderFreeBody(spec),
            PhysicsBodyType.Pulley => RenderPulley(spec),
            _ => RenderFreeBody(spec) // fallback
        };

        var hash = ComputeContentHash(svg);
        var ariaLabel = spec.AriaLabel;

        _logger.LogDebug("Rendered {BodyType} diagram: {Bytes} bytes, hash={Hash}",
            spec.Body, Encoding.UTF8.GetByteCount(svg), hash[..8]);

        return new PhysicsDiagramResult(svg, hash, ariaLabel, 600, 400);
    }

    private string RenderInclinedPlane(PhysicsDiagramSpec spec)
    {
        var angleDeg = spec.Parameters.GetValueOrDefault("angle_deg", 30);
        var angleRad = angleDeg * Math.PI / 180;
        var mass = spec.Parameters.GetValueOrDefault("mass_kg", 5);

        var builder = new SvgBuilder(600, 400);

        // Ground line
        builder.Line(40, 340, 560, 340, "#333", 2);

        // Inclined plane triangle
        double baseLen = 400;
        double height = baseLen * Math.Tan(angleRad);
        if (height > 280) { baseLen *= 280 / height; height = 280; }

        double bx = 100, by = 340;
        double tx = bx + baseLen, ty = by;
        double px = bx, py = by - height;

        builder.Polygon(
            $"{bx:F0},{by:F0} {tx:F0},{ty:F0} {bx:F0},{py:F0}",
            "#e8e8e8", "#333", 2);

        // Angle arc at base
        builder.Arc(bx, by, 40, 0, angleDeg, "#666", 1.5);
        builder.Text(bx + 50, by - 12, $"θ = {angleDeg:F0}°", "#666", 13, fontStyle: "italic");

        // Mass box on the slope
        double slopeMidFrac = 0.5;
        double mx = bx + baseLen * slopeMidFrac;
        double my = by - height * slopeMidFrac;
        double boxSize = 40;

        // Rotate box to sit on slope
        var cosA = Math.Cos(angleRad);
        var sinA = Math.Sin(angleRad);
        builder.Raw($"  <rect x=\"{mx - boxSize / 2:F1}\" y=\"{my - boxSize:F1}\" " +
            $"width=\"{boxSize:F1}\" height=\"{boxSize:F1}\" fill=\"#4a90d9\" stroke=\"#2c3e50\" " +
            $"stroke-width=\"2\" rx=\"3\" transform=\"rotate({-angleDeg:F1} {mx:F1} {my:F1})\" />");

        // Center-of-mass dot
        builder.Circle(mx, my - boxSize / 2, 3, "#fff");

        // Mass label
        builder.Text(mx + 25, my - boxSize - 5, $"m = {mass:F0} kg", "#2c3e50", 13);

        // Force vectors from center of mass
        double cmx = mx, cmy = my - boxSize / 2;

        // Gravity (straight down)
        foreach (var force in spec.Forces)
        {
            var color = GetForceColor(force.Label);
            var fAngleRad = force.AngleDeg * Math.PI / 180;
            var len = Math.Min(force.Magnitude * 15, 120);
            var fx = cmx + len * Math.Cos(fAngleRad);
            var fy = cmy - len * Math.Sin(fAngleRad); // SVG Y is inverted

            builder.Arrow(cmx, cmy, fx, fy, color, 2.5, force.Label);
        }

        // If no forces specified, draw default gravity + normal + friction
        if (spec.Forces.Count == 0)
        {
            // Gravity: mg straight down
            builder.Arrow(cmx, cmy, cmx, cmy + 80, "#c0392b", 2.5, "mg");

            // Normal: perpendicular to slope, away from surface
            double nx = cmx - 70 * sinA;
            double ny = cmy - 70 * cosA;
            builder.Arrow(cmx, cmy, nx, ny, "#27ae60", 2.5, "N");

            // Friction: along slope, opposing motion (up the slope)
            double ffx = cmx - 50 * cosA;
            double ffy = cmy + 50 * sinA;
            builder.Arrow(cmx, cmy, ffx, ffy, "#e67e22", 2, "Ff");

            // Component decomposition (dashed)
            builder.Line(cmx, cmy + 80, cmx + 80 * sinA * cosA, cmy + 80 - 80 * sinA * sinA,
                "#c0392b", 1, "4,4");
        }

        // Coordinate axes (bottom-right)
        builder.Arrow(500, 370, 550, 370, "#333", 1.5);
        builder.Text(555, 374, "x", "#333", 12, fontStyle: "italic");
        builder.Arrow(500, 370, 500, 320, "#333", 1.5);
        builder.Text(495, 315, "y", "#333", 12, "end", "italic");

        return builder.Build(spec.AriaLabel);
    }

    private string RenderFreeBody(PhysicsDiagramSpec spec)
    {
        var builder = new SvgBuilder(600, 400);

        // Central body (dot or box)
        double cx = 300, cy = 200;
        builder.Circle(cx, cy, 8, "#2c3e50");
        builder.Text(cx + 15, cy + 5, "body", "#2c3e50", 13);

        // Force vectors radiating from center
        foreach (var force in spec.Forces)
        {
            var color = GetForceColor(force.Label);
            var angleRad = force.AngleDeg * Math.PI / 180;
            var len = Math.Min(force.Magnitude * 20, 150);
            var fx = cx + len * Math.Cos(angleRad);
            var fy = cy - len * Math.Sin(angleRad);

            builder.Arrow(cx, cy, fx, fy, color, 2.5, force.Label);
        }

        // Coordinate axes
        builder.Arrow(50, 370, 120, 370, "#333", 1.5);
        builder.Text(125, 374, "x", "#333", 12, fontStyle: "italic");
        builder.Arrow(50, 370, 50, 300, "#333", 1.5);
        builder.Text(45, 295, "y", "#333", 12, "end", "italic");

        if (spec.Frame == CoordinateFrame.NonInertial)
        {
            builder.Text(50, 385, "(non-inertial frame)", "#7f8c8d", 11, fontStyle: "italic");
        }

        return builder.Build(spec.AriaLabel);
    }

    private string RenderPulley(PhysicsDiagramSpec spec)
    {
        var builder = new SvgBuilder(600, 400);

        // Pulley wheel
        double px = 300, py = 80, pr = 30;
        builder.Circle(px, py, pr, "none");
        builder.Raw($"  <circle cx=\"{px:F0}\" cy=\"{py:F0}\" r=\"{pr:F0}\" fill=\"none\" stroke=\"#333\" stroke-width=\"2\" />");
        builder.Circle(px, py, 4, "#333"); // axle

        // Support
        builder.Line(px, py - pr, px, 30, "#333", 3);
        builder.Line(px - 40, 30, px + 40, 30, "#333", 3);

        // Left rope + mass
        builder.Line(px - pr, py, px - pr, 280, "#333", 1.5);
        builder.Rect(px - pr - 25, 280, 50, 50, "#4a90d9", "#2c3e50", 2);
        var m1 = spec.Parameters.GetValueOrDefault("mass1_kg", 3);
        builder.Text(px - pr, 310, $"{m1:F0} kg", "#fff", 14, "middle");

        // Right rope + mass
        builder.Line(px + pr, py, px + pr, 320, "#333", 1.5);
        builder.Rect(px + pr - 25, 320, 50, 50, "#e74c3c", "#2c3e50", 2);
        var m2 = spec.Parameters.GetValueOrDefault("mass2_kg", 5);
        builder.Text(px + pr, 350, $"{m2:F0} kg", "#fff", 14, "middle");

        // Force vectors
        foreach (var force in spec.Forces)
        {
            var color = GetForceColor(force.Label);
            // Apply forces at mass positions
        }

        // Default tension + gravity labels
        builder.Arrow(px - pr, 280, px - pr, 240, "#2980b9", 2, "T");
        builder.Arrow(px - pr, 330, px - pr, 370, "#c0392b", 2, $"m₁g");
        builder.Arrow(px + pr, 320, px + pr, 280, "#2980b9", 2, "T");
        builder.Arrow(px + pr, 370, px + pr, 400, "#c0392b", 2, $"m₂g");

        return builder.Build(spec.AriaLabel);
    }

    private static string GetForceColor(string label)
    {
        foreach (var (key, color) in ForceColors)
        {
            if (label.Contains(key, StringComparison.OrdinalIgnoreCase))
                return color;
        }
        return "#2980b9"; // default blue
    }

    /// <summary>
    /// Content-addressed hash for caching.
    /// </summary>
    public static string ComputeContentHash(string svg)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(svg));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Result of rendering a physics diagram.
/// </summary>
public sealed record PhysicsDiagramResult(
    string Svg,
    string ContentHash,
    string AriaLabel,
    int WidthPx,
    int HeightPx);
