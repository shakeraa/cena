// =============================================================================
// Cena Platform — SMS encoding helper tests (prr-018).
// =============================================================================

using Cena.Actors.Notifications.OutboundSms;

namespace Cena.Actors.Tests.Notifications.OutboundSms;

public sealed class SmsEncodingTests
{
    [Theory]
    [InlineData("Hello Rami, Noa studied 3 hours this week.", SmsEncoding.Gsm7)]
    [InlineData("", SmsEncoding.Gsm7)]
    [InlineData("Plain ASCII", SmsEncoding.Gsm7)]
    [InlineData("£$¥èéù", SmsEncoding.Gsm7)]     // all in GSM-7 basic
    [InlineData("^{}\\[~]|€", SmsEncoding.Gsm7)] // all in GSM-7 extension
    public void Classify_Gsm7_HappyPath(string body, SmsEncoding expected)
    {
        Assert.Equal(expected, SmsEncodingRules.Classify(body));
    }

    [Theory]
    [InlineData("עברית")]      // Hebrew
    [InlineData("العربية")]     // Arabic
    [InlineData("你好")]         // Chinese
    [InlineData("Noa 😀")]       // Emoji (surrogate pair)
    [InlineData("→ arrow")]    // BMP glyph outside GSM-7
    public void Classify_NonGsm7_ReturnsUcs2(string body)
    {
        Assert.Equal(SmsEncoding.Ucs2, SmsEncodingRules.Classify(body));
    }

    [Theory]
    [InlineData("Hello", SmsEncoding.Gsm7, 5)]       // basic chars
    [InlineData("{hi}", SmsEncoding.Gsm7, 6)]        // 2 extension + 2 basic = 6 septets
    [InlineData("€€", SmsEncoding.Gsm7, 4)]          // 2x extension = 4 septets
    [InlineData("שלום", SmsEncoding.Ucs2, 4)]        // 4 BMP code units
    [InlineData("😀", SmsEncoding.Ucs2, 2)]          // surrogate pair = 2 UTF-16 code units
    public void MeasuredLength_MatchesWireCost(string body, SmsEncoding enc, int expected)
    {
        Assert.Equal(expected, SmsEncodingRules.MeasuredLength(body, enc));
    }

    [Fact]
    public void StripControlAndBidi_RemovesRtlOverrideAttack()
    {
        // Classic RLO phishing: "evil.com/" shown as "moc.live/"
        var phishy = "Link: \u202Emoc.live/";
        var cleaned = SmsEncodingRules.StripControlAndBidi(phishy);
        var codes = string.Join(",", cleaned.Select(c => ((int)c).ToString("X")));
        Assert.False(cleaned.Contains('\u202E'),
            $"RLO still present. codes=[{codes}], len={cleaned.Length}");
        Assert.Equal("Link: moc.live/", cleaned);
    }

    [Theory]
    [InlineData("\u202ANoa")]   // LRE
    [InlineData("\u202BNoa")]   // RLE
    [InlineData("\u202CNoa")]   // PDF
    [InlineData("\u202DNoa")]   // LRO
    [InlineData("\u202ENoa")]   // RLO
    [InlineData("\u2066Noa")]   // LRI
    [InlineData("\u2067Noa")]   // RLI
    [InlineData("\u2068Noa")]   // FSI
    [InlineData("\u2069Noa")]   // PDI
    [InlineData("\u200BNoa")]   // ZWSP
    [InlineData("\u200CNoa")]   // ZWNJ
    [InlineData("\uFEFFNoa")]   // BOM
    public void StripControlAndBidi_RemovesAllBidiAndFormatChars(string input)
    {
        var cleaned = SmsEncodingRules.StripControlAndBidi(input);
        Assert.Equal("Noa", cleaned);
    }

    [Theory]
    [InlineData("\t", "")]           // TAB stripped
    [InlineData("\r", "")]           // CR stripped
    [InlineData("\0", "")]           // NUL stripped
    [InlineData("\n", "\n")]         // LF preserved (valid multi-line)
    [InlineData("A\u007FB", "AB")]   // DEL stripped
    [InlineData("A\u0085B", "AB")]   // NEL (C1) stripped
    public void StripControlAndBidi_StripsC0C1ButKeepsLf(string input, string expected)
    {
        Assert.Equal(expected, SmsEncodingRules.StripControlAndBidi(input));
    }

    [Fact]
    public void StripControlAndBidi_PreservesEmoji()
    {
        var withEmoji = "Nice work! 👍";
        var cleaned = SmsEncodingRules.StripControlAndBidi(withEmoji);
        Assert.Equal(withEmoji, cleaned);
    }

    [Theory]
    [InlineData("  hello  world  ", "hello world")]
    [InlineData("hello\tworld", "hello world")]
    [InlineData("line1\nline2", "line1\nline2")]      // newline preserved
    [InlineData("   ", "")]                            // all-whitespace → empty
    [InlineData("a  b  c", "a b c")]
    public void NormalizeWhitespace_CollapsesHorizontalRuns(string input, string expected)
    {
        Assert.Equal(expected, SmsEncodingRules.NormalizeWhitespace(input));
    }

    [Fact]
    public void SingleSegmentCap_Gsm7Is160()
    {
        Assert.Equal(160, SmsEncodingRules.SingleSegmentCap(SmsEncoding.Gsm7));
    }

    [Fact]
    public void SingleSegmentCap_Ucs2Is70()
    {
        Assert.Equal(70, SmsEncodingRules.SingleSegmentCap(SmsEncoding.Ucs2));
    }
}
