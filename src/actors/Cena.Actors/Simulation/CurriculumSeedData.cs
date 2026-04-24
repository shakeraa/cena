// =============================================================================
// Cena Platform -- Curriculum Seed Data
// Realistic Israeli Bagrut math curriculum graph: 45 concepts, 3 depth levels,
// 6 topic clusters, prerequisite chains reflecting actual pedagogical structure.
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Simulation;

/// <summary>
/// Generates a realistic math curriculum graph modeled on the Israeli Bagrut
/// (5-unit matriculation) syllabus. Concepts span algebra, geometry, calculus,
/// trigonometry, probability, and vectors with real prerequisite chains.
/// </summary>
public static class CurriculumSeedData
{
    public static (IReadOnlyList<MasteryConceptNode> Concepts, IReadOnlyList<MasteryPrerequisiteEdge> Edges)
        BuildBagrutMathCurriculum()
    {
        var concepts = new List<MasteryConceptNode>
        {
            // ── Algebra (depth 1-3, cluster "algebra") ──
            Node("ALG-001", "Number Properties",       "math", "algebra", 1, 0.20f, 0.3f, 3),
            Node("ALG-002", "Linear Equations",        "math", "algebra", 1, 0.30f, 0.5f, 4),
            Node("ALG-003", "Inequalities",            "math", "algebra", 1, 0.35f, 0.4f, 4),
            Node("ALG-004", "Quadratic Equations",     "math", "algebra", 2, 0.50f, 0.7f, 5),
            Node("ALG-005", "Systems of Equations",    "math", "algebra", 2, 0.45f, 0.6f, 5),
            Node("ALG-006", "Polynomials",             "math", "algebra", 2, 0.55f, 0.5f, 5),
            Node("ALG-007", "Rational Expressions",    "math", "algebra", 3, 0.65f, 0.4f, 6),
            Node("ALG-008", "Sequences & Series",      "math", "algebra", 3, 0.60f, 0.6f, 6),

            // ── Functions (depth 1-3, cluster "functions") ──
            Node("FUN-001", "Function Basics",         "math", "functions", 1, 0.25f, 0.5f, 3),
            Node("FUN-002", "Linear Functions",        "math", "functions", 1, 0.30f, 0.6f, 4),
            Node("FUN-003", "Quadratic Functions",     "math", "functions", 2, 0.50f, 0.7f, 5),
            Node("FUN-004", "Exponential Functions",   "math", "functions", 2, 0.55f, 0.5f, 5),
            Node("FUN-005", "Logarithmic Functions",   "math", "functions", 2, 0.60f, 0.4f, 5),
            Node("FUN-006", "Composite Functions",     "math", "functions", 3, 0.70f, 0.3f, 6),
            Node("FUN-007", "Inverse Functions",       "math", "functions", 3, 0.65f, 0.3f, 6),

            // ── Geometry (depth 1-3, cluster "geometry") ──
            Node("GEO-001", "Angles & Lines",          "math", "geometry", 1, 0.20f, 0.3f, 3),
            Node("GEO-002", "Triangles",               "math", "geometry", 1, 0.30f, 0.5f, 4),
            Node("GEO-003", "Circle Properties",       "math", "geometry", 2, 0.45f, 0.5f, 5),
            Node("GEO-004", "Coordinate Geometry",     "math", "geometry", 2, 0.50f, 0.6f, 5),
            Node("GEO-005", "Trigonometric Ratios",    "math", "geometry", 2, 0.55f, 0.7f, 5),
            Node("GEO-006", "Area & Volume",           "math", "geometry", 2, 0.40f, 0.5f, 4),
            Node("GEO-007", "Analytic Geometry",       "math", "geometry", 3, 0.70f, 0.6f, 6),

            // ── Trigonometry (depth 2-3, cluster "trigonometry") ──
            Node("TRG-001", "Trig Identities",         "math", "trigonometry", 2, 0.55f, 0.6f, 5),
            Node("TRG-002", "Trig Equations",          "math", "trigonometry", 2, 0.60f, 0.5f, 5),
            Node("TRG-003", "Sine & Cosine Rules",     "math", "trigonometry", 3, 0.65f, 0.7f, 6),
            Node("TRG-004", "Radian Measure",          "math", "trigonometry", 2, 0.50f, 0.4f, 5),

            // ── Calculus (depth 2-3, cluster "calculus") ──
            Node("CAL-001", "Limits",                  "math", "calculus", 2, 0.60f, 0.7f, 5),
            Node("CAL-002", "Derivative Definition",   "math", "calculus", 2, 0.65f, 0.8f, 5),
            Node("CAL-003", "Derivative Rules",        "math", "calculus", 3, 0.70f, 0.8f, 6),
            Node("CAL-004", "Applications of Deriv.",   "math", "calculus", 3, 0.75f, 0.9f, 6),
            Node("CAL-005", "Integrals Intro",         "math", "calculus", 3, 0.80f, 0.7f, 6),
            Node("CAL-006", "Definite Integrals",      "math", "calculus", 3, 0.85f, 0.8f, 6),

            // ── Probability & Statistics (depth 1-3, cluster "probability") ──
            Node("PRB-001", "Counting Principles",     "math", "probability", 1, 0.25f, 0.4f, 3),
            Node("PRB-002", "Basic Probability",       "math", "probability", 1, 0.30f, 0.5f, 4),
            Node("PRB-003", "Conditional Probability",  "math", "probability", 2, 0.50f, 0.6f, 5),
            Node("PRB-004", "Binomial Distribution",   "math", "probability", 2, 0.55f, 0.5f, 5),
            Node("PRB-005", "Normal Distribution",     "math", "probability", 3, 0.65f, 0.6f, 6),
            Node("PRB-006", "Statistical Inference",   "math", "probability", 3, 0.70f, 0.5f, 6),

            // ── Vectors (depth 2-3, cluster "vectors") ──
            Node("VEC-001", "Vector Basics",           "math", "vectors", 2, 0.45f, 0.4f, 4),
            Node("VEC-002", "Dot Product",             "math", "vectors", 2, 0.55f, 0.5f, 5),
            Node("VEC-003", "Cross Product",           "math", "vectors", 3, 0.65f, 0.4f, 6),
            Node("VEC-004", "Vector Applications",     "math", "vectors", 3, 0.70f, 0.5f, 6),
        };

        var edges = new List<MasteryPrerequisiteEdge>
        {
            // Algebra chain
            Edge("ALG-001", "ALG-002"), Edge("ALG-001", "ALG-003"),
            Edge("ALG-002", "ALG-004"), Edge("ALG-002", "ALG-005"),
            Edge("ALG-004", "ALG-006"), Edge("ALG-006", "ALG-007"),
            Edge("ALG-004", "ALG-008"),

            // Functions chain (depends on algebra)
            Edge("ALG-001", "FUN-001"), Edge("FUN-001", "FUN-002"),
            Edge("ALG-004", "FUN-003"), Edge("FUN-002", "FUN-003"),
            Edge("FUN-002", "FUN-004"), Edge("FUN-004", "FUN-005"),
            Edge("FUN-003", "FUN-006"), Edge("FUN-004", "FUN-006"),
            Edge("FUN-002", "FUN-007"),

            // Geometry chain
            Edge("GEO-001", "GEO-002"), Edge("GEO-002", "GEO-003"),
            Edge("GEO-001", "GEO-004"), Edge("ALG-002", "GEO-004"),
            Edge("GEO-002", "GEO-005"), Edge("GEO-002", "GEO-006"),
            Edge("GEO-004", "GEO-007"), Edge("FUN-002", "GEO-007"),

            // Trigonometry (depends on geometry + functions)
            Edge("GEO-005", "TRG-001"), Edge("TRG-001", "TRG-002"),
            Edge("GEO-005", "TRG-003"), Edge("TRG-001", "TRG-003"),
            Edge("GEO-005", "TRG-004"),

            // Calculus (depends on functions + algebra)
            Edge("FUN-003", "CAL-001"), Edge("ALG-006", "CAL-001"),
            Edge("CAL-001", "CAL-002"), Edge("CAL-002", "CAL-003"),
            Edge("CAL-003", "CAL-004"), Edge("CAL-003", "CAL-005"),
            Edge("CAL-005", "CAL-006"),

            // Probability chain
            Edge("ALG-001", "PRB-001"), Edge("PRB-001", "PRB-002"),
            Edge("PRB-002", "PRB-003"), Edge("PRB-003", "PRB-004"),
            Edge("PRB-004", "PRB-005"), Edge("PRB-005", "PRB-006"),

            // Vectors (depends on geometry + trig)
            Edge("GEO-004", "VEC-001"), Edge("TRG-001", "VEC-001"),
            Edge("VEC-001", "VEC-002"), Edge("VEC-002", "VEC-003"),
            Edge("VEC-002", "VEC-004"),
        };

        return (concepts, edges);
    }

    /// <summary>
    /// Build an IConceptGraphCache from the seed data.
    /// </summary>
    public static InMemoryGraphCache BuildGraphCache()
    {
        var (concepts, edges) = BuildBagrutMathCurriculum();
        return new InMemoryGraphCache(concepts, edges);
    }

    private static MasteryConceptNode Node(string id, string name, string subject,
        string cluster, int depth, float load, float bagrutWeight, int bloomMax) =>
        new(id, name, subject, cluster, depth, load, bagrutWeight, bloomMax);

    private static MasteryPrerequisiteEdge Edge(string from, string to, float strength = 1.0f) =>
        new(from, to, strength);
}

/// <summary>
/// In-memory graph cache backed by dictionaries. Used for simulation and testing.
/// </summary>
public sealed class InMemoryGraphCache : IConceptGraphCache
{
    private readonly Dictionary<string, MasteryConceptNode> _concepts;
    private readonly Dictionary<string, List<MasteryPrerequisiteEdge>> _prerequisites;
    private readonly Dictionary<string, List<string>> _descendants;
    private readonly Dictionary<string, int> _depths;

    public InMemoryGraphCache(
        IReadOnlyList<MasteryConceptNode> concepts,
        IReadOnlyList<MasteryPrerequisiteEdge> edges)
    {
        _concepts = concepts.ToDictionary(c => c.Id);
        _prerequisites = new();
        _descendants = new();
        _depths = concepts.ToDictionary(c => c.Id, c => c.DepthLevel);

        // Build prerequisite lookup (target -> sources)
        foreach (var edge in edges)
        {
            if (!_prerequisites.ContainsKey(edge.TargetConceptId))
                _prerequisites[edge.TargetConceptId] = new();
            _prerequisites[edge.TargetConceptId].Add(edge);

            // Build descendant lookup (source -> targets)
            if (!_descendants.ContainsKey(edge.SourceConceptId))
                _descendants[edge.SourceConceptId] = new();
            _descendants[edge.SourceConceptId].Add(edge.TargetConceptId);
        }

        // Expand descendants transitively
        foreach (var conceptId in _concepts.Keys)
            GetDescendantsRecursive(conceptId, new HashSet<string>());
    }

    public IReadOnlyDictionary<string, MasteryConceptNode> Concepts => _concepts;

    public IReadOnlyList<MasteryPrerequisiteEdge> GetPrerequisites(string conceptId) =>
        _prerequisites.TryGetValue(conceptId, out var list) ? list : Array.Empty<MasteryPrerequisiteEdge>();

    public IReadOnlyList<string> GetDescendants(string conceptId) =>
        _descendants.TryGetValue(conceptId, out var list) ? list : Array.Empty<string>();

    public int GetDepth(string conceptId) =>
        _depths.TryGetValue(conceptId, out var d) ? d : 0;

    private HashSet<string> GetDescendantsRecursive(string conceptId, HashSet<string> visited)
    {
        if (!visited.Add(conceptId)) return visited;
        if (_descendants.TryGetValue(conceptId, out var direct))
        {
            foreach (var child in direct.ToList())
                GetDescendantsRecursive(child, visited);
        }
        return visited;
    }
}
