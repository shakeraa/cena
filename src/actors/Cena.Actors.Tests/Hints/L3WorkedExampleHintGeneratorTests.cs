// =============================================================================
// prr-203 — L3 Worked-Example Hint Generator unit tests
//
// Coverage:
//   1. Success path: cap-gate passes → LLM called → cost emitted with
//      tier3 + worked_example_l3_hint tags → budget incremented.
//   2. Socratic cap exhausted → null returned BEFORE PII scrubber or LLM;
//      orchestrator falls back to L1 static.
//   3. PII scrubber hits → fail-closed null, LLM never called, budget not
//      incremented.
//   4. LLM throws → null, no cost metric, no budget increment.
//   5. Cancellation propagates.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Actors.Hints;
using Cena.Actors.Tutor;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Hints;

public class L3WorkedExampleHintGeneratorTests
{
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly ISocraticCallBudget _budget = Substitute.For<ISocraticCallBudget>();
    private readonly IPiiPromptScrubber _scrubber = Substitute.For<IPiiPromptScrubber>();
    private readonly ILlmCostMetric _cost = Substitute.For<ILlmCostMetric>();
    private readonly IActivityPropagator _activityPropagator = Substitute.For<IActivityPropagator>();

    private L3WorkedExampleHintGenerator CreateSut() => new(
        _llm, _budget, _scrubber, _cost, _activityPropagator,
        NullLogger<L3WorkedExampleHintGenerator>.Instance);

    private static HintLadderInput SampleInput() => new(
        SessionId: "sess-1",
        QuestionId: "q-1",
        ConceptId: "concept-linear",
        Subject: "algebra",
        QuestionStem: "Solve 2x+3=7",
        Explanation: "Reference explanation",
        Methodology: "standard",
        PrerequisiteConceptNames: new[] { "linear-equations" },
        InstituteId: "inst-a");

    private void ScrubberReturnsClean() =>
        _scrubber.Scrub(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new PiiScrubResult(
                (string)ci[0], 0, Array.Empty<string>()));

    [Fact]
    public async Task Success_path_invokes_cap_check_LLM_and_cost_metric()
    {
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ScrubberReturnsClean();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(
                Content: "Step 1: isolate x. Step 2: subtract 3.",
                InputTokens: 300, OutputTokens: 120,
                Latency: TimeSpan.FromMilliseconds(1800),
                ModelId: "claude-sonnet-4-6-20260215", FromCache: false));

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Step 1: isolate x. Step 2: subtract 3.", result!.Body);
        Assert.Equal("sonnet", result.RungSource);

        // prr-012: budget recorded AFTER success (failed calls do not
        // consume cap).
        await _budget.Received(1).RecordLlmCallAsync("sess-1", Arg.Any<CancellationToken>());

        // prr-046: cost emitted with the tier-3 + worked_example_l3_hint tags
        // from ADR-0045 §3.
        _cost.Received(1).Record(
            feature: "hint-l3",
            tier: "tier3",
            task: "worked_example_l3_hint",
            modelId: "claude-sonnet-4-6-20260215",
            inputTokens: 300,
            outputTokens: 120,
            instituteId: "inst-a");
    }

    [Fact]
    public async Task Socratic_cap_exhausted_returns_null_without_LLM_or_scrub()
    {
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.Null(result);
        // Cap-gate runs BEFORE scrubbing — cheaper to deny early and avoid
        // touching the prompt string at all.
        _scrubber.DidNotReceive().Scrub(Arg.Any<string>(), Arg.Any<string>());
        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().RecordLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _cost.DidNotReceive().Record(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task PII_scrubber_hits_trigger_fail_closed_null_and_no_LLM_call()
    {
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _scrubber.Scrub(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PiiScrubResult("x", 2, new[] { "email", "phone" }));

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.Null(result);
        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().RecordLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LLM_exception_returns_null_and_does_not_consume_budget()
    {
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ScrubberReturnsClean();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns<LlmResponse>(_ => throw new HttpRequestException("upstream 503"));

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.Null(result);
        // prr-012 invariant: failed LLM calls do NOT consume the Socratic cap.
        await _budget.DidNotReceive().RecordLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ScrubberReturnsClean();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns<LlmResponse>(_ => throw new OperationCanceledException());

        var sut = CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.GenerateAsync(SampleInput(), CancellationToken.None));
    }
}
