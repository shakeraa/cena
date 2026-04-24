// =============================================================================
// Cena Platform -- FIND-privacy-015: IP Address Normalizer Tests
// Regression suite ensuring IP addresses are truncated before persistence.
// =============================================================================

using System.Net;
using Cena.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Infrastructure.Tests.Network;

public sealed class IpAddressNormalizerTests
{
    // ---- IPv4 (/24 truncation) ----

    [Theory]
    [InlineData("203.0.113.42", "203.0.113.0")]
    [InlineData("192.168.1.123", "192.168.1.0")]
    [InlineData("10.0.0.1", "10.0.0.0")]
    [InlineData("255.255.255.255", "255.255.255.0")]
    [InlineData("0.0.0.0", "0.0.0.0")]
    [InlineData("127.0.0.1", "127.0.0.0")]
    public void Normalize_IPv4_ZerosLastOctet(string raw, string expected)
    {
        var result = IpAddressNormalizer.Normalize(raw);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalize_IPv4_LastOctetIsAlwaysZero()
    {
        // Regression: ensure no full IPv4 leaks through -- the 4th octet must be 0
        var result = IpAddressNormalizer.Normalize("203.0.113.42");
        var parts = result.Split('.');
        Assert.Equal(4, parts.Length);
        Assert.Equal("0", parts[3]);
    }

    // ---- IPv6 (/64 truncation) ----

    [Fact]
    public void Normalize_IPv6_KeepsFirst64Bits()
    {
        var result = IpAddressNormalizer.Normalize("2001:db8:85a3:1234:ffff:ffff:ffff:ffff");
        Assert.Equal("2001:db8:85a3:1234::", result);
    }

    [Fact]
    public void Normalize_IPv6_AlreadyTruncated_Idempotent()
    {
        var result = IpAddressNormalizer.Normalize("2001:db8:85a3:1234::");
        Assert.Equal("2001:db8:85a3:1234::", result);
    }

    [Fact]
    public void Normalize_IPv6_FullZeros()
    {
        var result = IpAddressNormalizer.Normalize("::");
        Assert.Equal("::", result);
    }

    [Fact]
    public void Normalize_IPv6_Loopback()
    {
        // ::1 has interface ID bits set, so truncation zeroes them
        var result = IpAddressNormalizer.Normalize("::1");
        Assert.Equal("::", result);
    }

    // ---- IPv4-mapped IPv6 ----

    [Fact]
    public void Normalize_IPv4MappedIPv6_ExtractsAndTruncatesIPv4()
    {
        // ::ffff:192.168.1.123 should become 192.168.1.0
        var result = IpAddressNormalizer.Normalize("::ffff:192.168.1.123");
        Assert.Equal("192.168.1.0", result);
    }

    [Fact]
    public void Normalize_IPv4MappedIPv6_FullNotation()
    {
        var result = IpAddressNormalizer.Normalize("::ffff:203.0.113.42");
        Assert.Equal("203.0.113.0", result);
    }

    // ---- Null / empty / unknown ----

    [Fact]
    public void Normalize_Null_ReturnsUnknown()
    {
        Assert.Equal("unknown", IpAddressNormalizer.Normalize((string?)null));
    }

    [Fact]
    public void Normalize_Empty_ReturnsUnknown()
    {
        Assert.Equal("unknown", IpAddressNormalizer.Normalize(""));
    }

    [Fact]
    public void Normalize_Whitespace_ReturnsUnknown()
    {
        Assert.Equal("unknown", IpAddressNormalizer.Normalize("   "));
    }

    [Fact]
    public void Normalize_Unknown_ReturnsUnknown()
    {
        Assert.Equal("unknown", IpAddressNormalizer.Normalize("unknown"));
    }

    // ---- Malformed ----

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("abc:def:ghi")]
    [InlineData("hello world")]
    public void Normalize_Malformed_ReturnsUnknown(string malformed)
    {
        Assert.Equal("unknown", IpAddressNormalizer.Normalize(malformed));
    }

    // ---- IPAddress overload ----

    [Fact]
    public void Normalize_IPAddressOverload_Null_ReturnsUnknown()
    {
        Assert.Equal("unknown", IpAddressNormalizer.Normalize((IPAddress?)null));
    }

    [Fact]
    public void Normalize_IPAddressOverload_IPv4()
    {
        var addr = IPAddress.Parse("203.0.113.42");
        Assert.Equal("203.0.113.0", IpAddressNormalizer.Normalize(addr));
    }

    [Fact]
    public void Normalize_IPAddressOverload_IPv6()
    {
        var addr = IPAddress.Parse("2001:db8:85a3:1234:ffff:ffff:ffff:ffff");
        Assert.Equal("2001:db8:85a3:1234::", IpAddressNormalizer.Normalize(addr));
    }

    // ---- X-Forwarded-For with multiple IPs ----

    [Fact]
    public void Normalize_XForwardedFor_CommaList_TakesFirstAndTruncates()
    {
        // X-Forwarded-For: client, proxy1, proxy2
        var result = IpAddressNormalizer.Normalize("203.0.113.42, 70.41.3.18, 150.172.238.178");
        Assert.Equal("203.0.113.0", result);
    }

    // ---- Logger integration ----

    [Fact]
    public void Normalize_ValidIp_LogsPrivacyAudit()
    {
        var logger = Substitute.For<ILogger>();
        IpAddressNormalizer.Normalize("203.0.113.42", logger);

        // Verify that LogDebug was called (NSubstitute intercepts the extension method)
        logger.Received(1).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("IP anonymized before persistence")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void Normalize_Malformed_LogsWarning()
    {
        var logger = Substitute.For<ILogger>();
        IpAddressNormalizer.Normalize("not-an-ip", logger);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("IP anonymization skipped")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ---- Regression: StudentRecordAccessLog must never contain a full IPv4 ----

    [Fact]
    public void Regression_NormalizedIPv4_NeverHasNonZeroLastOctet()
    {
        // This test is the CI-wired regression guard for FIND-privacy-015.
        // It verifies the normalizer contract that any IPv4 output has ".0" suffix.
        var ips = new[]
        {
            "1.2.3.4", "10.20.30.40", "172.16.0.99", "192.168.100.200",
            "203.0.113.42", "8.8.8.8", "255.255.255.254"
        };

        foreach (var ip in ips)
        {
            var normalized = IpAddressNormalizer.Normalize(ip);
            Assert.EndsWith(".0", normalized);
        }
    }
}
