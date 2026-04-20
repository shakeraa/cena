// =============================================================================
// prr-203 — L1 Template Hint Generator unit tests
//
// Coverage:
//   1. No profile on file → raw template body, no governor call.
//   2. LD-anxious profile flag ON → governor is invoked and its rewritten
//      body is returned.
//   3. Governor throws → fail-open; raw template is returned unchanged.
//   4. RungSource is always "template" — tier-1 invariant.
// =============================================================================

using Cena.Actors.Accommodations;
using Cena.Actors.Hints;
using Cena.Actors.Services;
using Cena.Actors.Tutor;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Hints;

public class L1TemplateHintGeneratorTests
{
    private readonly IStaticHintLadderFallback _staticLadder = Substitute.For<IStaticHintLadderFallback>();
    private readonly ILdAnxiousHintGovernor _governor = Substitute.For<ILdAnxiousHintGovernor>();

    private L1TemplateHintGenerator CreateSut() => new(_staticLadder, _governor);

    private static HintLadderInput SampleInput() => new(
        SessionId: "sess-1",
        QuestionId: "q-1",
        ConceptId: "concept-linear",
        Subject: "algebra",
        QuestionStem: "Solve x+2 = 4",
        Explanation: null,
        Methodology: null,
        PrerequisiteConceptNames: new[] { "linear-equations" },
        InstituteId: "inst-a");

    [Fact]
    public void No_accommodation_profile_returns_raw_static_template_body()
    {
        _staticLadder.GetHint(Arg.Any<TutorContext>(), fallbackIndex: 0)
            .Returns(new StaticHintResponse("STATIC L1 TEXT", StaticHintRung.L1_TryThisStep));

        var sut = CreateSut();
        var result = sut.Generate(SampleInput(), accommodationProfile: null, instituteId: "inst-a");

        Assert.Equal("STATIC L1 TEXT", result.Body);
        Assert.Equal("template", result.RungSource);
        _governor.DidNotReceive().Apply(
            Arg.Any<HintContent>(), Arg.Any<HintRequest>(),
            Arg.Any<AccommodationProfile>(), Arg.Any<string>());
    }

    [Fact]
    public void Accommodation_profile_triggers_governor_invocation()
    {
        _staticLadder.GetHint(Arg.Any<TutorContext>(), fallbackIndex: 0)
            .Returns(new StaticHintResponse("STATIC L1 TEXT", StaticHintRung.L1_TryThisStep));

        _governor.Apply(
                Arg.Any<HintContent>(),
                Arg.Any<HintRequest>(),
                Arg.Any<AccommodationProfile>(),
                Arg.Any<string>())
            .Returns(call => call.Arg<HintContent>() with { Text = "Try this step: GOVERNED" });

        var profile = AccommodationProfile.Default("stu-anon");
        var sut = CreateSut();
        var result = sut.Generate(SampleInput(), accommodationProfile: profile, instituteId: "inst-a");

        Assert.Equal("Try this step: GOVERNED", result.Body);
        Assert.Equal("template", result.RungSource);
    }

    [Fact]
    public void Governor_exception_falls_back_to_raw_template()
    {
        _staticLadder.GetHint(Arg.Any<TutorContext>(), fallbackIndex: 0)
            .Returns(new StaticHintResponse("STATIC L1 TEXT", StaticHintRung.L1_TryThisStep));

        _governor.Apply(
                Arg.Any<HintContent>(),
                Arg.Any<HintRequest>(),
                Arg.Any<AccommodationProfile>(),
                Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("boom"));

        var profile = AccommodationProfile.Default("stu-anon");
        var sut = CreateSut();
        var result = sut.Generate(SampleInput(), accommodationProfile: profile, instituteId: "inst-a");

        // Fail-open: student still gets the raw template body when the
        // governor throws. This is the invariant the prr-029 governor
        // already documents — the orchestrator / L1 generator must never
        // block hint rendering on a governor error.
        Assert.Equal("STATIC L1 TEXT", result.Body);
        Assert.Equal("template", result.RungSource);
    }
}
