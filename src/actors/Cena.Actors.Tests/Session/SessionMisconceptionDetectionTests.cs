// =============================================================================
// RDY-033b: Tests for the wire-up between /answer and the misconception
// detector. Covers the DetectMisconception helper (pure delegation + error
// handling) and verifies that the call contract carries the right fields
// through to IMisconceptionDetectionService.
// =============================================================================

using Cena.Actors.Services;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Session;

public sealed class SessionMisconceptionDetectionTests
{
    private readonly IMisconceptionDetectionService _detector =
        Substitute.For<IMisconceptionDetectionService>();

    private static QuestionDocument MakeQuestion(string subject = "Math") => new()
    {
        QuestionId = "q-1",
        ConceptId = "concept-1",
        Subject = subject,
        Prompt = "Expand (x+3)^2",
        CorrectAnswer = "x^2 + 6x + 9",
    };

    [Fact]
    public void DetectMisconception_DelegatesAllArgumentsToDetector_LowercasingSubject()
    {
        _detector.Detect(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ExplanationErrorType?>())
            .Returns(new MisconceptionDetectionResult(
                Detected: true,
                BuggyRuleId: "DIST-EXP-SUM",
                Confidence: 1.0,
                CounterExample: "(2+3)^2 = 25 but 2^2+3^2 = 13",
                RemediationTask: null));

        var result = SessionEndpoints.DetectMisconception(
            _detector,
            MakeQuestion(subject: "Mathematics"),
            rawStudentInput: "x^2 + 9",
            normalizedAnswer: "x^2+9",
            errorType: "Conceptual",
            NullLogger.Instance);

        Assert.NotNull(result);
        Assert.True(result!.Detected);
        Assert.Equal("DIST-EXP-SUM", result.BuggyRuleId);

        _detector.Received(1).Detect(
            questionStem: "Expand (x+3)^2",
            correctAnswer: "x^2 + 6x + 9",
            studentAnswer: "x^2 + 9", // raw student input wins when present
            subject: "mathematics",   // lowercased
            conceptId: "concept-1",
            errorType: ExplanationErrorType.ConceptualMisunderstanding);
    }

    [Fact]
    public void DetectMisconception_FallsBackToNormalizedWhenRawIsBlank()
    {
        _detector.Detect(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ExplanationErrorType?>())
            .Returns(new MisconceptionDetectionResult(false, null, 0, null, null));

        SessionEndpoints.DetectMisconception(
            _detector,
            MakeQuestion(),
            rawStudentInput: null,
            normalizedAnswer: "x^2+9",
            errorType: "Conceptual",
            NullLogger.Instance);

        _detector.Received(1).Detect(
            questionStem: Arg.Any<string>(),
            correctAnswer: Arg.Any<string>(),
            studentAnswer: "x^2+9",
            subject: Arg.Any<string>(),
            conceptId: Arg.Any<string?>(),
            errorType: Arg.Any<ExplanationErrorType?>());
    }

    [Fact]
    public void DetectMisconception_MapsUnknownErrorTypeToNull()
    {
        _detector.Detect(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ExplanationErrorType?>())
            .Returns(new MisconceptionDetectionResult(false, null, 0, null, null));

        SessionEndpoints.DetectMisconception(
            _detector,
            MakeQuestion(),
            rawStudentInput: "x",
            normalizedAnswer: "x",
            errorType: "None", // correct answers feed this through as "None"
            NullLogger.Instance);

        _detector.Received(1).Detect(
            questionStem: Arg.Any<string>(),
            correctAnswer: Arg.Any<string>(),
            studentAnswer: Arg.Any<string>(),
            subject: Arg.Any<string>(),
            conceptId: Arg.Any<string?>(),
            errorType: (ExplanationErrorType?)null);
    }

    [Fact]
    public void DetectMisconception_DetectorThrows_ReturnsNullAndLogsWarning()
    {
        _detector.Detect(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ExplanationErrorType?>())
            .Returns<MisconceptionDetectionResult>(_ => throw new InvalidOperationException("sidecar down"));

        var result = SessionEndpoints.DetectMisconception(
            _detector,
            MakeQuestion(),
            rawStudentInput: "x",
            normalizedAnswer: "x",
            errorType: "Conceptual",
            NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void DetectMisconception_MissingSubject_DefaultsToMath()
    {
        _detector.Detect(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ExplanationErrorType?>())
            .Returns(new MisconceptionDetectionResult(false, null, 0, null, null));

        var qd = MakeQuestion();
        qd.Subject = null!;

        SessionEndpoints.DetectMisconception(
            _detector, qd,
            rawStudentInput: "x",
            normalizedAnswer: "x",
            errorType: "Conceptual",
            NullLogger.Instance);

        _detector.Received(1).Detect(
            questionStem: Arg.Any<string>(),
            correctAnswer: Arg.Any<string>(),
            studentAnswer: Arg.Any<string>(),
            subject: "math",
            conceptId: Arg.Any<string?>(),
            errorType: Arg.Any<ExplanationErrorType?>());
    }
}
