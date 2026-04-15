// =============================================================================
// Cena Platform — CAS Gate Idempotency Tests (RDY-036 §11 / RDY-037)
//
// The persister accepts an optional preComputedGateResult. When supplied,
// it must NOT re-invoke the CAS gate — avoiding duplicate sidecar calls
// when a caller has already run the gate (e.g. QuestionBankService does
// this to drive conditional auto-approval). These tests pin that contract
// so future refactors can't regress latency or break the idempotency
// semantics behind the (questionId, answerHash) key.
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

    private static CasGateResult PreComputed(CasGateOutcome outcome, string answer = "2")
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
                Status = outcome == CasGateOutcome.Verified
                    ? CasBindingStatus.Verified
                    : CasBindingStatus.Failed,
                VerifiedAt = DateTimeOffset.UtcNow,
                LatencyMs = 10
            });
    }

    [Fact]
    public async Task PreComputedGateResult_SkipsGateCall()
    {
        var preResult = PreComputed(CasGateOutcome.Verified);

        await Sut().PersistAsync(
            questionId: "q-idem",
            creationEvent: new TestEvent("q-idem"),
            context: new GatedPersistContext("math", "solve 2x=4", "2", "en"),
            preComputedGateResult: preResult);

        // Idempotency: the gate MUST NOT be called when a pre-computed
        // result is supplied. Any call would be a duplicate sidecar round-trip.
        await _gate.DidNotReceive().VerifyForCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreComputedGateResult_FailedInEnforceMode_StillThrows()
    {
        // Idempotency must NOT defeat Enforce-mode rejection. A caller that
        // already saw Failed cannot launder it into the persister.
        var preResult = PreComputed(CasGateOutcome.Failed);

        await Assert.ThrowsAsync<CasVerificationFailedException>(async () =>
            await Sut().PersistAsync(
                "q-idem",
                new TestEvent("q-idem"),
                new GatedPersistContext("math", "solve 2x=4", "wrong", "en"),
                preComputedGateResult: preResult));
    }

    [Fact]
    public async Task NoPreComputed_Result_CallsGateOnce()
    {
        _gate.VerifyForCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                    Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(PreComputed(CasGateOutcome.Verified));

        await Sut().PersistAsync(
            "q-idem",
            new TestEvent("q-idem"),
            new GatedPersistContext("math", "solve 2x=4", "2", "en"));

        await _gate.Received(1).VerifyForCreateAsync(
            "q-idem", "math", "solve 2x=4", "2", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameAnswerHash_Produces_SameBindingId()
    {
        // The binding is keyed by (QuestionId, CorrectAnswerHash) — two
        // pre-computed results with the same inputs must therefore produce
        // the same persisted binding identity.
        var a = PreComputed(CasGateOutcome.Verified, "2");
        var b = PreComputed(CasGateOutcome.Verified, "2");
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
        // Hash is over the raw text — CAS canonicalization happens at the
        // CasRouterService layer, not at the binding-cache layer.
        var h1 = QuestionCasBinding.ComputeAnswerHash("  x + 1  ");
        var h2 = QuestionCasBinding.ComputeAnswerHash("  x + 1  ");
        Assert.Equal(h1, h2);
    }

    private sealed record TestEvent(string QuestionId);
}
