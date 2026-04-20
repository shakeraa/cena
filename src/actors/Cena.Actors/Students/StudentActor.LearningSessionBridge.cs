// =============================================================================
// Cena Platform -- StudentActor (Partial) -- LearningSession shadow-write bridge
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Holds the ILearningSessionShadowWriter dependency in a separate partial file
// so the main StudentActor.cs file does not grow past its grandfathered LOC
// baseline. The shadow-write concern is a short-lived cross-context bridge
// that migrates to LearningSessionActor in Sprint 2+ (see ADR-0012 "Phase 2:
// Cutover"); at that point this partial disappears along with the rest of
// the StudentActor god-aggregate.
//
// The dependency is attached post-construction via an internal setter so the
// main StudentActor.cs ctor signature is unchanged (keeps grandfathered LOC
// and legacy direct-construction tests green). Production DI wires it in
// Program.cs through StudentActor.Props or an IActorProvider factory.
//
// No event-handler `case X_V<N>:` lines in this file by design — the
// NoNewStudentActorStateTest counts across all StudentActor*.cs files and
// must stay at the 2026-04-20 baseline of 18.
// =============================================================================

namespace Cena.Actors.Students;

public sealed partial class StudentActor
{
    // EPIC-PRR-A Sprint 1 (ADR-0012): shadow-writes SessionStarted_V2 alongside the legacy V1.
    // Defaults to a no-op null-writer; production DI overrides via UseLearningSessionShadowWriter.
    private Sessions.Shadow.ILearningSessionShadowWriter _learningSessionShadowWriter
        = Sessions.Shadow.NullLearningSessionShadowWriter.Instance;

    /// <summary>
    /// Post-construction injection point for the shadow writer. Called by
    /// the actor-registration DI wiring in Program.cs when Proto.Actor spawns
    /// a new StudentActor instance. Returns <c>this</c> to allow fluent
    /// chaining in a factory closure.
    /// </summary>
    public StudentActor UseLearningSessionShadowWriter(Sessions.Shadow.ILearningSessionShadowWriter writer)
    {
        _learningSessionShadowWriter = writer ?? throw new ArgumentNullException(nameof(writer));
        return this;
    }
}
