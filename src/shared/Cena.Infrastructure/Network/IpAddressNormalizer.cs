// =============================================================================
// Cena Platform -- FIND-privacy-015: IP Address Normalizer
// Truncates IPv4 to /24 and IPv6 to /64 before persistence.
// GDPR Art 5(1)(c) data minimisation, ICO Children's Code Std 8.
//
// Raw IPs remain available in-memory for abuse detection (rate limiting,
// fail-fast on a single IP) but MUST NOT be persisted to any durable store.
// =============================================================================

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Network;

/// <summary>
/// Normalizes (truncates) IP addresses to remove the host-identifying portion
/// before any persistence operation. This is a GDPR data-minimisation control.
/// <list type="bullet">
///   <item>IPv4: zeroes the last octet (/24 mask), e.g. 203.0.113.42 -> 203.0.113.0</item>
///   <item>IPv6: keeps the first 8 bytes (/64 prefix), e.g. 2001:db8:85a3:1234:ffff:: -> 2001:db8:85a3:1234::</item>
///   <item>IPv4-mapped IPv6: extracts the IPv4 portion and applies /24 mask</item>
///   <item>null / empty / "unknown" / malformed: returns "unknown"</item>
/// </list>
/// </summary>
public static class IpAddressNormalizer
{
    /// <summary>
    /// Normalize an IP address string by truncating host-identifying bits.
    /// Thread-safe, allocation-minimal, no exceptions.
    /// </summary>
    /// <param name="rawIp">The raw IP address string (may be null, empty, or malformed).</param>
    /// <param name="logger">Optional logger for structured privacy audit trail.</param>
    /// <returns>The truncated IP address, or "unknown" if the input cannot be parsed.</returns>
    public static string Normalize(string? rawIp, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(rawIp) || rawIp == "unknown")
            return "unknown";

        // Handle X-Forwarded-For comma-separated list: take the first (client) IP
        var ipToParse = rawIp;
        var commaIndex = rawIp.IndexOf(',');
        if (commaIndex > 0)
            ipToParse = rawIp[..commaIndex].Trim();

        if (!IPAddress.TryParse(ipToParse, out var address))
        {
            logger?.LogWarning("[PRIVACY] IP anonymization skipped: malformed input");
            return "unknown";
        }

        var result = NormalizeAddress(address);

        logger?.LogDebug("[PRIVACY] IP anonymized before persistence");

        return result;
    }

    /// <summary>
    /// Normalize a parsed <see cref="IPAddress"/> by truncating host-identifying bits.
    /// </summary>
    /// <param name="address">The parsed IP address (may be null).</param>
    /// <returns>The truncated IP address string, or "unknown" if null.</returns>
    public static string Normalize(IPAddress? address)
    {
        if (address is null)
            return "unknown";

        return NormalizeAddress(address);
    }

    private static string NormalizeAddress(IPAddress address)
    {
        // IPv4-mapped IPv6 (e.g. ::ffff:192.168.1.1) -- extract and truncate the IPv4 portion
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPv4: zero the last octet (/24 mask)
            var bytes = address.GetAddressBytes(); // 4 bytes
            bytes[3] = 0;
            return new IPAddress(bytes).ToString();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6: keep the first 8 bytes (/64 prefix), zero the last 8
            var bytes = address.GetAddressBytes(); // 16 bytes
            for (var i = 8; i < 16; i++)
                bytes[i] = 0;
            return new IPAddress(bytes).ToString();
        }

        // Unknown address family -- should not happen in practice
        return "unknown";
    }
}
