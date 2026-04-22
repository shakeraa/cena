// =============================================================================
// Cena Platform — TeacherOverrideMartenRegistration (prr-150 prod binding)
//
// Bounded-context Marten event-type registration for
// TeacherOverrideAggregate. Mirrors StudentPlanMartenRegistration
// (prr-218), ConsentMartenRegistration (prr-155), and
// MasteryMartenRegistration (prr-222). Keeps cross-cutting
// MartenConfiguration under its 500-LOC ratchet (ADR-0012).
// =============================================================================

using Cena.Actors.Teacher.ScheduleOverride.Events;
using Marten;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>Marten event registration for the TeacherOverride bounded context.</summary>
public static class TeacherOverrideMartenRegistration
{
    /// <summary>
    /// Register every teacher-override event type on the Marten
    /// <see cref="StoreOptions"/>. No inline projections — the aggregate
    /// is rebuilt via <see cref="TeacherOverrideAggregate.ReplayFrom"/>.
    /// </summary>
    public static void RegisterTeacherOverrideContext(this StoreOptions opts)
    {
        opts.Events.AddEventType<BudgetAdjusted_V1>();
        opts.Events.AddEventType<MotivationProfileOverridden_V1>();
        opts.Events.AddEventType<PinTopicRequested_V1>();
    }
}
