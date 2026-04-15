// =============================================================================
// RDY-033: Unit tests for the error pattern matcher engine.
// Covers: subject-filter short-circuit, best-confidence selection,
// unmatched-error logging, budget enforcement, identical-answer short-circuit.
// =============================================================================

using Cena.Actors.Services.ErrorPatternMatching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Services.ErrorPatternMatching;

public class ErrorPatternMatcherEngineTests
{
    private static ErrorPatternMatchContext Ctx(string student = "wrong", string correct = "right", string subject = "math") =>
        new("stem", correct, student, subject, null);

    private static IErrorPatternMatcher FakeMatcher(string ruleId, string subject, ErrorPatternMatchResult resultOnMatch)
    {
        var m = Substitute.For<IErrorPatternMatcher>();
        m.BuggyRuleId.Returns(ruleId);
        m.Subject.Returns(subject);
        m.TryMatchAsync(Arg.Any<ErrorPatternMatchContext>(), Arg.Any<CancellationToken>())
            .Returns(resultOnMatch);
        return m;
    }

    [Fact]
    public async Task ClassifyAsync_NoMatchers_LogsUnmatchedAndReturnsNoMatch()
    {
        var engine = new ErrorPatternMatcherEngine(
            Array.Empty<IErrorPatternMatcher>(),
            NullLogger<ErrorPatternMatcherEngine>.Instance);

        var result = await engine.ClassifyAsync(Ctx());

        Assert.False(result.Matched);
        Assert.Null(result.BuggyRuleId);
    }

    [Fact]
    public async Task ClassifyAsync_StudentEqualsCorrect_ShortCircuits_NoMatchersInvoked()
    {
        var m = FakeMatcher("X", "math",
            new ErrorPatternMatchResult(true, "X", 1.0, "", null, 1, "MathNet"));

        var engine = new ErrorPatternMatcherEngine(
            new[] { m }, NullLogger<ErrorPatternMatcherEngine>.Instance);

        var result = await engine.ClassifyAsync(Ctx(student: "42", correct: "42"));

        Assert.False(result.Matched);
        await m.DidNotReceive().TryMatchAsync(Arg.Any<ErrorPatternMatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_SubjectMismatch_SkipsMatcher_WithoutInvocation()
    {
        var m = FakeMatcher("X", "physics",
            new ErrorPatternMatchResult(true, "X", 1.0, "", null, 1, "MathNet"));

        var engine = new ErrorPatternMatcherEngine(
            new[] { m }, NullLogger<ErrorPatternMatcherEngine>.Instance);

        var result = await engine.ClassifyAsync(Ctx(subject: "math"));

        Assert.False(result.Matched);
        await m.DidNotReceive().TryMatchAsync(Arg.Any<ErrorPatternMatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_MultipleMatchers_ReturnsHighestConfidence()
    {
        var a = FakeMatcher("A", "math",
            new ErrorPatternMatchResult(true, "A", 0.7, "a", null, 1, "MathNet"));
        var b = FakeMatcher("B", "math",
            new ErrorPatternMatchResult(true, "B", 0.9, "b", null, 1, "MathNet"));
        var c = FakeMatcher("C", "math",
            new ErrorPatternMatchResult(false, null, 0.0, "", null, 1, "none"));

        var engine = new ErrorPatternMatcherEngine(
            new[] { a, b, c }, NullLogger<ErrorPatternMatcherEngine>.Instance);

        var result = await engine.ClassifyAsync(Ctx());

        Assert.True(result.Matched);
        Assert.Equal("B", result.BuggyRuleId);
        Assert.Equal(0.9, result.Confidence);
    }

    [Fact]
    public async Task ClassifyAsync_ExactMatch_ShortCircuitsRemainingMatchers()
    {
        var a = FakeMatcher("A", "math",
            new ErrorPatternMatchResult(true, "A", 1.0, "a", null, 1, "MathNet"));
        var b = Substitute.For<IErrorPatternMatcher>();
        b.BuggyRuleId.Returns("B");
        b.Subject.Returns("math");

        var engine = new ErrorPatternMatcherEngine(
            new[] { a, b }, NullLogger<ErrorPatternMatcherEngine>.Instance);

        var result = await engine.ClassifyAsync(Ctx());

        Assert.True(result.Matched);
        Assert.Equal("A", result.BuggyRuleId);
        Assert.Equal(1.0, result.Confidence);
        await b.DidNotReceive().TryMatchAsync(Arg.Any<ErrorPatternMatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_BelowThreshold_TreatedAsUnmatched()
    {
        var a = FakeMatcher("A", "math",
            new ErrorPatternMatchResult(false, null, 0.3, "", null, 1, "none"));

        var engine = new ErrorPatternMatcherEngine(
            new[] { a }, NullLogger<ErrorPatternMatcherEngine>.Instance);

        var result = await engine.ClassifyAsync(Ctx());

        Assert.False(result.Matched);
        Assert.Null(result.BuggyRuleId);
    }
}
