// =============================================================================
// Cena Platform -- Knowledge State
// MST-013: A feasible knowledge state (downward-closed concept set)
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

/// <summary>
/// A knowledge state: an immutable set of mastered concept IDs.
/// A state is feasible (downward-closed) if for every concept in the set,
/// all its prerequisites are also in the set.
/// </summary>
public sealed record KnowledgeState(ImmutableHashSet<string> MasteredConcepts)
{
    public bool Contains(string conceptId) => MasteredConcepts.Contains(conceptId);
    public int Count => MasteredConcepts.Count;
}
