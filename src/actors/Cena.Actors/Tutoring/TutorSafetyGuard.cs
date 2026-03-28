// =============================================================================
// Cena Platform -- Tutor Safety Guard (SAI-08)
// Rule-based validation of tutor responses. Synchronous, no LLM calls.
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

    // Content that should never appear in educational tutoring
    private static readonly string[] BlockedPhrases =
    [
        "ignore previous instructions",
        "ignore all instructions",
        "system prompt",
        "as an ai language model"
    ];

    public SafetyResult Validate(string tutorResponse, string subject, string conceptName)
    {
        if (string.IsNullOrWhiteSpace(tutorResponse))
            return new SafetyResult(false, "Empty response");

        if (tutorResponse.Length > MaxResponseLength)
            return new SafetyResult(false, $"Response exceeds {MaxResponseLength} character limit ({tutorResponse.Length} chars)");

        // Check for blocked phrases (prompt injection echoes, AI disclaimers)
        var lowerResponse = tutorResponse.ToLowerInvariant();
        foreach (var phrase in BlockedPhrases)
        {
            if (lowerResponse.Contains(phrase))
                return new SafetyResult(false, $"Response contains blocked phrase: '{phrase}'");
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
