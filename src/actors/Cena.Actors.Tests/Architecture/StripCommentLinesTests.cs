// =============================================================================
// Cena Platform — StripCommentLines lexer tests (PRR-304)
//
// ConsentAggregateNoProfileCouplingTest.StripCommentLines was rewritten
// from a naïve line-walking stripper into a char-level lexer that tracks
// string/char literal state. The stripper is the bedrock of an arch test
// that decides whether a non-Consent file "couples" to ConsentAggregate.
// A bug here = either silent false-negative (miss a real coupling) or a
// noisy false-positive (flag a doc-comment).
//
// These tests pin the lexer's behaviour at the edges that the line-walker
// got wrong:
//   • // and /* … */ inside regular and verbatim strings are NOT comments
//   • verbatim strings ("") and regular escapes (\") are honoured
//   • interpolated strings $"..." don't parse the interpolation but the
//     symbol body is preserved (that's the desired arch-failure mode)
//   • char literals don't terminate string state
// =============================================================================

namespace Cena.Actors.Tests.Architecture;

public sealed class StripCommentLinesTests
{
    private static string Strip(string s) =>
        ConsentAggregateNoProfileCouplingTest.StripCommentLines(s);

    [Fact]
    public void Drops_line_comments()
    {
        var input = "var x = 1; // ConsentAggregate is mentioned here\nvar y = 2;";
        var output = Strip(input);

        Assert.DoesNotContain("ConsentAggregate", output);
        Assert.Contains("var x = 1;", output);
        Assert.Contains("var y = 2;", output);
    }

    [Fact]
    public void Drops_xml_doc_comments()
    {
        var input = "/// <summary>Wires ConsentAggregate.</summary>\npublic void Foo() {}";
        var output = Strip(input);

        Assert.DoesNotContain("ConsentAggregate", output);
        Assert.Contains("public void Foo()", output);
    }

    [Fact]
    public void Drops_single_line_block_comments()
    {
        var input = "var x /* ConsentAggregate */ = 1;";
        var output = Strip(input);

        Assert.DoesNotContain("ConsentAggregate", output);
        Assert.Contains("var x", output);
        Assert.Contains("= 1;", output);
    }

    [Fact]
    public void Drops_multi_line_block_comments_but_preserves_newlines()
    {
        var input = "var a = 1;\n/* line one\nConsentAggregate\nline three */\nvar b = 2;";
        var output = Strip(input);

        Assert.DoesNotContain("ConsentAggregate", output);
        Assert.Contains("var a = 1;", output);
        Assert.Contains("var b = 2;", output);
        // Line numbers preserved across the block (4 newlines total).
        Assert.Equal(4, output.Count(c => c == '\n'));
    }

    [Fact]
    public void Preserves_double_slash_inside_regular_string()
    {
        // The original line-walker dropped the URL fragment as a tail comment,
        // which would false-NEGATIVE on a coupling like
        // logger.LogError("see https://docs/ConsentAggregate for invariant…").
        var input = "var url = \"https://docs/ConsentAggregate\";";
        var output = Strip(input);

        Assert.Contains("ConsentAggregate", output);
        Assert.Contains("https://", output);
    }

    [Fact]
    public void Preserves_block_comment_delimiters_inside_regular_string()
    {
        var input = "var pat = \"/* not a comment */\";\nvar y = 1;";
        var output = Strip(input);

        Assert.Contains("/* not a comment */", output);
        Assert.Contains("var y = 1;", output);
    }

    [Fact]
    public void Preserves_double_slash_inside_verbatim_string()
    {
        var input = "var url = @\"https://example/ConsentAggregate\";";
        var output = Strip(input);

        Assert.Contains("ConsentAggregate", output);
        Assert.Contains("https://", output);
    }

    [Fact]
    public void Honours_escaped_quote_in_regular_string()
    {
        // "She said \"// comment\" but it isn't" — the escaped " should not
        // close the string, so // remains inside the string body.
        var input = "var msg = \"She said \\\"// ConsentAggregate\\\" out loud\";\nvar y = 1;";
        var output = Strip(input);

        Assert.Contains("ConsentAggregate", output);
        Assert.Contains("var y = 1;", output);
    }

    [Fact]
    public void Honours_doubled_quote_in_verbatim_string()
    {
        // Verbatim strings escape " by doubling it: @"foo""bar".
        var input = "var msg = @\"foo\"\"bar/* still in string */baz\";\nvar y = 1;";
        var output = Strip(input);

        Assert.Contains("/* still in string */", output);
        Assert.Contains("var y = 1;", output);
    }

    [Fact]
    public void Char_literal_with_quote_does_not_open_a_string()
    {
        // Pre-fix scanner could mistake the closing ' as opening a string,
        // and then the next " would seem to close it, causing havoc.
        var input = "var c = '\\\"'; // ConsentAggregate after\nvar y = 1;";
        var output = Strip(input);

        // The line comment should still be stripped despite the char literal.
        Assert.DoesNotContain("ConsentAggregate", output);
        Assert.Contains("var y = 1;", output);
    }

    [Fact]
    public void Interpolated_string_preserves_body()
    {
        // For an arch-coupling test, leaking a symbol name into an interpolation
        // expression IS coupling we want to flag.
        var input = "var s = $\"ref={ConsentAggregate.StreamPrefix}\";";
        var output = Strip(input);

        Assert.Contains("ConsentAggregate", output);
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal("", Strip(""));
    }

    [Fact]
    public void Pure_block_comment_input_strips_to_whitespace_only()
    {
        var input = "/* only ConsentAggregate */";
        var output = Strip(input);
        Assert.DoesNotContain("ConsentAggregate", output);
    }

    [Fact]
    public void Trailing_line_comment_strips_only_after_slashes()
    {
        var input = "var x = 1; // tail ConsentAggregate";
        var output = Strip(input);
        Assert.Contains("var x = 1;", output);
        Assert.DoesNotContain("ConsentAggregate", output);
    }

    [Fact]
    public void Combination_string_and_real_comment_each_handled_correctly()
    {
        // Real-world shape: a URL string AND a tail comment on the same line.
        var input = "var u = \"https://docs/ConsentAggregate\"; // also ConsentCommandHandler";
        var output = Strip(input);

        Assert.Contains("ConsentAggregate", output);          // URL preserved
        Assert.DoesNotContain("ConsentCommandHandler", output); // tail-comment stripped
    }
}
