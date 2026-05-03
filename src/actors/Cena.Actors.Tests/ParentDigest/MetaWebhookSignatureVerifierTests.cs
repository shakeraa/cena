// =============================================================================
// Cena Platform — MetaWebhookSignatureVerifier tests (PRR-437)
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.ParentDigest;
using Xunit;

namespace Cena.Actors.Tests.ParentDigest;

public class MetaWebhookSignatureVerifierTests
{
    private const string TestSecret = "whsec_test_app_secret_PRR-437";

    [Fact]
    public void Valid_signature_passes()
    {
        var body = Encoding.UTF8.GetBytes(
            "{\"entry\":[{\"changes\":[{\"value\":{\"statuses\":[]}}]}]}");
        var header = Sign(body, TestSecret);
        var verifier = new MetaWebhookSignatureVerifier(TestSecret);

        Assert.True(verifier.IsValid(body, header));
    }

    [Fact]
    public void Tampered_body_fails()
    {
        var body = Encoding.UTF8.GetBytes("{\"original\":\"body\"}");
        var header = Sign(body, TestSecret);
        // Tamper: change one byte of the body. Signature must fail.
        body[1] = (byte)'X';
        var verifier = new MetaWebhookSignatureVerifier(TestSecret);

        Assert.False(verifier.IsValid(body, header));
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        var body = Encoding.UTF8.GetBytes("{\"x\":1}");
        var header = Sign(body, TestSecret);
        var verifier = new MetaWebhookSignatureVerifier("whsec_different_secret");

        Assert.False(verifier.IsValid(body, header));
    }

    [Fact]
    public void Missing_header_fails()
    {
        var body = Encoding.UTF8.GetBytes("{\"x\":1}");
        var verifier = new MetaWebhookSignatureVerifier(TestSecret);

        Assert.False(verifier.IsValid(body, null));
        Assert.False(verifier.IsValid(body, ""));
        Assert.False(verifier.IsValid(body, "   "));
    }

    [Fact]
    public void Malformed_header_fails()
    {
        var body = Encoding.UTF8.GetBytes("{\"x\":1}");
        var verifier = new MetaWebhookSignatureVerifier(TestSecret);

        // Wrong algorithm tag.
        Assert.False(verifier.IsValid(body, "md5=00112233"));
        // Missing "=".
        Assert.False(verifier.IsValid(body, "sha256"));
        // Wrong hex length.
        Assert.False(verifier.IsValid(body, "sha256=abcd"));
        // Non-hex chars.
        Assert.False(verifier.IsValid(body, "sha256=" + new string('z', 64)));
    }

    [Fact]
    public void Case_variance_on_algorithm_prefix_accepted()
    {
        // Meta emits lowercase "sha256="; we tolerate case on the prefix
        // so a test harness / proxy that upper-cases it still verifies.
        var body = Encoding.UTF8.GetBytes("{\"x\":1}");
        var header = Sign(body, TestSecret);
        var uppercased = "SHA256=" + header.Substring("sha256=".Length);
        var verifier = new MetaWebhookSignatureVerifier(TestSecret);

        Assert.True(verifier.IsValid(body, uppercased));
    }

    [Fact]
    public void Empty_secret_at_construction_throws()
    {
        // Fail-loud guard: empty-secret config would otherwise accept
        // every signed-with-empty-secret payload = every attacker payload.
        Assert.Throws<ArgumentException>(() =>
            new MetaWebhookSignatureVerifier(""));
        Assert.Throws<ArgumentException>(() =>
            new MetaWebhookSignatureVerifier("   "));
        Assert.Throws<ArgumentException>(() =>
            new MetaWebhookSignatureVerifier(null!));
    }

    [Fact]
    public void TryHexDecode_round_trips()
    {
        var bytes = new byte[] { 0x00, 0x0F, 0xFF, 0xAB, 0xCD };
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();

        Span<byte> decoded = stackalloc byte[5];
        Assert.True(MetaWebhookSignatureVerifier.TryHexDecode(hex, decoded));
        Assert.Equal(bytes, decoded.ToArray());
    }

    [Fact]
    public void TryHexDecode_rejects_odd_length()
    {
        Span<byte> dest = stackalloc byte[10];
        Assert.False(MetaWebhookSignatureVerifier.TryHexDecode("abc", dest));
    }

    [Fact]
    public void TryHexDecode_rejects_non_hex_chars()
    {
        Span<byte> dest = stackalloc byte[10];
        Assert.False(MetaWebhookSignatureVerifier.TryHexDecode("zz00", dest));
    }

    private static string Sign(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(body);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
