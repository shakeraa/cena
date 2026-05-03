// =============================================================================
// Cena Platform — TeacherOverrideAggregate (prr-150)
//
// Aggregate root for teacher / tutor / mentor schedule overrides. Stream
// key `teacheroverride-{studentAnonId}`; each stream is one student's
// override history, keyed by student because the scheduler always asks
// "what did the mentor say about THIS student?" at session start.
//
// *** CRITICAL ARCHITECTURE CONSTRAINT (ADR-0012) ***
// This aggregate exists as a NEW bounded context so it does not add
// event-handler cases to StudentActor*.cs (NoNewStudentActorStateTest
// would block that). It is the sibling pattern to prr-148's
// StudentPlanAggregate: small, single-responsibility, per-student stream.
//
// *** TENANT INVARIANT (ADR-0001) ***
// Every event on the stream carries an InstituteId. The aggregate fold
// does not re-verify cross-tenant correctness — that happens in the
// command handler (TeacherOverrideCommands) before the event is appended.
// This separation keeps the fold pure and fast; the gate test
// TeacherOverrideNoCrossTenantTest enforces that every command path
// invokes the tenant-scope guard.
//
// Unknown events are silently ignored (forward-migration tolerance, same
// convention as prr-148).
// =============================================================================

using Cena.Actors.Teacher.ScheduleOverride.Events;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Aggregate root for a single teacher-override stream. Stream key:
/// <c>teacheroverride-{studentAnonId}</c>.
/// </summary>
public sealed class TeacherOverrideAggregate
{
    /// <summary>Conventional stream-key prefix for this aggregate.</summary>
    public const string StreamKeyPrefix = "teacheroverride-";

    /// <summary>
    /// Build the stream key for a student id. Callers must have already
    /// validated that <paramref name="studentAnonId"/> is non-empty.
    /// </summary>
    public static string StreamKey(string studentAnonId)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "Student anon id must be non-empty for stream-key construction.",
                nameof(studentAnonId));
        }
        return StreamKeyPrefix + studentAnonId;
    }

    /// <summary>Backing state carried by this aggregate instance.</summary>
    public TeacherOverrideState State { get; } = new();

    /// <summary>
    /// Apply an inbound domain event. Unknown events are silently ignored.
    /// </summary>
    public void Apply(object @event)
    {
        switch (@event)
        {
            case PinTopicRequested_V1 pin:
                State.Apply(pin);
                break;
            case BudgetAdjusted_V1 budget:
                State.Apply(budget);
                break;
            case MotivationProfileOverridden_V1 motivation:
                State.Apply(motivation);
                break;
        }
    }

    /// <summary>
    /// Replay a sequence of events into a fresh aggregate.
    /// </summary>
    public static TeacherOverrideAggregate ReplayFrom(IEnumerable<object> events)
    {
        var aggregate = new TeacherOverrideAggregate();
        foreach (var evt in events)
        {
            aggregate.Apply(evt);
        }
        return aggregate;
    }
}
