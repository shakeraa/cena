// =============================================================================
// Cena Platform -- Input Sanitizer (SEC-004)
// Central sanitization helpers applied at all user-input boundaries.
// No external dependencies — pure string operations, safe to call on hot paths.
// =============================================================================

namespace Cena.Infrastructure.Security;

/// <summary>
/// Sanitizes user-supplied strings at system boundaries to prevent injection attacks.
/// All methods are pure and allocate a new string; they never throw on null input.
/// </summary>
public static class InputSanitizer
{
    /// <summary>
    /// Sanitizes a free-text search query: trims whitespace, removes control characters,
    /// and caps length. Safe to embed in ILIKE patterns via parameterized queries.
    /// </summary>
    /// <param name="input">Raw search string from the request.</param>
    /// <param name="maxLength">Maximum accepted character count (default 200).</param>
    public static string SanitizeSearchQuery(string? input, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove control characters but preserve regular space (0x20) and common Unicode
        var sanitized = new string(
            input.Take(maxLength)
                 .Where(c => !char.IsControl(c) || c == ' ')
                 .ToArray()).Trim();

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a file name to prevent directory traversal and path injection.
    /// Returns only the file name component, stripping path separators and ".." sequences.
    /// </summary>
    /// <param name="fileName">Raw file name from the upload form.</param>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";

        // Path.GetFileName already strips directory components on the current OS;
        // the Replace calls handle cross-platform edge cases.
        return Path.GetFileName(fileName)
            .Replace("..", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sanitizes identifier strings (student IDs, session IDs, school IDs).
    /// Allows only alphanumeric characters, hyphens, and underscores — the character
    /// set used by UUIDs, slugs, and Firebase UIDs.
    /// </summary>
    /// <param name="id">Raw identifier string from the request.</param>
    public static string SanitizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        return new string(
            id.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
    }

    /// <summary>
    /// Sanitizes a NATS subject component (single token between dots).
    /// Allows alphanumeric, hyphens, underscores, and dots only.
    /// Prevents subject injection via wildcard characters ("*", ">").
    /// </summary>
    /// <param name="subject">Raw subject or subject segment.</param>
    /// <param name="maxLength">Maximum length (default 128).</param>
    public static string SanitizeNatsSubject(string? subject, int maxLength = 128)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return string.Empty;

        return new string(
            subject.Take(maxLength)
                   .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                   .ToArray());
    }

    /// <summary>
    /// Sanitizes an annotation or short free-text field.
    /// Removes control characters, caps length to prevent oversized payloads.
    /// </summary>
    /// <param name="text">Raw free-text from the user.</param>
    /// <param name="maxLength">Maximum accepted character count (default 5000).</param>
    public static string SanitizeFreeText(string? text, int maxLength = 5000)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sanitized = new string(
            text.Take(maxLength)
                .Where(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
                .ToArray()).Trim();

        return sanitized;
    }
}
