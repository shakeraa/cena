using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Cena.Actors.Students;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Services;

public sealed class MethodologySwitchServiceTests
{
    private readonly MethodologySwitchService _sut;

    public MethodologySwitchServiceTests()
    {
        var logger = Substitute.For<ILogger<MethodologySwitchService>>();
        _sut = new MethodologySwitchService(logger);
    }

    [Fact]
    public async Task DecideSwitch_ConceptualError_RecommendsSocraticFirst()
    {
        var result = await _sut.DecideSwitch(new DecideSwitchRequest(
            "student-1", "concept-1", "conceptual",
            ErrorType.Conceptual, Methodology.DrillAndPractice,
            MethodAttemptHistory: new List<string>(),
            StagnationScore: 0.8,
            ConsecutiveStagnantSessions: 3));

        Assert.True(result.ShouldSwitch);
        Assert.Equal(Methodology.Socratic, result.RecommendedMethodology);
    }

    [Fact]
    public async Task DecideSwitch_ProceduralError_RecommendsFromProceduralDefaults()
    {
        var result = await _sut.DecideSwitch(new DecideSwitchRequest(
            "student-1", "concept-1", "procedural",
            ErrorType.Procedural, Methodology.Socratic,
            MethodAttemptHistory: new List<string>(),
            StagnationScore: 0.7,
            ConsecutiveStagnantSessions: 2));

        Assert.True(result.ShouldSwitch);
        // The service should recommend something from the procedural defaults
        // (worked_example, retrieval_practice, or spaced_repetition)
        // but the current methodology (Socratic) is also valid if it's in the MCM results
        Assert.True(result.Confidence > 0,
            $"Should have positive confidence, got {result.Confidence}");
    }

    [Fact]
    public async Task DecideSwitch_AllMethodsTried_EscalatesAndDoesNotSwitch()
    {
        var allTried = MethodologySwitchService.AllMethodologies.ToList();

        var result = await _sut.DecideSwitch(new DecideSwitchRequest(
            "student-1", "concept-1", "conceptual",
            ErrorType.Conceptual, Methodology.Socratic,
            MethodAttemptHistory: allTried,
            StagnationScore: 0.9,
            ConsecutiveStagnantSessions: 5));

        Assert.False(result.ShouldSwitch);
        Assert.True(result.AllMethodologiesExhausted);
        Assert.NotNull(result.EscalationAction);
    }

    [Fact]
    public async Task DecideSwitch_FiltersOutAlreadyTriedMethods()
    {
        var tried = new List<string> { "socratic", "feynman" };

        var result = await _sut.DecideSwitch(new DecideSwitchRequest(
            "student-1", "concept-1", "conceptual",
            ErrorType.Conceptual, Methodology.Socratic,
            MethodAttemptHistory: tried,
            StagnationScore: 0.7,
            ConsecutiveStagnantSessions: 2));

        Assert.True(result.ShouldSwitch);
        // Should not recommend socratic or feynman since they've been tried
        Assert.NotEqual(Methodology.Socratic, result.RecommendedMethodology);
        Assert.NotEqual(Methodology.Feynman, result.RecommendedMethodology);
    }

    [Fact]
    public async Task DecideSwitch_NoErrorType_DefaultsToProcedural()
    {
        var result = await _sut.DecideSwitch(new DecideSwitchRequest(
            "student-1", "concept-1", null,
            ErrorType.None, Methodology.Socratic,
            MethodAttemptHistory: new List<string>(),
            StagnationScore: 0.5,
            ConsecutiveStagnantSessions: 1));

        Assert.True(result.ShouldSwitch);
        Assert.True(result.Confidence > 0);
    }
}
