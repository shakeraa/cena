// =============================================================================
// Cena Platform -- Tutor Safety Guard (SAI-08 / SEC-004)
// Rule-based validation of tutor responses. Synchronous, no LLM calls.
// Extended with Hebrew and Arabic prompt-injection detection (SEC-004).
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tutoring;

/// <summary>
/// Validates tutor LLM responses before sending to the student.
/// Synchronous and fast -- no LLM calls, pure rule checks.
/// </summary>
public interface ITutorSafetyGuard
{
    SafetyResult Validate(string tutorResponse, string subject, string conceptName);
}

/// <summary>
/// Result of safety validation.
/// </summary>
public sealed record SafetyResult(bool IsAllowed, string? BlockReason);

public sealed class TutorSafetyGuard : ITutorSafetyGuard
{
    private const int MaxResponseLength = 2000;

    // Patterns that suggest the tutor is leaking a direct answer
    // rather than guiding the student
    private static readonly Regex[] AnswerLeakPatterns =
    [
        new(@"(?i)\bthe\s+(?:correct\s+)?answer\s+is\b", RegexOptions.Compiled),
        new(@"(?i)\bthe\s+solution\s+is\b", RegexOptions.Compiled),
        new(@"(?i)\bthe\s+result\s+(?:is|equals)\b", RegexOptions.Compiled)
    ];

    // ── SEC-004: Prompt injection detection ──
    // English injection phrases (case-insensitive ordinal comparison via ToLowerInvariant)
    private static readonly string[] BlockedPhrasesEnglish =
    [
        "ignore previous instructions",
        "ignore all instructions",
        "disregard previous instructions",
        "disregard all instructions",
        "forget previous instructions",
        "system prompt",
        "you are now",
        "output json",
        "output: is_correct",
        "as an ai language model",
        "as an ai assistant",
        "new instructions:",
        "override instructions"
    ];

    // Hebrew injection patterns — exact substring matches on the original text.
    // These are the canonical transliterations of common injection attempts.
    private static readonly string[] BlockedPhrasesHebrew =
    [
        "התעלם מהוראות קודמות",   // "ignore previous instructions"
        "התעלם מכל ההוראות",       // "ignore all instructions"
        "אתה עכשיו",               // "you are now"
        "תדפיס",                   // "print" (output directive)
        "הפלט חייב להיות",         // "the output must be"
        "הוראות חדשות",            // "new instructions"
        "בטל הוראות",              // "cancel instructions"
        "התעלם מההוראות"           // "ignore the instructions"
    ];

    // Arabic injection patterns — exact substring matches on the original text.
    private static readonly string[] BlockedPhrasesArabic =
    [
        "تجاهل التعليمات السابقة",  // "ignore previous instructions"
        "تجاهل جميع التعليمات",     // "ignore all instructions"
        "أنت الآن",                 // "you are now"
        "اطبع النتيجة",             // "print the result"
        "يجب أن يكون الناتج",       // "the output must be"
        "تعليمات جديدة",            // "new instructions"
        "ألغ التعليمات"             // "cancel instructions"
    ];

    public SafetyResult Validate(string tutorResponse, string subject, string conceptName)
    {
        if (string.IsNullOrWhiteSpace(tutorResponse))
            return new SafetyResult(false, "Empty response");

        if (tutorResponse.Length > MaxResponseLength)
            return new SafetyResult(false, $"Response exceeds {MaxResponseLength} character limit ({tutorResponse.Length} chars)");

        // Check English blocked phrases (case-insensitive via lower-cased comparison)
        var lowerResponse = tutorResponse.ToLowerInvariant();
        foreach (var phrase in BlockedPhrasesEnglish)
        {
            if (lowerResponse.Contains(phrase))
                return new SafetyResult(false, $"Response contains blocked phrase: '{phrase}'");
        }

        // Check Hebrew injection patterns (Unicode-aware, case is not relevant for Hebrew)
        foreach (var phrase in BlockedPhrasesHebrew)
        {
            if (tutorResponse.Contains(phrase, StringComparison.Ordinal))
                return new SafetyResult(false, $"Response contains Hebrew injection pattern: '{phrase}'");
        }

        // Check Arabic injection patterns
        foreach (var phrase in BlockedPhrasesArabic)
        {
            if (tutorResponse.Contains(phrase, StringComparison.Ordinal))
                return new SafetyResult(false, $"Response contains Arabic injection pattern: '{phrase}'");
        }

        // Check for direct answer leaking patterns
        foreach (var pattern in AnswerLeakPatterns)
        {
            if (pattern.IsMatch(tutorResponse))
                return new SafetyResult(false, "Response appears to leak a direct answer");
        }

        return new SafetyResult(true, null);
    }
}
