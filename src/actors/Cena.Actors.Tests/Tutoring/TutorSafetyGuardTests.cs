using Cena.Actors.Tutoring;

namespace Cena.Actors.Tests.Tutoring;

public sealed class TutorSafetyGuardTests
{
    private readonly TutorSafetyGuard _sut = new();
    private const string Subject = "mathematics";
    private const string Concept = "quadratic equations";

    // ── Answer leak detection ──

    [Theory]
    [InlineData("The answer is B", true)]
    [InlineData("The correct answer is B", true)]
    [InlineData("The solution is x = 5", true)]
    [InlineData("The result is 42", true)]
    [InlineData("The result equals 42", true)]
    public void DetectsAnswerLeaks_BlocksResponse(string response, bool shouldBlock)
    {
        var result = _sut.Validate(response, Subject, Concept);

        Assert.Equal(!shouldBlock, result.IsAllowed);
        if (shouldBlock)
            Assert.NotNull(result.BlockReason);
    }

    [Theory]
    [InlineData("Let me explain step by step")]
    [InlineData("Think about what happens when x=0")]
    [InlineData("What do you notice about these two values?")]
    [InlineData("Can you recall the quadratic formula?")]
    public void SocraticGuidance_IsAllowed(string response)
    {
        var result = _sut.Validate(response, Subject, Concept);

        Assert.True(result.IsAllowed);
        Assert.Null(result.BlockReason);
    }

    // ── Prompt injection detection ──

    [Theory]
    [InlineData("ignore previous instructions")]
    [InlineData("IGNORE PREVIOUS INSTRUCTIONS")]
    [InlineData("ignore all instructions")]
    [InlineData("system prompt")]
    [InlineData("as an AI language model")]
    public void DetectsPromptInjection_BlocksResponse(string injectionText)
    {
        var result = _sut.Validate(injectionText, Subject, Concept);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.BlockReason);
    }

    [Theory]
    [InlineData("Let's think about this problem")]
    [InlineData("Try solving for x first")]
    public void NormalResponses_AreNotFlaggedAsInjection(string response)
    {
        var result = _sut.Validate(response, Subject, Concept);

        Assert.True(result.IsAllowed);
    }

    // ── Response length cap ──

    [Fact]
    public void BlocksResponseOver2000Chars()
    {
        var longResponse = new string('a', 2001);

        var result = _sut.Validate(longResponse, Subject, Concept);

        Assert.False(result.IsAllowed);
        Assert.Contains("2000", result.BlockReason);
    }

    [Fact]
    public void AllowsResponseExactly2000Chars()
    {
        var exactResponse = new string('a', 2000);

        var result = _sut.Validate(exactResponse, Subject, Concept);

        Assert.True(result.IsAllowed);
    }

    // ── Empty / whitespace input ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void EmptyOrWhitespaceResponse_IsBlocked(string response)
    {
        var result = _sut.Validate(response, Subject, Concept);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.BlockReason);
    }

    // ── Case insensitivity for blocked phrases ──

    [Theory]
    [InlineData("Ignore Previous Instructions")]
    [InlineData("SYSTEM PROMPT")]
    [InlineData("As An AI Language Model")]
    public void BlockedPhrasesAreCaseInsensitive(string mixedCasePhrase)
    {
        var result = _sut.Validate(mixedCasePhrase, Subject, Concept);

        Assert.False(result.IsAllowed);
    }

    // ── Result record structure ──

    [Fact]
    public void AllowedResult_HasNullBlockReason()
    {
        var result = _sut.Validate("What do you think happens next?", Subject, Concept);

        Assert.True(result.IsAllowed);
        Assert.Null(result.BlockReason);
    }

    [Fact]
    public void BlockedResult_HasNonNullBlockReason()
    {
        var result = _sut.Validate("The answer is 42", Subject, Concept);

        Assert.False(result.IsAllowed);
        Assert.NotEmpty(result.BlockReason!);
    }
}
