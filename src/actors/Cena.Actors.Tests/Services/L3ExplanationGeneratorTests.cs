// =============================================================================
// Tests: L3ExplanationGenerator (SAI-004)
// Verifies affect gates, verbosity scaling, and prompt construction.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Services;

public sealed class L3ExplanationGeneratorTests
{
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly L3ExplanationGenerator _generator;

    public L3ExplanationGeneratorTests()
    {
        _generator = new L3ExplanationGenerator(
            _llm, NullLogger<L3ExplanationGenerator>.Instance);
    }

    // =========================================================================
    // AFFECT GATE: ConfusionResolving -> return null (don't interrupt)
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_ConfusionResolving_ReturnsNull()
    {
        var request = CreateRequest() with
        {
            ConfusionState = ConfusionState.ConfusionResolving
        };

        var result = await _generator.GenerateAsync(request, CancellationToken.None);

        Assert.Null(result);
        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // AFFECT GATE: Bored_TooEasy -> return null (don't over-explain)
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_BoredTooEasy_ReturnsNull()
    {
        var request = CreateRequest() with
        {
            DisengagementType = DisengagementType.Bored_TooEasy
        };

        var result = await _generator.GenerateAsync(request, CancellationToken.None);

        Assert.Null(result);
        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // AFFECT GATE: ConfusionStuck -> generates (needs scaffolding)
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_ConfusionStuck_Generates()
    {
        var request = CreateRequest() with
        {
            ConfusionState = ConfusionState.ConfusionStuck
        };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("explanation", 100, 80, TimeSpan.FromMilliseconds(500), "sonnet", false));

        var result = await _generator.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("explanation", result!.Text);
    }

    // =========================================================================
    // VERBOSITY: Flow -> 500 tokens
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_FlowFocus_Uses500Tokens()
    {
        var request = CreateRequest() with { FocusLevel = FocusLevel.Flow };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.MaxTokens == 500),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // VERBOSITY: Drifting -> 300 tokens
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_DriftingFocus_Uses300Tokens()
    {
        var request = CreateRequest() with { FocusLevel = FocusLevel.Drifting };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.MaxTokens == 300),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // VERBOSITY: Fatigued -> 200 tokens
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_FatiguedFocus_Uses200Tokens()
    {
        var request = CreateRequest() with { FocusLevel = FocusLevel.Fatigued };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.MaxTokens == 200),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // VERBOSITY: Disengaged -> 150 tokens
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_DisengagedFocus_Uses150Tokens()
    {
        var request = CreateRequest() with { FocusLevel = FocusLevel.Disengaged };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.MaxTokens == 150),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // SYSTEM PROMPT: Hebrew language
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_HebrewLanguage_PromptContainsHebrew()
    {
        var request = CreateRequest() with { Language = "he" };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.SystemPrompt.Contains("Hebrew")),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // SYSTEM PROMPT: Arabic language
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_ArabicLanguage_PromptContainsArabic()
    {
        var request = CreateRequest() with { Language = "ar" };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.SystemPrompt.Contains("Arabic")),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // SYSTEM PROMPT: Methodology constraint
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_SocraticMethodology_PromptContainsSocratic()
    {
        var request = CreateRequest() with { Methodology = "Socratic" };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.SystemPrompt.Contains("Socratic")),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // USER PROMPT: High backspace count -> hesitation note
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_HighBackspaceCount_IncludesHesitationNote()
    {
        var request = CreateRequest() with { BackspaceCount = 10 };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.UserPrompt.Contains("hesitation")),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // USER PROMPT: Multiple answer changes -> uncertainty note
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_MultipleAnswerChanges_IncludesUncertaintyNote()
    {
        var request = CreateRequest() with { AnswerChangeCount = 3 };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.UserPrompt.Contains("changed their answer")),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // USER PROMPT: Low PSI -> prerequisite gap note
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_LowPsi_IncludesPrerequisiteGapNote()
    {
        var request = CreateRequest() with { Psi = 0.3 };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        await _generator.GenerateAsync(request, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.UserPrompt.Contains("weak prerequisite")),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // NORMAL GENERATION: returns text from LLM
    // =========================================================================

    [Fact]
    public async Task GenerateAsync_NormalRequest_ReturnsLlmOutput()
    {
        var request = CreateRequest();

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(
                "The key concept here is...", 150, 80,
                TimeSpan.FromMilliseconds(800), "claude-sonnet-4-6-20260215", false));

        var result = await _generator.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("The key concept here is...", result!.Text);
        Assert.Equal("claude-sonnet-4-6-20260215", result.ModelId);
        Assert.Equal(80, result.TokenCount);
    }

    // =========================================================================
    // OTHER DISENGAGEMENT TYPES: not gated (only Bored_TooEasy is)
    // =========================================================================

    [Theory]
    [InlineData(DisengagementType.Bored_NoValue)]
    [InlineData(DisengagementType.Fatigued_Cognitive)]
    [InlineData(DisengagementType.Fatigued_Motor)]
    [InlineData(DisengagementType.Mixed)]
    [InlineData(DisengagementType.Unknown)]
    public async Task GenerateAsync_OtherDisengagementTypes_StillGenerates(
        DisengagementType disengagement)
    {
        var request = CreateRequest() with { DisengagementType = disengagement };

        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ok", 100, 50, TimeSpan.FromMilliseconds(500), "sonnet", false));

        var result = await _generator.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(result);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static L3ExplanationRequest CreateRequest() => new()
    {
        QuestionId = "q1",
        QuestionStem = "What is 2+2?",
        CorrectAnswer = "4",
        StudentAnswer = "5",
        ErrorType = "ConceptualMisunderstanding",
        Subject = "Mathematics",
        Language = "he",
        MasteryProbability = 0.45,
        RecallProbability = 0.60,
        BloomLevel = 3,
        Psi = 0.85,
        QualityQuadrant = MasteryQuality.Effortful,
        ScaffoldingLevel = ScaffoldingLevel.Partial,
        Methodology = "Socratic",
        FocusLevel = FocusLevel.Engaged,
        ConfusionState = ConfusionState.NotConfused,
        BackspaceCount = 0,
        AnswerChangeCount = 0,
        ResponseTimeMs = 12000,
        MedianResponseTimeMs = 10000
    };
}
