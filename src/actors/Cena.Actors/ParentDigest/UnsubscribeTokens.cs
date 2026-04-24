// =============================================================================
// Cena Platform — Unsubscribe Token signing + verification (prr-051).
//
// Every parent digest (email or SMS) carries a one-click unsubscribe link of
// the shape  /unsubscribe/{token}  where <token> is an HMAC-signed payload
// containing: parent_id, student_id, institute_id, expiry, nonce.
//
// Token format (URL-safe base64, single segment so it survives SMS 160-char
// shortening without escaping):
//
//     <payload-b64url>.<sig-b64url>
//
// where the payload is a pipe-separated record:
//
//     v1|<parent_id>|<student_id>|<institute_id>|<issued_unix>|<expires_unix>|<nonce>
//
// Design decisions:
//
//   1. HMAC-SHA256 with a server-side secret — asymmetric signatures
//      would let clients pre-compute unsubscribes once they leaked a
//      public key; HMAC is recoverable from ops + cheap.
//
//   2. Payload is NOT encrypted. Token recipients are the parents whose
//      own identifiers are in the payload; there is no privacy leak
//      from carrying their (already anon) id inside a link they own.
//      Keeping the payload plaintext lets ops read a failed token to
//      understand why without unrolling a decrypt step.
//
//   3. Nonce is stored in an IUnsubscribeTokenNonceStore and atomically
//      consumed on first use. A second click surfaces `AlreadyUsed`
//      from the verifier — the endpoint maps that to an idempotent 200
//      (the user already unsubscribed; don't surprise them with 400).
//
//   4. Expiry is short (default 14 days). A parent who ignored the link
//      for two weeks can still unsubscribe — via the preferences API or
//      a new link in the next digest. We do NOT honor expired tokens
//      because an ancient leaked token is strictly worse than forcing
//      the parent to click a fresh one.
//
//   5. Tenant cross is caught at verification time: the token's
//      <institute_id> must equal the current request's resolved tenant.
//      A parent at institute X can't unsubscribe a pair at institute Y
//      even if they somehow obtained a token issued for Y.
//
//   6. Secret is sourced from configuration key
//      <c>Cena:ParentDigest:UnsubscribeHmacSecret</c> at DI registration
//      time. A missing secret in production throws at startup; a missing
//      secret in test fixtures falls back to a deterministic dev key so
//      tests do not have to wire configuration every time.
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Decoded unsubscribe-token payload.
/// </summary>
public sealed record UnsubscribeTokenPayload(
    string ParentActorId,
    string StudentSubjectId,
    string InstituteId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Nonce);

/// <summary>
/// Verification result. Distinct cases so the endpoint can pick the right
/// HTTP response:
///   - Valid: consume nonce, apply unsubscribe, 200.
///   - Expired: 410 (Gone) — parent should click a fresh link.
///   - AlreadyUsed: idempotent 200 — treat as "already unsubscribed".
///   - Tampered / Malformed: 403 (no error detail).
/// </summary>
public enum UnsubscribeTokenOutcome
{
    Valid = 0,
    Expired = 1,
    AlreadyUsed = 2,
    Tampered = 3,
    Malformed = 4,
    CrossTenant = 5,
}

/// <summary>
/// Composite verification result. <see cref="Payload"/> is only non-null
/// when the token parsed cleanly (even for Expired / AlreadyUsed /
/// CrossTenant) — Tampered and Malformed always return null payload so
/// the endpoint doesn't accidentally log a forged id.
/// </summary>
public sealed record UnsubscribeTokenVerification(
    UnsubscribeTokenOutcome Outcome,
    UnsubscribeTokenPayload? Payload,
    string Fingerprint);

/// <summary>
/// Tiny nonce table. Implemented in-memory; a Marten-backed variant can
/// drop in without changing the verifier. Entries older than the token
/// expiry are pruned by callers on write (no background sweep).
/// </summary>
public interface IUnsubscribeTokenNonceStore
{
    /// <summary>
    /// Atomically consume the nonce. Returns true iff this is the first
    /// time the nonce was seen; subsequent calls return false so the
    /// verifier can surface <see cref="UnsubscribeTokenOutcome.AlreadyUsed"/>.
    /// </summary>
    Task<bool> TryConsumeAsync(
        string nonce,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Peek without consuming — used by endpoint tests that want to verify
    /// a valid signature independently of the nonce state.
    /// </summary>
    Task<bool> ContainsConsumedAsync(string nonce, CancellationToken ct = default);
}

/// <summary>
/// HMAC-signed token encoder + decoder. Stateless; nonce uniqueness is
/// tracked by <see cref="IUnsubscribeTokenNonceStore"/>.
/// </summary>
public interface IUnsubscribeTokenService
{
    /// <summary>
    /// Produce a signed token for the supplied pair. Caller picks the expiry.
    /// </summary>
    string Issue(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset nowUtc,
        TimeSpan lifetime,
        string? nonceOverride = null);

    /// <summary>
    /// Verify + consume a token. The verifier consults the nonce store;
    /// a successful verification atomically marks the nonce as consumed
    /// so a second click returns <see cref="UnsubscribeTokenOutcome.AlreadyUsed"/>.
    /// </summary>
    Task<UnsubscribeTokenVerification> VerifyAndConsumeAsync(
        string token,
        string expectedInstituteId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Compute the short fingerprint (first 8 hex chars of the HMAC sig)
    /// used for the audit event. Exposed so callers can derive a fingerprint
    /// without re-signing.
    /// </summary>
    string FingerprintOf(string token);
}

/// <summary>
/// Default implementation.
/// </summary>
public sealed class UnsubscribeTokenService : IUnsubscribeTokenService
{
    private const string VersionTag = "v1";

    private readonly byte[] _secret;
    private readonly IUnsubscribeTokenNonceStore _nonces;

    public UnsubscribeTokenService(string hmacSecret, IUnsubscribeTokenNonceStore nonces)
    {
        if (string.IsNullOrWhiteSpace(hmacSecret))
            throw new ArgumentException(
                "HMAC secret is required — configure Cena:ParentDigest:UnsubscribeHmacSecret.",
                nameof(hmacSecret));
        if (hmacSecret.Length < 16)
            throw new ArgumentException(
                "HMAC secret must be at least 16 characters.",
                nameof(hmacSecret));

        _secret = Encoding.UTF8.GetBytes(hmacSecret);
        _nonces = nonces ?? throw new ArgumentNullException(nameof(nonces));
    }

    public string Issue(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset nowUtc,
        TimeSpan lifetime,
        string? nonceOverride = null)
    {
        if (string.IsNullOrWhiteSpace(parentActorId))
            throw new ArgumentException("parentActorId required", nameof(parentActorId));
        if (string.IsNullOrWhiteSpace(studentSubjectId))
            throw new ArgumentException("studentSubjectId required", nameof(studentSubjectId));
        if (string.IsNullOrWhiteSpace(instituteId))
            throw new ArgumentException("instituteId required", nameof(instituteId));
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentException("lifetime must be positive", nameof(lifetime));

        var nonce = string.IsNullOrWhiteSpace(nonceOverride)
            ? NewNonce()
            : nonceOverride;
        var issuedUnix = nowUtc.ToUnixTimeSeconds();
        var expiresUnix = nowUtc.Add(lifetime).ToUnixTimeSeconds();

        // Reject identifiers with our delimiter — we don't want to
        // silently corrupt the token if a future id format contains '|'.
        AssertNoDelimiter(parentActorId, nameof(parentActorId));
        AssertNoDelimiter(studentSubjectId, nameof(studentSubjectId));
        AssertNoDelimiter(instituteId, nameof(instituteId));
        AssertNoDelimiter(nonce, nameof(nonceOverride));

        var payload = string.Join('|',
            VersionTag,
            parentActorId,
            studentSubjectId,
            instituteId,
            issuedUnix.ToString(System.Globalization.CultureInfo.InvariantCulture),
            expiresUnix.ToString(System.Globalization.CultureInfo.InvariantCulture),
            nonce);

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var sigBytes = Hmac(payloadBytes);

        var payloadEncoded = Base64UrlEncode(payloadBytes);
        var sigEncoded = Base64UrlEncode(sigBytes);
        return payloadEncoded + "." + sigEncoded;
    }

    public async Task<UnsubscribeTokenVerification> VerifyAndConsumeAsync(
        string token,
        string expectedInstituteId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Malformed, null, string.Empty);
        if (string.IsNullOrWhiteSpace(expectedInstituteId))
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Malformed, null, string.Empty);

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Malformed, null, string.Empty);

        byte[] payloadBytes;
        byte[] suppliedSig;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            suppliedSig = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Malformed, null, string.Empty);
        }

        // Constant-time compare on the signature — a timing-leak here
        // would let an attacker distinguish a "valid-shape-bad-sig" token
        // from a "garbled-shape" token and iteratively recover the HMAC.
        var expectedSig = Hmac(payloadBytes);
        var fingerprint = FingerprintOfSig(suppliedSig);
        if (!CryptographicOperations.FixedTimeEquals(suppliedSig, expectedSig))
        {
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Tampered, null, fingerprint);
        }

        var payloadString = Encoding.UTF8.GetString(payloadBytes);
        var fields = payloadString.Split('|');
        if (fields.Length != 7 || fields[0] != VersionTag)
        {
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Malformed, null, fingerprint);
        }

        if (!long.TryParse(fields[4],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var issuedUnix) ||
            !long.TryParse(fields[5],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var expiresUnix))
        {
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Malformed, null, fingerprint);
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedUnix);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);

        var payload = new UnsubscribeTokenPayload(
            ParentActorId: fields[1],
            StudentSubjectId: fields[2],
            InstituteId: fields[3],
            IssuedAtUtc: issuedAt,
            ExpiresAtUtc: expiresAt,
            Nonce: fields[6]);

        // Tenant match — block before nonce consumption so a cross-tenant
        // probe can't burn a legitimate parent's nonce.
        if (!string.Equals(payload.InstituteId, expectedInstituteId, StringComparison.Ordinal))
        {
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.CrossTenant, payload, fingerprint);
        }

        if (nowUtc >= expiresAt)
        {
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.Expired, payload, fingerprint);
        }

        // AlreadyUsed check via the nonce store. We check-then-consume
        // atomically so a concurrent double-click returns exactly one
        // Valid and one AlreadyUsed.
        var firstUse = await _nonces
            .TryConsumeAsync(payload.Nonce, expiresAt, ct)
            .ConfigureAwait(false);
        if (!firstUse)
        {
            return new UnsubscribeTokenVerification(
                UnsubscribeTokenOutcome.AlreadyUsed, payload, fingerprint);
        }

        return new UnsubscribeTokenVerification(
            UnsubscribeTokenOutcome.Valid, payload, fingerprint);
    }

    public string FingerprintOf(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        var parts = token.Split('.', 2);
        if (parts.Length != 2) return string.Empty;
        try
        {
            var sig = Base64UrlDecode(parts[1]);
            return FingerprintOfSig(sig);
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string FingerprintOfSig(byte[] sig)
        => sig.Length == 0
            ? string.Empty
            : Convert.ToHexString(sig, 0, Math.Min(4, sig.Length)).ToLowerInvariant();

    private byte[] Hmac(byte[] payload)
    {
        using var h = new HMACSHA256(_secret);
        return h.ComputeHash(payload);
    }

    private static string NewNonce()
    {
        // 96 bits — plenty to ensure uniqueness across the lifetime of a
        // single-use token and short enough to keep the URL under SMS
        // length caps.
        var buf = new byte[12];
        RandomNumberGenerator.Fill(buf);
        return Base64UrlEncode(buf);
    }

    private static void AssertNoDelimiter(string value, string param)
    {
        if (value.Contains('|'))
            throw new ArgumentException(
                $"'{param}' must not contain the '|' delimiter.",
                param);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var b64 = Convert.ToBase64String(bytes);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string s)
    {
        if (string.IsNullOrEmpty(s)) throw new FormatException("empty token segment");
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 0: break;
            default: throw new FormatException("invalid token length");
        }
        return Convert.FromBase64String(b64);
    }
}

/// <summary>
/// Thread-safe in-memory nonce store. Suitable for single-process test
/// fixtures and dev. Production drops in a Marten-backed variant that
/// shares state across the admin API replicas; the interface is unchanged.
/// </summary>
public sealed class InMemoryUnsubscribeTokenNonceStore : IUnsubscribeTokenNonceStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _nonces = new();

    public Task<bool> TryConsumeAsync(
        string nonce,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return Task.FromResult(false);
        // Opportunistic eviction of any entries whose expiry has passed.
        // Keeps the map bounded without a background sweep.
        PruneExpired(expiresAtUtc);
        var added = _nonces.TryAdd(nonce, expiresAtUtc);
        return Task.FromResult(added);
    }

    public Task<bool> ContainsConsumedAsync(string nonce, CancellationToken ct = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(nonce) && _nonces.ContainsKey(nonce));

    private void PruneExpired(DateTimeOffset reference)
    {
        foreach (var (key, exp) in _nonces)
        {
            if (exp <= reference.Subtract(TimeSpan.FromDays(1)))
            {
                // Only prune entries expired more than 24h before the
                // reference — keeps recently-expired entries around so
                // a late-but-within-grace click still shows AlreadyUsed.
                _nonces.TryRemove(key, out _);
            }
        }
    }
}
