// =============================================================================
// Cena Platform — CAS Gating Behavioral Tests (RDY-036 §11 / RDY-037)
//
// Verifies the CasGatedQuestionPersister honours the three CasGateMode
// values exactly (Off / Shadow / Enforce) and applies the Enforce-mode
// reject contract (throws CasVerificationFailedException on Failed). These
// are the acceptance criteria that keep ADR-0002 enforceable without the
// pre-RDY-037 bypasses.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public class CasGatingTests
{
    private readonly ICasVerificationGate _gate = Substitute.For<ICasVerificationGate>();
    private readonly ICasGateModeProvider _mode = Substitute.For<ICasGateModeProvider>();
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();

    public CasGatingTests()
    {
        _store.LightweightSession().Returns(_session);
    }

    private CasGatedQuestionPersister Sut() =>
        new(_gate, _mode, _store, NullLogger<CasGatedQuestionPersister>.Instance);

    private static GatedPersistContext MathCtx(string correct) =>
        new("math", "Solve 2x+1=5.", correct, "en");

    private static QuestionCasBinding BindingOf(CasBindingStatus status) =>
        new()
        {
            Id = "q-test",
            QuestionId = "q-test",
            Engine = CasEngine.SymPy,
            CanonicalAnswer = "2",
            CorrectAnswerRaw = "2",
            CorrectAnswerHash = QuestionCasBinding.ComputeAnswerHash("2"),
            Status = status,
            VerifiedAt = DateTimeOffset.UtcNow,
            LatencyMs = 10
        };

    private static CasGateResult Result(CasGateOutcome outcome, string reason = null!) =>
        new(outcome, "SymPy", "2", QuestionCasBinding.ComputeAnswerHash("2"), 10, reason,
            BindingOf(outcome switch
            {
                CasGateOutcome.Verified => CasBindingStatus.Verified,
                CasGateOutcome.Failed => CasBindingStatus.Failed,
                _ => CasBindingStatus.Unverifiable
            }));

    // ── Mode: Off ─────────────────────────────────────────────────────

    [Fact]
    public async Task OffMode_SkipsGateEntirely_NoBindingStored()
    {
        _mode.CurrentMode.Returns(CasGateMode.Off);

        var outcome = await Sut().PersistAsync(
            "q-test", new TestCreationEvent("q-test"), MathCtx("2"));

        await _gate.DidNotReceive().VerifyForCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        Assert.Equal(CasGateOutcome.Unverifiable, outcome.Outcome);
        Assert.Equal("off", outcome.Engine);
    }

    // ── Mode: Enforce ─────────────────────────────────────────────────

    [Fact]
    public async Task EnforceMode_FailedOutcome_ThrowsCasVerificationFailedException()
    {
        _mode.CurrentMode.Returns(CasGateMode.Enforce);
        _gate.VerifyForCreateAsync("q-test", "math", Arg.Any<string>(), "wrong", null, Arg.Any<CancellationToken>())
             .Returns(Result(CasGateOutcome.Failed, "answer contradicts canonical form"));

        await Assert.ThrowsAsync<CasVerificationFailedException>(async () =>
            await Sut().PersistAsync(
                "q-test", new TestCreationEvent("q-test"), MathCtx("wrong")));
    }

    [Fact]
    public async Task EnforceMode_VerifiedOutcome_PersistsBindingAndEvents()
    {
        _mode.CurrentMode.Returns(CasGateMode.Enforce);
        _gate.VerifyForCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                   Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(Result(CasGateOutcome.Verified));

        var outcome = await Sut().PersistAsync(
            "q-test", new TestCreationEvent("q-test"), MathCtx("2"));

        Assert.Equal(CasGateOutcome.Verified, outcome.Outcome);
        _session.Received(1).Store(Arg.Any<QuestionCasBinding>());
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnforceMode_CircuitOpen_DoesNotThrow_MarksUnverifiable()
    {
        // CircuitOpen must NEVER fail the write — question flows into
        // NeedsReview so admin queue decides. Only Failed is fatal.
        _mode.CurrentMode.Returns(CasGateMode.Enforce);
        _gate.VerifyForCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                   Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(Result(CasGateOutcome.CircuitOpen, "sidecar unreachable"));

        var outcome = await Sut().PersistAsync(
            "q-test", new TestCreationEvent("q-test"), MathCtx("2"));

        Assert.Equal(CasGateOutcome.CircuitOpen, outcome.Outcome);
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Mode: Shadow ──────────────────────────────────────────────────

    [Fact]
    public async Task ShadowMode_FailedOutcome_DoesNotThrow_BindingStillStored()
    {
        // Shadow: record-only. Failed outcome is logged but the write
        // proceeds. This is the 48h observation window in the rollout plan.
        _mode.CurrentMode.Returns(CasGateMode.Shadow);
        _gate.VerifyForCreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                   Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(Result(CasGateOutcome.Failed, "would fail"));

        var outcome = await Sut().PersistAsync(
            "q-test", new TestCreationEvent("q-test"), MathCtx("wrong"));

        Assert.Equal(CasGateOutcome.Failed, outcome.Outcome);
        _session.Received(1).Store(Arg.Any<QuestionCasBinding>());
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Guard rails ───────────────────────────────────────────────────

    [Fact]
    public async Task NullQuestionId_Throws()
    {
        _mode.CurrentMode.Returns(CasGateMode.Off);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Sut().PersistAsync(
                string.Empty, new TestCreationEvent("x"), MathCtx("2")));
    }

    [Fact]
    public async Task NullCreationEvent_Throws()
    {
        _mode.CurrentMode.Returns(CasGateMode.Off);
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await Sut().PersistAsync("q-test", null!, MathCtx("2")));
    }

    private sealed record TestCreationEvent(string QuestionId);
}
