// =============================================================================
// Cena Platform — Bagrut Reference Consent Token Service (ADR-0059 §15.3 / PRR-267)
//
// HMAC-SHA256 token issuer + verifier for the 24h wire token that backs
// the Reference<T> consent invariant. The 90-day functional retention
// lives in the ConsentAggregate event stream (ADR-0042); this service
// is the wire-side primitive that re-issues from that fact.
//
// Token format:
//   - HMAC-SHA256({studentId} || ":" || {context} || ":" || {issuedAt} ||
//                 ":" || {expiresAt}, server-pepper)
//   - Wire payload is the four fields + the HMAC, base64url-encoded as
//     a single ConsentTokenId record. Wire TTL = 24h (ADR-0059 §15.3
//     redteam mitigation).
//
// Server-pepper sourcing:
//   - Production: Cena:Variants:ConsentTokenPepper from secrets store.
//   - Dev: hard-coded fallback with a "dev-only-not-for-prod" marker so
//     ops accidents leak the marker, not a quasi-real key.
//
// Threading: HMACSHA256 instances are not thread-safe; service uses a
// per-call instance which is cheap (HMAC ctor is O(1) once the key is
// already a byte[]). No lock needed.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Content;

namespace Cena.Actors.Consent;

/// <summary>
/// Issuer + verifier for <see cref="ConsentTokenId"/>. Single source of
/// truth for the HMAC binding; downstream code (Reference<T>.From, the
/// /reference endpoints) MUST round-trip through this service rather
/// than constructing tokens directly.
/// </summary>
public interface IBagrutReferenceConsentTokenService
{
    /// <summary>
    /// Issue a fresh wire token for the given student + context.
    /// </summary>
    /// <param name="studentId">Caller-supplied student id (from auth).</param>
    /// <param name="context">Context the token is scoped to.</param>
    /// <param name="now">Clock; pass via TimeProvider in production.</param>
    /// <returns>A signed <see cref="ConsentTokenId"/> with 24h wire TTL.</returns>
    ConsentTokenId Issue(string studentId, ReferenceContextKind context, DateTimeOffset now);

    /// <summary>
    /// Verify a wire token. Returns <c>true</c> iff: (a) the HMAC
    /// matches, (b) the wire TTL has not elapsed, (c) the token's
    /// student id matches the supplied <paramref name="studentId"/>,
    /// (d) the token's context matches <paramref name="context"/>.
    /// </summary>
    bool Verify(
        ConsentTokenId token,
        string studentId,
        ReferenceContextKind context,
        DateTimeOffset now);
}

public sealed class BagrutReferenceConsentTokenService : IBagrutReferenceConsentTokenService
{
    /// <summary>Wire TTL — ADR-0059 §15.3 redteam mitigation.</summary>
    public static readonly TimeSpan WireTtl = TimeSpan.FromHours(24);

    private readonly byte[] _pepper;

    public BagrutReferenceConsentTokenService(string pepper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);
        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    public ConsentTokenId Issue(
        string studentId,
        ReferenceContextKind context,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        var issuedAt = now;
        var expiresAt = now + WireTtl;
        var hmac = ComputeHmac(studentId, context, issuedAt, expiresAt);
        return new ConsentTokenId(studentId, context, issuedAt, expiresAt, hmac);
    }

    public bool Verify(
        ConsentTokenId token,
        string studentId,
        ReferenceContextKind context,
        DateTimeOffset now)
    {
        // Order of checks is deliberate: cheap binding fields first,
        // HMAC last (constant-time compare to avoid timing oracle).
        if (!string.Equals(token.StudentId, studentId, StringComparison.Ordinal)) return false;
        if (token.Context != context) return false;
        if (token.IsExpired(now)) return false;
        var expected = ComputeHmac(studentId, context, token.IssuedAt, token.ExpiresAt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token.TokenHmac),
            Encoding.UTF8.GetBytes(expected));
    }

    private string ComputeHmac(
        string studentId,
        ReferenceContextKind context,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var payload = $"{studentId}:{(int)context}:{issuedAt.ToUnixTimeSeconds()}:{expiresAt.ToUnixTimeSeconds()}";
        using var hmac = new HMACSHA256(_pepper);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(bytes);
    }
}
