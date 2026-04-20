// =============================================================================
// Cena Platform — AccommodationsRenderingTests (PRR-151 R-22 wiring)
//
// Proves the session-render seams that prr-151 R-22 demands:
//
//   1. A student with TtsForProblemStatements=true produces a profile
//      whose TtsForProblemStatementsEnabled accessor is true (the
//      session endpoint uses this flag to populate
//      SessionQuestionDto.TtsEnabled).
//
//   2. A student with ExtendedTime=true yields
//      ExtendedTimeMultiplier == 1.5 (the session endpoint multiplies
//      the base ExpectedTimeSeconds by this multiplier).
//
//   3. A student with DistractionReducedLayout=true yields
//      DistractionReducedLayoutEnabled == true (the session endpoint
//      maps this to SessionQuestionDto.GraphPaperRequired — graph-
//      paper overlay being the concrete render reduction this
//      accessibility class drives).
//
// These are unit-scoped: the service + accessors are pure domain code
// and do not require a Marten instance. A separate integration test
// (omitted here — would require a live Marten database) will assert
// the SessionEndpoints path end-to-end; the arch test above guards the
// wiring presence.
// =============================================================================

using Cena.Actors.Accommodations;
using Xunit;

namespace Cena.Actors.Tests.Accommodations;

public class AccommodationsRenderingTests
{
    private static AccommodationProfile ProfileWith(params AccommodationDimension[] enabled) =>
        new(
            StudentAnonId: "stu-anon-rendering-test",
            EnabledDimensions: new HashSet<AccommodationDimension>(enabled),
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: "parent-hmac-test",
            AssignedAtUtc: DateTimeOffset.UtcNow);

    // -------------------------------------------------------------------------
    // Test 1 — TTS: a profile with TtsForProblemStatements=true feeds
    // SessionQuestionDto.TtsEnabled via AccommodationProfile.TtsForProblemStatementsEnabled.
    // -------------------------------------------------------------------------

    [Fact]
    public void TtsEnabled_profile_reports_TtsForProblemStatementsEnabled_true()
    {
        var p = ProfileWith(AccommodationDimension.TtsForProblemStatements);

        Assert.True(p.TtsForProblemStatementsEnabled,
            "TTS accommodation present in the profile but "
            + "TtsForProblemStatementsEnabled returned false — the session "
            + "endpoint will not set SessionQuestionDto.TtsEnabled, so the "
            + "frontend TTS button will stay inactive. PRR-151 R-22 "
            + "compliance-critical wiring.");
    }

    [Fact]
    public void TtsDisabled_profile_reports_TtsForProblemStatementsEnabled_false()
    {
        var p = ProfileWith(); // no dimensions
        Assert.False(p.TtsForProblemStatementsEnabled);
    }

    [Fact]
    public void TtsForProblemStatementsEnabled_is_independent_of_other_dimensions()
    {
        // ExtendedTime alone must not flip the TTS flag — each dimension
        // carries an independent consent signature (RDY-066 §design).
        var p = ProfileWith(AccommodationDimension.ExtendedTime);
        Assert.False(p.TtsForProblemStatementsEnabled);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Extended time: profile.ExtendedTimeMultiplier == 1.5 when enabled.
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtendedTimeEnabled_profile_reports_multiplier_1_5()
    {
        var p = ProfileWith(AccommodationDimension.ExtendedTime);
        Assert.Equal(1.5, p.ExtendedTimeMultiplier);
    }

    [Fact]
    public void ExtendedTimeDisabled_profile_reports_multiplier_1_0()
    {
        var p = ProfileWith(); // no dimensions
        Assert.Equal(1.0, p.ExtendedTimeMultiplier);
    }

    [Fact]
    public void ExtendedTimeMultiplier_pacing_math_matches_endpoint_behaviour()
    {
        // The student endpoint does:
        //   var expectedTimeSeconds = (int)Math.Round(60 * profile.ExtendedTimeMultiplier);
        // We pin the arithmetic so any future refactor of the multiplier
        // surfaces as a test failure here, not as a silently-wrong pacing
        // budget on the client.
        var enabled = ProfileWith(AccommodationDimension.ExtendedTime);
        var disabled = ProfileWith();

        const int baseline = 60;
        Assert.Equal(90, (int)Math.Round(baseline * enabled.ExtendedTimeMultiplier));
        Assert.Equal(60, (int)Math.Round(baseline * disabled.ExtendedTimeMultiplier));
    }

    // -------------------------------------------------------------------------
    // Test 3 — Distraction-reduced / graph-paper overlay: profile carries
    // DistractionReducedLayoutEnabled which the session endpoint maps to
    // SessionQuestionDto.GraphPaperRequired.
    // -------------------------------------------------------------------------

    [Fact]
    public void DistractionReduced_profile_reports_layout_flag_true()
    {
        var p = ProfileWith(AccommodationDimension.DistractionReducedLayout);
        Assert.True(p.DistractionReducedLayoutEnabled,
            "Distraction-reduced accommodation present in the profile but "
            + "DistractionReducedLayoutEnabled returned false — the session "
            + "endpoint will not set SessionQuestionDto.GraphPaperRequired, "
            + "so the minimal layout and graph-paper overlay stay off.");
    }

    [Fact]
    public void DistractionReduced_default_profile_reports_layout_flag_false()
    {
        Assert.False(AccommodationProfile.Default("stu-anon-x").DistractionReducedLayoutEnabled);
    }

    // -------------------------------------------------------------------------
    // Cross-cutting: NoComparativeStats + Default profile is the safe baseline.
    // -------------------------------------------------------------------------

    [Fact]
    public void NoComparativeStats_flag_follows_dimension()
    {
        var on = ProfileWith(AccommodationDimension.NoComparativeStats);
        var off = ProfileWith();

        Assert.True(on.NoComparativeStatsRequired);
        Assert.False(off.NoComparativeStatsRequired);
    }

    [Fact]
    public void Default_profile_is_the_no_accommodation_baseline()
    {
        // Default profile must produce zero render-time changes. This is
        // the safety property that lets us add the wiring without changing
        // behaviour for the 99%+ of students with no profile on file.
        var p = AccommodationProfile.Default("stu-anon-none");

        Assert.False(p.TtsForProblemStatementsEnabled);
        Assert.Equal(1.0, p.ExtendedTimeMultiplier);
        Assert.False(p.DistractionReducedLayoutEnabled);
        Assert.False(p.NoComparativeStatsRequired);
    }

    [Fact]
    public void Phase1B_dimensions_in_profile_do_not_flip_any_accessor()
    {
        // Event replay might load a Phase 1B dimension that the UI cannot
        // render yet. The accessors must refuse to activate — IsEnabled(...)
        // already guards via Phase1ADimensions.IsShipped, so every derived
        // accessor inherits the safety property.
        var p = new AccommodationProfile(
            StudentAnonId: "stu-anon-phase1b",
            EnabledDimensions: new HashSet<AccommodationDimension>
            {
                AccommodationDimension.TtsForHints,
                AccommodationDimension.HighContrastTheme,
                AccommodationDimension.ReducedAnimations,
                AccommodationDimension.ProgressIndicatorToggle,
            },
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: "parent-hmac-replay",
            AssignedAtUtc: DateTimeOffset.UtcNow);

        Assert.False(p.TtsForProblemStatementsEnabled);
        Assert.Equal(1.0, p.ExtendedTimeMultiplier);
        Assert.False(p.DistractionReducedLayoutEnabled);
        Assert.False(p.NoComparativeStatsRequired);
    }
}
