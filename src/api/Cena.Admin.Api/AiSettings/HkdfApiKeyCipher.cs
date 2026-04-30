// =============================================================================
// Cena Platform — Admin API-key cipher (AES-GCM-256, HKDF-derived)
//
// Derives a fixed AES-GCM-256 key from the platform root master key
// (CENA_PII_ROOT_KEY_BASE64 → SubjectKeyDerivation) bound to the
// "admin:ai-settings:anthropic-api-key" purpose label. The same root master
// key powers the per-subject student keystore (ADR-0038); using the same
// HKDF derivation here means production already enforces "no master key
// configured → boot fails" via SubjectKeyDevFallbackCheck.
//
// Wire format: "cena.aesgcm.v1:" + base64( 0x01 || nonce(12) || tag(16) ||
// ciphertext ). Mirrors EncryptedFieldAccessor's wire shape so an operator
// reading raw Marten rows sees a familiar prefix.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Admin.Api.AiSettings;

public sealed class HkdfApiKeyCipher : IApiKeyCipher
{
    private const string WireFormatV1Prefix = "cena.aesgcm.v1:";
    private const byte WireFormatV1 = 0x01;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>HKDF info label bound into the derived key. Changing this
    /// invalidates every previously-encrypted blob — treat as schema.</summary>
    public const string PurposeId = "admin:ai-settings:anthropic-api-key";

    private readonly byte[] _key;

    public HkdfApiKeyCipher(SubjectKeyDerivation derivation)
    {
        ArgumentNullException.ThrowIfNull(derivation);
        _key = derivation.DeriveSubjectKey(PurposeId);
    }

    public string EncryptToWire(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";

        var nonce = new byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var ptBytes = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[ptBytes.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, ptBytes, ct, tag);

        var buf = new byte[1 + NonceBytes + TagBytes + ct.Length];
        buf[0] = WireFormatV1;
        Buffer.BlockCopy(nonce, 0, buf, 1, NonceBytes);
        Buffer.BlockCopy(tag, 0, buf, 1 + NonceBytes, TagBytes);
        Buffer.BlockCopy(ct, 0, buf, 1 + NonceBytes + TagBytes, ct.Length);

        return WireFormatV1Prefix + Convert.ToBase64String(buf);
    }

    public bool TryDecryptFromWire(string wire, out string plaintext)
    {
        plaintext = "";
        if (string.IsNullOrEmpty(wire)) return false;
        if (!wire.StartsWith(WireFormatV1Prefix, StringComparison.Ordinal)) return false;

        byte[] raw;
        try { raw = Convert.FromBase64String(wire.Substring(WireFormatV1Prefix.Length)); }
        catch (FormatException) { return false; }

        if (raw.Length < 1 + NonceBytes + TagBytes) return false;
        if (raw[0] != WireFormatV1) return false;

        var cipherLen = raw.Length - 1 - NonceBytes - TagBytes;
        var nonce = new byte[NonceBytes];
        var tag = new byte[TagBytes];
        var cipher = new byte[cipherLen];
        Buffer.BlockCopy(raw, 1, nonce, 0, NonceBytes);
        Buffer.BlockCopy(raw, 1 + NonceBytes, tag, 0, TagBytes);
        Buffer.BlockCopy(raw, 1 + NonceBytes + TagBytes, cipher, 0, cipherLen);

        try
        {
            var pt = new byte[cipherLen];
            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, pt);
            plaintext = Encoding.UTF8.GetString(pt);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
