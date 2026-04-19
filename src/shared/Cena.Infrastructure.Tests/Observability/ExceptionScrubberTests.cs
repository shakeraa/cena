// =============================================================================
// RDY-064: ExceptionScrubber tests
//
// Proves that PII does not leave the process in an exception payload. One
// test per pattern, plus the ScrubException wrapping behaviour.
// =============================================================================

using Cena.Infrastructure.Observability.ErrorAggregator;
using Xunit;

namespace Cena.Infrastructure.Tests.Observability;

public class ExceptionScrubberTests
{
    private readonly ExceptionScrubber _scrubber = new();

    [Theory]
    [InlineData("sara.cohen@school.edu", "email")]
    [InlineData("User email yael@gmail.com raised NRE", "email")]
    public void Email_is_redacted(string input, string category)
    {
        var result = _scrubber.Scrub(input);
        Assert.Contains($"<redacted:{category}>", result);
        Assert.DoesNotContain("@", result);
    }

    [Theory]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    public void Jwt_is_redacted(string input)
    {
        var result = _scrubber.Scrub(input);
        Assert.Contains("<redacted:jwt>", result);
        Assert.DoesNotContain("eyJ", result);
    }

    [Fact]
    public void Bearer_token_is_redacted()
    {
        var input = "Authorization: Bearer ABCDEFGHIJ1234567890xyz";
        var result = _scrubber.Scrub(input);
        Assert.Contains("<redacted:bearer>", result);
    }

    [Theory]
    [InlineData("+972-54-1234567")]
    [InlineData("(054) 123-4567")]
    [InlineData("+1 555 123 4567")]
    public void Phone_is_redacted(string input)
    {
        var result = _scrubber.Scrub(input);
        Assert.Contains("<redacted:phone>", result);
    }

    [Theory]
    [InlineData("192.168.0.1", "ipv4")]
    [InlineData("10.0.0.255", "ipv4")]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334", "ipv6")]
    public void Ip_address_is_redacted(string input, string category)
    {
        var result = _scrubber.Scrub(input);
        Assert.Contains($"<redacted:{category}>", result);
    }

    [Fact]
    public void Israeli_id_is_redacted()
    {
        var result = _scrubber.Scrub("teudat zehut 123456789");
        Assert.Contains("<redacted:id>", result);
    }

    [Theory]
    [InlineData("studentId=abc123xyz")]
    [InlineData("student_id=XYZ-789")]
    [InlineData("studentId: pupil42")]
    public void Student_id_marker_is_redacted(string input)
    {
        var result = _scrubber.Scrub(input);
        Assert.Contains("<redacted:student>", result);
    }

    [Fact]
    public void Empty_input_passes_through()
    {
        Assert.Equal(string.Empty, _scrubber.Scrub(null));
        Assert.Equal(string.Empty, _scrubber.Scrub(string.Empty));
    }

    [Fact]
    public void Plain_text_without_pii_is_unchanged()
    {
        var input = "Step 1: simplify. Step 2: factor.";
        Assert.Equal(input, _scrubber.Scrub(input));
    }

    [Fact]
    public void ScrubException_wraps_message_and_preserves_type_name()
    {
        var inner = new InvalidOperationException("student email yael@school.il timed out");
        var outer = new InvalidOperationException("wrapper", inner);

        var scrubbed = _scrubber.ScrubException(outer) as ScrubbedException;

        Assert.NotNull(scrubbed);
        Assert.Equal(typeof(InvalidOperationException).FullName, scrubbed!.OriginalTypeName);
        Assert.DoesNotContain("@school.il", scrubbed.ToString());
        Assert.IsType<ScrubbedException>(scrubbed.InnerException);
        Assert.Contains("<redacted:email>", scrubbed.InnerException!.Message);
    }

    [Fact]
    public void ScrubException_handles_null_stack_trace()
    {
        var ex = new ArgumentNullException("fake");
        var scrubbed = _scrubber.ScrubException(ex);
        Assert.NotNull(scrubbed);
    }
}
