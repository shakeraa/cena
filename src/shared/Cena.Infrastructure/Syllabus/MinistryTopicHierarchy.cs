// =============================================================================
// Cena Platform — Ministry Topic Hierarchy (RDY-070 Phase 1A, F6 teacher heatmap)
//
// Typed, read-only wrapper over the authored syllabus YAML tree
// (config/syllabi/*.yaml). Exposes Ministry-aligned topic slugs grouped
// by bagrut track, each carrying its Ministry code ("806.1", "807.4", ...)
// and a synthetic parent slug ("ministry-806" / "ministry-807") derived
// from the Ministry code prefix so the teacher heatmap can render a two-
// level hierarchy without re-authoring the syllabus.
//
// Loaded ONCE at startup. The class itself is immutable after construction.
// Source-of-truth stays in the YAML file; this is a projection.
// =============================================================================
//
// Ownership: Prof. Amjad (curriculum-expert review of the YAML); the
// hierarchy wrapper itself has no pedagogical content of its own — it
// only exposes the authored tree shape.
//
// Design notes:
//   - Re-authoring the syllabus is explicitly OUT of scope. If the YAML is
//     missing or malformed, we fail loudly on startup rather than serve a
//     silent fallback.
//   - Parent relationship is derived from the MinistryCode prefix, not
//     from `prerequisiteChapterSlugs` (which is a DAG, not a tree).
//   - LO → topic reverse index is built once at load so the heatmap
//     aggregation is O(1) per attempt event.
// =============================================================================

using Cena.Infrastructure.Documents;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cena.Infrastructure.Syllabus;

/// <summary>
/// A single Ministry-aligned topic (equivalent to a chapter in the syllabus
/// YAML), enriched with its Ministry code, synthetic parent root, and the
/// learning objective ids it owns.
/// </summary>
public sealed record MinistryTopic(
    string Slug,
    int Order,
    BagrutTrack Track,
    string? MinistryCode,
    string? ParentSlug,
    IReadOnlyDictionary<string, string> TitleByLocale,
    IReadOnlyList<string> LearningObjectiveIds);

/// <summary>
/// Read-only hierarchy over Ministry-aligned topics across all authored
/// bagrut tracks. Injected as a singleton.
/// </summary>
public interface IMinistryTopicHierarchy
{
    /// <summary>
    /// All topics for the given track, in manifest order.
    /// Empty list if the track has no authored syllabus.
    /// </summary>
    IReadOnlyList<MinistryTopic> TopicsFor(BagrutTrack track);

    /// <summary>
    /// The Ministry code ("806.1", "807.4", ...) for the topic, or null
    /// if the topic isn't known or wasn't authored with a Ministry code.
    /// </summary>
    string? GetMinistryCode(string topicSlug);

    /// <summary>
    /// The synthetic parent slug for a topic, derived from the Ministry
    /// code's root segment: "ministry-806" / "ministry-807". Null if the
    /// topic has no Ministry code.
    /// </summary>
    string? Parent(string topicSlug);

    /// <summary>
    /// Look up a single topic by slug. Null if unknown.
    /// </summary>
    MinistryTopic? GetTopic(string topicSlug);

    /// <summary>
    /// Reverse index: the Ministry topic that owns a learning objective
    /// id, or null if the LO isn't covered by any authored chapter.
    /// Used by the heatmap projection to roll attempt events up from the
    /// concept grain to the chapter grain.
    /// </summary>
    string? TopicSlugForLearningObjective(string learningObjectiveId);
}

/// <summary>
/// Default implementation: loads one or more syllabus YAML manifests from
/// disk and builds an in-memory index.
/// </summary>
public sealed class MinistryTopicHierarchy : IMinistryTopicHierarchy
{
    private readonly Dictionary<string, MinistryTopic> _bySlug;
    private readonly Dictionary<BagrutTrack, IReadOnlyList<MinistryTopic>> _byTrack;
    private readonly Dictionary<string, string> _loToTopicSlug;

    internal MinistryTopicHierarchy(IReadOnlyList<SyllabusYamlManifest> manifests)
    {
        _bySlug = new(StringComparer.Ordinal);
        _loToTopicSlug = new(StringComparer.Ordinal);
        var byTrack = new Dictionary<BagrutTrack, List<MinistryTopic>>();

        foreach (var manifest in manifests)
        {
            if (manifest.Chapters is null) continue;

            foreach (var chapter in manifest.Chapters.OrderBy(c => c.Order))
            {
                if (string.IsNullOrWhiteSpace(chapter.Slug))
                    throw new InvalidOperationException(
                        $"[MinistryTopicHierarchy] manifest '{manifest.Track}' has a chapter with an empty slug");

                var parent = DeriveParentSlug(chapter.MinistryCode);
                var los = (chapter.LearningObjectiveIds ?? new()).ToList();

                var topic = new MinistryTopic(
                    Slug: chapter.Slug,
                    Order: chapter.Order,
                    Track: manifest.BagrutTrack,
                    MinistryCode: chapter.MinistryCode,
                    ParentSlug: parent,
                    TitleByLocale: chapter.Title ?? new(),
                    LearningObjectiveIds: los);

                if (_bySlug.ContainsKey(topic.Slug))
                    throw new InvalidOperationException(
                        $"[MinistryTopicHierarchy] duplicate topic slug '{topic.Slug}' across manifests");

                _bySlug[topic.Slug] = topic;

                if (!byTrack.TryGetValue(topic.Track, out var list))
                    byTrack[topic.Track] = list = new();
                list.Add(topic);

                foreach (var loId in los)
                {
                    // First-write wins — the same LO in two chapters is a
                    // curriculum authoring bug; surface it, don't hide it.
                    if (_loToTopicSlug.TryGetValue(loId, out var existing) && existing != topic.Slug)
                        throw new InvalidOperationException(
                            $"[MinistryTopicHierarchy] learning-objective '{loId}' appears in both " +
                            $"chapter '{existing}' and chapter '{topic.Slug}' — resolve in the YAML");

                    _loToTopicSlug[loId] = topic.Slug;
                }
            }
        }

        _byTrack = byTrack.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<MinistryTopic>)kv.Value.AsReadOnly());
    }

    public IReadOnlyList<MinistryTopic> TopicsFor(BagrutTrack track)
        => _byTrack.TryGetValue(track, out var list) ? list : Array.Empty<MinistryTopic>();

    public string? GetMinistryCode(string topicSlug)
        => _bySlug.TryGetValue(topicSlug, out var t) ? t.MinistryCode : null;

    public string? Parent(string topicSlug)
        => _bySlug.TryGetValue(topicSlug, out var t) ? t.ParentSlug : null;

    public MinistryTopic? GetTopic(string topicSlug)
        => _bySlug.TryGetValue(topicSlug, out var t) ? t : null;

    public string? TopicSlugForLearningObjective(string learningObjectiveId)
        => _loToTopicSlug.TryGetValue(learningObjectiveId, out var slug) ? slug : null;

    /// <summary>
    /// Load a set of YAML manifest files and return an immutable hierarchy.
    /// Fails loudly if any file is missing or malformed — the heatmap
    /// console depends on this data.
    /// </summary>
    public static MinistryTopicHierarchy LoadFromFiles(IEnumerable<string> paths)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var manifests = new List<SyllabusYamlManifest>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"[MinistryTopicHierarchy] syllabus manifest not found: {path}", path);

            var yaml = File.ReadAllText(path);
            var manifest = deserializer.Deserialize<SyllabusYamlManifest>(yaml)
                ?? throw new InvalidOperationException(
                    $"[MinistryTopicHierarchy] manifest at '{path}' deserialised as null");
            manifests.Add(manifest);
        }

        return new MinistryTopicHierarchy(manifests);
    }

    /// <summary>
    /// Load every <c>*.yaml</c> manifest under the given directory. Used at
    /// startup to pick up all authored tracks at once.
    /// </summary>
    public static MinistryTopicHierarchy LoadFromDirectory(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            throw new DirectoryNotFoundException(
                $"[MinistryTopicHierarchy] syllabus directory not found: {dirPath}");

        var files = Directory.EnumerateFiles(dirPath, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
            throw new InvalidOperationException(
                $"[MinistryTopicHierarchy] no *.yaml manifests found in '{dirPath}'");

        return LoadFromFiles(files);
    }

    internal static string? DeriveParentSlug(string? ministryCode)
    {
        if (string.IsNullOrWhiteSpace(ministryCode)) return null;
        var dot = ministryCode.IndexOf('.');
        var root = dot > 0 ? ministryCode.Substring(0, dot) : ministryCode;
        return $"ministry-{root}";
    }
}

// -----------------------------------------------------------------------------
// YAML-binding DTOs (internal to this module — the public surface is
// MinistryTopic + IMinistryTopicHierarchy).
// -----------------------------------------------------------------------------

internal sealed class SyllabusYamlManifest
{
    public string Track { get; set; } = "";
    public string Version { get; set; } = "";
    public BagrutTrack BagrutTrack { get; set; }
    public List<string>? MinistryCodes { get; set; }
    public List<SyllabusYamlChapter>? Chapters { get; set; }
}

internal sealed class SyllabusYamlChapter
{
    public string Slug { get; set; } = "";
    public int Order { get; set; }
    public Dictionary<string, string>? Title { get; set; }
    public List<string>? LearningObjectiveIds { get; set; }
    public List<string>? PrerequisiteChapterSlugs { get; set; }
    public int ExpectedWeeks { get; set; }
    public string? MinistryCode { get; set; }
}
