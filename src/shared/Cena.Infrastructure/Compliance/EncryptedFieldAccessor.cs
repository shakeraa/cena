// =============================================================================
// Cena Platform -- Encrypted Field Accessor (ADR-0038, prr-003b)
//
// AES-GCM-256 encrypt/decrypt for event PII fields, keyed per-subject via
// ISubjectKeyStore. The transport form of an encrypted value is a single
// Base64 string carrying { nonce(12) || ciphertext(n) || tag(16) } so the
// field remains a JSON string (preserving Marten schema compat).
//
// Pre-ADR plaintext values deserialize untouched: TryDecrypt detects an
// invalid blob and returns the raw input as plaintext. This is safe
// because pre-ADR events carry plaintext StudentAnswer values that are
// free-form student input, never structured. Once encrypted writes land
// on main, every new event round-trips through this accessor.
//
// Read-path contract (ADR-0038 §"Read-path contract"):
//   - tombstoned subject       -> (false, ErasedSentinel.Value)
//   - missing subject key      -> (false, ErasedSentinel.Value)
//   - decrypt failure          -> (false, ErasedSentinel.Value)
//   - plaintext (pre-ADR event)-> (true,  original_plaintext)
//   - valid ciphertext         -> (true,  decrypted_plaintext)
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Sentinel value returned when a decrypt cannot produce plaintext —
/// either the key has been tombstoned or the blob is malformed.
/// Callers MUST treat this as a UI affordance, never as an error.
/// </summary>
public static class ErasedSentinel
{
    /// <summary>The string returned for erased or unreadable encrypted fields.</summary>
    public const string Value = "[erased]";
}

/// <summary>
/// Wire format for an encrypted field. The on-disk form is a Base64 string
/// of <c>version || nonce(12) || tag(16) || ciphertext</c>.
/// </summary>
public readonly record struct EncryptedBlob(byte[] Nonce, byte[] Ciphertext, byte[] Tag)
{
    private const byte WireFormatV1 = 0x01;
    private const string WireFormatV1Prefix = "cena.aesgcm.v1:";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>
    /// Serialize to a single Base64 string with a stable wire-format prefix
    /// so we can tell an EncryptedBlob apart from a pre-ADR plaintext value.
    /// </summary>
    public string ToWireString()
    {
        var total = 1 + NonceBytes + TagBytes + Ciphertext.Length;
        var buf = new byte[total];
        buf[0] = WireFormatV1;
        Buffer.BlockCopy(Nonce, 0, buf, 1, NonceBytes);
        Buffer.BlockCopy(Tag, 0, buf, 1 + NonceBytes, TagBytes);
        Buffer.BlockCopy(Ciphertext, 0, buf, 1 + NonceBytes + TagBytes, Ciphertext.Length);
        return WireFormatV1Prefix + Convert.ToBase64String(buf);
    }

    /// <summary>
    /// Try to parse a wire-format string back into an EncryptedBlob.
    /// Returns <c>false</c> for plaintext or malformed input.
    /// </summary>
    public static bool TryParse(string? wire, out EncryptedBlob blob)
    {
        blob = default;
        if (string.IsNullOrEmpty(wire) || !wire.StartsWith(WireFormatV1Prefix, StringComparison.Ordinal))
        {
            return false;
        }
        var b64 = wire.Substring(WireFormatV1Prefix.Length);
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return false;
        }
        if (raw.Length < 1 + NonceBytes + TagBytes)
        {
            return false;
        }
        if (raw[0] != WireFormatV1)
        {
            return false;
        }
        var nonce = new byte[NonceBytes];
        var tag = new byte[TagBytes];
        Buffer.BlockCopy(raw, 1, nonce, 0, NonceBytes);
        Buffer.BlockCopy(raw, 1 + NonceBytes, tag, 0, TagBytes);
        var cipherLen = raw.Length - 1 - NonceBytes - TagBytes;
        var cipher = new byte[cipherLen];
        Buffer.BlockCopy(raw, 1 + NonceBytes + TagBytes, cipher, 0, cipherLen);
        blob = new EncryptedBlob(nonce, cipher, tag);
        return true;
    }
}

/// <summary>
/// Subject-scoped AES-GCM encrypt/decrypt for event PII fields (ADR-0038).
/// </summary>
public sealed class EncryptedFieldAccessor
{
    private readonly ISubjectKeyStore _keyStore;

    public EncryptedFieldAccessor(ISubjectKeyStore keyStore)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    /// <summary>
    /// Encrypt <paramref name="plaintext"/> under the subject's derived key.
    /// Returns the wire-format string. Null input yields null. A tombstoned
    /// subject throws <see cref="InvalidOperationException"/> because
    /// writing new ciphertext for an erased subject is a contract violation
    /// (re-populating an erased record is forbidden).
    /// </summary>
    public async ValueTask<string?> EncryptAsync(string? plaintext, string subjectId, CancellationToken ct = default)
    {
        if (plaintext is null) return null;
        var key = await _keyStore.GetOrCreateAsync(subjectId, ct).ConfigureAwait(false);
        if (key is null)
        {
            throw new InvalidOperationException(
                $"Cannot encrypt for subject {subjectId}: key is tombstoned (ADR-0038).");
        }

        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return new EncryptedBlob(nonce, ciphertext, tag).ToWireString();
    }

    /// <summary>
    /// Try to decrypt a wire-format string. Returns <c>(true, plaintext)</c>
    /// on success, <c>(false, ErasedSentinel.Value)</c> when the key is
    /// missing/tombstoned or the blob is corrupt. Pre-ADR plaintext values
    /// pass through as <c>(true, original)</c>.
    /// </summary>
    public async ValueTask<(bool Success, string Plaintext)> TryDecryptAsync(
        string? wire, string subjectId, CancellationToken ct = default)
    {
        if (wire is null)
        {
            return (true, string.Empty);
        }
        if (!EncryptedBlob.TryParse(wire, out var blob))
        {
            // Pre-ADR plaintext passes through untouched.
            return (true, wire);
        }

        var key = await _keyStore.GetOrCreateAsync(subjectId, ct).ConfigureAwait(false);
        if (key is null)
        {
            return (false, ErasedSentinel.Value);
        }

        try
        {
            var plaintextBytes = new byte[blob.Ciphertext.Length];
            using var aes = new AesGcm(key, blob.Tag.Length);
            aes.Decrypt(blob.Nonce, blob.Ciphertext, blob.Tag, plaintextBytes);
            return (true, Encoding.UTF8.GetString(plaintextBytes));
        }
        catch (CryptographicException)
        {
            // Tampered ciphertext, wrong key, whatever — treat as erased per ADR.
            return (false, ErasedSentinel.Value);
        }
    }
}
