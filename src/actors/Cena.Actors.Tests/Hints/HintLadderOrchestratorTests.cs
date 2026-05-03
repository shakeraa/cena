// =============================================================================
// prr-203 — HintLadderOrchestrator unit tests
//
// Coverage goals from the task DoD:
//
//   (a) L1 requested → template, no LLM
//   (b) L2 requested → L2 Haiku generator invoked (tier 2 path)
//   (c) L3 requested → L3 Sonnet generator invoked (tier 3 path)
//   (d) L2 returns null → falls back to L1 static (RungSource = "template-fallback")
//   (e) L3 returns null (cap exhausted) → falls back to L1 static
//   (f) LD-anxious governor applied at L1 when the profile flag is set
//   (g) Rung advancement: currentRung=0 → 1 → 2 → 3, clamps at 3
// =============================================================================

using Cena.Actors.Accommodations;
using Cena.Actors.Hints;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Hints;

public class HintLadderOrchestratorTests
{
    private readonly IL1TemplateHintGenerator _l1 = Substitute.For<IL1TemplateHintGenerator>();
    private readonly IL2HaikuHintGenerator _l2 = Substitute.For<IL2HaikuHintGenerator>();
    private readonly IL3WorkedExampleHintGenerator _l3 = Substitute.For<IL3WorkedExampleHintGenerator>();

    private HintLadderOrchestrator CreateSut() => new(
        _l1, _l2, _l3, NullLogger<HintLadderOrchestrator>.Instance);

    private static HintLadderInput Input(
        string sessionId = "sess-1",
        string questionId = "q-1") => new(
            SessionId: sessionId,
            QuestionId: questionId,
            ConceptId: "concept-linear",
            Subject: "algebra",
            QuestionStem: "Solve 2x+3 = 7",
            Explanation: "Subtract 3, divide by 2",
            Methodology: null,
            PrerequisiteConceptNames: new[] { "linear-equations" },
            InstituteId: "institute-test");

    // ─── Test (a) — L1 requested: template only, zero LLM ──────────────────

    [Fact]
    public async Task CurrentRung_0_returns_L1_from_template_generator_without_calling_L2_or_L3()
    {
        _l1.Generate(Arg.Any<HintLadderInput>(), Arg.Any<AccommodationProfile?>(), Arg.Any<string>())
            .Returns(new L1HintPayload("L1 TEMPLATE BODY"));

        var sut = CreateSut();
        var result = await sut.AdvanceAsync(Input(), currentRung: 0, accommodationProfile: null,
            CancellationToken.None);

        Assert.Equal(1, result.Rung);
        Assert.Equal("L1 TEMPLATE BODY", result.Body);
        Assert.Equal("template", result.RungSource);
        Assert.Equal(1, result.MaxRungReached);
        Assert.True(result.NextRungAvailable);

        // ADR-0045: L1 strictly no-LLM. The L2/L3 generators must never be
        // invoked on this path — this is the whole point of pinning L1 to
        // tier 1.
        await _l2.DidNotReceive().GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>());
        await _l3.DidNotReceive().GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>());
    }

    // ─── Test (b) — L2 requested: tier 2 path invoked ──────────────────────

    [Fact]
    public async Task CurrentRung_1_invokes_L2_Haiku_generator_and_does_not_invoke_L3()
    {
        _l2.GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>())
            .Returns(new L2HintPayload("Try factoring the common term."));

        var sut = CreateSut();
        var result = await sut.AdvanceAsync(Input(), currentRung: 1, accommodationProfile: null,
            CancellationToken.None);

        Assert.Equal(2, result.Rung);
        Assert.Equal("Try factoring the common term.", result.Body);
        Assert.Equal("haiku", result.RungSource);
        Assert.Equal(2, result.MaxRungReached);
        Assert.True(result.NextRungAvailable);

        await _l2.Received(1).GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>());
        await _l3.DidNotReceive().GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>());
    }

    // ─── Test (c) — L3 requested: tier 3 path invoked ──────────────────────

    [Fact]
    public async Task CurrentRung_2_invokes_L3_Sonnet_generator_and_reports_no_next_rung()
    {
        _l3.GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>())
            .Returns(new L3HintPayload("Step 1: ... Step 2: ..."));

        var sut = CreateSut();
        var result = await sut.AdvanceAsync(Input(), currentRung: 2, accommodationProfile: null,
            CancellationToken.None);

        Assert.Equal(3, result.Rung);
        Assert.Equal("Step 1: ... Step 2: ...", result.Body);
        Assert.Equal("sonnet", result.RungSource);
        Assert.Equal(3, result.MaxRungReached);
        Assert.False(result.NextRungAvailable);

        await _l3.Received(1).GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>());
        await _l2.DidNotReceive().GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>());
    }

    // ─── Test (d) — L2 null (LLM refused) falls back to static template ────

    [Fact]
    public async Task L2_null_falls_back_to_L1_static_with_template_fallback_source()
    {
        _l2.GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>())
            .Returns((L2HintPayload?)null);
        _l1.Generate(Arg.Any<HintLadderInput>(), Arg.Any<AccommodationProfile?>(), Arg.Any<string>())
            .Returns(new L1HintPayload("STATIC FALLBACK"));

        var sut = CreateSut();
        var result = await sut.AdvanceAsync(Input(), currentRung: 1, accommodationProfile: null,
            CancellationToken.None);

        // Rung stays at 2 (the endpoint already advanced); RungSource makes
        // the degradation visible to clients + observability.
        Assert.Equal(2, result.Rung);
        Assert.Equal("STATIC FALLBACK", result.Body);
        Assert.Equal("template-fallback", result.RungSource);
        Assert.True(result.NextRungAvailable);
    }

    // ─── Test (e) — L3 null (cap exhausted) falls back ─────────────────────

    [Fact]
    public async Task L3_null_falls_back_to_L1_static_and_reports_no_next_rung()
    {
        // Simulates the prr-012 Socratic cap exhaustion path: the L3 generator
        // returns null when CanMakeLlmCallAsync says "no budget", and the
        // orchestrator serves the static ladder copy.
        _l3.GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>())
            .Returns((L3HintPayload?)null);
        _l1.Generate(Arg.Any<HintLadderInput>(), Arg.Any<AccommodationProfile?>(), Arg.Any<string>())
            .Returns(new L1HintPayload("STATIC FALLBACK L3"));

        var sut = CreateSut();
        var result = await sut.AdvanceAsync(Input(), currentRung: 2, accommodationProfile: null,
            CancellationToken.None);

        Assert.Equal(3, result.Rung);
        Assert.Equal("STATIC FALLBACK L3", result.Body);
        Assert.Equal("template-fallback", result.RungSource);
        Assert.False(result.NextRungAvailable);
    }

    // ─── Test (f) — LD-anxious governor applied at L1 via profile ──────────
    // The orchestrator passes the profile through to L1TemplateHintGenerator;
    // the governor logic lives there. This test asserts the pass-through.

    [Fact]
    public async Task Accommodation_profile_is_threaded_through_to_L1_generator()
    {
        var profile = AccommodationProfile.Default("stu-anon-test");

        _l1.Generate(Arg.Any<HintLadderInput>(), profile, Arg.Any<string>())
            .Returns(new L1HintPayload("GOVERNED L1"));

        var sut = CreateSut();
        var result = await sut.AdvanceAsync(Input(), currentRung: 0, accommodationProfile: profile,
            CancellationToken.None);

        Assert.Equal("GOVERNED L1", result.Body);
        _l1.Received(1).Generate(Arg.Any<HintLadderInput>(), profile, Arg.Any<string>());
    }

    // ─── Test (g) — rung advancement + clamp at MaxRung ────────────────────

    [Fact]
    public async Task Advances_rung_by_one_per_call_up_to_MaxRung_then_clamps()
    {
        _l1.Generate(Arg.Any<HintLadderInput>(), Arg.Any<AccommodationProfile?>(), Arg.Any<string>())
            .Returns(new L1HintPayload("L1"));
        _l2.GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>())
            .Returns(new L2HintPayload("L2"));
        _l3.GenerateAsync(Arg.Any<HintLadderInput>(), Arg.Any<CancellationToken>())
            .Returns(new L3HintPayload("L3"));

        var sut = CreateSut();

        var r0 = await sut.AdvanceAsync(Input(), 0, null, CancellationToken.None);
        Assert.Equal(1, r0.Rung);

        var r1 = await sut.AdvanceAsync(Input(), 1, null, CancellationToken.None);
        Assert.Equal(2, r1.Rung);

        var r2 = await sut.AdvanceAsync(Input(), 2, null, CancellationToken.None);
        Assert.Equal(3, r2.Rung);
        Assert.False(r2.NextRungAvailable);

        // Already at max — stays at max. L3 generator gets hit again because
        // the orchestrator cannot assume the call was idempotent (some
        // students genuinely want the same worked example re-served).
        var r3 = await sut.AdvanceAsync(Input(), 3, null, CancellationToken.None);
        Assert.Equal(3, r3.Rung);
        Assert.False(r3.NextRungAvailable);
    }

    // ─── Bonus — fresh session resets rung (endpoint-level behaviour) ──────
    // Ladder state is stored per (sessionId, questionId) so different sessions
    // for the same student + same question start fresh. This is implicit in
    // the orchestrator's pure-function nature — no shared state between
    // sessions — but we assert it explicitly to guard against a refactor
    // that adds a cache.

    [Fact]
    public async Task Two_different_sessions_do_not_share_ladder_state()
    {
        _l1.Generate(Arg.Any<HintLadderInput>(), Arg.Any<AccommodationProfile?>(), Arg.Any<string>())
            .Returns(new L1HintPayload("L1"));

        var sut = CreateSut();

        var a = await sut.AdvanceAsync(
            Input(sessionId: "sess-A", questionId: "q-1"), 0, null, CancellationToken.None);
        var b = await sut.AdvanceAsync(
            Input(sessionId: "sess-B", questionId: "q-1"), 0, null, CancellationToken.None);

        // Each independent call from rung 0 returns rung 1 — the orchestrator
        // holds no cross-session state.
        Assert.Equal(1, a.Rung);
        Assert.Equal(1, b.Rung);
    }

    // ─── Validation ────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_on_out_of_range_currentRung()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.AdvanceAsync(Input(), -1, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.AdvanceAsync(Input(), 99, null, CancellationToken.None));
    }
}
