// =============================================================================
// Cena Platform — CAS Gate Idempotency Tests (RDY-036 §11 / RDY-037 / RDY-041)
//
// RDY-041 removed the `preComputedGateResult` trust-the-caller bypass. The
// persister no longer accepts a forged gate result — it always calls the
// gate itself. Idempotency is delivered by the gate's own cache
// (CasVerificationGate's (QuestionId, CorrectAnswerHash) query).
//
// These tests now pin:
//   1. The persister always calls the gate (no bypass).
//   2. Enforce-mode Failed outcome still throws.
//   3. Hash computation is stable across identical inputs.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public class CasIdempotencyTests
{
    private readonly ICasVerificationGate _gate = Substitute.For<ICasVerificationGate>();
    private readonly ICasGateModeProvider _mode = Substitute.For<ICasGateModeProvider>();
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();

    public CasIdempotencyTests()
    {
        _store.LightweightSession().Returns(_session);
        _mode.CurrentMode.Returns(CasGateMode.Enforce);
    }

    private CasGatedQuestionPersister Sut() =>
        new(_gate, _mode, _store, NullLogger<CasGatedQuestionPersister>.Instance);

    private static CasGateResult GateResult(CasGateOutcome outcome, string answer = "2")
    {
        var hash = QuestionCasBinding.ComputeAnswerHash(answer);
        return new CasGateResult(outcome, "SymPy", answer, hash, 10, null,
            new QuestionCasBinding
            {
                Id = "q-idem",
                QuestionId = "q-idem",
                Engine = CasEngine.SymPy,
                CanonicalAnswer = answer,
                CorrectAnswerRaw = answer,
                CorrectAnswerHash = hash,
                Status = outcome switch
                {
                    CasGateOutcome.Verified => CasBindingStatus.Verified,
                    CasGateOutcome.Failed => CasBindingStatus.Failed,
                    _ => CasBindingStatus.Unverifiable
                },
                VerifiedAt = DateTimeOffset.UtcNow,
                LatencyMs = 10
            });
    }

    [Fact]
    public async Task Persister_AlwaysCallsGate_NoBypass()
    {
        // RDY-041: no caller-supplied result path. The persister calls the
        // gate exactly once and uses its outcome. Forgery surface closed.
        _gate.VerifyForCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(GateResult(CasGateOutcome.Verified));

        await Sut().PersistAsync(
            questionId: "q-idem",
            creationEvent: new TestEvent("q-idem"),
            context: new GatedPersistContext("math", "solve 2x=4", "2", "en"));

        await _gate.Received(1).VerifyForCreateAsync(
            "q-idem", "math", "solve 2x=4", "2", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnforceMode_FailedOutcome_Throws()
    {
        // The persister translates CasGateOutcome.Failed into an exception
        // in Enforce mode — caller cannot sidestep ADR-0002.
        _gate.VerifyForCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(GateResult(CasGateOutcome.Failed));

        await Assert.ThrowsAsync<CasVerificationFailedException>(async () =>
            await Sut().PersistAsync(
                "q-idem",
                new TestEvent("q-idem"),
                new GatedPersistContext("math", "solve 2x=4", "wrong", "en")));
    }

    [Fact]
    public async Task OffMode_SkipsGateEntirely()
    {
        _mode.CurrentMode.Returns(CasGateMode.Off);

        await Sut().PersistAsync(
            "q-idem",
            new TestEvent("q-idem"),
            new GatedPersistContext("math", "solve 2x=4", "2", "en"));

        await _gate.DidNotReceive().VerifyForCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SessionAwareOverload_DoesNotSave_CallerOwnsCommit()
    {
        // RDY-039: session-aware overload must not call SaveChangesAsync —
        // caller composes with their own unit-of-work.
        _gate.VerifyForCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(GateResult(CasGateOutcome.Verified));

        await Sut().PersistAsync(
            session: _session,
            questionId: "q-idem",
            creationEvent: new TestEvent("q-idem"),
            context: new GatedPersistContext("math", "solve 2x=4", "2", "en"));

        await _session.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SameAnswerHash_Produces_SameBindingId()
    {
        var a = GateResult(CasGateOutcome.Verified, "2");
        var b = GateResult(CasGateOutcome.Verified, "2");
        Assert.Equal(a.Binding.CorrectAnswerHash, b.Binding.CorrectAnswerHash);
        Assert.Equal(a.Binding.Id, b.Binding.Id);
    }

    [Fact]
    public void DifferentAnswer_ProducesDifferentHash()
    {
        var h1 = QuestionCasBinding.ComputeAnswerHash("2");
        var h2 = QuestionCasBinding.ComputeAnswerHash("2.0"); // not same textually
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void EquivalentAnswerStrings_SameTextualForm_SameHash()
    {
        var h1 = QuestionCasBinding.ComputeAnswerHash("  x + 1  ");
        var h2 = QuestionCasBinding.ComputeAnswerHash("  x + 1  ");
        Assert.Equal(h1, h2);
    }

    private sealed record TestEvent(string QuestionId);
}
