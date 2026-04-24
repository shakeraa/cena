// =============================================================================
// Cena Platform -- ContentCoverageService tests (Phase 3 / RDY-019c)
//
// Drives the service with a synthetic taxonomy + an in-memory list of
// QuestionReadModel rows, verifying:
//   - leaf counts roll up correctly per track
//   - coverage_percent computes against min_items_per_leaf
//   - gap list is sorted (track then shortfall desc)
//   - bloom histogram + avg_difficulty aggregate right
//   - questions without mapped concepts don't contaminate any leaf
// =============================================================================

using Cena.Actors.Questions;
using Cena.Admin.Api.Content;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Content;

public sealed class ContentCoverageServiceTests
{
    // A tiny synthetic taxonomy the tests fully control. Two tracks,
    // two leaves each. Real taxonomy-loading path covered by the
    // TaxonomyCache_Loads_Real_File smoke test below.
    private static TaxonomyCache BuildTaxonomy() =>
        new(
            "test-1.0",
            new List<TaxonomyCache.TrackEntry>
            {
                new("math_3u", "Mathematics 3-Unit", new List<TaxonomyCache.LeafEntry>
                {
                    new("math_3u.algebra.linear",    "math_3u", "algebra",  "linear",    "ALG-002", 1, 3),
                    new("math_3u.geometry.triangles","math_3u", "geometry", "triangles", "GEO-002", 2, 3),
                }),
                new("math_5u", "Mathematics 5-Unit", new List<TaxonomyCache.LeafEntry>
                {
                    new("math_5u.calculus.derivatives","math_5u", "calculus", "derivatives","CAL-003", 3, 5),
                    new("math_5u.calculus.integrals",  "math_5u", "calculus", "integrals",  "CAL-005", 3, 5),
                }),
            });

    private sealed class InMemoryQuestionSource : IContentCoverageQuestionSource
    {
        private readonly IReadOnlyList<QuestionReadModel> _items;
        public InMemoryQuestionSource(IEnumerable<QuestionReadModel> items) => _items = items.ToList();
        public Task<IReadOnlyList<QuestionReadModel>> GetQuestionsAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<QuestionReadModel>>(_items.Take(limit).ToList());
    }

    private static ContentCoverageService BuildService(TaxonomyCache taxonomy, params QuestionReadModel[] qs) =>
        new(new InMemoryQuestionSource(qs), NullLogger<ContentCoverageService>.Instance, taxonomy);

    [Fact]
    public async Task BuildReportAsync_Rolls_Up_Counts_Per_Leaf()
    {
        var taxonomy = BuildTaxonomy();
        var qs = new[]
        {
            Q("q1", concept: "ALG-002", difficulty: 0.4f, bloom: 2, status: "Published"),
            Q("q2", concept: "ALG-002", difficulty: 0.5f, bloom: 3, status: "Published"),
            Q("q3", concept: "CAL-003", difficulty: 0.8f, bloom: 5, status: "Draft"),
        };
        var service = BuildService(taxonomy, qs);

        var report = await service.BuildReportAsync(minItemsPerLeaf: 1);

        // math_3u.algebra.linear should have 2 questions, the other 3u leaf 0.
        var track3u = report.Tracks.Single(t => t.TrackId == "math_3u");
        var linear  = track3u.Leaves.Single(l => l.LeafId == "math_3u.algebra.linear");
        var tri     = track3u.Leaves.Single(l => l.LeafId == "math_3u.geometry.triangles");

        Assert.Equal(2, linear.QuestionCount);
        Assert.Equal(2, linear.PublishedCount);
        Assert.Equal(0, linear.DraftCount);
        Assert.InRange(linear.AverageDifficulty, 0.44, 0.46);
        Assert.Equal(1, linear.BloomHistogram[2]);
        Assert.Equal(1, linear.BloomHistogram[3]);
        Assert.Equal(0, tri.QuestionCount);

        // math_5u.calculus.derivatives has 1 Draft question.
        var derivs = report.Tracks
            .Single(t => t.TrackId == "math_5u")
            .Leaves.Single(l => l.LeafId == "math_5u.calculus.derivatives");
        Assert.Equal(1, derivs.QuestionCount);
        Assert.Equal(0, derivs.PublishedCount);
        Assert.Equal(1, derivs.DraftCount);
    }

    [Fact]
    public async Task BuildReportAsync_Coverage_Percent_Honors_MinItemsPerLeaf()
    {
        var taxonomy = BuildTaxonomy();
        var qs = new[]
        {
            Q("q1", concept: "ALG-002"),
            Q("q2", concept: "ALG-002"),
            Q("q3", concept: "ALG-002"),
        };
        var service = BuildService(taxonomy, qs);

        // min=3 → only linear qualifies; 1 of 4 leaves = 25%
        var report = await service.BuildReportAsync(minItemsPerLeaf: 3);
        Assert.Equal(25.0, report.OverallCoveragePercent);

        // min=1 → still only linear has any; still 1/4 = 25%
        var reportLower = await service.BuildReportAsync(minItemsPerLeaf: 1);
        Assert.Equal(25.0, reportLower.OverallCoveragePercent);
    }

    [Fact]
    public async Task BuildReportAsync_Gap_List_Sorted_By_Track_Then_Shortfall()
    {
        var taxonomy = BuildTaxonomy();
        var qs = new[]
        {
            Q("q1", concept: "ALG-002"),                    // 1 on math_3u.algebra.linear
            Q("q2", concept: "CAL-003"), Q("q3", concept: "CAL-003"),   // 2 on math_5u.calculus.derivatives
        };
        var service = BuildService(taxonomy, qs);

        var report = await service.BuildReportAsync(minItemsPerLeaf: 3);

        // 4 leaves total, 0 populated → every leaf is in the gap list.
        Assert.Equal(4, report.Gaps.Count);
        // Sorted: math_3u first (alphabetical), then math_5u.
        Assert.Equal("math_3u", report.Gaps[0].TrackId);
        Assert.Equal("math_3u", report.Gaps[1].TrackId);
        Assert.Equal("math_5u", report.Gaps[2].TrackId);
        Assert.Equal("math_5u", report.Gaps[3].TrackId);
        // Within each track, highest shortfall first.
        Assert.True(report.Gaps[0].Shortfall >= report.Gaps[1].Shortfall);
        Assert.True(report.Gaps[2].Shortfall >= report.Gaps[3].Shortfall);
    }

    [Fact]
    public async Task BuildReportAsync_Questions_Without_Concepts_Ignored()
    {
        var taxonomy = BuildTaxonomy();
        var qs = new[]
        {
            Q("q1", concept: "ALG-002"),
            Q("q2", concept: null),                  // no concept list
            Q("q3", concept: "UNKNOWN-999"),         // not in taxonomy
        };
        var service = BuildService(taxonomy, qs);

        var report = await service.BuildReportAsync(minItemsPerLeaf: 1);

        var total = report.Tracks.Sum(t => t.Leaves.Sum(l => l.QuestionCount));
        Assert.Equal(1, total);   // only the ALG-002 one landed on a leaf
    }

    [Fact]
    public void TaxonomyCache_Loads_Real_File()
    {
        var tax = TaxonomyCache.LoadFromDisk();
        // The committed bagrut-taxonomy.json has 3 tracks, 73 total leaves
        // (42 + 17 + 14). Guards against taxonomy-loader regressions.
        Assert.Equal(3, tax.Tracks.Count);
        Assert.Equal(73, tax.AllLeaves.Count);
        Assert.Contains(tax.AllLeaves, l => l.ConceptId == "CAL-003");
    }

    // --- helpers -----------------------------------------------------------
    private static QuestionReadModel Q(
        string id, string? concept, float difficulty = 0.5f, int bloom = 3, string status = "Draft")
    {
        return new QuestionReadModel
        {
            Id = id,
            Concepts = concept is null ? new List<string>() : new List<string> { concept },
            Difficulty = difficulty,
            BloomsLevel = bloom,
            Status = status,
        };
    }
}
