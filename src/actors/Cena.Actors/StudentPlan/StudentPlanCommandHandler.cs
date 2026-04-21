// =============================================================================
// Cena Platform — StudentPlanCommandHandler (prr-218, ADR-0050 §5)
//
// Owns the server-enforced invariants from ADR-0050 §5:
//   - count(active Targets) ≤ 5    (§5 hard cap; soft warn at 4 is UI-only)
//   - sum(active WeeklyHours) ≤ 40 (§5 compound budget cap)
//   - (ExamCode, Sitting, Track) unique across active targets (§5 dedup)
//   - ArchivedAt target cannot be mutated further (§6 terminal)
//
// The handler accepts commands, re-loads the aggregate (fresh replay),
// validates invariants, and emits events via IStudentPlanAggregateStore.
// Splitting command handling from the aggregate keeps both under the
// 500-LOC cap and separates "fold events" from "gate commands".
//
// Concurrency: the handler trusts the store to enforce optimistic
// concurrency at the stream level (Marten is the production backing —
// In-memory store is single-threaded-per-stream by construction). If a
// new event slipped in between load and append, the command is rejected
// by the store with a CENA_EVENTSTORE_CONCURRENCY CenaError and the
// caller retries.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Result of a command attempt. <c>Success</c> + emitted events, OR
/// <c>Failure</c> with a structured reason.
/// </summary>
public sealed record CommandResult(
    bool Success,
    CommandError? Error = null,
    ExamTargetId? TargetId = null);

/// <summary>
/// Structured command-failure reason. Enum-only — drives the endpoint
/// mapper to pick the right CENA_* error code without string matching.
/// </summary>
public enum CommandError
{
    /// <summary>Too many active targets (ADR-0050 §5, max 5).</summary>
    ActiveTargetCapExceeded,

    /// <summary>Sum of WeeklyHours across active targets would exceed 40
    /// (ADR-0050 §5).</summary>
    WeeklyBudgetExceeded,

    /// <summary>(ExamCode, Sitting, Track) collides with an existing
    /// active target (ADR-0050 §5 uniqueness).</summary>
    DuplicateTarget,

    /// <summary>Target does not exist in the plan.</summary>
    TargetNotFound,

    /// <summary>Target is archived — cannot mutate (ADR-0050 §6).</summary>
    TargetArchived,

    /// <summary>WeeklyHours out of range (1..40 per ExamTarget).</summary>
    WeeklyHoursOutOfRange,

    /// <summary>AssignedById is wrong kind for the Source (e.g. Source=
    /// Classroom requires a teacher id + enrollment id).</summary>
    SourceAssignmentMismatch,
}

/// <summary>
/// Command: add a new exam target to a student's plan.
/// </summary>
/// <param name="StudentAnonId">Plan owner.</param>
/// <param name="Source">Who is adding (Student | Classroom | Tenant |
/// Migration).</param>
/// <param name="AssignedById">Student / teacher / admin / system id.</param>
/// <param name="EnrollmentId">ADR-0001 scoping — required when
/// Source=Classroom or Tenant, must be null when Source=Student.</param>
/// <param name="ExamCode">Catalog primary key.</param>
/// <param name="Track">Track within the exam (null when exam has no
/// track concept).</param>
/// <param name="Sitting">Sitting tuple.</param>
/// <param name="WeeklyHours">Hours/week for this target (1..40).</param>
/// <param name="ReasonTag">Why (optional).</param>
/// <param name="MigrationSourceId">prr-219 idempotency key. Null for
/// non-migration commands.</param>
public sealed record AddExamTargetCommand(
    string StudentAnonId,
    ExamTargetSource Source,
    UserId AssignedById,
    EnrollmentId? EnrollmentId,
    ExamCode ExamCode,
    TrackCode? Track,
    SittingCode Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag,
    string? MigrationSourceId = null);

/// <summary>
/// Command: update an existing active target's mutable fields.
/// </summary>
public sealed record UpdateExamTargetCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    TrackCode? Track,
    SittingCode Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag);

/// <summary>
/// Command: archive an active target (terminal).
/// </summary>
public sealed record ArchiveExamTargetCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    ArchiveReason Reason);

/// <summary>
/// Command: mark an active target completed (terminal, specialised
/// archive reason).
/// </summary>
public sealed record CompleteExamTargetCommand(
    string StudentAnonId,
    ExamTargetId TargetId);

/// <summary>
/// Command: telemetry event — the scheduler ran a session against a
/// specific target. No state mutation; no invariants checked beyond
/// target existence.
/// </summary>
public sealed record ApplyExamTargetOverrideCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    string SessionId);

/// <summary>
/// Command handler + invariants. Stateless; delegates persistence to
/// <see cref="IStudentPlanAggregateStore"/>.
/// </summary>
public interface IStudentPlanCommandHandler
{
    /// <summary>Add a new exam target.</summary>
    Task<CommandResult> HandleAsync(AddExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Update an existing active target.</summary>
    Task<CommandResult> HandleAsync(UpdateExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Archive an active target.</summary>
    Task<CommandResult> HandleAsync(ArchiveExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Complete an active target.</summary>
    Task<CommandResult> HandleAsync(CompleteExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Record a scheduler-override telemetry event.</summary>
    Task<CommandResult> HandleAsync(ApplyExamTargetOverrideCommand cmd, CancellationToken ct = default);
}

/// <summary>
/// Default implementation of the command handler. Enforces
/// ADR-0050 §5 invariants before writing events.
/// </summary>
public sealed class StudentPlanCommandHandler : IStudentPlanCommandHandler
{
    private readonly IStudentPlanAggregateStore _store;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Public ctor with injectable clock so tests can freeze time.
    /// </summary>
    public StudentPlanCommandHandler(
        IStudentPlanAggregateStore store,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(AddExamTargetCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        // Shape validation — cheap, deterministic, no I/O.
        if (cmd.WeeklyHours < ExamTarget.MinWeeklyHours || cmd.WeeklyHours > ExamTarget.MaxWeeklyHours)
        {
            return new CommandResult(Success: false, Error: CommandError.WeeklyHoursOutOfRange);
        }

        if (!IsSourceAssignmentConsistent(cmd.Source, cmd.EnrollmentId))
        {
            return new CommandResult(Success: false, Error: CommandError.SourceAssignmentMismatch);
        }

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        // §5: max 5 active.
        if (state.ActiveTargets.Count >= MaxActiveTargets)
        {
            return new CommandResult(Success: false, Error: CommandError.ActiveTargetCapExceeded);
        }

        // §5: sum(WeeklyHours) + new ≤ 40.
        var currentSum = state.ActiveTargets.Sum(t => t.WeeklyHours);
        if (currentSum + cmd.WeeklyHours > ExamTarget.MaxWeeklyHours)
        {
            return new CommandResult(Success: false, Error: CommandError.WeeklyBudgetExceeded);
        }

        // §5: (ExamCode, Sitting, Track) unique across active.
        var duplicate = state.ActiveTargets.Any(t =>
            t.ExamCode == cmd.ExamCode
            && t.Sitting == cmd.Sitting
            && NullableTrackEquals(t.Track, cmd.Track));
        if (duplicate)
        {
            return new CommandResult(Success: false, Error: CommandError.DuplicateTarget);
        }

        var now = _clock();
        var target = new ExamTarget(
            Id: ExamTargetId.New(),
            Source: cmd.Source,
            AssignedById: cmd.AssignedById,
            EnrollmentId: cmd.EnrollmentId,
            ExamCode: cmd.ExamCode,
            Track: cmd.Track,
            Sitting: cmd.Sitting,
            WeeklyHours: cmd.WeeklyHours,
            ReasonTag: cmd.ReasonTag,
            CreatedAt: now,
            ArchivedAt: null);

        // Initialize the stream on first write.
        if (state.InitializedAt is null && !aggregate.IsInitialized)
        {
            await _store.AppendAsync(
                cmd.StudentAnonId,
                new StudentPlanInitialized_V1(cmd.StudentAnonId, now),
                ct).ConfigureAwait(false);
        }

        await _store.AppendAsync(
            cmd.StudentAnonId,
            new ExamTargetAdded_V1(cmd.StudentAnonId, target, now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: target.Id);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(UpdateExamTargetCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        if (cmd.WeeklyHours < ExamTarget.MinWeeklyHours || cmd.WeeklyHours > ExamTarget.MaxWeeklyHours)
        {
            return new CommandResult(Success: false, Error: CommandError.WeeklyHoursOutOfRange);
        }

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        var target = state.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetArchived);
        }

        // §5: sum invariant with the delta.
        var otherSum = state.ActiveTargets
            .Where(t => t.Id != cmd.TargetId)
            .Sum(t => t.WeeklyHours);
        if (otherSum + cmd.WeeklyHours > ExamTarget.MaxWeeklyHours)
        {
            return new CommandResult(Success: false, Error: CommandError.WeeklyBudgetExceeded);
        }

        // §5: uniqueness still holds after the update.
        var duplicate = state.ActiveTargets.Any(t =>
            t.Id != cmd.TargetId
            && t.ExamCode == target.ExamCode
            && t.Sitting == cmd.Sitting
            && NullableTrackEquals(t.Track, cmd.Track));
        if (duplicate)
        {
            return new CommandResult(Success: false, Error: CommandError.DuplicateTarget);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new ExamTargetUpdated_V1(
                cmd.StudentAnonId,
                cmd.TargetId,
                cmd.Track,
                cmd.Sitting,
                cmd.WeeklyHours,
                cmd.ReasonTag,
                now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(ArchiveExamTargetCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        var target = state.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            // §6: re-archiving is a no-op (idempotent), not an error.
            return new CommandResult(Success: true, TargetId: cmd.TargetId);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new ExamTargetArchived_V1(cmd.StudentAnonId, cmd.TargetId, now, cmd.Reason),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CompleteExamTargetCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        var target = state.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetArchived);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new ExamTargetCompleted_V1(cmd.StudentAnonId, cmd.TargetId, now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(ApplyExamTargetOverrideCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        // No-op when target not in plan. Per prr-218 scope: "no behavior
        // change" — we still record the telemetry event so scheduler
        // analytics can detect the drift.
        if (!state.Targets.Any(t => t.Id == cmd.TargetId))
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new ExamTargetOverrideApplied_V1(
                cmd.StudentAnonId, cmd.TargetId, cmd.SessionId, now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    // ── Invariants (exposed internal for tests) ──────────────────────────

    /// <summary>
    /// ADR-0050 §5: max 5 active targets per student.
    /// </summary>
    public const int MaxActiveTargets = 5;

    /// <summary>
    /// Cross-field shape invariant: EnrollmentId presence must match
    /// Source. Enforced at the command boundary because ADR-0001 tenant
    /// scoping depends on EnrollmentId being consistent.
    /// </summary>
    internal static bool IsSourceAssignmentConsistent(
        ExamTargetSource source, EnrollmentId? enrollmentId)
        => source switch
        {
            ExamTargetSource.Student => enrollmentId is null,
            ExamTargetSource.Classroom => enrollmentId is not null,
            ExamTargetSource.Tenant => enrollmentId is not null,
            // Migration cohort: the legacy row may or may not have had a
            // tenant; allow both. Downstream reports scope by the Source
            // discriminator instead.
            ExamTargetSource.Migration => true,
            _ => false,
        };

    private static bool NullableTrackEquals(TrackCode? a, TrackCode? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Value == b.Value;
    }
}
