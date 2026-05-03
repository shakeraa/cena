// =============================================================================
// Cena Platform — PostReflectionMasteryService tests (EPIC-PRR-J PRR-381)
//
// Covers:
//   · CAS verified success → event emitted with correct fields.
//   · CAS failure (Verified=false, Status=Ok) → null, no event.
//   · CAS non-Ok status (Timeout / Error / CircuitBreakerOpen /
//     UnsupportedOperation) → null, no event (no fake mastery on outage,
//     ADR-0002).
//   · MasteryDelta honours options (default 0.05, override respected).
//   · TriggerSource is the canonical post_reflection_retry_success.
//   · Empty studentAnonId / examTargetCode / skillCode → ArgumentException.
//   · Payload field names / values have no shipgate-banned terms.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Mastery;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public sealed class PostReflectionMasteryServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static PostReflectionMasteryService BuildService(
        InMemoryMasterySignalEmitter emitter,
        MasterySignalOptions? options = null,
        DateTimeOffset? now = null)
    {
        var clock = new FakeTimeProvider(now ?? Now);
        return new PostReflectionMasteryService(
            emitter,
            Options.Create(options ?? new MasterySignalOptions()),
            clock);
    }

    private static CasVerifyResult Verified() =>
        CasVerifyResult.Success(CasOperation.StepValidity, engine: "sympy", latencyMs: 12d);

    private static CasVerifyResult NotVerified() =>
        CasVerifyResult.Failure(CasOperation.StepValidity, engine: "sympy", latencyMs: 9d,
            errorMessage: "step does not preserve equality");

    private static CasVerifyResult EngineError(CasVerifyStatus status) =>
        CasVerifyResult.Error(CasOperation.StepValidity, engine: "sympy", latencyMs: 50d,
            errorMessage: status.ToString(), status: status);

    [Fact]
    public async Task CAS_verified_success_emits_event_with_correct_fields()
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        var emitted = await svc.HandleRetrySuccessAsync(
            studentAnonId: "stu_abc",
            examTargetCode: "bagrut.math.5yu",
            skillCode: "math.algebra.quadratic-equations",
            casResult: Verified(),
            CancellationToken.None);

        Assert.NotNull(emitted);
        Assert.Equal("stu_abc", emitted!.StudentAnonId);
        Assert.Equal("bagrut.math.5yu", emitted.ExamTargetCode);
        Assert.Equal("math.algebra.quadratic-equations", emitted.SkillCode);
        Assert.Equal(MasterySignalOptions.DefaultDelta, emitted.MasteryDelta);
        Assert.Equal(MasterySignalTrigger.PostReflectionRetrySuccess, emitted.TriggerSource);
        Assert.Equal(Now, emitted.EmittedAt);

        // Emitter received exactly one event.
        Assert.Single(emitter.Events);
        Assert.Same(emitted, emitter.Events.Single());
    }

    [Fact]
    public async Task CAS_not_verified_returns_null_and_emits_nothing()
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        var emitted = await svc.HandleRetrySuccessAsync(
            "stu_abc", "bagrut.math.5yu", "math.algebra.quadratic-equations",
            NotVerified(), CancellationToken.None);

        Assert.Null(emitted);
        Assert.Empty(emitter.Events);
    }

    [Theory]
    [InlineData(CasVerifyStatus.Timeout)]
    [InlineData(CasVerifyStatus.Error)]
    [InlineData(CasVerifyStatus.CircuitBreakerOpen)]
    [InlineData(CasVerifyStatus.UnsupportedOperation)]
    public async Task CAS_non_Ok_status_returns_null_and_emits_nothing(CasVerifyStatus status)
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        var emitted = await svc.HandleRetrySuccessAsync(
            "stu_abc", "bagrut.math.5yu", "math.algebra.quadratic-equations",
            EngineError(status), CancellationToken.None);

        Assert.Null(emitted);
        Assert.Empty(emitter.Events);
    }

    [Fact]
    public async Task MasteryDelta_honours_options_default()
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        var emitted = await svc.HandleRetrySuccessAsync(
            "stu_abc", "bagrut.math.5yu", "math.algebra.quadratic-equations",
            Verified(), CancellationToken.None);

        Assert.NotNull(emitted);
        Assert.Equal(0.05d, emitted!.MasteryDelta);
    }

    [Fact]
    public async Task MasteryDelta_honours_options_override()
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter, new MasterySignalOptions { MasteryDelta = 0.02d });

        var emitted = await svc.HandleRetrySuccessAsync(
            "stu_abc", "bagrut.math.5yu", "math.algebra.quadratic-equations",
            Verified(), CancellationToken.None);

        Assert.NotNull(emitted);
        Assert.Equal(0.02d, emitted!.MasteryDelta);
    }

    [Fact]
    public async Task TriggerSource_is_post_reflection_retry_success()
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        var emitted = await svc.HandleRetrySuccessAsync(
            "stu_abc", "bagrut.math.5yu", "math.algebra.quadratic-equations",
            Verified(), CancellationToken.None);

        Assert.NotNull(emitted);
        Assert.Equal("post_reflection_retry_success", emitted!.TriggerSource);
    }

    [Theory]
    [InlineData("", "target", "skill")]
    [InlineData(" ", "target", "skill")]
    [InlineData("stu", "", "skill")]
    [InlineData("stu", " ", "skill")]
    [InlineData("stu", "target", "")]
    [InlineData("stu", "target", " ")]
    public async Task Empty_identifiers_throw_ArgumentException(
        string studentAnonId, string examTargetCode, string skillCode)
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.HandleRetrySuccessAsync(
                studentAnonId, examTargetCode, skillCode,
                Verified(), CancellationToken.None));

        Assert.Empty(emitter.Events);
    }

    [Fact]
    public async Task Null_cas_result_throws_ArgumentNullException()
    {
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            svc.HandleRetrySuccessAsync(
                "stu_abc", "bagrut.math.5yu", "math.algebra.quadratic-equations",
                casResult: null!, CancellationToken.None));
    }

    [Fact]
    public void Options_with_out_of_range_delta_rejects_in_ctor()
    {
        var emitter = new InMemoryMasterySignalEmitter();

        Assert.Throws<ArgumentOutOfRangeException>(() => BuildService(
            emitter, new MasterySignalOptions { MasteryDelta = 0.0d }));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildService(
            emitter, new MasterySignalOptions { MasteryDelta = 1.0d }));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildService(
            emitter, new MasterySignalOptions { MasteryDelta = -0.1d }));
    }

    [Fact]
    public async Task Emitted_payload_contains_no_shipgate_banned_terms()
    {
        // Sanity check: the event's field names and values (trigger,
        // target / skill codes) must NOT contain ship-gate banned dark-
        // pattern terms. They shouldn't — we explicitly avoid them —
        // but a direct assertion here means a future refactor that sneaks
        // one in still fails a focused test rather than the CI scanner.
        var emitter = new InMemoryMasterySignalEmitter();
        var svc = BuildService(emitter);

        var emitted = await svc.HandleRetrySuccessAsync(
            "stu_abc", "bagrut.math.5yu", "math.algebra.quadratic-equations",
            Verified(), CancellationToken.None);

        Assert.NotNull(emitted);

        var payload = string.Join(" | ",
            emitted!.StudentAnonId,
            emitted.ExamTargetCode,
            emitted.SkillCode,
            emitted.TriggerSource,
            emitted.MasteryDelta.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Word-bounded matches so legitimate substrings ("streak-" in a
        // hypothetical test code) would still trip but our real payload
        // stays clean.
        Assert.DoesNotMatch(@"(?i)\bstreak\b", payload);
        Assert.DoesNotMatch(@"(?i)countdown", payload);
        Assert.DoesNotMatch(@"(?i)falling\s+behind", payload);
        Assert.DoesNotMatch(@"(?i)catch\s*up", payload);
        Assert.DoesNotMatch(@"(?i)variable\s*-?\s*ratio", payload);
        Assert.DoesNotMatch(@"(?i)time\s+is\s+running\s+out", payload);

        // Banned-terms audit against the field NAMES of the event record
        // itself (so a rename that introduces "Streak" breaks here).
        var fieldNames = typeof(MasterySignalEmitted_V1)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();
        foreach (var name in fieldNames)
        {
            Assert.DoesNotMatch(@"(?i)\bstreak\b", name);
            Assert.DoesNotMatch(@"(?i)countdown", name);
            Assert.DoesNotMatch(@"(?i)variable\s*ratio", name);
        }
    }

    // ---- test doubles ----

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
