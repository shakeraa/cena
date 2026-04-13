// =============================================================================
// Tests: PersonalizedExplanationService (SAI-003 / Task 03)
// Verifies L3 gates, scaffolding upgrade, fallback chain, budget enforcement,
// and methodology prompt mapping.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Actors.Infrastructure;
using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Diagnostics.Metrics;

namespace Cena.Actors.Tests.Services;

public sealed class PersonalizedExplanationServiceTests : IDisposable
{
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly IExplanationCacheService _cache = Substitute.For<IExplanationCacheService>();
    private readonly IMeterFactory _meterFactory = new PersonalizedMeterFactory();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly PersonalizedExplanationService _service;

    public PersonalizedExplanationServiceTests()
    {
        _service = new PersonalizedExplanationService(
            _llm, _cache,
            NullLogger<PersonalizedExplanationService>.Instance,
            _meterFactory,
            _clock);

        // Reset static token tracking between tests
        PersonalizedExplanationService.ResetDailyTokenTracking();
    }

    public void Dispose()
    {
        PersonalizedExplanationService.ResetDailyTokenTracking();
        (_meterFactory as IDisposable)?.Dispose();
    }

    // =========================================================================
    // Gate: ScaffoldingLevel.None -> redirect to L2
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ScaffoldingNone_SkipsL3_FallsBackToL2()
    {
        var cached = new CachedExplanation("L2 cached", "sonnet", 50, DateTimeOffset.UtcNow);
        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(cached);

        var ctx = CreateContext(scaffolding: ScaffoldingLevel.None);
        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("L2", result.Tier);
        Assert.Equal("L2 cached", result.Text);
        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ScaffoldingNone_NoL2_FallsToL1()
    {
        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        var ctx = CreateContext(
            scaffolding: ScaffoldingLevel.None,
            staticExplanation: "The L1 explanation text");
        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("L1", result.Tier);
        Assert.Equal("The L1 explanation text", result.Text);
    }

    // =========================================================================
    // Gate: ConfusionResolving -> suppress L3, serve L2
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ConfusionResolving_SuppressesL3_ServesL2()
    {
        var cached = new CachedExplanation("L2 during confusion", "sonnet", 30, DateTimeOffset.UtcNow);
        _cache.GetAsync("q1", ExplanationErrorType.ConceptualMisunderstanding, "he", Arg.Any<CancellationToken>())
            .Returns(cached);

        var ctx = CreateContext(
            confusionState: ConfusionState.ConfusionResolving,
            errorType: ExplanationErrorType.ConceptualMisunderstanding);
        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("L2", result.Tier);
        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Gate: Daily token budget exhausted -> fallback to L2
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_BudgetExhausted_FallsBackToL2()
    {
        // Exhaust budget by simulating prior usage
        var ctx = CreateContext();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("explanation", 100, 25_001, TimeSpan.FromSeconds(1), "sonnet", false));

        // First call: succeeds and exhausts budget
        var result1 = await _service.ResolveAsync(ctx, CancellationToken.None);
        Assert.Equal("L3", result1.Tier);

        // Second call: budget exhausted, falls to L2
        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("L2 budget fallback", "sonnet", 30, DateTimeOffset.UtcNow));

        var result2 = await _service.ResolveAsync(ctx, CancellationToken.None);
        Assert.Equal("L2", result2.Tier);
    }

    // =========================================================================
    // ConfusionStuck -> scaffolding upgrade
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ConfusionStuck_UpgradesScaffolding()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("Upgraded explanation", 100, 200, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext(
            confusionState: ConfusionState.ConfusionStuck,
            scaffolding: ScaffoldingLevel.HintsOnly);

        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("L3", result.Tier);
        // Verify the system prompt contains "Partial" scaffolding instruction (upgraded from HintsOnly)
        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.Contains("Acknowledge what the student got right", receivedRequest.SystemPrompt);
    }

    // =========================================================================
    // L3 succeeds -> returns personalized explanation
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_L3Succeeds_ReturnsPersonalized()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("Personalized L3", 100, 150, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext();
        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("L3", result.Tier);
        Assert.Equal("Personalized L3", result.Text);
        Assert.Equal(150, result.OutputTokens);
    }

    // =========================================================================
    // L3 fails -> fallback chain to L2 -> L1 -> generic
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_L3Fails_FallsToL2()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("circuit breaker open"));

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("L2 fallback", "sonnet", 40, DateTimeOffset.UtcNow));

        var ctx = CreateContext();
        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("L2", result.Tier);
        Assert.Equal("L2 fallback", result.Text);
    }

    [Fact]
    public async Task ResolveAsync_L3Fails_L2Miss_FallsToL1()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("timeout"));

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        var ctx = CreateContext(staticExplanation: "L1 static text");
        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("L1", result.Tier);
        Assert.Equal("L1 static text", result.Text);
    }

    [Fact]
    public async Task ResolveAsync_AllTiersFail_ReturnsGeneric()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("everything broken"));

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        var ctx = CreateContext(staticExplanation: null);
        var result = await _service.ResolveAsync(ctx, CancellationToken.None);

        Assert.Equal("generic", result.Tier);
        Assert.Contains("Review this concept", result.Text);
    }

    // =========================================================================
    // Socratic methodology -> system prompt asks guiding questions
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SocraticMethodology_AsksGuidingQuestions()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("What would happen if...", 100, 80, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext(methodology: "Socratic");
        await _service.ResolveAsync(ctx, CancellationToken.None);

        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.Contains("NEVER reveal the answer", receivedRequest.SystemPrompt);
    }

    // =========================================================================
    // WorkedExample methodology -> system prompt shows steps
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_WorkedExample_ShowsSteps()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("Step 1: ...", 100, 200, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext(methodology: "WorkedExample");
        await _service.ResolveAsync(ctx, CancellationToken.None);

        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.Contains("step-by-step solution", receivedRequest.SystemPrompt);
    }

    // =========================================================================
    // Feynman methodology -> challenges articulation
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_Feynman_ChallengesArticulation()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("Can you explain...", 100, 80, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext(methodology: "Feynman");
        await _service.ResolveAsync(ctx, CancellationToken.None);

        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.Contains("explain the concept", receivedRequest.SystemPrompt);
    }

    // =========================================================================
    // High backspace count -> user prompt acknowledges uncertainty
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_HighBackspaceCount_AcknowledgesUncertainty()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("You seem uncertain...", 100, 100, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext(backspaceCount: 10);
        await _service.ResolveAsync(ctx, CancellationToken.None);

        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.Contains("hesitation", receivedRequest.UserPrompt);
    }

    // =========================================================================
    // Prompt caching -> CacheSystemPrompt = true
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SetsCacheSystemPrompt()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("cached prompt", 100, 80, TimeSpan.FromSeconds(1), "sonnet", true));

        var ctx = CreateContext();
        await _service.ResolveAsync(ctx, CancellationToken.None);

        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.True(receivedRequest.CacheSystemPrompt);
    }

    // =========================================================================
    // Hebrew language -> system prompt in Hebrew
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_HebrewQuestion_GeneratesInHebrew()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("\u05D4\u05E1\u05D1\u05E8 \u05D1\u05E2\u05D1\u05E8\u05D9\u05EA", 100, 80, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext(language: "he");
        await _service.ResolveAsync(ctx, CancellationToken.None);

        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.Contains("Hebrew", receivedRequest.SystemPrompt);
    }

    // =========================================================================
    // Arabic language -> system prompt in Arabic
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ArabicQuestion_GeneratesInArabic()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("\u0634\u0631\u062D \u0628\u0627\u0644\u0639\u0631\u0628\u064A\u0629", 100, 80, TimeSpan.FromSeconds(1), "sonnet", false));

        var ctx = CreateContext(language: "ar");
        await _service.ResolveAsync(ctx, CancellationToken.None);

        var receivedRequest = _llm.ReceivedCalls().First().GetArguments()[0] as LlmRequest;
        Assert.NotNull(receivedRequest);
        Assert.Contains("Arabic", receivedRequest.SystemPrompt);
    }

    // =========================================================================
    // L3 never called when ScaffoldingLevel == None
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ScaffoldingNone_NeverCallsLlm()
    {
        _cache.GetAsync("q1", Arg.Any<ExplanationErrorType>(), "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        var ctx = CreateContext(scaffolding: ScaffoldingLevel.None, staticExplanation: "static");
        await _service.ResolveAsync(ctx, CancellationToken.None);

        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Scaffolding upgrade mapping
    // =========================================================================

    [Theory]
    [InlineData(ScaffoldingLevel.HintsOnly, ScaffoldingLevel.Partial)]
    [InlineData(ScaffoldingLevel.Partial, ScaffoldingLevel.Full)]
    [InlineData(ScaffoldingLevel.Full, ScaffoldingLevel.Full)]
    public void UpgradeScaffolding_ReturnsExpected(ScaffoldingLevel input, ScaffoldingLevel expected)
    {
        Assert.Equal(expected, PersonalizedExplanationService.UpgradeScaffolding(input));
    }

    // =========================================================================
    // Methodology mapping (all 9)
    // =========================================================================

    [Theory]
    [InlineData("Socratic", "NEVER reveal the answer")]
    [InlineData("WorkedExample", "step-by-step solution")]
    [InlineData("Feynman", "explain the concept")]
    [InlineData("Analogy", "comparison to a prerequisite")]
    [InlineData("RetrievalPractice", "What do you remember")]
    [InlineData("DirectInstruction", "what went wrong")]
    public void MapMethodology_ContainsExpectedInstruction(string methodology, string expected)
    {
        var result = PersonalizedExplanationService.MapMethodology(methodology);
        Assert.Contains(expected, result);
    }

    // =========================================================================
    // Scaffolding mapping
    // =========================================================================

    [Theory]
    [InlineData(ScaffoldingLevel.Full, "COMPLETE worked example")]
    [InlineData(ScaffoldingLevel.Partial, "Acknowledge what the student got right")]
    [InlineData(ScaffoldingLevel.HintsOnly, "brief pointer")]
    public void MapScaffolding_ContainsExpectedInstruction(ScaffoldingLevel level, string expected)
    {
        var result = PersonalizedExplanationService.MapScaffolding(level);
        Assert.Contains(expected, result);
    }

    // =========================================================================
    // HELPER: Build default context
    // =========================================================================

    private static PersonalizedExplanationContext CreateContext(
        ScaffoldingLevel scaffolding = ScaffoldingLevel.Partial,
        ConfusionState confusionState = ConfusionState.NotConfused,
        ExplanationErrorType errorType = ExplanationErrorType.ProceduralError,
        string methodology = "Direct",
        string language = "he",
        int backspaceCount = 0,
        int answerChangeCount = 0,
        string? staticExplanation = null)
    {
        return new PersonalizedExplanationContext(
            QuestionId: "q1",
            QuestionStem: "What is 2+2?",
            CorrectAnswer: "4",
            StudentAnswer: "5",
            ErrorType: errorType,
            Language: language,
            Subject: "Mathematics",
            StaticExplanation: staticExplanation,
            DistractorRationale: null,
            MasteryProbability: 0.35f,
            BloomLevel: 3,
            Scaffolding: scaffolding,
            PrerequisiteSatisfactionIndex: 0.8f,
            ActiveMethodology: methodology,
            ConfusionState: confusionState,
            DisengagementType: null,
            BackspaceCount: backspaceCount,
            AnswerChangeCount: answerChangeCount,
            HintsUsed: 0,
            ResponseTimeMs: 5000,
            MedianResponseTimeMs: 5000,
            StudentBudgetKey: "test-student-hash");
    }
}

// =============================================================================
// Minimal IMeterFactory for tests
// =============================================================================

internal sealed class PersonalizedMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = new();

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var m in _meters) m.Dispose();
    }
}
