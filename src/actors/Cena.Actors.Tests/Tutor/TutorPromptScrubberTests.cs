// =============================================================================
// Cena Platform -- TutorPromptScrubber Tests (FIND-privacy-008)
// Asserts PII scrubbing strips known student data and generic PII patterns.
// =============================================================================

using Cena.Actors.Tutor;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Tutor;

public sealed class TutorPromptScrubberTests
{
    private readonly TutorPromptScrubber _sut = new(NullLogger<TutorPromptScrubber>.Instance);

    private static StudentPiiContext TestStudent => new(
        StudentId: "student-1",
        FirstName: "Yael",
        LastName: "Cohen",
        Email: "yael.cohen@school.edu",
        SchoolName: "Herzliya High School",
        ParentName: "David Cohen",
        City: "Tel Aviv");

    // ── Known PII scrubbing ──────────────────────────────────────────────

    [Fact]
    public void Scrub_RedactsFullName()
    {
        var result = _sut.Scrub("My name is Yael Cohen and I need help.", TestStudent);

        Assert.DoesNotContain("Yael Cohen", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted:name>", result.ScrubbedText);
        Assert.True(result.RedactionCount > 0);
        Assert.Contains("name", result.RedactionCategories);
    }

    [Fact]
    public void Scrub_RedactsLastNameAlone()
    {
        var result = _sut.Scrub("Ask Mr. Cohen for help", TestStudent);

        Assert.DoesNotContain("Cohen", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted:name>", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_RedactsEmail()
    {
        var result = _sut.Scrub("My email is yael.cohen@school.edu", TestStudent);

        Assert.DoesNotContain("yael.cohen@school.edu", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted:email>", result.ScrubbedText);
        Assert.Contains("email", result.RedactionCategories);
    }

    [Fact]
    public void Scrub_RedactsSchoolName()
    {
        var result = _sut.Scrub("I go to Herzliya High School", TestStudent);

        Assert.DoesNotContain("Herzliya High School", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted:school>", result.ScrubbedText);
        Assert.Contains("school", result.RedactionCategories);
    }

    [Fact]
    public void Scrub_RedactsParentName()
    {
        var result = _sut.Scrub("My dad is David Cohen", TestStudent);

        Assert.DoesNotContain("David Cohen", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted:parent>", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_RedactsCity()
    {
        var result = _sut.Scrub("I live in Tel Aviv", TestStudent);

        Assert.DoesNotContain("Tel Aviv", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted:location>", result.ScrubbedText);
    }

    // ── Generic PII pattern scrubbing ────────────────────────────────────

    [Fact]
    public void Scrub_RedactsUnknownEmail()
    {
        var result = _sut.Scrub("Contact me at alice@example.com", TestStudent);

        Assert.DoesNotContain("alice@example.com", result.ScrubbedText);
        Assert.Contains("<redacted:email>", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_RedactsPhoneNumber()
    {
        var result = _sut.Scrub("Call me at 054-1234567", TestStudent);

        Assert.DoesNotContain("054-1234567", result.ScrubbedText);
        Assert.Contains("<redacted:phone>", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_RedactsInternationalPhoneNumber()
    {
        var result = _sut.Scrub("My number is +972-54-1234567", TestStudent);

        Assert.DoesNotContain("+972-54-1234567", result.ScrubbedText);
        Assert.Contains("<redacted:", result.ScrubbedText);
    }

    // ── Edge cases ───────────────────────────────────────────────────────

    [Fact]
    public void Scrub_NormalAcademicText_NoRedaction()
    {
        var result = _sut.Scrub("What is the formula for photosynthesis?", TestStudent);

        Assert.Equal("What is the formula for photosynthesis?", result.ScrubbedText);
        Assert.Equal(0, result.RedactionCount);
    }

    [Fact]
    public void Scrub_EmptyString_ReturnsEmpty()
    {
        var result = _sut.Scrub("", TestStudent);

        Assert.Equal("", result.ScrubbedText);
        Assert.Equal(0, result.RedactionCount);
    }

    [Fact]
    public void Scrub_NullFirstName_DoesNotCrash()
    {
        var pii = TestStudent with { FirstName = null, LastName = null };
        var result = _sut.Scrub("No PII here", pii);

        Assert.Equal("No PII here", result.ScrubbedText);
        Assert.Equal(0, result.RedactionCount);
    }

    [Fact]
    public void Scrub_CaseInsensitive_RedactsUpperCaseName()
    {
        var result = _sut.Scrub("YAEL COHEN is here", TestStudent);

        Assert.DoesNotContain("YAEL", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COHEN", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scrub_MultiplePiiInOneMessage_AllRedacted()
    {
        var result = _sut.Scrub(
            "I'm Yael Cohen from Herzliya High School. Email: yael.cohen@school.edu. My dad David Cohen lives in Tel Aviv.",
            TestStudent);

        Assert.DoesNotContain("Yael Cohen", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Herzliya High School", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("yael.cohen@school.edu", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("David Cohen", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tel Aviv", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.RedactionCount >= 5);
    }

    // ── Pact-style assertion: outbound payload zero known-PII substrings ─

    [Fact]
    public void Scrub_PactAssertion_OutputContainsZeroKnownPiiSubstrings()
    {
        // This test mirrors the "Pact test against a mock Anthropic asserting
        // outbound payload contains zero substrings from a known-PII test fixture"
        // from the DoD.
        var knownPiiFixture = new[]
        {
            "Yael Cohen", "yael.cohen@school.edu", "Herzliya High School",
            "David Cohen", "Tel Aviv", "Cohen"
        };

        var input = "Hi, I'm Yael Cohen, my email is yael.cohen@school.edu, " +
                    "I go to Herzliya High School in Tel Aviv. My dad David Cohen says hi.";

        var result = _sut.Scrub(input, TestStudent);

        foreach (var pii in knownPiiFixture)
        {
            Assert.DoesNotContain(pii, result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
