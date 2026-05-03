// =============================================================================
// Cena Platform — UnsubscribeTokens unit tests (prr-051).
//
// Covers:
//   - Issue → verify → Valid on first use; AlreadyUsed on second use.
//   - Tampered signature → Tampered (no payload leaked).
//   - Expired token → Expired.
//   - Cross-tenant token → CrossTenant (detected BEFORE nonce consumption
//     so a malicious probe cannot burn a legit nonce).
//   - Short secret → constructor throws.
//   - Delimiter-containing id → constructor throws (forward compatibility).
// =============================================================================

using Cena.Actors.ParentDigest;

namespace Cena.Actors.Tests.ParentDigest;

public sealed class UnsubscribeTokensTests
{
    private const string Secret = "prr-051-test-secret-at-least-16";
    private const string ParentA = "parent-A";
    private const string ChildA = "child-A";
    private const string InstX = "institute-X";
    private const string InstY = "institute-Y";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 21, 12, 0, 0, TimeSpan.Zero);

    private static (IUnsubscribeTokenService Svc, IUnsubscribeTokenNonceStore Nonces) NewService()
    {
        var nonces = new InMemoryUnsubscribeTokenNonceStore();
        return (new UnsubscribeTokenService(Secret, nonces), nonces);
    }

    [Fact]
    public async Task Issue_Verify_Valid()
    {
        var (svc, _) = NewService();
        var token = svc.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));
        var result = await svc.VerifyAndConsumeAsync(token, InstX, Now.AddMinutes(1));

        Assert.Equal(UnsubscribeTokenOutcome.Valid, result.Outcome);
        Assert.NotNull(result.Payload);
        Assert.Equal(ParentA, result.Payload!.ParentActorId);
        Assert.Equal(ChildA, result.Payload.StudentSubjectId);
        Assert.Equal(InstX, result.Payload.InstituteId);
        Assert.NotEmpty(result.Fingerprint);
    }

    [Fact]
    public async Task SecondClick_ReturnsAlreadyUsed()
    {
        var (svc, _) = NewService();
        var token = svc.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));

        var first = await svc.VerifyAndConsumeAsync(token, InstX, Now.AddMinutes(1));
        var second = await svc.VerifyAndConsumeAsync(token, InstX, Now.AddMinutes(2));

        Assert.Equal(UnsubscribeTokenOutcome.Valid, first.Outcome);
        Assert.Equal(UnsubscribeTokenOutcome.AlreadyUsed, second.Outcome);
    }

    [Fact]
    public async Task TamperedSignature_ReturnsTampered_NoPayloadLeak()
    {
        var (svc, _) = NewService();
        var token = svc.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));

        // Tamper at the byte level. See <see cref="TamperSignature"/> for
        // why a base64url last-char flip is racy (~6.25 % no-op decode).
        var tampered = TamperSignature(token);

        var result = await svc.VerifyAndConsumeAsync(tampered, InstX, Now.AddMinutes(1));
        Assert.Equal(UnsubscribeTokenOutcome.Tampered, result.Outcome);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsExpired()
    {
        var (svc, _) = NewService();
        var token = svc.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromHours(1));

        var result = await svc.VerifyAndConsumeAsync(token, InstX, Now.AddHours(2));
        Assert.Equal(UnsubscribeTokenOutcome.Expired, result.Outcome);
        Assert.NotNull(result.Payload);
    }

    [Fact]
    public async Task CrossTenant_ReturnsCrossTenant_DoesNotConsumeNonce()
    {
        var (svc, nonces) = NewService();
        var token = svc.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));

        // Probe from wrong tenant — should fail BEFORE nonce consumption.
        var probe = await svc.VerifyAndConsumeAsync(token, InstY, Now.AddMinutes(1));
        Assert.Equal(UnsubscribeTokenOutcome.CrossTenant, probe.Outcome);

        // A subsequent legitimate click from the right tenant must still
        // succeed — the malicious probe must not have burned the nonce.
        var legit = await svc.VerifyAndConsumeAsync(token, InstX, Now.AddMinutes(2));
        Assert.Equal(UnsubscribeTokenOutcome.Valid, legit.Outcome);

        // Sanity: nonce IS now consumed.
        Assert.True(await nonces.ContainsConsumedAsync(legit.Payload!.Nonce));
    }

    [Fact]
    public async Task Malformed_ReturnsMalformed()
    {
        var (svc, _) = NewService();

        Assert.Equal(UnsubscribeTokenOutcome.Malformed,
            (await svc.VerifyAndConsumeAsync("", InstX, Now)).Outcome);
        Assert.Equal(UnsubscribeTokenOutcome.Malformed,
            (await svc.VerifyAndConsumeAsync("no-dot", InstX, Now)).Outcome);
        Assert.Equal(UnsubscribeTokenOutcome.Malformed,
            (await svc.VerifyAndConsumeAsync("!!!!.@@@@", InstX, Now)).Outcome);
    }

    [Fact]
    public void ShortSecret_Throws()
    {
        var nonces = new InMemoryUnsubscribeTokenNonceStore();
        Assert.Throws<ArgumentException>(() =>
            new UnsubscribeTokenService("tooshort", nonces));
    }

    [Fact]
    public void PipeCharacterInIds_Rejected()
    {
        var (svc, _) = NewService();
        Assert.Throws<ArgumentException>(() =>
            svc.Issue("parent|pipe", ChildA, InstX, Now, TimeSpan.FromHours(1)));
        Assert.Throws<ArgumentException>(() =>
            svc.Issue(ParentA, "child|pipe", InstX, Now, TimeSpan.FromHours(1)));
        Assert.Throws<ArgumentException>(() =>
            svc.Issue(ParentA, ChildA, "inst|pipe", Now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task FingerprintOf_MatchesVerificationFingerprint()
    {
        var (svc, _) = NewService();
        var token = svc.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromHours(1));
        var result = await svc.VerifyAndConsumeAsync(token, InstX, Now.AddMinutes(1));
        Assert.Equal(svc.FingerprintOf(token), result.Fingerprint);
    }

    [Fact]
    public async Task Nonce_Override_ProducesKnownValue()
    {
        // Used by the integration tests to write deterministic token bodies.
        var (svc, _) = NewService();
        var token = svc.Issue(ParentA, ChildA, InstX, Now,
            TimeSpan.FromHours(1), nonceOverride: "deterministic-nonce");
        var result = await svc.VerifyAndConsumeAsync(token, InstX, Now.AddMinutes(1));
        Assert.Equal("deterministic-nonce", result.Payload!.Nonce);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    // Deterministically tampers the HMAC signature segment of <token> by
    // decoding it, XORing the first byte with 0xFF, and re-encoding.
    //
    // The earlier approach — flipping the LAST CHAR of the base64url-encoded
    // signature ('A'<->'B') — was racy: HMAC-SHA256 produces 32 bytes,
    // which encode to 43 base64url chars whose final character carries only
    // the top 4 bits of the last byte (the bottom 2 bits are unused). Any
    // pair of last chars sharing high-4-bits (e.g. {A,B,C,D}, {E,F,G,H},
    // …) decodes to the SAME 32 bytes, so the "tampered" token re-decodes
    // to the original signature ~6.25 % of the time, the verifier returns
    // Valid, and the test fails. Byte-level XOR is independent of base64url
    // alphabet quirks and provably non-trivial.
    private static string TamperSignature(string token)
    {
        var parts = token.Split('.', 2);
        Assert.Equal(2, parts.Length);
        var sig = Base64UrlDecode(parts[1]);
        Assert.NotEmpty(sig);
        sig[0] ^= 0xFF;
        return parts[0] + "." + Base64UrlEncode(sig);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var b64 = Convert.ToBase64String(bytes);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
        }
        return Convert.FromBase64String(b64);
    }
}
