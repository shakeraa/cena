// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Mastery Decay Propagation Through Prerequisite Chains
//
// When a foundational concept's mastery decays (HLR timer fires),
// downstream dependent concepts should also degrade proportionally.
// A student who forgets "limits" shouldn't still show 95% mastery
// on "derivatives" — the derivative knowledge is built ON the limit
// knowledge.
//
// References:
// - mastery-measurement-research.md Section 3.6: Decay Propagation
// - Corbett & Anderson (1994): BKT doesn't model prerequisite dependencies
// - Doignon & Falmagne (1999): KST prerequisite structure
//
// This service fills the gap: BKT tracks per-concept mastery independently,
// but concepts are NOT independent — they form a DAG. Decay propagation
// ensures the DAG's mastery state is consistent.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Propagates mastery decay through the prerequisite graph.
/// When concept A decays, all concepts that DEPEND on A should also degrade.
/// </summary>
public interface IDecayPropagationService
{
    /// <summary>
    /// Given that a concept's mastery has decayed, compute the cascading
    /// impact on all downstream dependent concepts.
    /// </summary>
    DecayPropagationResult PropagateDecay(DecayPropagationInput input);
}

public sealed class DecayPropagationService : IDecayPropagationService
{
    private readonly ILogger<DecayPropagationService> _logger;

    // ACT-031: instance-based via IMeterFactory
    private readonly Counter<long> _propagationCounter;

    // ── Propagation parameters ──
    // How much of the decay transfers to each downstream concept
    // dampingFactor < 1.0 means decay weakens as it propagates further
    private const double DampingFactor = 0.6;

    // Minimum mastery drop to propagate (ignore tiny changes)
    private const double MinimumDropToPropagate = 0.02;

    // Maximum depth to propagate (prevent infinite chains in deep graphs)
    private const int MaxPropagationDepth = 5;

    public DecayPropagationService(ILogger<DecayPropagationService> logger, IMeterFactory meterFactory)
    {
        _logger = logger;
        var meter = meterFactory.Create("Cena.Actors.Decay", "1.0.0");
        _propagationCounter = meter.CreateCounter<long>("cena.decay.propagations_total");
    }

    /// <summary>
    /// Propagates mastery decay through the prerequisite DAG.
    ///
    /// Algorithm:
    /// 1. Start with the decayed concept
    /// 2. Find all direct dependents (concepts that list this as a prerequisite)
    /// 3. For each dependent: compute effective mastery degradation
    ///    effective_mastery(c) = measured_mastery(c) * product(
    ///        max(measured_mastery(p) / threshold, 1.0) for p in prerequisites(c))
    ///    When a prerequisite drops below threshold, the dependent's effective
    ///    mastery drops proportionally.
    /// 4. Recurse to depth MaxPropagationDepth with damping
    /// 5. Return all affected concepts with their new effective mastery
    ///
    /// From mastery-measurement-research.md Section 3.6:
    /// "When scheduling reviews, foundational concepts with many dependents
    ///  should be prioritized even if their measured mastery is only slightly
    ///  below threshold. The cost of letting a foundational concept decay is
    ///  high because it effectively degrades the entire subtree."
    /// </summary>
    public DecayPropagationResult PropagateDecay(DecayPropagationInput input)
    {
        _propagationCounter.Add(1);

        var affected = new Dictionary<string, AffectedConcept>();
        var visited = new HashSet<string>();

        // Start BFS/DFS from the decayed concept
        PropagateRecursive(
            conceptId: input.DecayedConceptId,
            decayAmount: input.MasteryDrop,
            depth: 0,
            input: input,
            affected: affected,
            visited: visited
        );

        if (affected.Count > 0)
        {
            _logger.LogInformation(
                "Decay propagation from {ConceptId}: {Drop:F3} drop → {AffectedCount} downstream concepts affected",
                input.DecayedConceptId, input.MasteryDrop, affected.Count);
        }

        return new DecayPropagationResult(
            SourceConceptId: input.DecayedConceptId,
            SourceMasteryDrop: input.MasteryDrop,
            AffectedConcepts: affected.Values.ToList(),
            TotalConceptsAffected: affected.Count,
            MaxDepthReached: affected.Count > 0
                ? affected.Values.Max(a => a.PropagationDepth)
                : 0
        );
    }

    private void PropagateRecursive(
        string conceptId,
        double decayAmount,
        int depth,
        DecayPropagationInput input,
        Dictionary<string, AffectedConcept> affected,
        HashSet<string> visited)
    {
        if (depth >= MaxPropagationDepth) return;
        if (decayAmount < MinimumDropToPropagate) return;
        if (!visited.Add(conceptId)) return; // Cycle protection (DAG should have none, but defensive)

        // Find all concepts that depend on this one
        if (!input.DependentsMap.TryGetValue(conceptId, out var dependents))
            return;

        foreach (var dependent in dependents)
        {
            if (visited.Contains(dependent.ConceptId)) continue;

            // Current mastery of the dependent concept
            double currentMastery = input.MasteryMap.GetValueOrDefault(dependent.ConceptId, 0.5);

            // Compute the effective mastery reduction
            // Formula: dependent loses mastery proportional to:
            //   (prerequisite's drop) × (edge strength) × (damping ^ depth)
            double propagatedDrop = decayAmount
                * dependent.EdgeStrength
                * Math.Pow(DampingFactor, depth);

            // Scale by how much the dependent's mastery actually relies on this prerequisite
            // If the dependent is already at low mastery, the drop is proportionally smaller
            double effectiveDrop = propagatedDrop * Math.Min(currentMastery, 1.0);

            if (effectiveDrop < MinimumDropToPropagate) continue;

            double newEffectiveMastery = Math.Max(0.0, currentMastery - effectiveDrop);

            // Check if this drops the concept below the prerequisite gate threshold
            // or the progression threshold
            bool droppedBelowGate = currentMastery >= MasteryConstants.PrerequisiteGateThreshold && newEffectiveMastery < MasteryConstants.PrerequisiteGateThreshold;
            bool droppedBelowProgression = currentMastery >= MasteryConstants.ProgressionThreshold && newEffectiveMastery < MasteryConstants.ProgressionThreshold;

            affected[dependent.ConceptId] = new AffectedConcept(
                ConceptId: dependent.ConceptId,
                PreviousEffectiveMastery: currentMastery,
                NewEffectiveMastery: newEffectiveMastery,
                MasteryDrop: effectiveDrop,
                PropagationDepth: depth + 1,
                DroppedBelowPrerequisiteGate: droppedBelowGate,
                DroppedBelowProgressionThreshold: droppedBelowProgression,
                NeedsReview: newEffectiveMastery < MasteryConstants.ProgressionThreshold
            );

            // Recurse to propagate further downstream
            PropagateRecursive(
                conceptId: dependent.ConceptId,
                decayAmount: effectiveDrop,
                depth: depth + 1,
                input: input,
                affected: affected,
                visited: visited
            );
        }
    }
}

// ── Types ──

public record DecayPropagationInput(
    /// <summary>The concept whose mastery just decayed (HLR timer fired).</summary>
    string DecayedConceptId,

    /// <summary>How much the mastery dropped (e.g., 0.15 = dropped from 0.90 to 0.75).</summary>
    double MasteryDrop,

    /// <summary>Current mastery map for this student: conceptId → P(known).</summary>
    IReadOnlyDictionary<string, double> MasteryMap,

    /// <summary>
    /// Dependents map: conceptId → list of concepts that depend on it.
    /// Built from the prerequisite graph (reverse edges).
    /// From CurriculumGraphActor.
    /// </summary>
    IReadOnlyDictionary<string, List<DependentEdge>> DependentsMap
);

public record DependentEdge(
    /// <summary>The concept that depends on the decayed concept.</summary>
    string ConceptId,

    /// <summary>Strength of the prerequisite relationship (0.0-1.0).
    /// From Neo4j PREREQUISITE_OF edge 'strength' property.</summary>
    double EdgeStrength
);

public record AffectedConcept(
    string ConceptId,
    double PreviousEffectiveMastery,
    double NewEffectiveMastery,
    double MasteryDrop,
    int PropagationDepth,
    bool DroppedBelowPrerequisiteGate,
    bool DroppedBelowProgressionThreshold,
    bool NeedsReview
);

public record DecayPropagationResult(
    string SourceConceptId,
    double SourceMasteryDrop,
    IReadOnlyList<AffectedConcept> AffectedConcepts,
    int TotalConceptsAffected,
    int MaxDepthReached
);
