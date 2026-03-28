// =============================================================================
// Tests: ExplanationOrchestrator (SAI-02)
// Verifies the classify -> cache -> generate -> fallback chain.
// =============================================================================

using Cena.Actors.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cena.Actors.Tests.Services;

public sealed class ExplanationOrchestratorTests
{
    private readonly IExplanationCacheService _cache = Substitute.For<IExplanationCacheService>();
    private readonly IExplanationGenerator _generator = Substitute.For<IExplanationGenerator>();
    private readonly IL3ExplanationGenerator _l3Generator = Substitute.For<IL3ExplanationGenerator>();
    private readonly IErrorClassificationService _classifier = Substitute.For<IErrorClassificationService>();
    private readonly ExplanationOrchestrator _orchestrator;

    public ExplanationOrchestratorTests()
    {
        _orchestrator = new ExplanationOrchestrator(
            _cache, _generator, _l3Generator, _classifier,
            NullLogger<ExplanationOrchestrator>.Instance);
    }

    // =========================================================================
    // CLASSIFY -> CACHE HIT -> return immediately (zero LLM generation cost)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_CacheHit_ReturnsWithoutGeneration()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ProceduralError);

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("Cached explanation", "sonnet", 30, DateTimeOffset.UtcNow));

        var result = await _orchestrator.ResolveAsync(CreateRequest("q1"), CancellationToken.None);

        Assert.Equal("Cached explanation", result);
        await _generator.DidNotReceive().GenerateAsync(Arg.Any<ExplanationContext>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // CLASSIFY -> CACHE MISS -> GENERATE -> CACHE -> return
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_CacheMiss_GeneratesAndCaches()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ConceptualMisunderstanding);

        _cache.GetAsync("q1", ExplanationErrorType.ConceptualMisunderstanding, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);
        _cache.GetAsync("q1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        _generator.GenerateAsync(Arg.Any<ExplanationContext>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedExplanation("Generated explanation", "claude-sonnet-4-6-20260215", 80));

        var result = await _orchestrator.ResolveAsync(CreateRequest("q1"), CancellationToken.None);

        Assert.Equal("Generated explanation", result);

        // Verify cache write was issued (fire-and-forget)
        await _cache.Received(1).SetAsync(
            "q1",
            ExplanationErrorType.ConceptualMisunderstanding,
            "he",
            Arg.Is<CachedExplanation>(c => c.Text == "Generated explanation"),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // CLASSIFY -> CACHE MISS -> GENERATION FAILS -> L1 STATIC
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_GenerationFails_FallsBackToStatic()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.Guessing);

        _cache.GetAsync("q1", ExplanationErrorType.Guessing, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);
        _cache.GetAsync("q1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        _generator.GenerateAsync(Arg.Any<ExplanationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("LLM failed"));

        var request = CreateRequest("q1") with { StaticExplanation = "Static L1 explanation" };
        var result = await _orchestrator.ResolveAsync(request, CancellationToken.None);

        Assert.Equal("Static L1 explanation", result);
    }

    // =========================================================================
    // ALL TIERS FAIL -> GENERIC FALLBACK
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_AllTiersFail_ReturnsGenericFallback()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.PartialUnderstanding);

        _cache.GetAsync("q1", ExplanationErrorType.PartialUnderstanding, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);
        _cache.GetAsync("q1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        _generator.GenerateAsync(Arg.Any<ExplanationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("LLM failed"));

        var request = CreateRequest("q1") with { StaticExplanation = null };
        var result = await _orchestrator.ResolveAsync(request, CancellationToken.None);

        Assert.Equal("Review the question and consider each option carefully.", result);
    }

    // =========================================================================
    // CLASSIFICATION FAILURE -> defaults to PartialUnderstanding
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ClassificationFails_DefaultsToPartialUnderstanding()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Classifier down"));

        _cache.GetAsync("q1", ExplanationErrorType.PartialUnderstanding, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("Cached for partial", "sonnet", 20, DateTimeOffset.UtcNow));

        var result = await _orchestrator.ResolveAsync(CreateRequest("q1"), CancellationToken.None);

        Assert.Equal("Cached for partial", result);
    }

    // =========================================================================
    // DIFFERENT ERROR TYPES -> DIFFERENT CACHE LOOKUPS
    // =========================================================================

    [Theory]
    [InlineData(ExplanationErrorType.ConceptualMisunderstanding)]
    [InlineData(ExplanationErrorType.ProceduralError)]
    [InlineData(ExplanationErrorType.CarelessMistake)]
    [InlineData(ExplanationErrorType.Guessing)]
    [InlineData(ExplanationErrorType.PartialUnderstanding)]
    public async Task ResolveAsync_EachErrorType_QueriesCacheWithCorrectType(
        ExplanationErrorType errorType)
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(errorType);

        _cache.GetAsync("q1", errorType, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation($"Cached for {errorType}", "sonnet", 30, DateTimeOffset.UtcNow));

        var result = await _orchestrator.ResolveAsync(CreateRequest("q1"), CancellationToken.None);

        Assert.Equal($"Cached for {errorType}", result);
    }

    // =========================================================================
    // HEBREW AND ARABIC CACHED SEPARATELY
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_DifferentLanguages_UseDifferentCacheKeys()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ProceduralError);

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("Hebrew explanation", "sonnet", 30, DateTimeOffset.UtcNow));
        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "ar", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("Arabic explanation", "sonnet", 30, DateTimeOffset.UtcNow));

        var heRequest = CreateRequest("q1") with { Language = "he" };
        var arRequest = CreateRequest("q1") with { Language = "ar" };

        var heResult = await _orchestrator.ResolveAsync(heRequest, CancellationToken.None);
        var arResult = await _orchestrator.ResolveAsync(arRequest, CancellationToken.None);

        Assert.Equal("Hebrew explanation", heResult);
        Assert.Equal("Arabic explanation", arResult);
    }

    // =========================================================================
    // GENERATED EXPLANATION USES ERROR TYPE FROM CLASSIFIER (not original)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_GeneratedContext_UsesClassifiedErrorType()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.CarelessMistake);

        _cache.GetAsync("q1", ExplanationErrorType.CarelessMistake, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);
        _cache.GetAsync("q1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        _generator.GenerateAsync(Arg.Any<ExplanationContext>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedExplanation("Generated", "sonnet", 50));

        await _orchestrator.ResolveAsync(CreateRequest("q1"), CancellationToken.None);

        await _generator.Received(1).GenerateAsync(
            Arg.Is<ExplanationContext>(c => c.ErrorType == "CarelessMistake"),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // SAI-004: L3 ESCALATION — high uncertainty triggers L3 over L2 cache
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_CacheHitWithHighUncertainty_EscalatesToL3()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ProceduralError);

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("Cached", "sonnet", 30, DateTimeOffset.UtcNow));

        _l3Generator.GenerateAsync(Arg.Any<L3ExplanationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedExplanation("Personalized L3", "sonnet", 120));

        var l3Context = CreateL3Context() with { BackspaceCount = 8 };
        var request = CreateRequest("q1") with { L3Context = l3Context };

        var result = await _orchestrator.ResolveAsync(request, CancellationToken.None);

        Assert.Equal("Personalized L3", result);
        await _l3Generator.Received(1).GenerateAsync(Arg.Any<L3ExplanationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_CacheHitNoUncertainty_ReturnsL2WithoutL3()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ProceduralError);

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("Cached", "sonnet", 30, DateTimeOffset.UtcNow));

        var l3Context = CreateL3Context() with { BackspaceCount = 2, AnswerChangeCount = 0 };
        var request = CreateRequest("q1") with { L3Context = l3Context };

        var result = await _orchestrator.ResolveAsync(request, CancellationToken.None);

        Assert.Equal("Cached", result);
        await _l3Generator.DidNotReceive().GenerateAsync(Arg.Any<L3ExplanationRequest>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // SAI-004: L3 AFFECT GATE — ConfusionResolving suppresses L3
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ConfusionResolving_L3Suppressed_FallsThrough()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ProceduralError);

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);
        _cache.GetAsync("q1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        // L3 generator returns null when affect gate fires (ConfusionResolving)
        _l3Generator.GenerateAsync(Arg.Any<L3ExplanationRequest>(), Arg.Any<CancellationToken>())
            .Returns((GeneratedExplanation?)null);

        _generator.GenerateAsync(Arg.Any<ExplanationContext>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedExplanation("L2 generated fallback", "sonnet", 50));

        var l3Context = CreateL3Context() with
        {
            ConfusionState = ConfusionState.ConfusionResolving,
            BackspaceCount = 10  // High uncertainty, but L3 returns null due to affect gate
        };
        var request = CreateRequest("q1") with { L3Context = l3Context };

        var result = await _orchestrator.ResolveAsync(request, CancellationToken.None);

        // Should fall through to L2-generate since L3 was suppressed
        Assert.Equal("L2 generated fallback", result);
    }

    // =========================================================================
    // SAI-004: L3 FAILURE → L2 CACHE FALLBACK
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_L3Fails_FallsBackToL2Cache()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ProceduralError);

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns(new CachedExplanation("Cached L2", "sonnet", 30, DateTimeOffset.UtcNow));

        _l3Generator.GenerateAsync(Arg.Any<L3ExplanationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Circuit breaker open"));

        var l3Context = CreateL3Context() with { BackspaceCount = 10 };
        var request = CreateRequest("q1") with { L3Context = l3Context };

        var result = await _orchestrator.ResolveAsync(request, CancellationToken.None);

        // L3 failed, but we had a cached result → return it
        Assert.Equal("Cached L2", result);
    }

    // =========================================================================
    // SAI-004: L3 NOT CACHED BACK — ephemeral results
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_L3Success_DoesNotCacheResult()
    {
        _classifier.ClassifyAsync(Arg.Any<ErrorClassificationInput>(), Arg.Any<CancellationToken>())
            .Returns(ExplanationErrorType.ProceduralError);

        _cache.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);
        _cache.GetAsync("q1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedExplanation?)null);

        _l3Generator.GenerateAsync(Arg.Any<L3ExplanationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedExplanation("Personalized", "sonnet", 100));

        var l3Context = CreateL3Context() with { BackspaceCount = 10 };
        var request = CreateRequest("q1") with { L3Context = l3Context };

        await _orchestrator.ResolveAsync(request, CancellationToken.None);

        // L3 is ephemeral — should NOT be cached
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<ExplanationErrorType>(), Arg.Any<string>(),
            Arg.Is<CachedExplanation>(c => c.Text == "Personalized"),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static ExplanationRequest CreateRequest(string questionId)
    {
        return new ExplanationRequest(
            QuestionId: questionId,
            StaticExplanation: null,
            QuestionStem: "What is 2+2?",
            CorrectAnswer: "4",
            StudentAnswer: "5",
            ErrorType: "None",
            Methodology: "socratic",
            DistractorRationale: null,
            BloomsLevel: 3,
            Subject: "Mathematics",
            Language: "he");
    }

    private static L3ExplanationRequest CreateL3Context()
    {
        return new L3ExplanationRequest
        {
            QuestionId = "q1",
            QuestionStem = "What is 2+2?",
            CorrectAnswer = "4",
            StudentAnswer = "5",
            ErrorType = "ProceduralError",
            Subject = "Mathematics",
            Language = "he",
            MasteryProbability = 0.5,
            RecallProbability = 0.5,
            BloomLevel = 3,
            Psi = 0.8,
            ScaffoldingLevel = Cena.Actors.Mastery.ScaffoldingLevel.Partial,
            Methodology = "socratic",
            FocusLevel = FocusLevel.Engaged,
            ConfusionState = ConfusionState.NotConfused,
            BackspaceCount = 0,
            AnswerChangeCount = 0,
            ResponseTimeMs = 5000,
            MedianResponseTimeMs = 4000
        };
    }
}
