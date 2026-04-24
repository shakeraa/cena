// =============================================================================
// Cena Platform -- Delivery Gate Interface (SAI-005.1)
//
// Pure domain service that decides whether a hint or explanation should be
// delivered, deferred, or suppressed based on cognitive state.
//
// D'Mello & Graesser (2012): confusion that resolves produces deep learning.
// Auto-delivered content during productive confusion DISRUPTS resolution.
// Student-initiated requests always pass through (they explicitly asked).
// =============================================================================

using Cena.Actors.Services;

namespace Cena.Actors.Hints;

/// <summary>
/// Evaluates whether a hint or explanation should be delivered to the student
/// based on their current confusion state, disengagement type, and focus level.
/// </summary>
public interface IDeliveryGate
{
    DeliveryDecision Evaluate(DeliveryContext context);
}

public sealed record DeliveryContext(
    ConfusionState ConfusionState,
    DisengagementType? DisengagementType,
    FocusLevel FocusLevel,
    bool IsStudentInitiated,
    int QuestionsUntilPatience);

public sealed record DeliveryDecision(
    DeliveryAction Action,
    string? Reason,
    string? StudentMessage);

public enum DeliveryAction
{
    /// <summary>Deliver the hint/explanation normally.</summary>
    Deliver,

    /// <summary>Defer delivery -- student is in productive confusion. Try again later.</summary>
    Defer,

    /// <summary>Suppress entirely -- content would be counterproductive (e.g., student is bored).</summary>
    Suppress
}
