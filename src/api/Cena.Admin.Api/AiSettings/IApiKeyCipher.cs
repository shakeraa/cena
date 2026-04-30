// =============================================================================
// Cena Platform — Admin API-key cipher
//
// Encrypt/decrypt admin-configured secrets at rest. The encrypted form is a
// single string that lives in Marten document fields; callers never see raw
// keys.
//
// Separate from the per-subject crypto-shred (EncryptedFieldAccessor): admin
// secrets must NOT be tied to a student-subject keystore that supports
// tombstoning by GDPR erasure. Erasing a student must not orphan an admin
// secret and vice-versa.
// =============================================================================

namespace Cena.Admin.Api.AiSettings;

public interface IApiKeyCipher
{
    /// <summary>Encrypt a non-empty plaintext to wire form. Returns empty
    /// string on empty input — never null.</summary>
    string EncryptToWire(string plaintext);

    /// <summary>Try to decrypt a wire-form blob. Returns false on empty
    /// input, malformed wire, or AES-GCM tag mismatch. Never throws.</summary>
    bool TryDecryptFromWire(string wire, out string plaintext);
}
