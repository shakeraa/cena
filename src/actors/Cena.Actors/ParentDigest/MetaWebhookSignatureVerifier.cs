// =============================================================================
// Cena Platform — MetaWebhookSignatureVerifier (PRR-437)
//
// Inbound-webhook signature verification for Meta WhatsApp Cloud API
// delivery-status callbacks. Meta signs each POST with
// X-Hub-Signature-256: sha256=<hex> using the app secret. We compute the
// HMAC-SHA256 over the raw request body (bytes, not parsed JSON —
// parsing re-serialisation would change the byte-level payload and the
// signature would never match) with the MetaCloud:AppSecret key; compare
// constant-time.
//
// Why constant-time compare: naive byte-by-byte comparison short-circuits
// on the first mismatch, leaking information about how many leading
// bytes of the hash matched. Meta's HMAC is not a practical timing-
// attack target at this scale (the app secret never leaves our config)
// but the constant-time discipline is correct-by-default crypto —
// trivially cheap on a 32-byte hash, zero reason to skimp.
//
// Why require AppSecret to be non-empty at verifier construction: if
// we accepted an empty secret, every "unsigned" POST would hash to the
// same sentinel and a mis-configured deploy would silently accept
// attacker traffic. Fail-loud at startup instead.
//
// Signature header format (Meta/Facebook Graph convention):
//   X-Hub-Signature-256: sha256=<64 hex chars>
// We tolerate case variance on the "sha256=" prefix but require exact
// case on the hex (Meta emits lowercase; some test harnesses send
// uppercase — both verify identically because we compare the raw bytes
// after hex decoding, not the hex string).
//
// Pure static + injectable interface shape so the endpoint can mock it
// in tests without a keyed instance.
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.ParentDigest;

/// <summary>Verifier port for Meta webhook signatures.</summary>
public interface IMetaWebhookSignatureVerifier
{
    /// <summary>
    /// True if the X-Hub-Signature-256 header value is a valid HMAC-SHA256
    /// of <paramref name="rawBody"/> under the configured app secret. False
    /// on any mismatch, missing header, or malformed header.
    /// </summary>
    bool IsValid(ReadOnlySpan<byte> rawBody, string? headerValue);
}

/// <summary>
/// Default implementation — constructor takes the app secret; caller
/// supplies it via MetaCloudWhatsAppOptions.AppSecret.
/// </summary>
public sealed class MetaWebhookSignatureVerifier : IMetaWebhookSignatureVerifier
{
    /// <summary>Expected header prefix (case-insensitive on the algorithm tag).</summary>
    public const string HeaderPrefix = "sha256=";

    /// <summary>Length of a SHA256 hex digest (64 chars).</summary>
    public const int HexDigestLength = 64;

    private readonly byte[] _appSecretBytes;

    /// <summary>
    /// Build the verifier. <paramref name="appSecret"/> must be non-empty;
    /// empty → <see cref="ArgumentException"/> so the host fails fast on
    /// a misconfigured deploy instead of silently accepting unsigned
    /// webhook traffic.
    /// </summary>
    public MetaWebhookSignatureVerifier(string appSecret)
    {
        if (string.IsNullOrWhiteSpace(appSecret))
        {
            throw new ArgumentException(
                "Meta app secret is required; empty secret would accept "
                + "all signatures and is a fatal mis-configuration.",
                nameof(appSecret));
        }
        _appSecretBytes = Encoding.UTF8.GetBytes(appSecret);
    }

    /// <inheritdoc />
    public bool IsValid(ReadOnlySpan<byte> rawBody, string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        // Tolerate case variance on the "sha256=" prefix; the suffix hex
        // must be the expected length.
        if (!headerValue.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var hex = headerValue[HeaderPrefix.Length..];
        if (hex.Length != HexDigestLength)
        {
            return false;
        }

        // Hex-decode the expected signature. Invalid hex → false.
        Span<byte> expected = stackalloc byte[32];
        if (!TryHexDecode(hex, expected))
        {
            return false;
        }

        // Compute the HMAC-SHA256 of the raw body under the secret.
        Span<byte> actual = stackalloc byte[32];
        using var hmac = new HMACSHA256(_appSecretBytes);
        if (!hmac.TryComputeHash(rawBody, actual, out var written) || written != 32)
        {
            return false;
        }

        // Constant-time compare. CryptographicOperations.FixedTimeEquals is
        // the idiomatic .NET primitive for this.
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>
    /// Try to hex-decode <paramref name="hex"/> into <paramref name="dest"/>.
    /// Returns false on odd length, non-hex chars, or under-length dest.
    /// </summary>
    public static bool TryHexDecode(ReadOnlySpan<char> hex, Span<byte> dest)
    {
        if (hex.Length % 2 != 0) return false;
        if (dest.Length * 2 < hex.Length) return false;

        for (var i = 0; i < hex.Length; i += 2)
        {
            if (!TryHexNibble(hex[i], out var hi)) return false;
            if (!TryHexNibble(hex[i + 1], out var lo)) return false;
            dest[i / 2] = (byte)((hi << 4) | lo);
        }
        return true;
    }

    private static bool TryHexNibble(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }
}
