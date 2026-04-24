using Cena.Actors.Messaging;

namespace Cena.Actors.Tests.Messaging;

public sealed class ContentModeratorTests
{
    private readonly ContentModerator _mod = new();

    // ── Phone Numbers: Blocked ──

    [Theory]
    [InlineData("Call me at +972501234567")]
    [InlineData("My number is 0501234567")]
    [InlineData("Ring +1234567890123")]
    public void BlocksPhoneNumbers(string text)
    {
        var result = _mod.Check(text);
        Assert.False(result.Safe);
        Assert.Equal("phone_number_detected", result.Reason);
    }

    // ── Email Addresses: Blocked ──

    [Theory]
    [InlineData("Email me at user@gmail.com")]
    [InlineData("Send to parent@school.org")]
    [InlineData("Contact admin@cena.edu.il")]
    public void BlocksEmailAddresses(string text)
    {
        var result = _mod.Check(text);
        Assert.False(result.Safe);
        Assert.Equal("email_detected", result.Reason);
    }

    // ── Non-Allowlisted URLs: Blocked ──

    [Theory]
    [InlineData("Visit https://random-site.com")]
    [InlineData("Check out http://malware.xyz/stuff")]
    [InlineData("Go to https://facebook.com/profile")]
    public void BlocksNonAllowlistedUrls(string text)
    {
        var result = _mod.Check(text);
        Assert.False(result.Safe);
        Assert.Equal("url_not_allowlisted", result.Reason);
    }

    // ── Allowlisted Educational URLs: Allowed ──

    [Theory]
    [InlineData("Watch this: https://www.youtube.com/watch?v=abc123")]
    [InlineData("Try https://www.desmos.com/calculator")]
    [InlineData("Read https://en.wikipedia.org/wiki/Fractions")]
    [InlineData("Check https://www.khanacademy.org/math")]
    [InlineData("Use https://www.geogebra.org/classic")]
    public void AllowsEducationalLinks(string text)
    {
        var result = _mod.Check(text);
        Assert.True(result.Safe);
    }

    // ── Normal Messages: Allowed ──

    [Theory]
    [InlineData("Great job on today's quiz!")]
    [InlineData("כל הכבוד! המשיכי כך")]
    [InlineData("أحسنت! واصل العمل الجيد")]
    [InlineData("Review fractions before tomorrow")]
    [InlineData("42")]
    [InlineData("")]
    public void AllowsNormalMessages(string text)
    {
        var result = _mod.Check(text);
        Assert.True(result.Safe);
        Assert.Null(result.Reason);
    }

    // ── Excessive Caps: Flagged but NOT Blocked ──

    [Fact]
    public void FlagsExcessiveCaps_ButDoesNotBlock()
    {
        var result = _mod.Check("STOP DOING THAT RIGHT NOW!!!");
        Assert.True(result.Safe);
        Assert.Equal("excessive_caps", result.Flag);
    }

    [Fact]
    public void ShortCaps_NotFlagged()
    {
        // Under 20 chars, caps check doesn't trigger
        var result = _mod.Check("YES OK FINE");
        Assert.True(result.Safe);
        Assert.Null(result.Flag);
    }
}
