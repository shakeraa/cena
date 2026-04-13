// =============================================================================
// Cena Platform — SVG Builder (FIGURE-005)
// Fluent API for building SVG elements programmatically.
// No external dependencies — pure C# string construction.
// =============================================================================

using System.Globalization;
using System.Text;

namespace Cena.Admin.Api.Figures;

/// <summary>
/// Fluent SVG builder for constructing publication-ready SVG diagrams.
/// All numeric formatting uses InvariantCulture to avoid locale-dependent decimals.
/// </summary>
public sealed class SvgBuilder
{
    private readonly StringBuilder _content = new();
    private readonly int _width;
    private readonly int _height;
    private readonly double _padding;

    public SvgBuilder(int width = 600, int height = 400, double padding = 40)
    {
        _width = width;
        _height = height;
        _padding = padding;
    }

    public SvgBuilder Line(double x1, double y1, double x2, double y2,
        string stroke = "#333", double strokeWidth = 1.5, string? dashArray = null)
    {
        _content.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <line x1=\"{x1:F1}\" y1=\"{y1:F1}\" x2=\"{x2:F1}\" y2=\"{y2:F1}\" " +
            $"stroke=\"{stroke}\" stroke-width=\"{strokeWidth:F1}\"" +
            (dashArray != null ? $" stroke-dasharray=\"{dashArray}\"" : "") + " />"));
        return this;
    }

    public SvgBuilder Rect(double x, double y, double w, double h,
        string fill = "#4a90d9", string stroke = "#333", double strokeWidth = 1.5, double rx = 2)
    {
        _content.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <rect x=\"{x:F1}\" y=\"{y:F1}\" width=\"{w:F1}\" height=\"{h:F1}\" " +
            $"fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth:F1}\" rx=\"{rx:F1}\" />"));
        return this;
    }

    public SvgBuilder Circle(double cx, double cy, double r, string fill = "#333")
    {
        _content.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <circle cx=\"{cx:F1}\" cy=\"{cy:F1}\" r=\"{r:F1}\" fill=\"{fill}\" />"));
        return this;
    }

    public SvgBuilder Polygon(string points, string fill = "#ddd", string stroke = "#333", double strokeWidth = 1.5)
    {
        _content.AppendLine(
            $"  <polygon points=\"{points}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth.ToString("F1", CultureInfo.InvariantCulture)}\" />");
        return this;
    }

    public SvgBuilder Arrow(double x1, double y1, double x2, double y2,
        string color = "#c0392b", double strokeWidth = 2, string? label = null)
    {
        var markerId = $"arrow-{color.Replace("#", "")}";
        _content.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <line x1=\"{x1:F1}\" y1=\"{y1:F1}\" x2=\"{x2:F1}\" y2=\"{y2:F1}\" " +
            $"stroke=\"{color}\" stroke-width=\"{strokeWidth:F1}\" marker-end=\"url(#{markerId})\" />"));

        if (label != null)
        {
            var mx = (x1 + x2) / 2;
            var my = (y1 + y2) / 2;
            Text(mx + 8, my - 8, label, color, 14);
        }
        return this;
    }

    public SvgBuilder Text(double x, double y, string text, string fill = "#333",
        double fontSize = 14, string anchor = "start", string fontStyle = "normal")
    {
        _content.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <text x=\"{x:F1}\" y=\"{y:F1}\" fill=\"{fill}\" font-size=\"{fontSize:F0}\" " +
            $"font-family=\"'Latin Modern Math', 'STIX Two Math', serif\" " +
            $"text-anchor=\"{anchor}\" font-style=\"{fontStyle}\">{EscapeXml(text)}</text>"));
        return this;
    }

    public SvgBuilder Arc(double cx, double cy, double radius, double startAngleDeg, double endAngleDeg,
        string stroke = "#666", double strokeWidth = 1)
    {
        var startRad = startAngleDeg * Math.PI / 180;
        var endRad = endAngleDeg * Math.PI / 180;
        var x1 = cx + radius * Math.Cos(startRad);
        var y1 = cy - radius * Math.Sin(startRad);
        var x2 = cx + radius * Math.Cos(endRad);
        var y2 = cy - radius * Math.Sin(endRad);
        var largeArc = Math.Abs(endAngleDeg - startAngleDeg) > 180 ? 1 : 0;

        _content.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <path d=\"M {x1:F1} {y1:F1} A {radius:F1} {radius:F1} 0 {largeArc} 0 {x2:F1} {y2:F1}\" " +
            $"fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth:F1}\" />"));
        return this;
    }

    public SvgBuilder Raw(string svgContent)
    {
        _content.AppendLine(svgContent);
        return this;
    }

    public string Build(string? ariaLabel = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            $"viewBox=\"0 0 {_width} {_height}\" " +
            $"width=\"{_width}\" height=\"{_height}\"" +
            (ariaLabel != null ? $" role=\"img\" aria-label=\"{EscapeXml(ariaLabel)}\"" : "") + ">");

        // Arrow marker definitions
        sb.AppendLine("  <defs>");
        foreach (var color in new[] { "#c0392b", "#27ae60", "#e67e22", "#2980b9", "#7f8c8d" })
        {
            var id = $"arrow-{color.Replace("#", "")}";
            sb.AppendLine($"    <marker id=\"{id}\" viewBox=\"0 0 10 10\" refX=\"10\" refY=\"5\" " +
                $"markerWidth=\"8\" markerHeight=\"8\" orient=\"auto-start-reverse\">" +
                $"<path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"{color}\" /></marker>");
        }
        sb.AppendLine("  </defs>");

        sb.Append(_content);
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
