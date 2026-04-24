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

        // Flip the last character of the signature segment.
        var parts = token.Split('.');
        var sig = parts[1];
        var tampered = parts[0] + "." + (sig[^1] == 'A' ? sig[..^1] + "B" : sig[..^1] + "A");

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
}
