// =============================================================================
// Cena Platform -- Delivery Gate Implementation (SAI-005.1)
//
// Decision matrix:
//   ConfusionResolving + NOT student-initiated -> Defer (productive confusion)
//   ConfusionResolving + student-initiated     -> Deliver (explicit request)
//   ConfusionStuck                             -> Deliver (needs scaffolding)
//   Bored_TooEasy + NOT student-initiated      -> Suppress (hints won't help)
//   Bored_TooEasy + student-initiated          -> Deliver (explicit request)
//   Everything else                            -> Deliver
//
// KEY: student-initiated hints ALWAYS pass through.
// =============================================================================

using Cena.Actors.Services;

namespace Cena.Actors.Hints;

public sealed class DeliveryGate : IDeliveryGate
{
    public DeliveryDecision Evaluate(DeliveryContext context)
    {
        // Student-initiated requests always pass through
        if (context.IsStudentInitiated)
            return new DeliveryDecision(DeliveryAction.Deliver, null, null);

        // ConfusionResolving: student is working through confusion -- defer auto-hints
        if (context.ConfusionState == ConfusionState.ConfusionResolving)
        {
            return new DeliveryDecision(
                DeliveryAction.Defer,
                "confusion_resolving",
                null);
        }

        // Bored_TooEasy: hints are counterproductive -- suppress auto-hints
        if (context.DisengagementType == Services.DisengagementType.Bored_TooEasy)
        {
            return new DeliveryDecision(
                DeliveryAction.Suppress,
                "bored_too_easy",
                null);
        }

        // ConfusionStuck: always deliver (needs scaffolding intervention)
        // NotConfused, Confused, all other disengagement types: deliver normally
        return new DeliveryDecision(DeliveryAction.Deliver, null, null);
    }
}
