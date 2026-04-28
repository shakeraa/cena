// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) Marten registration
//
// Activates the orphan exam-simulation scaffolding (ExamFormat,
// ExamSimulationState, ExamSimulationStarted_V1 / Submitted_V2) by registering
// the document + events with Marten. Bounded-context owns its own
// registration so cross-cutting MartenConfiguration.cs stays under the
// 500-LOC ratchet (ADR-0012).
//
// Storage shape:
//   * ExamSimulationState — keyed on SimulationId (the runId). Single row per
//     run; mutated as the student progresses (PartB selection, answers,
//     visibility events, submission). Append-only events on the student
//     stream provide the durable audit trail; the doc is the read model.
//   * Events appended to the student stream so a future Marten replay can
//     reconstruct any historical run.
//
// ADR alignment:
//   * ADR-0043 — every item the runner serves goes through
//     ExamSimulationDelivery.AssertDeliverable so raw Ministry text never
//     leaks. Enforced at the endpoint layer, not here.
//   * ADR-0048 / GD-004 — runner UI must not carry streak / loss-aversion
//     copy. Enforced by the ship-gate scanner + spec assertions, not here.
//   * ADR-0002 — grading uses ICasRouterService. The grader is the only
//     correctness oracle; LLM never decides correctness.
// =============================================================================

using Cena.Actors.Events;
using Marten;

namespace Cena.Actors.Assessment;

public static class MockExamRunMartenRegistration
{
    public static void RegisterMockExamRunContext(this StoreOptions opts)
    {
        // Document: ExamSimulationState keyed on SimulationId.
        // Identity is set explicitly because the class predates Marten convention
        // (it has SimulationId, not Id).
        opts.Schema.For<ExamSimulationState>().Identity(s => s.SimulationId);

        // Events on the student stream so audit replay reconstructs runs.
        opts.Events.AddEventType<ExamSimulationStarted_V1>();
        opts.Events.AddEventType<ExamSimulationSubmitted_V2>();
        opts.Events.AddEventType<ExamSimulationItemDelivered_V1>();
        opts.Events.AddEventType<ExamVisibilityWarning_V1>();
    }
}
