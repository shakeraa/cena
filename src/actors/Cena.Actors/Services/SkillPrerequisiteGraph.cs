// =============================================================================
// Cena Platform — Skill Prerequisite DAG (BKT-PLUS-001)
//
// Static prerequisite graph per Bagrut track. Loaded from JSON at startup.
// DAG is hand-curated by content team; validated at load time (no cycles).
// =============================================================================

using System.Text.Json;

namespace Cena.Actors.Services;

public interface ISkillPrerequisiteGraph
{
    /// <summary>
    /// Get prerequisite skill IDs for a given skill.
    /// Returns empty list if skill has no prerequisites or is unknown.
    /// </summary>
    IReadOnlyList<string> GetPrerequisites(string skillId);

    /// <summary>
    /// Get all skills that depend on the given skill (downstream dependents).
    /// </summary>
    IReadOnlyList<string> GetDependents(string skillId);

    /// <summary>
    /// Get all skill IDs in the graph.
    /// </summary>
    IReadOnlyList<string> AllSkills { get; }
}

/// <summary>
/// In-memory prerequisite graph loaded from JSON files.
/// Expected JSON format:
/// {
///   "skills": {
///     "algebra-linear-equations": { "prerequisites": [] },
///     "algebra-quadratic-equations": { "prerequisites": ["algebra-linear-equations"] },
///     ...
///   }
/// }
/// </summary>
public sealed class SkillPrerequisiteGraph : ISkillPrerequisiteGraph
{
    private readonly Dictionary<string, List<string>> _prerequisites = new();
    private readonly Dictionary<string, List<string>> _dependents = new();
    private readonly List<string> _allSkills;
    private static readonly IReadOnlyList<string> Empty = Array.Empty<string>();

    private SkillPrerequisiteGraph(Dictionary<string, List<string>> prerequisites)
    {
        _prerequisites = prerequisites;
        _allSkills = prerequisites.Keys.ToList();

        // Build reverse index (dependents)
        foreach (var (skill, prereqs) in prerequisites)
        {
            foreach (var prereq in prereqs)
            {
                if (!_dependents.TryGetValue(prereq, out var deps))
                {
                    deps = new List<string>();
                    _dependents[prereq] = deps;
                }
                deps.Add(skill);
            }
        }
    }

    public IReadOnlyList<string> GetPrerequisites(string skillId) =>
        _prerequisites.TryGetValue(skillId, out var prereqs) ? prereqs : Empty;

    public IReadOnlyList<string> GetDependents(string skillId) =>
        _dependents.TryGetValue(skillId, out var deps) ? deps : Empty;

    public IReadOnlyList<string> AllSkills => _allSkills;

    /// <summary>
    /// Load prerequisite graph from a JSON string.
    /// Validates: no cycles, all referenced prerequisites exist.
    /// </summary>
    public static SkillPrerequisiteGraph LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var skills = doc.RootElement.GetProperty("skills");
        var prerequisites = new Dictionary<string, List<string>>();

        foreach (var skill in skills.EnumerateObject())
        {
            var prereqs = new List<string>();
            if (skill.Value.TryGetProperty("prerequisites", out var prereqArray))
            {
                foreach (var prereq in prereqArray.EnumerateArray())
                {
                    prereqs.Add(prereq.GetString()!);
                }
            }
            prerequisites[skill.Name] = prereqs;
        }

        // Validate: all referenced prerequisites must exist in the graph
        foreach (var (skill, prereqs) in prerequisites)
        {
            foreach (var prereq in prereqs)
            {
                if (!prerequisites.ContainsKey(prereq))
                    throw new InvalidOperationException(
                        $"Skill '{skill}' references prerequisite '{prereq}' which is not defined in the graph.");
            }
        }

        // Validate: no cycles (topological sort)
        ValidateNoCycles(prerequisites);

        return new SkillPrerequisiteGraph(prerequisites);
    }

    private static void ValidateNoCycles(Dictionary<string, List<string>> graph)
    {
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();

        foreach (var skill in graph.Keys)
        {
            if (HasCycleDfs(skill, graph, visited, inStack))
                throw new InvalidOperationException(
                    $"Cycle detected in prerequisite graph involving skill '{skill}'.");
        }
    }

    private static bool HasCycleDfs(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> inStack)
    {
        if (inStack.Contains(node)) return true;
        if (visited.Contains(node)) return false;

        visited.Add(node);
        inStack.Add(node);

        if (graph.TryGetValue(node, out var prereqs))
        {
            foreach (var prereq in prereqs)
            {
                if (HasCycleDfs(prereq, graph, visited, inStack))
                    return true;
            }
        }

        inStack.Remove(node);
        return false;
    }
}
