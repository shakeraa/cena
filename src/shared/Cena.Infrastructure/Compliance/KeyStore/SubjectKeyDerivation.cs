// =============================================================================
// Cena Platform -- Subject Key Derivation (ADR-0038, prr-003b)
//
// HKDF-SHA256 key derivation from a root key. Binds the derived key to a
// specific subject ID and purpose label so a single-subject compromise
// does not leak every subject, and re-deriving on an unrelated host
// without the install salt does not produce the same key.
//
// Root key provisioning:
//   - Production:  CENA_PII_ROOT_KEY_BASE64 env var, 32 raw bytes (Base64).
//                  Absence fails the compliance health-check.
//   - Dev/test:    Absence triggers a deterministic fallback keyed from a
//                  hardcoded string. Logged with a big WARNING. NEVER use
//                  this in production — the fallback is public-domain.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance.KeyStore;

/// <summary>
/// Derives per-subject AES-GCM 256-bit keys from a root key via HKDF-SHA256.
/// </summary>
public sealed class SubjectKeyDerivation
{
    /// <summary>
    /// Environment variable the production root key is read from. 32 raw
    /// bytes, Base64-encoded.
    /// </summary>
    public const string RootKeyEnvVar = "CENA_PII_ROOT_KEY_BASE64";

    /// <summary>Default install salt. Overridable for integration tests.</summary>
    public const string DefaultInstallSalt = "cena.install.default";

    /// <summary>HKDF info label bound into every derived subject key.</summary>
    public const string SubjectKeyInfoPrefix = "cena.subject.";

    /// <summary>Length of the derived subject key (AES-GCM-256).</summary>
    public const int SubjectKeyLengthBytes = 32;

    private static readonly byte[] DevFallbackSeed =
        Encoding.UTF8.GetBytes("CENA-DEV-ONLY-DO-NOT-USE-IN-PROD-2026-ADR0038");

    private readonly byte[] _rootKey;
    private readonly byte[] _installSalt;
    private readonly bool _isDevFallback;

    public SubjectKeyDerivation(byte[] rootKey, string installSalt, bool isDevFallback)
    {
        _rootKey = rootKey ?? throw new ArgumentNullException(nameof(rootKey));
        if (rootKey.Length < 32)
        {
            throw new ArgumentException($"Root key must be >= 32 bytes (was {rootKey.Length}).", nameof(rootKey));
        }
        _installSalt = Encoding.UTF8.GetBytes(installSalt ?? DefaultInstallSalt);
        _isDevFallback = isDevFallback;
    }

    /// <summary>
    /// True when the derivation is using the hardcoded dev-only fallback key.
    /// The compliance health-check refuses to start production if this is true.
    /// </summary>
    public bool IsUsingDevFallback => _isDevFallback;

    /// <summary>
    /// Build a derivation from environment / configuration. If
    /// <c>CENA_PII_ROOT_KEY_BASE64</c> is set, use it. Otherwise fall back
    /// to the dev-only seed and log a prominent warning.
    /// </summary>
    public static SubjectKeyDerivation FromEnvironment(ILogger? logger = null, string? installSalt = null)
    {
        var b64 = Environment.GetEnvironmentVariable(RootKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(b64))
        {
            try
            {
                var raw = Convert.FromBase64String(b64.Trim());
                if (raw.Length >= 32)
                {
                    return new SubjectKeyDerivation(raw, installSalt ?? DefaultInstallSalt, isDevFallback: false);
                }
                logger?.LogError(
                    "[SIEM] SubjectKeyDerivation: {EnvVar} was set but decodes to {Len} bytes (< 32). Falling back to DEV seed.",
                    RootKeyEnvVar, raw.Length);
            }
            catch (FormatException ex)
            {
                logger?.LogError(ex,
                    "[SIEM] SubjectKeyDerivation: {EnvVar} is not valid Base64. Falling back to DEV seed.",
                    RootKeyEnvVar);
            }
        }

        logger?.LogWarning(
            "[SIEM] !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!\n"
            + "!!! {EnvVar} is NOT set — using the DEV-ONLY fallback root key.      !!!\n"
            + "!!! This MUST NOT be used in production. Crypto-shred is a no-op.   !!!\n"
            + "!!! ADR-0038 compliance health-check will fail in production hosts. !!!\n"
            + "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!",
            RootKeyEnvVar);

        return new SubjectKeyDerivation(DevFallbackSeed, installSalt ?? DefaultInstallSalt, isDevFallback: true);
    }

    /// <summary>
    /// Derive a 32-byte AES-GCM key for the given subject ID. Deterministic
    /// while the root key and install salt are unchanged.
    /// </summary>
    public byte[] DeriveSubjectKey(string subjectId)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            throw new ArgumentException("subjectId must be non-empty.", nameof(subjectId));
        }

        var info = Encoding.UTF8.GetBytes(SubjectKeyInfoPrefix + subjectId);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _rootKey, SubjectKeyLengthBytes, _installSalt, info);
    }
}
