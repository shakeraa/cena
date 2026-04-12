// =============================================================================
// Cena Platform -- Tutor Prompt Scrubber (FIND-privacy-008)
// PII scrubbing middleware for outbound LLM prompts.
//
// Strips student-identifiable data (names, emails, phone numbers, school
// names, addresses) from student free-text BEFORE sending to Anthropic.
// Replaces each match with a category-specific redaction placeholder so the
// LLM still receives coherent text.
//
// This runs on the hot path so we pre-compile all regexes and cache the
// per-student pattern set.
// =============================================================================

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Tutor;

/// <summary>
/// PII that the scrubber knows about for a single student context.
/// Populated from the student profile at the start of a tutor session.
/// </summary>
public sealed record StudentPiiContext(
    string StudentId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? SchoolName,
    string? ParentName,
    string? City);

/// <summary>
/// Result of a PII-scrubbing pass on a single input string.
/// </summary>
public sealed record ScrubResult(
    string ScrubbedText,
    int RedactionCount,
    IReadOnlyList<string> RedactionCategories);

/// <summary>
/// Scrubs PII from student free-text before it is sent to an external LLM.
/// </summary>
public interface ITutorPromptScrubber
{
    /// <summary>
    /// Scrub known student PII and common PII patterns (phone numbers, emails)
    /// from <paramref name="rawInput"/>. Returns the scrubbed text and metadata.
    /// </summary>
    ScrubResult Scrub(string rawInput, StudentPiiContext studentPii);
}

public sealed class TutorPromptScrubber : ITutorPromptScrubber
{
    private readonly ILogger<TutorPromptScrubber> _logger;

    // ── Generic PII patterns (compiled once, thread-safe) ──────────────────

    // Email addresses
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    // Phone numbers: international and local formats
    // Covers: +972-54-1234567, (054) 123-4567, 054-1234567, +1 (555) 123-4567
    private static readonly Regex PhonePattern = new(
        @"(?:\+?\d{1,3}[\s\-]?)?\(?\d{2,4}\)?[\s\-]?\d{3,4}[\s\-]?\d{3,4}",
        RegexOptions.Compiled);

    // Israeli ID numbers (9 digits, possibly with leading zero)
    private static readonly Regex IsraeliIdPattern = new(
        @"\b\d{9}\b",
        RegexOptions.Compiled);

    // Street addresses: number + street name pattern
    private static readonly Regex AddressPattern = new(
        @"\b\d{1,5}\s+(?:[A-Za-z\u0590-\u05FF\u0600-\u06FF]+\s*){1,5}(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Way|Place|Pl|Court|Ct|רחוב|שדרות|דרך|شارع|طريق)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Postal codes: IL (7 digits), US (5 or 5+4), UK (letter-digit mix)
    private static readonly Regex PostalCodePattern = new(
        @"\b(?:\d{5}(?:\-\d{4})?|\d{7}|[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2})\b",
        RegexOptions.Compiled);

    public TutorPromptScrubber(ILogger<TutorPromptScrubber> logger)
    {
        _logger = logger;
    }

    public ScrubResult Scrub(string rawInput, StudentPiiContext studentPii)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return new ScrubResult(rawInput, 0, Array.Empty<string>());

        var text = rawInput;
        var categories = new List<string>();
        int redactionCount = 0;

        // ── 1. Student-specific PII (exact matches, case-insensitive) ──

        // Full name (last + first together, or separately)
        if (!string.IsNullOrWhiteSpace(studentPii.LastName))
        {
            // Full name first (more specific)
            if (!string.IsNullOrWhiteSpace(studentPii.FirstName))
            {
                var fullName = $"{studentPii.FirstName} {studentPii.LastName}";
                var (t1, c1) = ReplaceIgnoreCase(text, fullName, "<redacted:name>");
                if (c1 > 0) { text = t1; redactionCount += c1; categories.Add("name"); }
            }
            var (t2, c2) = ReplaceIgnoreCase(text, studentPii.LastName, "<redacted:name>");
            if (c2 > 0) { text = t2; redactionCount += c2; if (!categories.Contains("name")) categories.Add("name"); }
        }

        if (!string.IsNullOrWhiteSpace(studentPii.FirstName) && studentPii.FirstName.Length >= 3)
        {
            // Only scrub first name if it's 3+ characters to avoid false positives
            // on short common words that happen to match a name
            var (t3, c3) = ReplaceIgnoreCase(text, studentPii.FirstName, "<redacted:name>");
            if (c3 > 0) { text = t3; redactionCount += c3; if (!categories.Contains("name")) categories.Add("name"); }
        }

        if (!string.IsNullOrWhiteSpace(studentPii.Email))
        {
            var (t4, c4) = ReplaceIgnoreCase(text, studentPii.Email, "<redacted:email>");
            if (c4 > 0) { text = t4; redactionCount += c4; categories.Add("email"); }
        }

        if (!string.IsNullOrWhiteSpace(studentPii.SchoolName))
        {
            var (t5, c5) = ReplaceIgnoreCase(text, studentPii.SchoolName, "<redacted:school>");
            if (c5 > 0) { text = t5; redactionCount += c5; categories.Add("school"); }
        }

        if (!string.IsNullOrWhiteSpace(studentPii.ParentName))
        {
            var (t6, c6) = ReplaceIgnoreCase(text, studentPii.ParentName, "<redacted:parent>");
            if (c6 > 0) { text = t6; redactionCount += c6; categories.Add("parent"); }
        }

        if (!string.IsNullOrWhiteSpace(studentPii.City))
        {
            var (t7, c7) = ReplaceIgnoreCase(text, studentPii.City, "<redacted:location>");
            if (c7 > 0) { text = t7; redactionCount += c7; categories.Add("location"); }
        }

        // ── 2. Generic PII patterns ──

        // Emails not already scrubbed by the known-email pass
        var (t8, c8) = ReplaceRegex(text, EmailPattern, "<redacted:email>");
        if (c8 > 0) { text = t8; redactionCount += c8; if (!categories.Contains("email")) categories.Add("email"); }

        // Phone numbers
        var (t9, c9) = ReplaceRegex(text, PhonePattern, "<redacted:phone>");
        if (c9 > 0) { text = t9; redactionCount += c9; categories.Add("phone"); }

        // Addresses
        var (t10, c10) = ReplaceRegex(text, AddressPattern, "<redacted:address>");
        if (c10 > 0) { text = t10; redactionCount += c10; categories.Add("address"); }

        // Postal codes
        var (t11, c11) = ReplaceRegex(text, PostalCodePattern, "<redacted:postal>");
        if (c11 > 0) { text = t11; redactionCount += c11; if (!categories.Contains("address")) categories.Add("address"); }

        // Israeli IDs
        var (t12, c12) = ReplaceRegex(text, IsraeliIdPattern, "<redacted:id>");
        if (c12 > 0) { text = t12; redactionCount += c12; categories.Add("government_id"); }

        if (redactionCount > 0)
        {
            _logger.LogInformation(
                "[PII_SCRUB] redacted {Count} PII items in categories [{Categories}] for student {StudentId}",
                redactionCount,
                string.Join(", ", categories),
                studentPii.StudentId);
        }

        return new ScrubResult(text, redactionCount, categories);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string Result, int Count) ReplaceIgnoreCase(string input, string search, string replacement)
    {
        if (string.IsNullOrEmpty(search))
            return (input, 0);

        int count = 0;
        int idx;
        var result = input;
        while ((idx = result.IndexOf(search, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            result = string.Concat(result.AsSpan(0, idx), replacement, result.AsSpan(idx + search.Length));
            count++;
            // Safety: prevent infinite loop if replacement contains search
            if (count > 100) break;
        }
        return (result, count);
    }

    private static (string Result, int Count) ReplaceRegex(string input, Regex pattern, string replacement)
    {
        int count = 0;
        var result = pattern.Replace(input, _ =>
        {
            count++;
            return replacement;
        });
        return (result, count);
    }
}
