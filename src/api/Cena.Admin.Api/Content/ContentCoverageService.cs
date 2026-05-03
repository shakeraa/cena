// =============================================================================
// Cena Platform — Content Coverage Service (RDY-019c / Phase 3)
//
// Reads scripts/bagrut-taxonomy.json and the persisted QuestionReadModel
// projection, produces a coverage report:
//
//   per-track × per-leaf question count
//   per-track × per-leaf Bloom histogram + difficulty avg
//   gap list: every leaf with count < MinItemsPerLeaf
//   coverage_percent: (leaves with count >= MinItemsPerLeaf) / total leaves
//
// Surface: GET /api/v1/admin/content/coverage (see ContentCoverageEndpoints).
//
// Mapping: QuestionReadModel.Concepts carries concept ids (e.g. "CAL-003").
// The taxonomy's concept_to_seed_name_mapping is the other direction; we
// invert it at service startup so conceptId → seed_name and then walk the
// tree to find the leaf that owns each concept id.
//
// NO STUBS. Real Marten query, real taxonomy JSON, real aggregation.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Content;

public interface IContentCoverageService
{
    Task<ContentCoverageReport> BuildReportAsync(
        int minItemsPerLeaf = 3, CancellationToken ct = default);
}

public sealed record ContentCoverageReport(
    string                                   SchemaVersion,
    string                                   TaxonomyVersion,
    DateTimeOffset                           GeneratedAt,
    int                                      MinItemsPerLeaf,
    double                                   OverallCoveragePercent,
    IReadOnlyList<TrackCoverage>             Tracks,
    IReadOnlyList<TaxonomyGap>               Gaps);

public sealed record TrackCoverage(
    string                                   TrackId,              // "math_3u" etc.
    string                                   TrackName,
    int                                      TotalLeaves,
    int                                      PopulatedLeaves,
    double                                   CoveragePercent,
    IReadOnlyList<LeafCoverage>              Leaves);

public sealed record LeafCoverage(
    string                                   LeafId,               // e.g. "math_5u.calculus.derivatives"
    string                                   ConceptId,            // "CAL-003"
    string                                   SubtopicKey,          // "derivatives"
    string                                   TopicKey,             // "calculus"
    int                                      QuestionCount,
    double                                   AverageDifficulty,
    IReadOnlyDictionary<int, int>            BloomHistogram,       // bloom level → count
    int                                      PublishedCount,
    int                                      DraftCount);

public sealed record TaxonomyGap(
    string                                   LeafId,
    string                                   ConceptId,
    string                                   TrackId,
    int                                      CurrentCount,
    int                                      Shortfall);

/// <summary>
/// Pluggable fetch seam so the service stays testable without mocking the
/// Marten LINQ extension surface. Production uses MartenQuestionSource; the
/// tests use an in-memory list.
/// </summary>
public interface IContentCoverageQuestionSource
{
    Task<IReadOnlyList<QuestionReadModel>> GetQuestionsAsync(int limit, CancellationToken ct);
}

public sealed class MartenQuestionSource : IContentCoverageQuestionSource
{
    private readonly IDocumentStore _store;
    public MartenQuestionSource(IDocumentStore store) => _store = store;

    public async Task<IReadOnlyList<QuestionReadModel>> GetQuestionsAsync(int limit, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        return await session.Query<QuestionReadModel>().Take(limit).ToListAsync(ct);
    }
}

public sealed class ContentCoverageService : IContentCoverageService
{
    private readonly IContentCoverageQuestionSource _source;
    private readonly ILogger<ContentCoverageService> _logger;
    private readonly TaxonomyCache _taxonomy;

    public ContentCoverageService(
        IContentCoverageQuestionSource source,
        ILogger<ContentCoverageService> logger,
        TaxonomyCache? taxonomy = null)
    {
        _source = source;
        _logger = logger;
        _taxonomy = taxonomy ?? TaxonomyCache.LoadFromDisk();
    }

    public async Task<ContentCoverageReport> BuildReportAsync(
        int minItemsPerLeaf = 3, CancellationToken ct = default)
    {
        if (minItemsPerLeaf < 1) minItemsPerLeaf = 1;

        var questions = await _source.GetQuestionsAsync(limit: 10_000, ct);

        // Build per-leaf counters. Every question's Concepts list is
        // scanned; if a concept id maps into the taxonomy, we attribute
        // the question to that leaf. Questions without any mapped concept
        // contribute nothing (they'd show up as gaps-by-absence).
        var leafCounts = new Dictionary<string, LeafAccumulator>(StringComparer.Ordinal);

        foreach (var leaf in _taxonomy.AllLeaves)
        {
            leafCounts[leaf.LeafId] = new LeafAccumulator(leaf);
        }

        foreach (var q in questions)
        {
            if (q.Concepts is null || q.Concepts.Count == 0) continue;
            foreach (var conceptId in q.Concepts)
            {
                if (!_taxonomy.TryFindLeafForConcept(conceptId, out var leaves)) continue;
                foreach (var leaf in leaves)
                {
                    leafCounts[leaf.LeafId].Add(q);
                }
            }
        }

        // Materialize per-track roll-ups.
        var tracks = _taxonomy.Tracks.Select(track =>
        {
            var leafs = track.Leaves.Select(l =>
            {
                var acc = leafCounts[l.LeafId];
                return new LeafCoverage(
                    LeafId:            l.LeafId,
                    ConceptId:         l.ConceptId,
                    SubtopicKey:       l.SubtopicKey,
                    TopicKey:          l.TopicKey,
                    QuestionCount:     acc.Count,
                    AverageDifficulty: acc.AverageDifficulty,
                    BloomHistogram:    acc.BloomHistogram,
                    PublishedCount:    acc.PublishedCount,
                    DraftCount:        acc.DraftCount);
            }).ToList();

            var populated = leafs.Count(l => l.QuestionCount >= minItemsPerLeaf);
            var percent = leafs.Count == 0 ? 0.0 : populated * 100.0 / leafs.Count;

            return new TrackCoverage(
                TrackId:         track.TrackId,
                TrackName:       track.Name,
                TotalLeaves:     leafs.Count,
                PopulatedLeaves: populated,
                CoveragePercent: Math.Round(percent, 2),
                Leaves:          leafs);
        }).ToList();

        // Gap list across all tracks.
        var gaps = tracks
            .SelectMany(t => t.Leaves
                .Where(l => l.QuestionCount < minItemsPerLeaf)
                .Select(l => new TaxonomyGap(
                    LeafId:      l.LeafId,
                    ConceptId:   l.ConceptId,
                    TrackId:     t.TrackId,
                    CurrentCount: l.QuestionCount,
                    Shortfall:   Math.Max(0, minItemsPerLeaf - l.QuestionCount))))
            .OrderBy(g => g.TrackId)
            .ThenByDescending(g => g.Shortfall)
            .ToList();

        var totalLeaves = tracks.Sum(t => t.TotalLeaves);
        var totalPopulated = tracks.Sum(t => t.PopulatedLeaves);
        var overall = totalLeaves == 0 ? 0.0 : Math.Round(totalPopulated * 100.0 / totalLeaves, 2);

        _logger.LogInformation(
            "ContentCoverage: min_per_leaf={Min} questions={Q} overall={Overall}% gaps={Gaps}",
            minItemsPerLeaf, questions.Count, overall, gaps.Count);

        return new ContentCoverageReport(
            SchemaVersion:        "1.0",
            TaxonomyVersion:      _taxonomy.Version,
            GeneratedAt:          DateTimeOffset.UtcNow,
            MinItemsPerLeaf:      minItemsPerLeaf,
            OverallCoveragePercent: overall,
            Tracks:               tracks,
            Gaps:                 gaps);
    }

    // ------------------------------------------------------------------
    private sealed class LeafAccumulator
    {
        public LeafAccumulator(TaxonomyCache.LeafEntry leaf) => Leaf = leaf;

        public TaxonomyCache.LeafEntry Leaf { get; }
        public int Count { get; private set; }
        public int PublishedCount { get; private set; }
        public int DraftCount { get; private set; }
        private double _difficultySum;
        private readonly Dictionary<int, int> _bloom = new();

        public double AverageDifficulty => Count == 0 ? 0 : Math.Round(_difficultySum / Count, 3);
        public IReadOnlyDictionary<int, int> BloomHistogram => _bloom;

        public void Add(QuestionReadModel q)
        {
            Count++;
            _difficultySum += q.Difficulty;
            if (string.Equals(q.Status, "Published", StringComparison.OrdinalIgnoreCase)) PublishedCount++;
            else DraftCount++;
            var b = Math.Clamp(q.BloomsLevel, 1, 6);
            _bloom[b] = _bloom.GetValueOrDefault(b, 0) + 1;
        }
    }
}

// ----------------------------------------------------------------------------
// TaxonomyCache — parses scripts/bagrut-taxonomy.json once and exposes the
// flat leaf index the coverage service walks.
// ----------------------------------------------------------------------------
public sealed class TaxonomyCache
{
    public string Version { get; }
    public IReadOnlyList<TrackEntry> Tracks { get; }
    public IReadOnlyList<LeafEntry> AllLeaves { get; }
    private readonly Dictionary<string, List<LeafEntry>> _byConcept;

    // Internal so tests can build synthetic taxonomies without touching disk.
    internal TaxonomyCache(string version, IReadOnlyList<TrackEntry> tracks)
    {
        Version = version;
        Tracks = tracks;
        AllLeaves = tracks.SelectMany(t => t.Leaves).ToList();
        _byConcept = AllLeaves
            .GroupBy(l => l.ConceptId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public bool TryFindLeafForConcept(string conceptId, out IReadOnlyList<LeafEntry> leaves)
    {
        if (!string.IsNullOrWhiteSpace(conceptId) && _byConcept.TryGetValue(conceptId, out var list))
        {
            leaves = list;
            return true;
        }
        leaves = Array.Empty<LeafEntry>();
        return false;
    }

    public sealed record TrackEntry(string TrackId, string Name, IReadOnlyList<LeafEntry> Leaves);

    public sealed record LeafEntry(
        string LeafId,             // "math_5u.calculus.derivatives"
        string TrackId,            // "math_5u"
        string TopicKey,           // "calculus"
        string SubtopicKey,        // "derivatives"
        string ConceptId,          // "CAL-003"
        int    BloomMin,
        int    BloomMax);

    public static TaxonomyCache LoadFromDisk(string? path = null)
    {
        path ??= ResolveDefaultPath();
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var version = root.TryGetProperty("version", out var v) ? (v.GetString() ?? "unknown") : "unknown";
        var tracksElement = root.GetProperty("tracks");
        var tracks = new List<TrackEntry>(3);

        foreach (var trackProperty in tracksElement.EnumerateObject())
        {
            var trackId = trackProperty.Name;
            var trackJson = trackProperty.Value;
            var name = trackJson.TryGetProperty("name", out var nm) ? (nm.GetString() ?? trackId) : trackId;
            var topicsJson = trackJson.GetProperty("topics");

            var leaves = new List<LeafEntry>(32);
            foreach (var topicProperty in topicsJson.EnumerateObject())
            {
                var topicKey = topicProperty.Name;
                var subtopicsJson = topicProperty.Value.GetProperty("subtopics");

                foreach (var subtopicProperty in subtopicsJson.EnumerateObject())
                {
                    var subKey = subtopicProperty.Name;
                    var subJson = subtopicProperty.Value;
                    var conceptId = subJson.GetProperty("conceptId").GetString() ?? "";
                    var bloomArr = subJson.GetProperty("bloom_range");
                    var blMin = bloomArr[0].GetInt32();
                    var blMax = bloomArr[1].GetInt32();
                    leaves.Add(new LeafEntry(
                        LeafId:     $"{trackId}.{topicKey}.{subKey}",
                        TrackId:    trackId,
                        TopicKey:   topicKey,
                        SubtopicKey: subKey,
                        ConceptId:  conceptId,
                        BloomMin:   blMin,
                        BloomMax:   blMax));
                }
            }
            tracks.Add(new TrackEntry(trackId, name, leaves));
        }
        return new TaxonomyCache(version, tracks);
    }

    internal static string ResolveDefaultPath()
    {
        // Tests run from bin/Debug/net9.0/. Production runs from the repo
        // root. Walk up until scripts/bagrut-taxonomy.json is visible.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "bagrut-taxonomy.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "bagrut-taxonomy.json not found; coverage report cannot run. " +
            "Ensure scripts/bagrut-taxonomy.json is shipped with the deployable.");
    }
}
