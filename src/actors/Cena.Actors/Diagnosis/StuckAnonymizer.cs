// =============================================================================
// Cena Platform — StuckAnonymizer (RDY-063 Phase 1)
//
// HMAC-SHA256(studentId || sessionId || salt) → 16-char hex prefix.
// - Stable within a session (dedupe) but not across sessions (can't
//   rebuild cross-session history from the anon id alone).
// - Not reversible without the salt; the salt lives in secret config.
// - Salt rotation per environment cycles the anon id, which is the
//   desired property for ADR-0003 (misconception labels lose their
//   linkage to a student on salt rotation).
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.Diagnosis;

public interface IStuckAnonymizer
{
    /// <summary>
    /// Returns an anon id that is stable for (studentId, sessionId) under
    /// the current salt, but unrecoverable outside that tuple.
    /// </summary>
    string Anonymize(string studentId, string sessionId);
}

public sealed class StuckAnonymizer : IStuckAnonymizer
{
    private readonly byte[] _saltBytes;

    public StuckAnonymizer(string salt)
    {
        if (string.IsNullOrEmpty(salt))
            throw new ArgumentException("Anon salt must be non-empty.", nameof(salt));
        _saltBytes = Encoding.UTF8.GetBytes(salt);
    }

    public string Anonymize(string studentId, string sessionId)
    {
        if (string.IsNullOrEmpty(studentId)) throw new ArgumentException("studentId required", nameof(studentId));
        if (string.IsNullOrEmpty(sessionId)) throw new ArgumentException("sessionId required", nameof(sessionId));

        Span<byte> input = stackalloc byte[Encoding.UTF8.GetByteCount(studentId) +
                                           Encoding.UTF8.GetByteCount(sessionId) + 1];
        int written = Encoding.UTF8.GetBytes(studentId, input);
        input[written++] = (byte)'|';
        Encoding.UTF8.GetBytes(sessionId, input[written..]);

        Span<byte> hash = stackalloc byte[32];
        int hashLen = HMACSHA256.HashData(_saltBytes, input, hash);

        // 8 bytes = 16 hex chars, enough to collide-resist within a
        // 30-day retention window at 450 diagnoses/week/school.
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
