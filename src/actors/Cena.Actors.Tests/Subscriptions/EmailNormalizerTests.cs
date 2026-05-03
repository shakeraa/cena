// =============================================================================
// Cena Platform — EmailNormalizer tests (per-user discount-codes feature)
//
// Coverage matrix:
//   Validity check
//     - Reject null / blank / whitespace
//     - Reject missing @ / multiple @ / leading or trailing @
//     - Reject domain without dot
//     - Reject domain starting/ending with dot
//     - Accept simple shapes: a@b.c, alice@gmail.com, etc.
//   Normalization
//     - Lowercase the local part + domain (Alice@Gmail.com → alice@gmail.com)
//     - Trim leading/trailing whitespace
//     - Gmail dot-stripping (a.l.i.c.e@gmail.com → alice@gmail.com)
//     - Gmail plus-stripping (alice+study@gmail.com → alice@gmail.com)
//     - googlemail.com → gmail.com
//     - Combination: A.L.I.C.E+study@GoogleMail.com → alice@gmail.com
//     - Non-Gmail addresses keep their dots and pluses (alice.smith+x@yahoo.com)
//     - Idempotent — Normalize(Normalize(x)) == Normalize(x)
//     - Bad inputs return string.Empty (not null)
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class EmailNormalizerTests
{
    // ---- IsValidShape ------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain")]
    [InlineData("@no-local.com")]
    [InlineData("no-at-sign")]
    [InlineData("missing@domain")]
    [InlineData("missing-tld@local")]
    [InlineData("two@@at.signs")]
    [InlineData("alice@@gmail.com")]
    [InlineData("trailing-@.com")]
    [InlineData("alice@.com")]
    [InlineData("alice@gmail.com.")]
    [InlineData("white space@gmail.com")]
    public void IsValidShape_rejects_invalid(string? input)
    {
        Assert.False(EmailNormalizer.IsValidShape(input));
    }

    [Theory]
    [InlineData("a@b.co")]
    [InlineData("alice@gmail.com")]
    [InlineData("alice.smith@example.org")]
    [InlineData("alice+study@gmail.com")]
    [InlineData("AlIcE@MaIl.com")]
    public void IsValidShape_accepts_valid(string input)
    {
        Assert.True(EmailNormalizer.IsValidShape(input));
    }

    // ---- Normalize: trivial cases -----------------------------------------

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("not-an-email", "")]
    [InlineData("@no-local.com", "")]
    [InlineData("no-at-sign", "")]
    public void Normalize_returns_empty_for_bad_input(string? input, string expected)
    {
        Assert.Equal(expected, EmailNormalizer.Normalize(input));
    }

    // ---- Normalize: lowercasing + trimming --------------------------------

    [Fact]
    public void Normalize_lowercases_local_and_domain()
    {
        Assert.Equal("alice@example.com",
            EmailNormalizer.Normalize("Alice@Example.Com"));
    }

    [Fact]
    public void Normalize_trims_whitespace()
    {
        Assert.Equal("alice@example.com",
            EmailNormalizer.Normalize("   Alice@Example.com   "));
    }

    // ---- Normalize: Gmail dot/+ folding ------------------------------------

    [Theory]
    [InlineData("a.l.i.c.e@gmail.com", "alice@gmail.com")]
    [InlineData("alice@gmail.com", "alice@gmail.com")]
    [InlineData("alice+anything@gmail.com", "alice@gmail.com")]
    [InlineData("alice+study+more@gmail.com", "alice@gmail.com")]
    [InlineData("a.lice+study@gmail.com", "alice@gmail.com")]
    [InlineData("Alice.Smith+work@GMAIL.COM", "alicesmith@gmail.com")]
    [InlineData("alice@googlemail.com", "alice@gmail.com")]
    [InlineData("A.L.I.C.E+study@GoogleMail.COM", "alice@gmail.com")]
    public void Normalize_applies_gmail_folding(string input, string expected)
    {
        Assert.Equal(expected, EmailNormalizer.Normalize(input));
    }

    // ---- Normalize: non-Gmail keeps dots / plus ---------------------------

    [Theory]
    [InlineData("alice.smith@yahoo.com", "alice.smith@yahoo.com")]
    [InlineData("alice+study@example.com", "alice+study@example.com")]
    [InlineData("ALICE+study@Outlook.COM", "alice+study@outlook.com")]
    public void Normalize_does_not_fold_non_gmail(string input, string expected)
    {
        Assert.Equal(expected, EmailNormalizer.Normalize(input));
    }

    // ---- Idempotency ------------------------------------------------------

    [Theory]
    [InlineData("Alice@Example.com")]
    [InlineData("a.l.i.c.e+x@gmail.com")]
    [InlineData("ALICE@GOOGLEMAIL.COM")]
    [InlineData("plain@example.com")]
    public void Normalize_is_idempotent(string input)
    {
        var once = EmailNormalizer.Normalize(input);
        var twice = EmailNormalizer.Normalize(once);
        Assert.Equal(once, twice);
    }

    // ---- Edge: Gmail with only dots collapses to non-empty ----------------

    [Fact]
    public void Normalize_drops_dots_in_gmail_local_part()
    {
        Assert.Equal("a@gmail.com",
            EmailNormalizer.Normalize("a.@gmail.com"));
    }

    [Fact]
    public void Normalize_drops_only_first_plus_segment_for_gmail()
    {
        // Per Gmail: everything after FIRST '+' is alias. Multiple plus
        // segments collapse to nothing because we cut at first '+'.
        Assert.Equal("alice@gmail.com",
            EmailNormalizer.Normalize("alice+a+b+c@gmail.com"));
    }

    [Fact]
    public void Normalize_keeps_googlemail_local_dots_collapsed()
    {
        Assert.Equal("alice@gmail.com",
            EmailNormalizer.Normalize("a.lice@googlemail.com"));
    }
}
