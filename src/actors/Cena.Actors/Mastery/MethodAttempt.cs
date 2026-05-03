// =============================================================================
// Cena Platform -- MethodAttempt record
// MST-001: Tracks methodology usage per concept
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Immutable record tracking a methodology attempt for a concept.
/// </summary>
public sealed record MethodAttempt(string MethodologyId, int SessionCount, string Outcome);
