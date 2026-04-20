// =============================================================================
// Cena Platform — IStudentPlanConfigService (prr-149 scheduler-facing bridge)
//
// Reads the student's plan-level configuration that feeds AdaptiveScheduler:
// deadline, weekly time budget, motivation profile. This type is the
// SCHEDULER-FACING view and deliberately bundles the three inputs with
// the default-fallback policy applied.
//
// Source-of-truth layering (per prr-148 ↔ prr-149 seam):
//   - prr-148's Cena.Actors.StudentPlan.IStudentPlanInputsService owns the
//     raw write-side state (deadline + weekly budget, null when unset).
//   - prr-149's IStudentPlanConfigService (this file) is the bridge: it
//     reads the raw values via prr-148, applies the canonical fallback
//     for any null field, and supplies the MotivationProfile source
//     (currently Neutral default; RDY-057 onboarding feeds in here).
//
// A missing prr-148 service is ALSO tolerated — if the DI container does
// not register IStudentPlanInputsService (tests that want the pure
// default path), InMemoryStudentPlanConfigService returns the canonical
// defaults. The result is observably labelled "default-fallback" in
// SessionPlanGenerationResult so ops can track adoption of prr-148.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Sessions;

/// <summary>
/// Scheduler-facing view of a student's plan configuration. Immutable
/// value object so the scheduler and the /plan endpoint see exactly
/// the same snapshot even if the config is updated mid-session.
/// </summary>
/// <param name="DeadlineUtc">Target exam deadline. Null when the student
/// has not set one — scheduler uses a sensible horizon default.</param>
/// <param name="WeeklyBudget">Weekly study time budget. Defaults to 5h
/// when the student has not set one.</param>
/// <param name="MotivationProfile">Student's self-reported stance from
/// RDY-057 onboarding. Drives rationale copy; never a diagnostic.</param>
/// <remarks>
/// Intentionally distinct from <see cref="Cena.Actors.StudentPlan.StudentPlanConfig"/>
/// (prr-148). The StudentPlan variant is the raw write-side projection
/// with nullable fields; this record is the scheduler-facing bundle
/// with defaults applied + MotivationProfile included.
/// </remarks>
public sealed record StudentPlanConfig(
    DateTimeOffset? DeadlineUtc,
    TimeSpan WeeklyBudget,
    MotivationProfile MotivationProfile);

/// <summary>
/// Scheduler-facing read of the student's plan config. Implementations
/// MUST return a non-null config — the "no config yet" case maps to
/// <see cref="StudentPlanConfigDefaults.Build"/>, not null, so callers
/// can unconditionally use the result.
/// </summary>
public interface IStudentPlanConfigService
{
    /// <summary>Resolve the plan config for the student.</summary>
    Task<StudentPlanConfig> GetAsync(
        string studentAnonId, CancellationToken ct = default);
}

/// <summary>
/// Centralised defaults so every path that needs the scheduler inputs
/// agrees on what "no config supplied" means. Changing these requires
/// Dr. Nadia + Dr. Yael sign-off per the RDY-073 scheduler envelope —
/// do not tune them locally.
/// </summary>
public static class StudentPlanConfigDefaults
{
    /// <summary>Default horizon when the student has not supplied one: 12 weeks.</summary>
    public static readonly TimeSpan FallbackHorizon = TimeSpan.FromDays(7 * 12);

    /// <summary>Default weekly budget: 5 hours.</summary>
    public static readonly TimeSpan FallbackWeeklyBudget = TimeSpan.FromHours(5);

    /// <summary>
    /// Canonical fallback. Deadline is computed relative to the supplied
    /// "now" so the plan shows a sensible relative horizon rather than
    /// a hardcoded future date.
    /// </summary>
    public static StudentPlanConfig Build(DateTimeOffset nowUtc) => new(
        DeadlineUtc: nowUtc + FallbackHorizon,
        WeeklyBudget: FallbackWeeklyBudget,
        MotivationProfile: MotivationProfile.Neutral);
}

/// <summary>
/// Bridge that reads prr-148's <see cref="IStudentPlanInputsService"/>
/// and folds in the session-scheduler defaults. This is the
/// production wiring: register it in Program.cs alongside
/// <c>AddStudentPlanServices()</c> (prr-148's registration) and the
/// scheduler will pick up real student-supplied deadlines/budgets.
/// </summary>
public sealed class StudentPlanConfigBridgeService : IStudentPlanConfigService
{
    private readonly IStudentPlanInputsService _inputs;
    private readonly Func<DateTimeOffset> _clock;

    public StudentPlanConfigBridgeService(IStudentPlanInputsService inputs)
        : this(inputs, () => DateTimeOffset.UtcNow) { }

    public StudentPlanConfigBridgeService(
        IStudentPlanInputsService inputs,
        Func<DateTimeOffset> clock)
    {
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<StudentPlanConfig> GetAsync(
        string studentAnonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            throw new ArgumentException(
                "Student anon id must be non-empty.", nameof(studentAnonId));

        var raw = await _inputs.GetAsync(studentAnonId, ct).ConfigureAwait(false);
        var now = _clock();
        var defaults = StudentPlanConfigDefaults.Build(now);

        return new StudentPlanConfig(
            DeadlineUtc: raw.DeadlineUtc ?? defaults.DeadlineUtc,
            WeeklyBudget: raw.WeeklyBudget ?? defaults.WeeklyBudget,
            // MotivationProfile will be wired to the RDY-057 onboarding
            // self-assessment reader in a follow-up; for now default Neutral.
            MotivationProfile: defaults.MotivationProfile);
    }
}

/// <summary>
/// In-memory fallback used when prr-148's service is not registered
/// (tests, pre-prr-148 deployments). Every call returns the canonical
/// default — no mutation, no persistence. Safe to register as a
/// singleton. Intentionally dumb: tests can swap in a different clock
/// for deterministic deadlines.
/// </summary>
public sealed class InMemoryStudentPlanConfigService : IStudentPlanConfigService
{
    private readonly Func<DateTimeOffset> _clock;

    public InMemoryStudentPlanConfigService()
        : this(() => DateTimeOffset.UtcNow) { }

    public InMemoryStudentPlanConfigService(Func<DateTimeOffset> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public Task<StudentPlanConfig> GetAsync(
        string studentAnonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            throw new ArgumentException(
                "Student anon id must be non-empty.", nameof(studentAnonId));

        return Task.FromResult(StudentPlanConfigDefaults.Build(_clock()));
    }
}
