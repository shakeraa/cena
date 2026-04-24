// =============================================================================
// Cena Platform — TeacherOverrideCommands (prr-150)
//
// Command surface for the teacher-override aggregate. Every command:
//   1. Validates primitive arguments (non-empty ids, bounded values).
//   2. Calls VerifyTenantScope to enforce ADR-0001 — teacher's institute
//      must match the student's active enrollment institute. A mismatch
//      throws CrossTenantOverrideDeniedException, which the architecture
//      test TeacherOverrideNoCrossTenantTest proves is unskippable.
//   3. Appends the corresponding event to the override stream.
//
// Precedence summary (see docs/adr/0044-teacher-schedule-override.md):
//   override > student-set plan input > scheduler default.
//
// This class is intentionally a thin stateless service. All coordination
// with Marten / the SIEM log / AdminActionAuditMiddleware happens in the
// admin API endpoint layer; the aggregate only cares about correctness
// and tenant safety.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Teacher.ScheduleOverride.Events;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Strongly-typed commands for the TeacherOverride aggregate. Thin wrapper
/// around <see cref="ITeacherOverrideStore"/> that enforces the tenant
/// invariant on every write.
/// </summary>
public sealed class TeacherOverrideCommands
{
    /// <summary>Minimum weekly budget allowed on an override (1h).</summary>
    public static readonly TimeSpan MinWeeklyBudget = TimeSpan.FromHours(1);

    /// <summary>Maximum weekly budget allowed on an override (40h).</summary>
    public static readonly TimeSpan MaxWeeklyBudget = TimeSpan.FromHours(40);

    /// <summary>Minimum pinned-session count (1).</summary>
    public const int MinPinnedSessions = 1;

    /// <summary>Maximum pinned-session count (20).</summary>
    public const int MaxPinnedSessions = 20;

    /// <summary>Sentinel scope meaning "applies to every session type".</summary>
    public const string ScopeAll = "all";

    private readonly ITeacherOverrideStore _store;
    private readonly IStudentInstituteLookup _instituteLookup;
    private readonly ILogger<TeacherOverrideCommands>? _logger;

    public TeacherOverrideCommands(
        ITeacherOverrideStore store,
        IStudentInstituteLookup instituteLookup,
        ILogger<TeacherOverrideCommands>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _instituteLookup = instituteLookup ?? throw new ArgumentNullException(nameof(instituteLookup));
        _logger = logger;
    }

    // ---- Commands ----------------------------------------------------------

    /// <summary>Pin a topic for the student's next N sessions.</summary>
    public async Task PinTopicAsync(
        PinTopicCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidatePin(command);
        await VerifyTenantScope(
            command.StudentAnonId, command.TeacherActorId, command.TeacherInstituteId, ct)
            .ConfigureAwait(false);

        var evt = new PinTopicRequested_V1(
            StudentAnonId: command.StudentAnonId,
            TopicSlug: command.TopicSlug,
            PinnedSessionCount: command.PinnedSessionCount,
            TeacherActorId: command.TeacherActorId,
            InstituteId: command.TeacherInstituteId,
            Rationale: command.Rationale,
            SetAt: command.SetAt);
        await _store.AppendAsync(command.StudentAnonId, evt, ct).ConfigureAwait(false);
    }

    /// <summary>Override the student's weekly time budget.</summary>
    public async Task AdjustBudgetAsync(
        AdjustBudgetCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateBudget(command);
        await VerifyTenantScope(
            command.StudentAnonId, command.TeacherActorId, command.TeacherInstituteId, ct)
            .ConfigureAwait(false);

        var evt = new BudgetAdjusted_V1(
            StudentAnonId: command.StudentAnonId,
            NewWeeklyBudget: command.NewWeeklyBudget,
            TeacherActorId: command.TeacherActorId,
            InstituteId: command.TeacherInstituteId,
            Rationale: command.Rationale,
            SetAt: command.SetAt);
        await _store.AppendAsync(command.StudentAnonId, evt, ct).ConfigureAwait(false);
    }

    /// <summary>Override the motivation profile for a session-type scope.</summary>
    public async Task OverrideMotivationAsync(
        OverrideMotivationCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateMotivation(command);
        await VerifyTenantScope(
            command.StudentAnonId, command.TeacherActorId, command.TeacherInstituteId, ct)
            .ConfigureAwait(false);

        var evt = new MotivationProfileOverridden_V1(
            StudentAnonId: command.StudentAnonId,
            SessionTypeScope: command.SessionTypeScope,
            OverrideProfile: command.OverrideProfile,
            TeacherActorId: command.TeacherActorId,
            InstituteId: command.TeacherInstituteId,
            Rationale: command.Rationale,
            SetAt: command.SetAt);
        await _store.AppendAsync(command.StudentAnonId, evt, ct).ConfigureAwait(false);
    }

    // ---- Tenant invariant (ADR-0001) ---------------------------------------

    /// <summary>
    /// Enforces the ADR-0001 tenant invariant. Resolves the student's
    /// active enrollment institute and throws
    /// <see cref="CrossTenantOverrideDeniedException"/> if it does not
    /// match the teacher's claimed institute. Emits a structured SIEM log
    /// on failure so redteam forensics can correlate mismatches.
    /// </summary>
    internal async Task VerifyTenantScope(
        string studentAnonId,
        string teacherActorId,
        string teacherInstituteId,
        CancellationToken ct)
    {
        var studentInstituteId = await _instituteLookup
            .GetActiveInstituteAsync(studentAnonId, ct)
            .ConfigureAwait(false);

        // Missing active enrollment is also a denial — we do not leak
        // existence of students outside the caller's tenant.
        if (string.IsNullOrEmpty(studentInstituteId)
            || !string.Equals(studentInstituteId, teacherInstituteId, StringComparison.Ordinal))
        {
            _logger?.LogWarning(
                "[TEACHER_OVERRIDE_CROSS_TENANT_DENIED] student={Sid} teacher={Tid} " +
                "teacher_institute={TInst} student_institute={SInst}",
                studentAnonId, teacherActorId, teacherInstituteId,
                studentInstituteId ?? "<none>");

            throw new CrossTenantOverrideDeniedException(
                studentAnonId: studentAnonId,
                teacherActorId: teacherActorId,
                teacherInstituteId: teacherInstituteId,
                studentInstituteId: studentInstituteId ?? string.Empty);
        }
    }

    // ---- Primitive validation ----------------------------------------------

    private static void ValidatePin(PinTopicCommand c)
    {
        RequireNonEmpty(c.StudentAnonId, nameof(c.StudentAnonId));
        RequireNonEmpty(c.TopicSlug, nameof(c.TopicSlug));
        RequireNonEmpty(c.TeacherActorId, nameof(c.TeacherActorId));
        RequireNonEmpty(c.TeacherInstituteId, nameof(c.TeacherInstituteId));
        if (c.PinnedSessionCount < MinPinnedSessions || c.PinnedSessionCount > MaxPinnedSessions)
            throw new ArgumentOutOfRangeException(
                nameof(c.PinnedSessionCount),
                $"PinnedSessionCount must be in [{MinPinnedSessions}, {MaxPinnedSessions}]; got {c.PinnedSessionCount}.");
    }

    private static void ValidateBudget(AdjustBudgetCommand c)
    {
        RequireNonEmpty(c.StudentAnonId, nameof(c.StudentAnonId));
        RequireNonEmpty(c.TeacherActorId, nameof(c.TeacherActorId));
        RequireNonEmpty(c.TeacherInstituteId, nameof(c.TeacherInstituteId));
        if (c.NewWeeklyBudget < MinWeeklyBudget || c.NewWeeklyBudget > MaxWeeklyBudget)
            throw new ArgumentOutOfRangeException(
                nameof(c.NewWeeklyBudget),
                $"NewWeeklyBudget must be in [{MinWeeklyBudget}, {MaxWeeklyBudget}]; got {c.NewWeeklyBudget}.");
    }

    private static void ValidateMotivation(OverrideMotivationCommand c)
    {
        RequireNonEmpty(c.StudentAnonId, nameof(c.StudentAnonId));
        RequireNonEmpty(c.SessionTypeScope, nameof(c.SessionTypeScope));
        RequireNonEmpty(c.TeacherActorId, nameof(c.TeacherActorId));
        RequireNonEmpty(c.TeacherInstituteId, nameof(c.TeacherInstituteId));
        if (!Enum.IsDefined(typeof(MotivationProfile), c.OverrideProfile))
            throw new ArgumentOutOfRangeException(
                nameof(c.OverrideProfile),
                $"OverrideProfile value '{c.OverrideProfile}' is not a defined MotivationProfile.");
    }

    private static void RequireNonEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} must be non-empty.", name);
    }
}

// ---- Command DTOs ----------------------------------------------------------

/// <summary>
/// Pin a topic for a student's next N sessions.
/// </summary>
public sealed record PinTopicCommand(
    string StudentAnonId,
    string TopicSlug,
    int PinnedSessionCount,
    string TeacherActorId,
    string TeacherInstituteId,
    string Rationale,
    DateTimeOffset SetAt);

/// <summary>
/// Override the weekly time budget for a student.
/// </summary>
public sealed record AdjustBudgetCommand(
    string StudentAnonId,
    TimeSpan NewWeeklyBudget,
    string TeacherActorId,
    string TeacherInstituteId,
    string Rationale,
    DateTimeOffset SetAt);

/// <summary>
/// Override the motivation profile for a student (scoped to a session-type subset).
/// </summary>
public sealed record OverrideMotivationCommand(
    string StudentAnonId,
    string SessionTypeScope,
    MotivationProfile OverrideProfile,
    string TeacherActorId,
    string TeacherInstituteId,
    string Rationale,
    DateTimeOffset SetAt);
