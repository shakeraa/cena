// =============================================================================
// Cena Platform — Topic Prerequisite Graph (RDY-073 Phase 1B)
//
// Pure in-memory DAG of topic → prerequisites, built from either the
// SyllabusDocument chapter tree or a test-supplied edge list. Exposes
// the PrerequisiteUrgency(topic) score the AdaptiveScheduler needs to
// replace its Phase-1A 1.0 placeholder.
//
// Urgency semantics (pre-registered — changing these requires
// Dr. Yael + Dr. Nadia sign-off):
//   - A LEAF topic (no outgoing "is-prerequisite-of" edges) has
//     urgency 0.5 — it doesn't unblock anything else on its own.
//   - A topic that is a prerequisite for 1+ other scheduled topics
//     has urgency 1.0.
//   - A topic that is a prerequisite for 3+ other topics has
//     urgency 1.5 — scheduling it unblocks a cluster.
//
// This module never loads Marten — it accepts its edges at construction
// time so the scheduler can be unit-tested cleanly and callers (phase
// 1C admin loader) hydrate from SyllabusDocument on startup.
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

public sealed class TopicPrerequisiteGraph
{
    /// <summary>
    /// Adjacency list: key = topic slug, value = list of topics that
    /// directly require this one as a prerequisite. Read-only after
    /// construction.
    /// </summary>
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _dependents;

    private TopicPrerequisiteGraph(
        ImmutableDictionary<string, ImmutableArray<string>> dependents)
    {
        _dependents = dependents;
    }

    /// <summary>
    /// Count of topics that list <paramref name="topicSlug"/> in their
    /// prerequisites list (direct dependents, no transitive closure).
    /// Returns 0 for unknown / leaf topics.
    /// </summary>
    public int DirectDependentCount(string topicSlug)
    {
        if (string.IsNullOrWhiteSpace(topicSlug)) return 0;
        return _dependents.TryGetValue(topicSlug, out var list) ? list.Length : 0;
    }

    /// <summary>
    /// Urgency multiplier per the §1 semantics above. Clamp is deliberate:
    /// nothing gets above 1.5 or below 0.5 — this multiplier is a tie-
    /// breaker, not a dominant weight. The scheduler's primary signal
    /// is (weakness × topicWeight); DAG urgency nudges order without
    /// overriding the pedagogy signal.
    /// </summary>
    public double PrerequisiteUrgency(string topicSlug)
    {
        var deps = DirectDependentCount(topicSlug);
        if (deps >= 3) return 1.5;
        if (deps >= 1) return 1.0;
        return 0.5;
    }

    /// <summary>
    /// Build from an explicit (topic, prerequisites) edge list.
    /// Each topic key is a node; its value is the set of topics that
    /// MUST be mastered first (incoming edges). The graph inverts
    /// this to compute the <c>dependents</c> adjacency internally.
    ///
    /// Bidirectional edges + self-loops are silently deduped; cycles
    /// are not detected here — the scheduler's topo-sort layer
    /// (phase 1C) catches them before scheduling.
    /// </summary>
    public static TopicPrerequisiteGraph FromPrerequisites(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> topicToPrereqs)
    {
        ArgumentNullException.ThrowIfNull(topicToPrereqs);
        var builder = new Dictionary<string, HashSet<string>>();

        foreach (var (topic, prereqs) in topicToPrereqs)
        {
            if (string.IsNullOrWhiteSpace(topic)) continue;
            foreach (var prereq in prereqs ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(prereq)) continue;
                if (prereq == topic) continue; // self-loop
                if (!builder.TryGetValue(prereq, out var set))
                {
                    set = new HashSet<string>();
                    builder[prereq] = set;
                }
                set.Add(topic);
            }
        }

        var frozen = builder.ToImmutableDictionary(
            kv => kv.Key,
            kv => kv.Value.ToImmutableArray());
        return new TopicPrerequisiteGraph(frozen);
    }

    /// <summary>
    /// Empty graph — every topic is treated as a leaf (urgency 0.5).
    /// Used when the syllabus manifest hasn't been loaded (tests) or
    /// when the scheduler runs in a cold-start cohort before the DAG
    /// is hydrated.
    /// </summary>
    public static TopicPrerequisiteGraph Empty { get; } =
        new(ImmutableDictionary<string, ImmutableArray<string>>.Empty);
}
