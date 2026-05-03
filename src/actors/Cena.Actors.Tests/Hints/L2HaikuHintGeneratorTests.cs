// =============================================================================
// prr-203 — L2 Haiku Hint Generator unit tests
//
// Coverage:
//   1. Success path: LLM responds → payload body trimmed, cost metric emitted
//      with feature=hint-l2 tier=tier2 task=ideation_l2_hint.
//   2. PII scrubber increments → refuse the LLM call (ADR-0047 fail-closed).
//      Returns null; orchestrator degrades to L1 static.
//   3. LLM throws → null, no cost metric.
//   4. Empty content → null.
//   5. Cancellation propagates.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Actors.Hints;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Hints;

public class L2HaikuHintGeneratorTests
{
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly IPiiPromptScrubber _scrubber = Substitute.For<IPiiPromptScrubber>();
    private readonly ILlmCostMetric _cost = Substitute.For<ILlmCostMetric>();
    private readonly IActivityPropagator _activityPropagator = Substitute.For<IActivityPropagator>();

    private L2HaikuHintGenerator CreateSut() => new(
        _llm, _scrubber, _cost, _activityPropagator,
        NullLogger<L2HaikuHintGenerator>.Instance);

    private static HintLadderInput SampleInput() => new(
        SessionId: "sess-1",
        QuestionId: "q-1",
        ConceptId: "concept-linear",
        Subject: "algebra",
        QuestionStem: "Solve 2x+3=7",
        Explanation: null,
        Methodology: null,
        PrerequisiteConceptNames: new[] { "linear-equations" },
        InstituteId: "inst-a");

    private void ScrubberReturnsClean() =>
        _scrubber.Scrub(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new PiiScrubResult(
                (string)ci[0], 0, Array.Empty<string>()));

    [Fact]
    public async Task Success_path_emits_cost_metric_with_tier2_labels()
    {
        ScrubberReturnsClean();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(
                Content: "  Try factoring the common term.  ",
                InputTokens: 120, OutputTokens: 45,
                Latency: TimeSpan.FromMilliseconds(600),
                ModelId: "claude-haiku-4-5-20260101", FromCache: false));

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Try factoring the common term.", result!.Body);
        Assert.Equal("haiku", result.RungSource);

        // prr-046: success-path emission with the exact tier + task tags
        // ADR-0045 §3 pins the L2 path to.
        _cost.Received(1).Record(
            feature: "hint-l2",
            tier: "tier2",
            task: "ideation_l2_hint",
            modelId: "claude-haiku-4-5-20260101",
            inputTokens: 120,
            outputTokens: 45,
            instituteId: "inst-a");
    }

    [Fact]
    public async Task PII_scrubber_hits_trigger_fail_closed_null_and_no_LLM_call()
    {
        _scrubber.Scrub(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PiiScrubResult(
                ScrubbedText: "whatever",
                RedactionCount: 1,
                Categories: new[] { "email" }));

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.Null(result);
        await _llm.DidNotReceive().CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
        _cost.DidNotReceive().Record(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task LLM_exception_returns_null_and_no_cost_metric()
    {
        ScrubberReturnsClean();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns<LlmResponse>(_ => throw new HttpRequestException("upstream 503"));

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.Null(result);
        _cost.DidNotReceive().Record(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Empty_LLM_content_returns_null()
    {
        ScrubberReturnsClean();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(
                Content: "   ", InputTokens: 10, OutputTokens: 0,
                Latency: TimeSpan.Zero, ModelId: "claude-haiku-4-5-20260101",
                FromCache: false));

        var sut = CreateSut();
        var result = await sut.GenerateAsync(SampleInput(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        ScrubberReturnsClean();
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns<LlmResponse>(_ => throw new OperationCanceledException());

        var sut = CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.GenerateAsync(SampleInput(), CancellationToken.None));
    }
}
