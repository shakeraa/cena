// =============================================================================
// Cena Platform -- Email hashing helper for PII-safe logging
// FIND-ux-006b: Never log raw email addresses (PII). Instead, produce a
// short deterministic identifier derived from SHA-256 so operators can still
// correlate events from the same account without the log capturing the
// address itself.
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Cena.Infrastructure.Auth;

/// <summary>
/// Produces stable, non-reversible short identifiers for email addresses so
/// that authentication telemetry (especially on anonymous endpoints such as
/// password-reset) never embeds the raw address. The first 8 hex characters
/// of a normalized SHA-256 digest are enough to correlate events from the
/// same account in a log query without being usable to recover the address.
/// </summary>
public static class EmailHasher
{
    /// <summary>
    /// Returns an 8-character lowercase hex token derived from the trimmed,
    /// lowercased UTF-8 bytes of <paramref name="email"/>. A null or
    /// whitespace input returns the sentinel "none" so callers never need
    /// a second null check when building log scopes.
    /// </summary>
    public static string Hash(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "none";
        }

        var normalized = email.Trim().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var digest = SHA256.HashData(bytes);

        // First 4 bytes → 8 hex chars is enough uniqueness for log correlation
        // without materially changing the cost of the hash for an attacker.
        var sb = new StringBuilder(8);
        for (var i = 0; i < 4; i++)
        {
            sb.Append(digest[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
