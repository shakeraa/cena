// =============================================================================
// Tests: ErrorClassificationService (SAI-02)
// Verifies LLM-based error classification and parsing logic.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Actors.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Diagnostics.Metrics;

namespace Cena.Actors.Tests.Services;

public sealed class ErrorClassificationServiceTests
{
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly ILogger<ErrorClassificationService> _logger =
        NullLogger<ErrorClassificationService>.Instance;
    private readonly IMeterFactory _meterFactory;
    private readonly ErrorClassificationService _service;

    public ErrorClassificationServiceTests()
    {
        _meterFactory = new ClassificationMeterFactory();
        _service = new ErrorClassificationService(_llm, _logger, _meterFactory);
    }

    // =========================================================================
    // ParseClassification (internal, deterministic)
    // =========================================================================

    [Theory]
    [InlineData("ConceptualMisunderstanding", ExplanationErrorType.ConceptualMisunderstanding)]
    [InlineData("conceptualmisunderstanding", ExplanationErrorType.ConceptualMisunderstanding)]
    [InlineData("Conceptual", ExplanationErrorType.ConceptualMisunderstanding)]
    [InlineData("ProceduralError", ExplanationErrorType.ProceduralError)]
    [InlineData("procedural", ExplanationErrorType.ProceduralError)]
    [InlineData("CarelessMistake", ExplanationErrorType.CarelessMistake)]
    [InlineData("careless", ExplanationErrorType.CarelessMistake)]
    [InlineData("Guessing", ExplanationErrorType.Guessing)]
    [InlineData("guess", ExplanationErrorType.Guessing)]
    [InlineData("PartialUnderstanding", ExplanationErrorType.PartialUnderstanding)]
    [InlineData("partial", ExplanationErrorType.PartialUnderstanding)]
    public void ParseClassification_ValidInputs_ReturnsCorrectType(
        string input, ExplanationErrorType expected)
    {
        var result = ErrorClassificationService.ParseClassification(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("  ConceptualMisunderstanding  ")]
    [InlineData("Conceptual_Misunderstanding")]
    [InlineData("conceptual-misunderstanding")]
    [InlineData("CONCEPTUAL MISUNDERSTANDING")]
    public void ParseClassification_HandlesWhitespaceUnderscoresDashes(string input)
    {
        var result = ErrorClassificationService.ParseClassification(input);
        Assert.Equal(ExplanationErrorType.ConceptualMisunderstanding, result);
    }

    [Theory]
    [InlineData("The student seems to be guessing randomly", ExplanationErrorType.Guessing)]
    [InlineData("This looks like a careless error", ExplanationErrorType.CarelessMistake)]
    [InlineData("Procedural mistake in step 3", ExplanationErrorType.ProceduralError)]
    [InlineData("Fundamental misunderstanding of the concept", ExplanationErrorType.ConceptualMisunderstanding)]
    public void ParseClassification_FuzzyMatch_ExtractsFromSentences(
        string input, ExplanationErrorType expected)
    {
        var result = ErrorClassificationService.ParseClassification(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("something completely different")]
    public void ParseClassification_UnrecognizedInput_ReturnsPartialUnderstanding(string input)
    {
        var result = ErrorClassificationService.ParseClassification(input);
        Assert.Equal(ExplanationErrorType.PartialUnderstanding, result);
    }

    // =========================================================================
    // ClassifyAsync (integration with ILlmClient mock)
    // =========================================================================

    [Fact]
    public async Task ClassifyAsync_SuccessfulLlmResponse_ReturnsClassifiedType()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ProceduralError", 100, 10, TimeSpan.FromMilliseconds(200),
                "claude-haiku-4-5-20260101", false));

        var input = CreateTestInput();
        var result = await _service.ClassifyAsync(input, CancellationToken.None);

        Assert.Equal(ExplanationErrorType.ProceduralError, result);
        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.ModelId == "claude-haiku-4-5-20260101"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_UsesHaikuModel()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("Guessing", 80, 5, TimeSpan.FromMilliseconds(100),
                "claude-haiku-4-5-20260101", false));

        var input = CreateTestInput();
        await _service.ClassifyAsync(input, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r =>
                r.ModelId == "claude-haiku-4-5-20260101" &&
                r.Temperature == 0.0f &&
                r.MaxTokens == 200),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_LlmFailure_ReturnsPartialUnderstanding()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Circuit breaker open"));

        var input = CreateTestInput();
        var result = await _service.ClassifyAsync(input, CancellationToken.None);

        Assert.Equal(ExplanationErrorType.PartialUnderstanding, result);
    }

    [Fact]
    public async Task ClassifyAsync_IncludesDistractorRationale_WhenPresent()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("ConceptualMisunderstanding", 120, 8,
                TimeSpan.FromMilliseconds(150), "claude-haiku-4-5-20260101", false));

        var input = new ErrorClassificationInput(
            "What is 2+2?", "4", "5", "Common off-by-one error", "Math", "he");

        await _service.ClassifyAsync(input, CancellationToken.None);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.UserPrompt.Contains("DISTRACTOR RATIONALE")),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static ErrorClassificationInput CreateTestInput()
    {
        return new ErrorClassificationInput(
            QuestionStem: "What is the derivative of x^2?",
            CorrectAnswer: "2x",
            StudentAnswer: "x^2",
            DistractorRationale: null,
            Subject: "Mathematics",
            Language: "he");
    }
}

/// <summary>
/// Minimal IMeterFactory for tests -- returns real Meter instances that don't report anywhere.
/// </summary>
internal sealed class ClassificationMeterFactory : IMeterFactory
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
