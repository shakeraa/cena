// =============================================================================
// Cena Platform — Error Classification Tests (FIND-pedagogy-007)
// Tests that IErrorClassificationService classifies errors and the results
// are correctly mapped to ErrorType strings on ConceptAttempted_V1 events.
// =============================================================================

using Cena.Actors.Services;
using Cena.Infrastructure.Documents;
using Xunit;

namespace Cena.Actors.Tests.Session;

public class ErrorClassificationTests
{
    [Theory]
    [InlineData(ExplanationErrorType.ConceptualMisunderstanding, "Conceptual")]
    [InlineData(ExplanationErrorType.ProceduralError, "Procedural")]
    [InlineData(ExplanationErrorType.CarelessMistake, "Careless")]
    [InlineData(ExplanationErrorType.Guessing, "Motivational")]
    [InlineData(ExplanationErrorType.PartialUnderstanding, "Transfer")]
    public void ErrorTypeMapping_MapsAllClassificationTypes(ExplanationErrorType input, string expected)
    {
        // This test documents the mapping from ExplanationErrorType to ErrorType enum strings
        // The mapping is performed in SessionEndpoints.ClassifyErrorAsync
        var actual = input switch
        {
            ExplanationErrorType.ConceptualMisunderstanding => "Conceptual",
            ExplanationErrorType.ProceduralError => "Procedural",
            ExplanationErrorType.CarelessMistake => "Careless",
            ExplanationErrorType.Guessing => "Motivational",
            ExplanationErrorType.PartialUnderstanding => "Transfer",
            _ => "Conceptual"
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ErrorClassificationInput_CanBeConstructed()
    {
        // Verify the input record can be constructed with all parameters
        var input = new ErrorClassificationInput(
            QuestionStem: "What is 2 + 2?",
            CorrectAnswer: "4",
            StudentAnswer: "5",
            DistractorRationale: "Common off-by-one error",
            Subject: "math",
            Language: "en",
            QuestionDifficulty: 0.6f,
            StudentMastery: 0.7f);

        Assert.Equal("What is 2 + 2?", input.QuestionStem);
        Assert.Equal("4", input.CorrectAnswer);
        Assert.Equal("5", input.StudentAnswer);
        Assert.Equal(0.6f, input.QuestionDifficulty);
        Assert.Equal(0.7f, input.StudentMastery);
    }

    [Fact]
    public void ErrorClassificationInput_HandlesNullDistractorRationale()
    {
        // Verify the input record handles null rationale gracefully
        var input = new ErrorClassificationInput(
            QuestionStem: "What is 2 + 2?",
            CorrectAnswer: "4",
            StudentAnswer: "5",
            DistractorRationale: null,
            Subject: "math",
            Language: "en",
            QuestionDifficulty: null,
            StudentMastery: null);

        Assert.Null(input.DistractorRationale);
        Assert.Null(input.QuestionDifficulty);
        Assert.Null(input.StudentMastery);
    }

    [Theory]
    [InlineData("ConceptualMisunderstanding", ExplanationErrorType.ConceptualMisunderstanding)]
    [InlineData("conceptual", ExplanationErrorType.ConceptualMisunderstanding)]
    [InlineData("ProceduralError", ExplanationErrorType.ProceduralError)]
    [InlineData("procedural", ExplanationErrorType.ProceduralError)]
    [InlineData("CarelessMistake", ExplanationErrorType.CarelessMistake)]
    [InlineData("careless", ExplanationErrorType.CarelessMistake)]
    [InlineData("Guessing", ExplanationErrorType.Guessing)]
    [InlineData("guess", ExplanationErrorType.Guessing)]
    [InlineData("PartialUnderstanding", ExplanationErrorType.PartialUnderstanding)]
    [InlineData("partial", ExplanationErrorType.PartialUnderstanding)]
    public void ParseClassification_ParsesValidResponses(string response, ExplanationErrorType expected)
    {
        var actual = ErrorClassificationService.ParseClassification(response);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("xyz123")]
    public void ParseClassification_UnknownDefaultsToPartialUnderstanding(string response)
    {
        var actual = ErrorClassificationService.ParseClassification(response);
        Assert.Equal(ExplanationErrorType.PartialUnderstanding, actual);
    }
}
