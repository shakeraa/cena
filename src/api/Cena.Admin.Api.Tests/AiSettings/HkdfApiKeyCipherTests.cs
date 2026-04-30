// =============================================================================
// HkdfApiKeyCipherTests — real AES-GCM round-trip + tamper rejection
//
// These tests run AesGcm + HKDF for real (no mocking) so the wire format,
// tag verification, and HKDF binding are all exercised. The dev-fallback
// SubjectKeyDerivation is fine here — production hosts use a real master
// key, but the cipher is identical either way.
// =============================================================================

using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Admin.Api.Tests.AiSettings;

public class HkdfApiKeyCipherTests
{
    private static SubjectKeyDerivation NewDerivation()
    {
        // 32 bytes of arbitrary entropy; tests don't need crypto-grade randomness.
        var rootKey = new byte[32];
        for (var i = 0; i < rootKey.Length; i++) rootKey[i] = (byte)(i + 1);
        return new SubjectKeyDerivation(rootKey, "test.install", isDevFallback: false);
    }

    [Fact]
    public void RoundTrip_PreservesPlaintext()
    {
        var cipher = new HkdfApiKeyCipher(NewDerivation());
        var wire = cipher.EncryptToWire("sk-ant-api03-AbCdEf1234567890");
        Assert.True(cipher.TryDecryptFromWire(wire, out var pt));
        Assert.Equal("sk-ant-api03-AbCdEf1234567890", pt);
    }

    [Fact]
    public void EmptyPlaintext_ProducesEmptyWire_AndDecryptIsFalse()
    {
        var cipher = new HkdfApiKeyCipher(NewDerivation());
        Assert.Equal("", cipher.EncryptToWire(""));
        Assert.False(cipher.TryDecryptFromWire("", out _));
    }

    [Fact]
    public void WireFormat_HasStablePrefix()
    {
        var cipher = new HkdfApiKeyCipher(NewDerivation());
        var wire = cipher.EncryptToWire("hello");
        Assert.StartsWith("cena.aesgcm.v1:", wire);
    }

    [Fact]
    public void EveryEncryption_UsesFreshNonce()
    {
        var cipher = new HkdfApiKeyCipher(NewDerivation());
        var a = cipher.EncryptToWire("sk-ant-same-input");
        var b = cipher.EncryptToWire("sk-ant-same-input");
        // Distinct nonces → distinct wire blobs even for identical plaintext.
        // This is a correctness gate, not a stylistic preference: AES-GCM
        // catastrophically fails when (key, nonce) is reused.
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TamperedCipherText_FailsAuthentication()
    {
        var cipher = new HkdfApiKeyCipher(NewDerivation());
        var wire = cipher.EncryptToWire("sk-ant-real");

        // Flip a byte deep in the ciphertext payload (after prefix and base64 decode it lives there)
        var prefix = "cena.aesgcm.v1:";
        var b64 = wire.Substring(prefix.Length);
        var raw = Convert.FromBase64String(b64);
        // Tamper a byte well past the version+nonce+tag header so we hit ciphertext
        if (raw.Length > 1 + 12 + 16)
        {
            raw[^1] ^= 0xFF;
        }
        else
        {
            raw[1 + 12 + 16] ^= 0xFF;
        }
        var tampered = prefix + Convert.ToBase64String(raw);

        Assert.False(cipher.TryDecryptFromWire(tampered, out var pt));
        Assert.Equal("", pt);
    }

    [Fact]
    public void DifferentRootKey_CannotDecrypt()
    {
        // Cipher A encrypts under one root; cipher B has a different root and
        // must reject the blob (tag mismatch). This is what guarantees that
        // rotating CENA_PII_ROOT_KEY_BASE64 invalidates persisted secrets
        // rather than silently returning garbage.
        var cipherA = new HkdfApiKeyCipher(NewDerivation());
        var rootB = new byte[32];
        for (var i = 0; i < rootB.Length; i++) rootB[i] = (byte)(255 - i);
        var cipherB = new HkdfApiKeyCipher(new SubjectKeyDerivation(rootB, "test.install", false));

        var wire = cipherA.EncryptToWire("sk-ant-cross");
        Assert.False(cipherB.TryDecryptFromWire(wire, out _));
    }

    [Fact]
    public void MalformedWire_IsRejectedCleanly()
    {
        var cipher = new HkdfApiKeyCipher(NewDerivation());
        Assert.False(cipher.TryDecryptFromWire("not-a-wire-string", out _));
        Assert.False(cipher.TryDecryptFromWire("cena.aesgcm.v1:not-base64!!!", out _));
        Assert.False(cipher.TryDecryptFromWire("cena.aesgcm.v1:" + Convert.ToBase64String(new byte[5]), out _));
    }
}
