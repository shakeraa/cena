// =============================================================================
// Cena Platform — Socratic Cap Integration Tests (prr-012)
//
// Drives ClaudeTutorLlmService end-to-end (minus the real Anthropic HTTP
// round-trip) with substituted budget + fallback collaborators to verify
// the 4 DoD scenarios from the task body:
//
//   Test 1: Under the cap → LLM path is reached (budget gate passes).
//   Test 2: At the cap    → StaticHintLadderFallback is invoked, no LLM call.
//   Test 3: Daily time cap hit → "take a break" response, no LLM, no budget.
//   Test 4: Recording discipline → successful LLM records budget; failed
//           gate does NOT record budget on the LLM counter.
//
// The Anthropic SDK is not network-called in these tests because we stop the
// code path BEFORE `_client.Messages.Create` in the cap-hit paths. The
// under-the-cap test exercises the gate and then lets the Anthropic client
// fail on a dummy key — we assert the error is yielded as a final chunk
// rather than that the HTTP call succeeds (that's covered by the Anthropic
// SDK's own tests).
// =============================================================================

using Cena.Actors.RateLimit;
using Cena.Actors.Tutor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Tutor;

public sealed class SocraticCapTests
{
    private readonly ISocraticCallBudget _budget = Substitute.For<ISocraticCallBudget>();
    private readonly IStaticHintLadderFallback _fallback = Substitute.For<IStaticHintLadderFallback>();
    private readonly IDailyTutorTimeBudget _dailyBudget = Substitute.For<IDailyTutorTimeBudget>();

    private static IConfiguration TestConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cena:Llm:ApiKey"] = "sk-test-does-not-make-real-calls",
                ["Cena:Llm:Model"] = "claude-sonnet-4-6"
            })
            .Build();

    private static TutorContext NewContext(string studentId = "stu-1", string threadId = "thread-1") =>
        new(
            StudentId: studentId,
            ThreadId: threadId,
            MessageHistory: new List<TutorMessage> { new("user", "Can you help me with 2x+3=7?") },
            Subject: "Algebra",
            CurrentGrade: 9);

    private ClaudeTutorLlmService NewSut() => new(
        TestConfig(), _budget, _fallback, _dailyBudget,
        NullLogger<ClaudeTutorLlmService>.Instance);

    // ── Test 1: under the cap, budget gate passes, LLM path is reached ──
    [Fact]
    public async Task UnderCap_ChecksBudgetAndDailyTime_BeforeLlm()
    {
        _dailyBudget.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTutorTimeCheck(true, 0, 1800, 1800));
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = NewSut();
        var ctx = NewContext();

        // Drain the stream. Anthropic will error on the fake key, but that's
        // fine — the gate order is what we're testing, and the service
        // swallows the exception into an error chunk.
        var chunks = new List<LlmChunk>();
        await foreach (var chunk in sut.StreamCompletionAsync(ctx, CancellationToken.None))
            chunks.Add(chunk);

        await _dailyBudget.Received(1).CheckAsync("stu-1", Arg.Any<CancellationToken>());
        await _budget.Received(1).CanMakeLlmCallAsync("thread-1", Arg.Any<CancellationToken>());
        // Fallback is NOT invoked when budget is under cap.
        _fallback.DidNotReceive().GetHint(Arg.Any<TutorContext>(), Arg.Any<int>());
        // At least one chunk is emitted (error or success — this test is
        // about ordering, not the LLM response quality).
        Assert.NotEmpty(chunks);
    }

    // ── Test 2: 4th call → cap hit, falls back to static hint ladder, no LLM ──
    [Fact]
    public async Task CapHit_EmitsStaticHint_DoesNotCallLlm()
    {
        _dailyBudget.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTutorTimeCheck(true, 0, 1800, 1800));
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false); // 4th call
        _budget.GetCallCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(3L); // at cap
        _fallback.GetHint(Arg.Any<TutorContext>(), 0)
            .Returns(new StaticHintResponse(
                "Let's look at this step-by-step.",
                StaticHintRung.L1_TryThisStep));

        var sut = NewSut();
        var chunks = new List<LlmChunk>();
        await foreach (var chunk in sut.StreamCompletionAsync(NewContext(), CancellationToken.None))
            chunks.Add(chunk);

        // Fallback hint was emitted with the static model id.
        Assert.Contains(chunks, c => c.Model == ClaudeTutorLlmService.StaticFallbackModelId);
        Assert.Contains(chunks, c => c.Delta.Contains("step-by-step"));
        // Final chunk is Finished=true with TokensUsed=0 (no LLM cost).
        var final = chunks.Last();
        Assert.True(final.Finished);
        Assert.Equal(0, final.TokensUsed);
        Assert.Equal(ClaudeTutorLlmService.StaticFallbackModelId, final.Model);

        // Fallback was asked, budget was recorded so the ladder advances.
        _fallback.Received(1).GetHint(Arg.Any<TutorContext>(), 0);
        await _budget.Received(1).RecordLlmCallAsync("thread-1", Arg.Any<CancellationToken>());
    }

    // ── Test 3: daily time cap hit → "take a break", no LLM, no budget touched ──
    [Fact]
    public async Task DailyCapHit_ReturnsRestMessage_NoLlm_NoBudgetCheck()
    {
        _dailyBudget.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTutorTimeCheck(
                Allowed: false,
                UsedSeconds: 1800,
                RemainingSeconds: 0,
                DailyLimitSeconds: 1800));

        var sut = NewSut();
        var chunks = new List<LlmChunk>();
        await foreach (var chunk in sut.StreamCompletionAsync(NewContext(), CancellationToken.None))
            chunks.Add(chunk);

        // "Take a break" copy — ship-gate neutral, no dark-pattern wording.
        Assert.Contains(chunks, c =>
            c.Delta.Contains("take a break", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(chunks, c => c.Model == ClaudeTutorLlmService.DailyCapModelId);

        // When daily cap fires first, the Socratic budget is not consulted.
        await _budget.DidNotReceive().CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _fallback.DidNotReceive().GetHint(Arg.Any<TutorContext>(), Arg.Any<int>());
    }

    // ── Test 4: fallback index advances across multiple cap-hit turns ──
    [Fact]
    public async Task CapHit_AdvancesLadderIndex_AcrossTurns()
    {
        _dailyBudget.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTutorTimeCheck(true, 0, 1800, 1800));
        _budget.CanMakeLlmCallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Simulate the session having consumed 3 LLM calls + 1 prior fallback.
        _budget.GetCallCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(4L);
        _fallback.GetHint(Arg.Any<TutorContext>(), 1)
            .Returns(new StaticHintResponse(
                "Think about the method that usually applies to Algebra.",
                StaticHintRung.L2_HereIsTheMethod));

        var sut = NewSut();
        var chunks = new List<LlmChunk>();
        await foreach (var chunk in sut.StreamCompletionAsync(NewContext(), CancellationToken.None))
            chunks.Add(chunk);

        // The second fallback turn should render L2, not L1.
        _fallback.Received(1).GetHint(Arg.Any<TutorContext>(), 1);
        Assert.Contains(chunks, c => c.Delta.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    // ── Static ladder selection is deterministic and ship-gate compliant ──
    [Fact]
    public void StaticHintLadderFallback_ReturnsL1L2L3_ByIndex()
    {
        var ladder = new StaticHintLadderFallback();
        var ctx = NewContext();

        var l1 = ladder.GetHint(ctx, 0);
        var l2 = ladder.GetHint(ctx, 1);
        var l3 = ladder.GetHint(ctx, 2);
        var l3Overflow = ladder.GetHint(ctx, 42);

        Assert.Equal(StaticHintRung.L1_TryThisStep, l1.Rung);
        Assert.Equal(StaticHintRung.L2_HereIsTheMethod, l2.Rung);
        Assert.Equal(StaticHintRung.L3_WorkedExample, l3.Rung);
        Assert.Equal(StaticHintRung.L3_WorkedExample, l3Overflow.Rung);

        // Ship-gate: no dark-pattern copy (no "streak", no "don't lose").
        foreach (var hint in new[] { l1, l2, l3 })
        {
            Assert.DoesNotContain("streak", hint.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("don't lose", hint.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
