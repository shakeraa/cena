// =============================================================================
// Cena Platform -- SafeguardingClassifier Tests (FIND-privacy-008)
// Asserts safeguarding keyword detection for self-harm, abuse, and bullying.
// Also asserts normal academic text does NOT trigger the classifier.
// =============================================================================

using Cena.Actors.Tutor;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Tutor;

public sealed class SafeguardingClassifierTests
{
    private readonly SafeguardingClassifier _sut = new(NullLogger<SafeguardingClassifier>.Instance);

    // ── HIGH severity: self-harm / suicidal ideation ─────────────────────

    [Theory]
    [InlineData("I want to hurt myself")]
    [InlineData("I want to kill myself")]
    [InlineData("I want to die")]
    [InlineData("I don't want to live anymore")]
    [InlineData("i don't want to be alive")]
    [InlineData("I've been cutting myself")]
    [InlineData("I feel like committing suicide")]
    public void Scan_SelfHarmEnglish_ReturnsHighSeverity(string input)
    {
        var result = _sut.Scan(input);

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.High, result.Severity);
        Assert.NotEmpty(result.TriggeredCategories);
    }

    [Theory]
    [InlineData("אני רוצה להרוג את עצמי")]
    [InlineData("אני רוצה למות")]
    [InlineData("לא רוצה לחיות")]
    [InlineData("לפגוע בעצמי")]
    public void Scan_SelfHarmHebrew_ReturnsHighSeverity(string input)
    {
        var result = _sut.Scan(input);

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.High, result.Severity);
    }

    [Theory]
    [InlineData("اريد ان اقتل نفسي")]
    [InlineData("أريد أن أموت")]
    [InlineData("لا أريد العيش")]
    [InlineData("إيذاء نفسي")]
    public void Scan_SelfHarmArabic_ReturnsHighSeverity(string input)
    {
        var result = _sut.Scan(input);

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.High, result.Severity);
    }

    // ── HIGH severity: abuse disclosure ──────────────────────────────────

    [Theory]
    [InlineData("Someone is hurting me at home")]
    [InlineData("My dad hits me every day")]
    [InlineData("I am being abused")]
    [InlineData("Someone touches me in a bad way")]
    [InlineData("I was raped")]
    public void Scan_AbuseDisclosure_ReturnsHighSeverity(string input)
    {
        var result = _sut.Scan(input);

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.High, result.Severity);
        Assert.NotEmpty(result.TriggeredCategories);
    }

    // ── HIGH severity: predatory contact ─────────────────────────────────

    [Theory]
    [InlineData("An adult asked me to send nudes")]
    [InlineData("A stranger online wants to meet up in secret")]
    public void Scan_PredatoryContact_ReturnsHighSeverity(string input)
    {
        var result = _sut.Scan(input);

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.High, result.Severity);
    }

    // ── MEDIUM severity: bullying / emotional distress ───────────────────

    [Theory]
    [InlineData("Everyone hates me at school")]
    [InlineData("I am being bullied")]
    [InlineData("Nobody likes me")]
    [InlineData("I hate my life")]
    [InlineData("I want to run away from home")]
    public void Scan_EmotionalDistress_ReturnsMediumSeverity(string input)
    {
        var result = _sut.Scan(input);

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.Medium, result.Severity);
        Assert.Contains("emotional_distress", result.TriggeredCategories);
    }

    // ── No concern: normal academic text ─────────────────────────────────

    [Theory]
    [InlineData("What is photosynthesis?")]
    [InlineData("Help me solve this equation")]
    [InlineData("Explain the water cycle")]
    [InlineData("I don't understand fractions")]
    [InlineData("Can you help me with my homework?")]
    [InlineData("Why is the sky blue?")]
    [InlineData("What causes earthquakes?")]
    [InlineData("How do plants grow?")]
    public void Scan_NormalAcademicText_ReturnsNoConcern(string input)
    {
        var result = _sut.Scan(input);

        Assert.False(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.None, result.Severity);
        Assert.Empty(result.TriggeredCategories);
    }

    [Fact]
    public void Scan_EmptyInput_ReturnsNoConcern()
    {
        var result = _sut.Scan("");

        Assert.False(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.None, result.Severity);
    }

    [Fact]
    public void Scan_WhitespaceInput_ReturnsNoConcern()
    {
        var result = _sut.Scan("   ");

        Assert.False(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.None, result.Severity);
    }

    // ── Safeguarding response has helpline info ──────────────────────────

    [Theory]
    [InlineData("GB", "NSPCC Childline", "0800 1111")]
    [InlineData("UK", "NSPCC Childline", "0800 1111")]
    [InlineData("US", "Childhelp", "1-800-422-4453")]
    [InlineData("IL", "ERAN", "1201")]
    public void GetSafeguardingResponse_PerMarket_ContainsHelpline(
        string market, string expectedName, string expectedNumber)
    {
        var response = _sut.GetSafeguardingResponse(market);

        Assert.Contains(expectedName, response);
        Assert.Contains(expectedNumber, response);
        Assert.Contains("trusted adult", response);
    }

    [Fact]
    public void GetSafeguardingResponse_UnknownMarket_ReturnsGenericResponse()
    {
        var response = _sut.GetSafeguardingResponse("XX");

        Assert.Contains("trusted adult", response);
        Assert.Contains("local child helpline", response);
    }

    [Fact]
    public void GetSafeguardingResponse_NullMarket_ReturnsGenericResponse()
    {
        var response = _sut.GetSafeguardingResponse(null);

        Assert.Contains("trusted adult", response);
    }

    // ── DoD fixture test: "I want to hurt myself" ────────────────────────

    [Fact]
    public void Scan_DoDFixture_IWantToHurtMyself_TriggersConcern()
    {
        // From the Definition of Done:
        // "Safeguarding test: a fixture message containing 'I want to hurt myself'
        //  triggers the classifier, suppresses the Anthropic call, and creates a
        //  SafeguardingAlert."
        var result = _sut.Scan("I want to hurt myself");

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.High, result.Severity);
        Assert.True(result.TriggeredCategories.Count > 0);
    }

    // ── Regex-based detection ────────────────────────────────────────────

    [Theory]
    [InlineData("I've been cutting my wrists")]
    [InlineData("I want to overdose on pills")]
    [InlineData("I'm going to swallow pills")]
    public void Scan_RegexSelfHarmPatterns_ReturnsHighSeverity(string input)
    {
        var result = _sut.Scan(input);

        Assert.True(result.IsConcern);
        Assert.Equal(SafeguardingSeverity.High, result.Severity);
        Assert.Contains("self_harm", result.TriggeredCategories);
    }

    // ── Categories are correctly assigned ────────────────────────────────

    [Fact]
    public void Scan_SuicidalIdeation_CategorizedCorrectly()
    {
        var result = _sut.Scan("I want to kill myself");

        Assert.Contains("suicidal_ideation", result.TriggeredCategories);
    }

    [Fact]
    public void Scan_AbuseDisclosure_CategorizedAsAbuse()
    {
        var result = _sut.Scan("My dad hits me");

        Assert.Contains("abuse", result.TriggeredCategories);
    }
}
