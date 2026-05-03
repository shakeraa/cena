// =============================================================================
// Cena Platform — AiPromptBuilder (PRR-304)
//
// Extracted from AiGenerationService.cs as part of the PRR-304 LOC drain.
// Owns the prompt-assembly responsibility: take an AiGenerateRequest +
// the resolved AiProviderConfig and emit the LLM-ready prompt string.
//
// Why a separate class:
//   * AiGenerationService.cs sits at 847 LOC — past the ADR-0012 ratchet.
//     Extracting the prompt-template assembly responsibility into its own
//     class brings the orchestrator below baseline AND surfaces the
//     prompt-building responsibility cleanly so future changes (e.g.
//     ADR-0059 §15.5 source-anchored variants — already integrated below)
//     have an obvious home.
//   * Pure functions, no I/O, no DI dependency: easy to unit-test in
//     isolation when prompt-template regressions land.
//
// Behaviour-preserving extract: every line of BuildPrompt is copied
// verbatim. BloomLabel + LangLabel are private helpers used only here.
// No dead-code change, no semantics drift, no string output drift.
// =============================================================================

using System.Text;

namespace Cena.Admin.Api;

/// <summary>
/// Assembles the LLM prompt for a question-generation request. Pure
/// function over <see cref="AiGenerateRequest"/> + the resolved
/// <see cref="AiProviderConfig"/>.
/// </summary>
internal static class AiPromptBuilder
{
    /// <summary>
    /// Build the prompt string the AI provider receives. Includes:
    ///   • count / subject / topic / grade / Bloom level / difficulty band
    ///   • language label
    ///   • Bagrut requirements + cultural-sensitivity guardrails
    ///   • optional source context (with ADR-0059 §15.5 [SOURCE-AS-CREATIVE-SEED]
    ///     do-not-copy guardrails when the marker is present)
    ///   • optional style reference (text + image)
    ///   • tool-use instruction trailer
    /// </summary>
    public static string BuildPrompt(AiGenerateRequest req, AiProviderConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generate {req.Count} multiple-choice question(s) with the following specifications:");
        sb.AppendLine($"- Subject: {req.Subject}");
        if (!string.IsNullOrEmpty(req.Topic)) sb.AppendLine($"- Topic: {req.Topic}");
        sb.AppendLine($"- Grade/Level: {req.Grade}");
        sb.AppendLine($"- Bloom's Taxonomy Level: {req.BloomsLevel} ({BloomLabel(req.BloomsLevel)})");

        if (Math.Abs(req.MinDifficulty - req.MaxDifficulty) < 0.01f)
        {
            sb.AppendLine($"- Target Difficulty: {req.MinDifficulty:F2} (0=easy, 1=hard)");
        }
        else
        {
            sb.AppendLine($"- Difficulty Range: {req.MinDifficulty:F2} to {req.MaxDifficulty:F2} (0=easy, 1=hard)");
            sb.AppendLine($"  Distribute the {req.Count} question(s) evenly across this difficulty range.");
        }

        sb.AppendLine($"- Language: {LangLabel(req.Language)}");
        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine("- Each question must have exactly 4 options (A, B, C, D)");
        sb.AppendLine("- Exactly one correct answer per question");
        sb.AppendLine("- Each distractor must target a specific misconception (provide rationale)");
        sb.AppendLine("- Questions must align with Bagrut curriculum standards");
        sb.AppendLine("- Avoid cultural insensitivity for Israeli Hebrew/Arabic student populations");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(req.Context))
        {
            // ADR-0059 §15.5 structural-variant: when the BatchGenerateAsync
            // caller marked the context with [SOURCE-AS-CREATIVE-SEED], emit
            // explicit do-not-copy guardrails so the LLM produces a
            // competency-equivalent variant rather than a near-clone.
            if (req.Context.StartsWith("[SOURCE-AS-CREATIVE-SEED]", StringComparison.Ordinal))
            {
                sb.AppendLine("CREATIVE SEED — the question below is a Ministry past-paper item. " +
                              "It is provided as inspiration ONLY. Generate questions that:");
                sb.AppendLine("  • Test the SAME skill / competency at the SAME Bloom level");
                sb.AppendLine("  • Use a DIFFERENT scenario, DIFFERENT numbers, DIFFERENT framing");
                sb.AppendLine("  • Vary in difficulty across the band (some easier, some harder)");
                sb.AppendLine("  • Optionally split a multi-part source question into atomic single-skill items");
                sb.AppendLine("  • DO NOT reuse the source wording verbatim or near-verbatim");
                sb.AppendLine("  • DO NOT copy figure/diagram captions verbatim");
                sb.AppendLine();
                sb.AppendLine("Source (do not copy):");
                // Strip the marker from the body before printing.
                var body = req.Context.Substring("[SOURCE-AS-CREATIVE-SEED]".Length).TrimStart();
                sb.AppendLine(body);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Context/Source material (use this as the basis for question generation):");
                sb.AppendLine(req.Context);
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrEmpty(req.ImageBase64))
        {
            sb.AppendLine("A question/source image has been attached. Use the content visible in the image as the basis for generating questions.");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(req.StyleContext) || !string.IsNullOrEmpty(req.StyleImageBase64))
        {
            sb.AppendLine("STYLE REFERENCE — match the style, tone, and format of these questions:");
            if (!string.IsNullOrEmpty(req.StyleContext))
                sb.AppendLine(req.StyleContext);
            if (!string.IsNullOrEmpty(req.StyleImageBase64))
                sb.AppendLine("A style reference image has been attached. Match the question format, phrasing style, and complexity pattern shown in that image.");
            sb.AppendLine();
        }

        sb.AppendLine("Use the generate_questions tool to return your response as structured JSON.");
        return sb.ToString();
    }

    private static string BloomLabel(int level) => level switch
    {
        1 => "Remember", 2 => "Understand", 3 => "Apply",
        4 => "Analyze", 5 => "Evaluate", 6 => "Create", _ => "Unknown"
    };

    private static string LangLabel(string lang) => lang switch
    {
        "he" => "Hebrew", "ar" => "Arabic", "en" => "English", _ => lang
    };
}
