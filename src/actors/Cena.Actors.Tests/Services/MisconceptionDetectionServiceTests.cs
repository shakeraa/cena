// =============================================================================
// RDY-014: Misconception Detection Service Tests
// =============================================================================

using Cena.Actors.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Services;

public class MisconceptionDetectionServiceTests
{
    private readonly MisconceptionDetectionService _service = new(
        NullLogger<MisconceptionDetectionService>.Instance);

    [Fact]
    public void Detect_DistExpSum_MatchesWhenSquaredTermsMissMiddle()
    {
        var result = _service.Detect(
            questionStem: "Expand (x+3)²",
            correctAnswer: "x²+6x+9",
            studentAnswer: "x²+9",
            subject: "Math",
            conceptId: "ALG-004",
            errorType: null);

        Assert.True(result.Detected);
        Assert.Equal("DIST-EXP-SUM", result.BuggyRuleId);
        Assert.True(result.Confidence >= 0.5);
        Assert.NotNull(result.CounterExample);
        Assert.NotNull(result.RemediationTask);
    }

    [Fact]
    public void Detect_SignFlipInequality_MatchesWrongDirection()
    {
        var result = _service.Detect(
            questionStem: "Solve: -2x > 6",
            correctAnswer: "x < -3",
            studentAnswer: "x > -3",
            subject: "Math",
            conceptId: "ALG-003",
            errorType: null);

        Assert.True(result.Detected);
        Assert.Equal("SIGN-FLIP-INEQ", result.BuggyRuleId);
    }

    [Fact]
    public void Detect_IntegralConstant_MatchesMissingC()
    {
        var result = _service.Detect(
            questionStem: "Find ∫2x dx",
            correctAnswer: "x² + C",
            studentAnswer: "x²",
            subject: "Math",
            conceptId: "CAL-005",
            errorType: null);

        Assert.True(result.Detected);
        Assert.Equal("INTEGRAL-CONSTANT", result.BuggyRuleId);
        Assert.True(result.Confidence >= 0.8);
    }

    [Fact]
    public void Detect_ChainRuleMissing_MatchesMissingFactor()
    {
        var result = _service.Detect(
            questionStem: "Find d/dx[sin(2x)]",
            correctAnswer: "2cos(2x)",
            studentAnswer: "cos(2x)",
            subject: "Math",
            conceptId: "CAL-003",
            errorType: null);

        Assert.True(result.Detected);
        Assert.Equal("CHAIN-RULE-MISSING", result.BuggyRuleId);
    }

    [Fact]
    public void Detect_VelocityAccelSign_MatchesZeroForAcceleration()
    {
        var result = _service.Detect(
            questionStem: "What is the acceleration at the top of a throw?",
            correctAnswer: "-9.8 m/s²",
            studentAnswer: "0",
            subject: "Physics",
            conceptId: null,
            errorType: null);

        Assert.True(result.Detected);
        Assert.Equal("VELOCITY-ACCEL-SIGN", result.BuggyRuleId);
    }

    [Fact]
    public void Detect_CorrectAnswer_NoMatch()
    {
        var result = _service.Detect(
            questionStem: "Expand (x+3)²",
            correctAnswer: "x²+6x+9",
            studentAnswer: "x²+6x+9",
            subject: "Math",
            conceptId: "ALG-004",
            errorType: null);

        Assert.False(result.Detected);
        Assert.Null(result.BuggyRuleId);
    }

    [Fact]
    public void Detect_WrongSubject_NoMatch()
    {
        var result = _service.Detect(
            questionStem: "What is H2O?",
            correctAnswer: "Water",
            studentAnswer: "Hydrogen",
            subject: "Chemistry",
            conceptId: null,
            errorType: null);

        Assert.False(result.Detected);
    }

    [Fact]
    public void GetRemediation_ExistingRule_ReturnsTask()
    {
        var task = _service.GetRemediation("DIST-EXP-SUM");

        Assert.NotNull(task);
        Assert.Equal("DIST-EXP-SUM", task.BuggyRuleId);
    }

    [Fact]
    public void GetRemediation_UnknownRule_ReturnsNull()
    {
        var task = _service.GetRemediation("NONEXISTENT-RULE");
        Assert.Null(task);
    }
}
