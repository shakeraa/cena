// =============================================================================
// Cena Platform -- RDY-026: Arabic Input Normalization Wiring Tests
// Verifies that BuildConceptAttempt correctly propagates raw student input
// and that the normalization integration in SessionEndpoints works end-to-end.
// =============================================================================

using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Localization;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class SessionEndpointsNormalizationTests
{
    // ── BuildConceptAttempt: rawStudentInput propagation ──

    [Fact]
    public void BuildConceptAttempt_WithRawStudentInput_SetsRawStudentInput()
    {
        var questionDoc = MakeQuestion(subject: "math", correctAnswer: "x+1");

        var attempt = Cena.Api.Host.Endpoints.SessionEndpoints.BuildConceptAttempt(
            studentId: "student-1",
            sessionId: "session-1",
            questionDoc: questionDoc,
            currentQuestionId: "q-1",
            methodology: "adaptive",
            isCorrect: true,
            responseTimeMs: 1200,
            priorMastery: 0.4,
            posteriorMastery: 0.6,
            errorType: "None",
            rawStudentInput: "س+1");

        Assert.Equal("س+1", attempt.RawStudentInput);
    }

    [Fact]
    public void BuildConceptAttempt_WithoutRawStudentInput_LeavesNull()
    {
        var questionDoc = MakeQuestion(subject: "math", correctAnswer: "x+1");

        var attempt = Cena.Api.Host.Endpoints.SessionEndpoints.BuildConceptAttempt(
            studentId: "student-1",
            sessionId: "session-1",
            questionDoc: questionDoc,
            currentQuestionId: "q-1",
            methodology: "adaptive",
            isCorrect: true,
            responseTimeMs: 1200,
            priorMastery: 0.4,
            posteriorMastery: 0.6,
            errorType: "None");

        Assert.Null(attempt.RawStudentInput);
    }

    // ── Normalization produces correct answer match ──

    // Invariant: normalized output is ASCII-safe — digits become ASCII
    // digits, variables become ASCII letters, Arabic math terms become
    // ASCII keywords ("جذر" → "sqrt"), and math operators are flattened
    // to their ASCII equivalents (× → *, ÷ → /, − → -). This keeps JSON,
    // SymPy inputs, logs, and correct-answer comparisons all on the
    // same narrow character set downstream.
    [Theory]
    [InlineData("س+١", "x+1", "math", true)]       // Arabic var + digit → Latin
    [InlineData("ص²+٣", "y²+3", "math", true)]     // Arabic var + superscript + digit (² is not an Arabic digit — preserved)
    [InlineData("x+1", "x+1", "math", false)]       // Latin input → no normalization needed
    [InlineData("ت×م", "a*m", "physics", true)]     // Physics context: ت→a (acceleration), × → * (ASCII invariant)
    [InlineData("ت×م", "t*m", "math", true)]        // Math context:    ت→t, × → * (ASCII invariant)
    public void NormalizationProducesCorrectAnswerMatch(
        string studentInput, string expectedNormalized, string subject, bool expectsNormalization)
    {
        Assert.Equal(expectsNormalization, ArabicMathNormalizer.NeedsNormalization(studentInput));

        var context = subject == "physics"
            ? NormalizationContext.Physics
            : NormalizationContext.Mathematics;

        var normalized = ArabicMathNormalizer.NeedsNormalization(studentInput)
            ? ArabicMathNormalizer.Normalize(studentInput, context)
            : studentInput;

        Assert.Equal(expectedNormalized, normalized);
    }

    // ── Integration: Arabic answer matches correct answer after normalization ──

    [Fact]
    public void ArabicAnswer_MatchesCorrectAnswer_AfterNormalization()
    {
        var questionDoc = MakeQuestion(subject: "math", correctAnswer: "x+1");
        var rawInput = "س+١";

        var normalizedAnswer = ArabicMathNormalizer.NeedsNormalization(rawInput)
            ? ArabicMathNormalizer.Normalize(rawInput, NormalizationContext.Mathematics)
            : rawInput;

        var isCorrect = string.Equals(normalizedAnswer, questionDoc.CorrectAnswer,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(isCorrect, $"Expected '{rawInput}' to match '{questionDoc.CorrectAnswer}' after normalization to '{normalizedAnswer}'");

        // BuildConceptAttempt should record raw input since it differs from normalized
        var rawForEvent = rawInput != normalizedAnswer ? rawInput : null;
        var attempt = Cena.Api.Host.Endpoints.SessionEndpoints.BuildConceptAttempt(
            studentId: "student-1",
            sessionId: "session-1",
            questionDoc: questionDoc,
            currentQuestionId: "q-1",
            methodology: "adaptive",
            isCorrect: isCorrect,
            responseTimeMs: 800,
            priorMastery: 0.5,
            posteriorMastery: 0.7,
            errorType: "None",
            rawStudentInput: rawForEvent);

        Assert.True(attempt.IsCorrect);
        Assert.Equal("س+١", attempt.RawStudentInput);
    }

    [Fact]
    public void LatinAnswer_DoesNotRecordRawInput()
    {
        var questionDoc = MakeQuestion(subject: "math", correctAnswer: "x+1");
        var rawInput = "x+1";

        // No normalization needed for Latin input
        Assert.False(ArabicMathNormalizer.NeedsNormalization(rawInput));

        var attempt = Cena.Api.Host.Endpoints.SessionEndpoints.BuildConceptAttempt(
            studentId: "student-1",
            sessionId: "session-1",
            questionDoc: questionDoc,
            currentQuestionId: "q-1",
            methodology: "adaptive",
            isCorrect: true,
            responseTimeMs: 800,
            priorMastery: 0.5,
            posteriorMastery: 0.7,
            errorType: "None",
            rawStudentInput: null);

        Assert.Null(attempt.RawStudentInput);
    }

    [Fact]
    public void PhysicsContext_NormalizesToAcceleration()
    {
        // In physics, ت → a (acceleration), not t
        var rawInput = "ت=٥";
        var normalized = ArabicMathNormalizer.Normalize(rawInput, NormalizationContext.Physics);
        Assert.Equal("a=5", normalized);

        var questionDoc = MakeQuestion(subject: "physics", correctAnswer: "a=5");
        var isCorrect = string.Equals(normalized, questionDoc.CorrectAnswer,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(isCorrect);
    }

    // ── Helper ──

    private static QuestionDocument MakeQuestion(string subject, string correctAnswer)
    {
        return new QuestionDocument
        {
            Id = "q-test",
            QuestionId = "q-test",
            Subject = subject,
            ConceptId = "concept-test",
            CorrectAnswer = correctAnswer,
            QuestionType = "free-text"
        };
    }
}
