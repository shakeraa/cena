// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Message Classifier
// Layer: Domain Service | Runtime: .NET 9
// Stateless, deterministic classifier that routes inbound messages to
// either the concept graph (learning signals) or message store (chat).
// No LLM call — pure regex + heuristic rules.
// ═══════════════════════════════════════════════════════════════════════

using System.Text.RegularExpressions;

namespace Cena.Actors.Messaging;

public interface IMessageClassifier
{
    ClassificationResult Classify(string text, string locale = "en");
}

public sealed partial class MessageClassifier : IMessageClassifier
{
    // ── Compiled regexes for hot-path classification ──

    [GeneratedRegex(@"^\s*-?\d+(\.\d+)?\s*$", RegexOptions.Compiled)]
    private static partial Regex NumericPattern();

    [GeneratedRegex(@"^\s*[a-dA-D]\s*$", RegexOptions.Compiled)]
    private static partial Regex SingleLetterPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // Confirmation words in English, Hebrew, Arabic
    private static readonly HashSet<string> ConfirmationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "no", "yeah", "nah", "yep", "nope",
        "כן", "לא", "כ", // Hebrew
        "نعم", "لا", // Arabic
    };

    // Question starters across languages
    private static readonly string[] QuestionStartersEn =
    {
        "what is", "what are", "how do", "how does", "how can",
        "explain", "why is", "why does", "can you explain"
    };

    private static readonly string[] QuestionStartersHe =
    {
        "מה זה", "מה הם", "איך", "למה", "הסבר", "תסביר"
    };

    private static readonly string[] QuestionStartersAr =
    {
        "ما هو", "ما هي", "كيف", "لماذا", "اشرح", "فسّر"
    };

    // Greeting patterns
    private static readonly string[] GreetingPatterns =
    {
        "good morning", "good evening", "hello", "hi ", "hey",
        "שלום", "בוקר טוב", "ערב טוב",
        "مرحبا", "صباح الخير", "مساء الخير",
    };

    // Educational URL allowlist domains
    private static readonly string[] AllowlistedDomains =
    {
        "youtube.com", "youtu.be", "khanacademy.org",
        "desmos.com", "geogebra.org", "wikipedia.org"
    };

    public ClassificationResult Classify(string text, string locale = "en")
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ClassificationResult(false, "general", 1.0);

        var trimmed = text.Trim();

        // 1. Pure numeric answer (highest confidence)
        if (NumericPattern().IsMatch(trimmed))
            return new ClassificationResult(true, "quiz-answer", 0.95);

        // 2. Single letter a-d (multiple choice answer)
        if (SingleLetterPattern().IsMatch(trimmed))
            return new ClassificationResult(true, "quiz-answer", 0.90);

        // 3. Confirmation word (exact match)
        if (ConfirmationWords.Contains(trimmed))
            return new ClassificationResult(true, "confirmation", 0.85);

        // 4. Concept question (starts with question pattern)
        var lower = trimmed.ToLowerInvariant();
        if (IsQuestionStarter(lower))
            return new ClassificationResult(true, "concept-question", 0.75);

        // 5. URL detected → communication (resource sharing)
        if (UrlPattern().IsMatch(trimmed))
            return new ClassificationResult(false, "resource-share", 0.95);

        // 6. Greeting pattern
        if (IsGreeting(lower, trimmed))
            return new ClassificationResult(false, "greeting", 0.90);

        // 7. Default: general communication
        return new ClassificationResult(false, "general", 1.0);
    }

    private static bool IsQuestionStarter(string lower)
    {
        foreach (var starter in QuestionStartersEn)
            if (lower.StartsWith(starter, StringComparison.Ordinal))
                return true;

        foreach (var starter in QuestionStartersHe)
            if (lower.StartsWith(starter, StringComparison.Ordinal))
                return true;

        foreach (var starter in QuestionStartersAr)
            if (lower.StartsWith(starter, StringComparison.Ordinal))
                return true;

        return false;
    }

    private static bool IsGreeting(string lower, string original)
    {
        foreach (var greeting in GreetingPatterns)
        {
            if (lower.StartsWith(greeting, StringComparison.Ordinal))
                return true;
            if (original.StartsWith(greeting, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
