// =============================================================================
// Cena Platform -- Item Candidate
// MST-010: Assessment item with Elo difficulty and expected correctness
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// An assessment item candidate for selection.
/// </summary>
public sealed record ItemCandidate(
    string ItemId,
    string ConceptId,
    int BloomLevel,
    float DifficultyElo,
    float ExpectedCorrectness);
