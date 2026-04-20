// =============================================================================
// Cena Platform — AI Figure Spec Generator (FIGURE-008)
//
// LLM proposes FigureSpec JSON during question variant generation.
// Includes retry loop: if the spec fails CAS validation or schema check,
// re-prompt with the error for up to 3 attempts.
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Figures;

/// <summary>
/// Result of AI figure spec generation.
/// </summary>
public record AiFigureGenerationResult(
    bool Success,
    string? FigureSpecJson,
    string? ValidationError,
    int AttemptsUsed,
    double TotalLatencyMs,
    bool QualityGateVerified
);

public interface IAiFigureGenerator
{
    /// <summary>
    /// Generate a figure spec for a question using LLM, with retry on validation failure.
    /// </summary>
    Task<AiFigureGenerationResult> GenerateFigureSpecAsync(
        string questionPrompt,
        string subject,
        string? existingFigureHint,
        CancellationToken ct = default);
}

/// <summary>
/// AI-powered figure spec generator with validation retry loop.
/// Uses the LLM to propose a FigureSpec JSON, validates it against the schema
/// and CAS (for physics equilibrium checks), and retries up to MaxAttempts.
/// </summary>
// ADR-0045: Multi-attempt figure-spec JSON generation with quality-gate retry
// loop (≤3 attempts). Shares the `diagram_generation` routing row (Kimi K2.5
// primary, Sonnet fallback) in contracts/llm/routing-config.yaml. Tier 3.
// prr-046: finops cost-center "figure-generation". The LLM call is currently
// scaffolded (CallLlmForFigureSpec returns null); when wired to
// AiGenerationService (future work), it will delegate cost emission to that
// service — same diagram-generation row.
[TaskRouting("tier3", "diagram_generation")]
[FeatureTag("figure-generation")]
[DelegatesLlmCost("AiGenerationService (when wired); currently scaffolded")]
public sealed class AiFigureGenerator : IAiFigureGenerator
{
    private readonly ILogger<AiFigureGenerator> _logger;
    private const int MaxAttempts = 3;

    // Required fields for a valid FigureSpec
    private static readonly string[] RequiredFields = ["type", "ariaLabel"];
    private static readonly string[] ValidTypes = ["functionPlot", "geometry", "physics", "raster"];

    public AiFigureGenerator(ILogger<AiFigureGenerator> logger)
    {
        _logger = logger;
    }

    public async Task<AiFigureGenerationResult> GenerateFigureSpecAsync(
        string questionPrompt,
        string subject,
        string? existingFigureHint,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? lastError = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Build the LLM prompt
            var prompt = BuildPrompt(questionPrompt, subject, existingFigureHint, lastError);

            // Call LLM (simplified — in production this calls ClaudeTutorLlmService or AiGenerationService)
            var llmResponse = await CallLlmForFigureSpec(prompt, ct);

            // Validate the response — schema check
            var validationError = ValidateFigureSpec(llmResponse);
            if (validationError is not null)
            {
                lastError = validationError;
                _logger.LogWarning(
                    "AI figure generation attempt {Attempt}/{Max} failed schema validation: {Error}",
                    attempt, MaxAttempts, validationError);
                continue;
            }

            // PP-012: Run full FigureQualityGate after schema validation passes
            var qualityResult = FigureQualityGate.ValidateJson(llmResponse!);
            if (!qualityResult.Passed)
            {
                var violations = string.Join("; ",
                    qualityResult.Violations.Select(v => $"[{v.RuleId}] {v.Message}"));
                lastError = $"Quality gate failed: {violations}";
                _logger.LogWarning(
                    "AI figure generation attempt {Attempt}/{Max} failed quality gate: {Violations}",
                    attempt, MaxAttempts, violations);
                continue;
            }

            _logger.LogInformation(
                "AI figure generation succeeded on attempt {Attempt}/{Max} in {Ms:F0}ms",
                attempt, MaxAttempts, sw.Elapsed.TotalMilliseconds);

            return new AiFigureGenerationResult(
                Success: true,
                FigureSpecJson: llmResponse,
                ValidationError: null,
                AttemptsUsed: attempt,
                TotalLatencyMs: sw.Elapsed.TotalMilliseconds,
                QualityGateVerified: true);
            _logger.LogWarning(
                "AI figure generation attempt {Attempt}/{Max} failed validation: {Error}",
                attempt, MaxAttempts, validationError);
        }

        _logger.LogError("AI figure generation failed after {Max} attempts", MaxAttempts);
        return new AiFigureGenerationResult(
            Success: false,
            FigureSpecJson: null,
            ValidationError: lastError,
            AttemptsUsed: MaxAttempts,
            TotalLatencyMs: sw.Elapsed.TotalMilliseconds,
            QualityGateVerified: false);
    }

    private static string BuildPrompt(
        string questionPrompt, string subject, string? hint, string? previousError)
    {
        var prompt = $"""
            Generate a FigureSpec JSON for this {subject} question:

            Question: {questionPrompt}

            The FigureSpec must have:
            - "type": one of "functionPlot", "geometry", "physics", "raster"
            - "ariaLabel": accessibility description of the figure
            - Type-specific config (functionPlotConfig, jsxGraphConfig, or physicsDiagramSpec)

            Return ONLY valid JSON, no markdown.
            """;

        if (hint is not null)
            prompt += $"\nHint from author: {hint}";

        if (previousError is not null)
            prompt += $"\nPrevious attempt failed: {previousError}. Fix the error.";

        return prompt;
    }

    private static string? ValidateFigureSpec(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "Empty response from LLM";

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var field in RequiredFields)
            {
                if (!root.TryGetProperty(field, out _))
                    return $"Missing required field: {field}";
            }

            var type = root.GetProperty("type").GetString();
            if (type is null || !ValidTypes.Contains(type))
                return $"Invalid type: {type}. Must be one of: {string.Join(", ", ValidTypes)}";

            // Type-specific validation
            switch (type)
            {
                case "functionPlot":
                    if (!root.TryGetProperty("functionPlotConfig", out _))
                        return "functionPlot type requires functionPlotConfig";
                    break;
                case "physics":
                    if (!root.TryGetProperty("physicsDiagramSpec", out _))
                        return "physics type requires physicsDiagramSpec";
                    break;
                case "raster":
                    if (!root.TryGetProperty("imageUrl", out _))
                        return "raster type requires imageUrl";
                    break;
            }

            return null; // Valid
        }
        catch (JsonException ex)
        {
            return $"Invalid JSON: {ex.Message}";
        }
    }

    /// <summary>
    /// Placeholder for LLM call — in production, this delegates to AiGenerationService.
    /// Returns the raw JSON string from the LLM.
    /// </summary>
    private static Task<string?> CallLlmForFigureSpec(string prompt, CancellationToken ct)
    {
        // Production implementation: call Claude via AiGenerationService
        // For now, return null to trigger validation error → retry → eventual failure
        // This ensures the retry loop is exercised and the caller handles failure gracefully.
        _ = prompt;
        return Task.FromResult<string?>(null);
    }
}
