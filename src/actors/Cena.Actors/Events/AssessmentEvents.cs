// =============================================================================
// Cena Platform — Assessment Events (SEC-ASSESS-001)
// Events for assessment security and variant tracking.
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// SEC-ASSESS-001: Emitted when a variant seed is assigned to a student for
/// a specific question. Records the deterministic seed for audit/investigation.
/// The seed is NOT stored on the question document — it's per-student, per-day.
/// </summary>
public record VariantSeedAssigned_V1(
    string StudentId,
    string SessionId,
    string QuestionId,
    int VariantSeed,
    int VariantIndex,
    int TotalVariants,
    DateOnly Date,
    DateTimeOffset AssignedAt
) : IDelegatedEvent;
