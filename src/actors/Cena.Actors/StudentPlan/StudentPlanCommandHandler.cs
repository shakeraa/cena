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

    /// <summary>Bagrut-family target missing required question-paper
    /// codes (PRR-243 / ADR-0050 §1: Bagrut ⇒ ≥1 paper).</summary>
    QuestionPaperCodesRequired,

    /// <summary>Standardized-family target carrying question-paper codes
    /// (PRR-243 / ADR-0050 §1: SAT/PET ⇒ 0 papers).</summary>
    QuestionPaperCodesForbidden,

    /// <summary>A supplied paper code is not in the exam catalog for the
    /// given (examCode, track) pair (PRR-243).</summary>
    QuestionPaperCodeUnknown,

    /// <summary>Duplicate paper code within a single target (PRR-243
    /// de-dup invariant).</summary>
    QuestionPaperCodeDuplicate,

    /// <summary>Paper code already present on the target (PRR-243
    /// idempotency — QuestionPaperAdded).</summary>
    QuestionPaperCodeAlreadyPresent,

    /// <summary>Paper code not present on the target (PRR-243
    /// QuestionPaperRemoved / PerPaperSittingOverrideSet precondition).</summary>
    QuestionPaperCodeNotPresent,

    /// <summary>Removing this paper would leave a Bagrut target with
    /// zero papers (PRR-243 DoD) — archive the target instead.</summary>
    QuestionPaperRemovalLeavesEmpty,

    /// <summary>A per-paper sitting override keys a paper code not in
    /// the target's QuestionPaperCodes (PRR-243).</summary>
    PerPaperSittingOverrideKeyUnknown,

    /// <summary>A per-paper sitting override maps to the same sitting as
    /// the target's primary — violates the minimal-map invariant
    /// (PRR-243 / ADR-0050 §1).</summary>
    PerPaperSittingOverrideMatchesPrimary,

    /// <summary>PRR-230: cannot set a SafetyFlag-tagged target to Hidden —
    /// duty-of-care carve-out mirrors ADR-0041.</summary>
    ParentVisibilitySafetyFlagLocked,
}

/// <summary>
/// Default implementation of the command handler. Enforces
/// ADR-0050 §5 invariants before writing events. Command DTOs +
/// interface live in <see cref="IStudentPlanCommandHandler"/> (StudentPlanCommands.cs).
/// The PRR-243 שאלון handlers live in StudentPlanCommandHandler.QuestionPapers.cs.
/// </summary>
public sealed partial class StudentPlanCommandHandler : IStudentPlanCommandHandler
{
    private readonly IStudentPlanAggregateStore _store;
    private readonly IQuestionPaperCatalogValidator _paperValidator;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Public ctor with injectable clock + catalog validator so tests can
    /// freeze time and inject a permissive catalog, and production can
    /// inject the real catalog-backed validator.
    /// </summary>
    public StudentPlanCommandHandler(
        IStudentPlanAggregateStore store,
        Func<DateTimeOffset>? clock = null,
        IQuestionPaperCatalogValidator? paperValidator = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _paperValidator = paperValidator ?? AllowAllQuestionPaperCatalogValidator.Instance;
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

        // PRR-243: normalise + validate question-paper codes BEFORE store
        // I/O so malformed requests fail fast with a clean error code.
        var rawPapers = cmd.QuestionPaperCodes ?? Array.Empty<string>();
        var (papers, paperError) = NormaliseQuestionPaperCodes(
            rawPapers, cmd.ExamCode, cmd.Track);
        if (paperError is { } pe)
        {
            return new CommandResult(Success: false, Error: pe);
        }

        // PRR-243: normalise + validate the per-paper sitting override map.
        var (perPaperOverride, overrideError) = NormalisePerPaperSittingOverride(
            cmd.PerPaperSittingOverride, papers, cmd.Sitting);
        if (overrideError is { } oe)
        {
            return new CommandResult(Success: false, Error: oe);
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

        // PRR-230: resolve default parent-visibility from age band + reason.
        // When the endpoint does not supply a band (migration cohort, legacy
        // callers), fall back to Visible to preserve pre-PRR-230 semantics
        // — the student can later hide explicitly via the toggle endpoint.
        var defaultVisibility = cmd.StudentAgeBand is { } band
            ? ParentVisibilityDefaults.Resolve(band, cmd.ReasonTag)
            : ParentVisibility.Visible;

        var target = new ExamTarget(
            Id: ExamTargetId.New(),
            Source: cmd.Source,
            AssignedById: cmd.AssignedById,
            EnrollmentId: cmd.EnrollmentId,
            ExamCode: cmd.ExamCode,
            Track: cmd.Track,
            QuestionPaperCodes: papers,
            Sitting: cmd.Sitting,
            PerPaperSittingOverride: perPaperOverride,
            WeeklyHours: cmd.WeeklyHours,
            ReasonTag: cmd.ReasonTag,
            CreatedAt: now,
            ArchivedAt: null,
            ParentVisibility: defaultVisibility);

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
