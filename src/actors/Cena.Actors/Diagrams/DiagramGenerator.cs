// =============================================================================
// Cena Platform -- LLM-Powered Diagram Generator (LLM-009)
// Layer: Actors / Diagrams | Runtime: .NET 9
//
// Generates Mermaid diagram code via ILlmClient for math/physics/chemistry
// educational content (Bagrut curriculum). Validates Mermaid syntax before
// returning results.
// =============================================================================

using Cena.Actors.Gateway;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagrams;

/// <summary>
/// Generates educational diagrams using an LLM to produce Mermaid code.
/// </summary>
public interface IDiagramGenerator
{
    Task<DiagramResult> GenerateAsync(DiagramRequest request, CancellationToken ct = default);
}

public enum DiagramType
{
    FunctionPlot,
    Geometry,
    Circuit,
    Molecular,
    Flowchart,
    PhysicsVector
}

public sealed record DiagramRequest(
    string Subject,
    string Topic,
    DiagramType DiagramType,
    string Description,
    string Language = "he");

public sealed record DiagramResult(
    string SvgContent,
    string MermaidCode,
    string Description,
    DateTimeOffset GeneratedAt);

public sealed class DiagramGenerator : IDiagramGenerator
{
    private readonly ILlmClient _llm;
    private readonly ILogger<DiagramGenerator> _logger;

    public DiagramGenerator(ILlmClient llm, ILogger<DiagramGenerator> logger)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DiagramResult> GenerateAsync(DiagramRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ArgumentException("Subject is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Topic))
            throw new ArgumentException("Topic is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Description is required.", nameof(request));

        var systemPrompt = BuildSystemPrompt(request);
        var userPrompt = BuildUserPrompt(request);

        _logger.LogInformation(
            "Generating {DiagramType} diagram for {Subject}/{Topic}",
            request.DiagramType, request.Subject, request.Topic);

        var llmRequest = new LlmRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            Temperature: 0.2f,
            MaxTokens: 4096,
            ModelId: "claude-sonnet-4-20250514",
            CacheSystemPrompt: true);

        var response = await _llm.CompleteAsync(llmRequest, ct);

        var mermaidCode = ExtractMermaidCode(response.Content);
        ValidateMermaidSyntax(mermaidCode);

        var description = ExtractDescription(response.Content, request);

        _logger.LogInformation(
            "Diagram generated: {DiagramType} for {Subject}/{Topic} ({InputTokens}+{OutputTokens} tokens)",
            request.DiagramType, request.Subject, request.Topic,
            response.InputTokens, response.OutputTokens);

        return new DiagramResult(
            SvgContent: "", // SVG rendering handled client-side from Mermaid code
            MermaidCode: mermaidCode,
            Description: description,
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    // ── Prompt Construction ──

    private static string BuildSystemPrompt(DiagramRequest request)
    {
        var languageInstruction = request.Language switch
        {
            "he" => "Write all labels and descriptions in Hebrew (עברית). Use standard Hebrew mathematical/scientific terminology.",
            "ar" => "Write all labels and descriptions in Arabic (العربية). Use standard MSA mathematical/scientific terminology.",
            _ => $"Write all labels and descriptions in {request.Language}."
        };

        var typeGuidance = request.DiagramType switch
        {
            DiagramType.FunctionPlot => """
                Create a flowchart-style diagram that represents a mathematical function.
                Use nodes for key points (intercepts, maxima, minima, inflection points).
                Use edges to show the curve direction between points.
                Label axes and key coordinates.
                """,
            DiagramType.Geometry => """
                Create a diagram representing geometric shapes and relationships.
                Use nodes for vertices/points and edges for sides/lines.
                Label angles, lengths, and geometric properties.
                Include construction lines where relevant.
                """,
            DiagramType.Circuit => """
                Create a flowchart representing an electrical circuit.
                Use nodes for components (resistor, capacitor, battery, etc.).
                Use edges for wire connections with current direction arrows.
                Label voltage, current, and resistance values.
                """,
            DiagramType.Molecular => """
                Create a diagram representing molecular structure.
                Use nodes for atoms with element symbols.
                Use edges for chemical bonds (single, double, triple).
                Show electron pairs where relevant.
                """,
            DiagramType.Flowchart => """
                Create a standard flowchart for the described process.
                Use appropriate shapes: rectangles for steps, diamonds for decisions.
                Keep the flow logical and top-to-bottom or left-to-right.
                """,
            DiagramType.PhysicsVector => """
                Create a diagram showing physics vectors and forces.
                Use nodes for objects/points of application.
                Use labeled edges for force vectors with magnitude and direction.
                Include coordinate axes and angle labels.
                """,
            _ => "Create an appropriate educational diagram."
        };

        return $"""
            You are an educational diagram generator for Israeli Bagrut (matriculation) exam preparation.
            You produce valid Mermaid diagram syntax that visualizes {request.Subject} concepts.

            LANGUAGE:
            - {languageInstruction}

            DIAGRAM TYPE: {request.DiagramType}
            {typeGuidance}

            OUTPUT FORMAT:
            - Return ONLY valid Mermaid diagram code inside a ```mermaid code block.
            - After the code block, include a one-line DESCRIPTION: line summarizing the diagram.
            - Do NOT include any other text, explanation, or commentary.
            - Ensure the Mermaid syntax is valid and will render correctly.
            - Use graph TD, graph LR, flowchart TD, flowchart LR, or other valid Mermaid diagram types.
            - Keep diagrams clear and not overly complex (max ~20 nodes).
            """;
    }

    private static string BuildUserPrompt(DiagramRequest request) =>
        $"""
        Subject: {request.Subject}
        Topic: {request.Topic}
        Diagram type: {request.DiagramType}
        Description: {request.Description}

        Generate the Mermaid diagram now.
        """;

    // ── Parsing ──

    internal static string ExtractMermaidCode(string llmOutput)
    {
        // Look for ```mermaid ... ``` block
        const string startMarker = "```mermaid";
        const string endMarker = "```";

        var startIdx = llmOutput.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0)
        {
            // Fallback: try to find bare Mermaid code starting with graph/flowchart/sequenceDiagram etc.
            var lines = llmOutput.Split('\n', StringSplitOptions.TrimEntries);
            var mermaidStart = Array.FindIndex(lines, l =>
                l.StartsWith("graph ", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase));

            if (mermaidStart >= 0)
            {
                // Take lines until next ``` or end
                var mermaidLines = new List<string>();
                for (var i = mermaidStart; i < lines.Length; i++)
                {
                    if (lines[i] == "```") break;
                    if (lines[i].StartsWith("DESCRIPTION:", StringComparison.OrdinalIgnoreCase)) break;
                    mermaidLines.Add(lines[i]);
                }
                return string.Join("\n", mermaidLines).Trim();
            }

            throw new InvalidOperationException(
                "LLM output does not contain a valid Mermaid code block.");
        }

        var contentStart = startIdx + startMarker.Length;
        var endIdx = llmOutput.IndexOf(endMarker, contentStart, StringComparison.Ordinal);
        if (endIdx < 0)
            endIdx = llmOutput.Length;

        return llmOutput[contentStart..endIdx].Trim();
    }

    internal static string ExtractDescription(string llmOutput, DiagramRequest request)
    {
        // Look for DESCRIPTION: line after the code block
        var lines = llmOutput.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("DESCRIPTION:", StringComparison.OrdinalIgnoreCase))
                return trimmed["DESCRIPTION:".Length..].Trim();
        }

        // Fallback: generate a description from the request
        return $"{request.DiagramType} diagram for {request.Subject} — {request.Topic}: {request.Description}";
    }

    // ── Validation ──

    internal static void ValidateMermaidSyntax(string mermaidCode)
    {
        if (string.IsNullOrWhiteSpace(mermaidCode))
            throw new InvalidOperationException("Generated Mermaid code is empty.");

        var firstLine = mermaidCode.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";

        // Must start with a valid Mermaid diagram type declaration
        var validStarts = new[]
        {
            "graph ", "graph\t", "flowchart ", "flowchart\t",
            "sequenceDiagram", "classDiagram", "stateDiagram",
            "erDiagram", "gantt", "pie ", "pie\t",
            "gitgraph", "mindmap", "timeline", "sankey",
            "xychart", "block-beta", "quadrantChart"
        };

        var hasValidStart = validStarts.Any(s =>
            firstLine.StartsWith(s, StringComparison.OrdinalIgnoreCase));

        if (!hasValidStart)
            throw new InvalidOperationException(
                $"Invalid Mermaid syntax: first line must declare a diagram type. Got: '{firstLine}'");

        // Basic structural checks
        var lines = mermaidCode.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            throw new InvalidOperationException(
                "Mermaid diagram must have at least a type declaration and one element.");

        // Check for unbalanced brackets/parentheses (common LLM error)
        var openBrackets = mermaidCode.Count(c => c == '[');
        var closeBrackets = mermaidCode.Count(c => c == ']');
        if (Math.Abs(openBrackets - closeBrackets) > 1) // Allow small mismatch for edge labels
            throw new InvalidOperationException(
                $"Mermaid syntax has unbalanced brackets: [{openBrackets} open, {closeBrackets} close.");

        var openParens = mermaidCode.Count(c => c == '(');
        var closeParens = mermaidCode.Count(c => c == ')');
        if (Math.Abs(openParens - closeParens) > 1)
            throw new InvalidOperationException(
                $"Mermaid syntax has unbalanced parentheses: ({openParens} open, {closeParens} close.");
    }
}
