// =============================================================================
// Cena Platform — PostReflectionMasteryService (EPIC-PRR-J PRR-381,
// EPIC-PRR-A mastery-engine link)
//
// Why this file exists
// --------------------
// PRR-381 post-reflection "productive failure" path: student fails a step,
// walks through the reflection gate, retries, CAS verifies the new step. If
// the CAS says "correct" (and returned a definitive Ok status), we emit a
// single MasterySignalEmitted_V1 event. That is the entire responsibility
// of this service — pure composition, no branching reward logic, no streak
// bookkeeping, no variable multiplier based on "how stuck" the student was.
// Those are banned dark patterns per memory "Ship-gate banned terms".
//
// Boundary contract
// -----------------
// The service is invoked by the retry endpoint / actor that already holds
// the CasVerifyResult. Inputs are the pseudonymous student id, the exam
// target code, the skill code, and the CAS result. Output is the emitted
// event (for caller logging / downstream projection), or null when the CAS
// result was not a verified success — which includes:
//
//   · Verified = false  — student's retry was wrong; no mastery nudge.
//   · Status  != Ok     — infrastructure degraded (SymPy timeout, circuit
//                          open, tier error). We MUST NOT fake a mastery
//                          signal when the correctness oracle itself failed
//                          to answer; doing so would let a wrong step earn
//                          mastery on transient outages, violating ADR-0002
//                          ("SymPy CAS is the sole correctness oracle").
//
// Both conditions collapse to "return null, emit nothing". That leaves the
// caller free to render a retry-still-wrong UI or an infrastructure-error
// UI without this service having any opinion on UX.
// =============================================================================

using Cena.Actors.Cas;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Mastery;

/// <summary>
/// Service that converts a post-reflection retry CAS result into a one-shot
/// mastery nudge. Only emits when the CAS verified the retry as correct and
/// the engine status was <see cref="CasVerifyStatus.Ok"/>.
/// </summary>
public interface IPostReflectionMasteryService
{
    /// <summary>
    /// Consider emitting a <see cref="MasterySignalEmitted_V1"/> for a
    /// post-reflection retry. Returns the emitted event on success, or
    /// <c>null</c> when nothing was emitted (wrong answer, or CAS
    /// infrastructure did not produce a definitive verdict).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="studentAnonId"/>, <paramref name="examTargetCode"/>,
    /// or <paramref name="skillCode"/> are null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="casResult"/> is null.
    /// </exception>
    Task<MasterySignalEmitted_V1?> HandleRetrySuccessAsync(
        string studentAnonId,
        string examTargetCode,
        string skillCode,
        CasVerifyResult casResult,
        CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IPostReflectionMasteryService"/>. Pure composition
/// over <see cref="IMasterySignalEmitter"/> and <see cref="MasterySignalOptions"/>.
/// No persistence beyond the emitter call.
/// </summary>
public sealed class PostReflectionMasteryService : IPostReflectionMasteryService
{
    private readonly IMasterySignalEmitter _emitter;
    private readonly MasterySignalOptions _options;
    private readonly TimeProvider _clock;

    public PostReflectionMasteryService(
        IMasterySignalEmitter emitter,
        IOptions<MasterySignalOptions> options,
        TimeProvider? clock = null)
    {
        _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new MasterySignalOptions();
        _clock = clock ?? TimeProvider.System;

        if (_options.MasteryDelta is <= 0d or >= 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.MasteryDelta,
                "MasterySignalOptions.MasteryDelta must be in the open interval (0, 1).");
        }
    }

    /// <inheritdoc />
    public async Task<MasterySignalEmitted_V1?> HandleRetrySuccessAsync(
        string studentAnonId,
        string examTargetCode,
        string skillCode,
        CasVerifyResult casResult,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }
        if (string.IsNullOrWhiteSpace(examTargetCode))
        {
            throw new ArgumentException(
                "examTargetCode must be non-empty.",
                nameof(examTargetCode));
        }
        if (string.IsNullOrWhiteSpace(skillCode))
        {
            throw new ArgumentException(
                "skillCode must be non-empty.",
                nameof(skillCode));
        }
        ArgumentNullException.ThrowIfNull(casResult);

        // Gate 1: CAS engine must have produced a definitive verdict. A
        // Timeout / Error / CircuitBreakerOpen / UnsupportedOperation status
        // means we don't know whether the step is correct, so we MUST NOT
        // reward it. ADR-0002: SymPy is the sole correctness oracle.
        if (casResult.Status != CasVerifyStatus.Ok)
        {
            return null;
        }

        // Gate 2: CAS must have verified the step as correct.
        if (!casResult.Verified)
        {
            return null;
        }

        var @event = new MasterySignalEmitted_V1(
            StudentAnonId: studentAnonId,
            ExamTargetCode: examTargetCode,
            SkillCode: skillCode,
            MasteryDelta: _options.MasteryDelta,
            TriggerSource: MasterySignalTrigger.PostReflectionRetrySuccess,
            EmittedAt: _clock.GetUtcNow());

        await _emitter.EmitAsync(@event, ct).ConfigureAwait(false);
        return @event;
    }
}
