// =============================================================================
// Cena Platform — StudentPlanMartenRegistration (prr-218 production binding)
//
// Bounded-context owns its own Marten event registration. Called by
// AddStudentPlanMarten() via services.ConfigureMarten so the registration
// lives inside this namespace rather than in the cross-cutting
// MartenConfiguration file (500-LOC ratchet per ADR-0012).
//
// Pattern mirrors SubscriptionMartenRegistration — each event type is
// registered explicitly so Marten's event-type resolver can deserialize
// payload JSON back to the concrete record without a fallback to
// assembly-scan heuristics (slower + noisier on startup).
// =============================================================================

using Cena.Actors.StudentPlan.Events;
using Marten;

namespace Cena.Actors.StudentPlan;

/// <summary>Marten event registration for the StudentPlan bounded context.</summary>
public static class StudentPlanMartenRegistration
{
    /// <summary>
    /// Register every StudentPlan event type (legacy + multi-target) on the
    /// Marten <see cref="StoreOptions"/>. Projections are intentionally
    /// absent: the aggregate is rebuilt by event replay in
    /// <see cref="StudentPlanAggregate.ReplayFrom"/>, not by a Marten
    /// projection, because the multi-target read model (prr-218
    /// <c>IStudentPlanReader</c>) composes from the aggregate state and
    /// does not justify a separate projection table today. When the
    /// read-volume profile changes, an inline projection lands here.
    /// </summary>
    public static void RegisterStudentPlanContext(this StoreOptions opts)
    {
        // ── Legacy (prr-148) events — preserved for stream replay ──
        opts.Events.AddEventType<ExamDateSet_V1>();
        opts.Events.AddEventType<WeeklyTimeBudgetSet_V1>();

        // ── Multi-target (prr-218, ADR-0050) core lifecycle ──
        opts.Events.AddEventType<StudentPlanInitialized_V1>();
        opts.Events.AddEventType<ExamTargetAdded_V1>();
        opts.Events.AddEventType<ExamTargetUpdated_V1>();
        opts.Events.AddEventType<ExamTargetArchived_V1>();
        opts.Events.AddEventType<ExamTargetCompleted_V1>();
        opts.Events.AddEventType<ExamTargetOverrideApplied_V1>();

        // ── PRR-243 question-paper (שאלון) management ──
        opts.Events.AddEventType<QuestionPaperAdded_V1>();
        opts.Events.AddEventType<QuestionPaperRemoved_V1>();
        opts.Events.AddEventType<PerPaperSittingOverrideSet_V1>();
        opts.Events.AddEventType<PerPaperSittingOverrideCleared_V1>();

        // ── PRR-230 parent visibility ──
        opts.Events.AddEventType<ParentVisibilityChanged_V1>();

        // ── PRR-219 migration safety net ──
        opts.Events.AddEventType<StudentPlanMigrated_V1>();
        opts.Events.AddEventType<StudentPlanMigrationFailed_V1>();
    }
}
