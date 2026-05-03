// =============================================================================
// Cena Platform — HybridConceptExtractor prompt builder (ADR-0062 Phase 1)
//
// Builds the system + user prompt for the LLM tier of HybridConceptExtractor.
// Extracted from the extractor body to keep the main class < 300 stripped LOC
// and to let unit tests assert closed-set hygiene on the prompt itself
// (the prompt is the closed-set leaf list; if it ever leaks free-form skill
// names, the LLM will return them and TryCanonicalize will drop them — but
// the right defense is a tight system prompt to begin with).
//
// Closed-set discipline:
//   - System block enumerates ONLY canonical SkillCode strings ("math.x.y").
//   - Track-filtered when ExtractionInput.TrackHint is supplied (5u/4u/3u);
//     reduces token count + noise, and prevents the LLM from suggesting
//     leaves that don't exist at the student's track.
//   - "Return ONLY codes from this list" is a non-negotiable instruction
//     repeated twice.
//   - Tool schema constrains the response shape (primary + supporting,
//     each with skillCode/rationale/confidence) so we don't parse free-form
//     JSON from the model.
// =============================================================================

using System.Text;
using Cena.Actors.Mastery;

namespace Cena.Admin.Api.Mastery.Extraction;

internal static class HybridConceptExtractorPrompt
{
    /// <summary>
    /// System-prompt body for the LLM tier. Enumerates the closed-set
    /// catalog (filtered by TrackHint when given). Cached at the
    /// Anthropic edge via <c>cache_control: { type: "ephemeral" }</c> on
    /// the TextBlockParam, so the per-call token cost is dominated by
    /// the variable user message.
    ///
    /// Returns the system block text. Empty leaves list is permitted —
    /// the caller already early-returns rules output before invoking
    /// the LLM, so this method is only called when we intend to ask
    /// the model to pick from a non-empty set. We still defend against
    /// it: an empty list yields a system prompt that says "the catalog
    /// is empty; return no concepts" rather than producing an unbounded
    /// generation.
    /// </summary>
    public static string BuildSystemPrompt(
        IReadOnlyList<BagrutTaxonomyCatalog.LeafEntry> leaves,
        string? trackHint)
    {
        // De-duplicate by SkillCode value (catalog has multi-track buckets;
        // ADR-0050 collapses 3u/4u/5u to one SkillCode for the same skill,
        // so feeding the LLM duplicates wastes tokens).
        var skillCodes = leaves
            .Select(l => l.SkillCode.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder(2048);
        sb.Append("You are a concept-tagger for Israeli Bagrut math questions. ");
        sb.Append("Given the question's prompt + LaTeX, identify the SINGLE primary skill ");
        sb.Append("and zero-to-four supporting skills the question exercises. ");
        sb.Append("Return ONLY codes from the closed-set catalog below — never invent codes, ");
        sb.Append("never paraphrase. If no catalog leaf matches, omit the field (return zero ");
        sb.Append("concepts rather than guessing).");
        sb.AppendLine();
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(trackHint))
        {
            sb.Append("Track context: ").Append(trackHint).Append('.');
            sb.Append(" The catalog below has been filtered to skills that exist at this track; ");
            sb.AppendLine("do not pick anything outside it.");
            sb.AppendLine();
        }

        sb.AppendLine("Catalog (canonical SkillCode strings — copy verbatim):");
        if (skillCodes.Count == 0)
        {
            sb.AppendLine("  (catalog is empty — return zero concepts)");
        }
        else
        {
            foreach (var code in skillCodes)
            {
                sb.Append("  - ").AppendLine(code);
            }
        }

        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("  1. The primary skill is the ONE concept the question is most directly testing.");
        sb.AppendLine("  2. Supporting skills are prerequisites or adjacent skills the question also exercises.");
        sb.AppendLine("     Cap the supporting list at 4. Omit when uncertain — false positives pollute mastery posteriors.");
        sb.AppendLine("  3. Confidence is your self-reported certainty in [0, 1]. Use < 0.5 for guesses.");
        sb.AppendLine("  4. Rationale is one short sentence explaining the pick — the curator reads it.");
        sb.AppendLine("  5. Return ONLY codes from the catalog above. Never invent.");
        return sb.ToString();
    }

    /// <summary>
    /// User-prompt body. The variable per-call payload — prompt + LaTeX
    /// from the question. Kept short so token cost stays low (the
    /// closed-set catalog in the system block is the dominant input).
    /// </summary>
    public static string BuildUserPrompt(string? prompt, string? latex)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("Question prompt:");
        sb.AppendLine(string.IsNullOrWhiteSpace(prompt) ? "(empty)" : prompt!.Trim());
        sb.AppendLine();
        sb.AppendLine("Question LaTeX (math content):");
        sb.AppendLine(string.IsNullOrWhiteSpace(latex) ? "(empty)" : latex!.Trim());
        sb.AppendLine();
        sb.Append("Identify the primary + supporting skills using the catalog from the system instructions. ");
        sb.Append("Call the tag_question_concepts tool exactly once.");
        return sb.ToString();
    }
}
