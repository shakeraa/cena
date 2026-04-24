// =============================================================================
// Cena Platform -- CorpusExpanderHandler tests (RDY-059)
//
// Covers the coordinator logic:
//   - dry-run returns plan without LLM calls
//   - selector resolution (happy + invalid)
//   - planner skips full leaves + budget exhaustion
//   - wet run calls GenerateSimilarHandler.RunCoreAsync via AiGenerationService
//   - per-source error doesn't abort the loop
//   - CAS-drop counts aggregate across bands + sources
//   - difficulty band validation
//   - MaxTotalCandidates validation
//
// Marten is fully substituted (handler calls it through IDocumentStore +
// ICorpusSourceProvider + IContentCoverageService). No TestServer.
// =============================================================================

using Cena.Actors.Questions;
using Cena.Admin.Api.Content;
using Cena.Admin.Api.QualityGate;
using Cena.Admin.Api.Questions;
using Cena.Admin.Api;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cena.Admin.Api.Tests.Questions;

public sealed class CorpusExpanderHandlerTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IQuerySession _querySession = Substitute.For<IQuerySession>();
    private readonly IDocumentSession _writeSession = Substitute.For<IDocumentSession>();
    private readonly ICorpusSourceProvider _sources = Substitute.For<ICorpusSourceProvider>();
    private readonly IContentCoverageService _coverage = Substitute.For<IContentCoverageService>();
    private readonly IAiGenerationService _ai = Substitute.For<IAiGenerationService>();
    private readonly IQualityGateService _quality = Substitute.For<IQualityGateService>();

    public CorpusExpanderHandlerTests()
    {
        _store.QuerySession().Returns(_querySession);
        _store.LightweightSession().Returns(_writeSession);
    }

    private static QuestionReadModel Q(string id, string conceptId = "CAL-003", float difficulty = 0.6f) =>
        new()
        {
            Id = id,
            Subject = "math",
            Topic = "derivatives",
            Grade = "5 Units",
            BloomsLevel = 3,
            Difficulty = difficulty,
            Language = "en",
            Concepts = new List<string> { conceptId },
            Status = "Published",
            StemPreview = $"stem-{id}",
        };

    private static ContentCoverageReport Coverage(
        string leafId = "math_5u.calculus.derivatives",
        string conceptId = "CAL-003",
        int currentCount = 0) =>
        new(
            SchemaVersion:          "test",
            TaxonomyVersion:        "test",
            GeneratedAt:            DateTimeOffset.UtcNow,
            MinItemsPerLeaf:        1,
            OverallCoveragePercent: 0,
            Tracks: new List<TrackCoverage>
            {
                new(
                    TrackId:         "math_5u",
                    TrackName:       "Mathematics 5-Unit",
                    TotalLeaves:     1,
                    PopulatedLeaves: 0,
                    CoveragePercent: 0,
                    Leaves: new List<LeafCoverage>
                    {
                        new(
                            LeafId:            leafId,
                            ConceptId:         conceptId,
                            SubtopicKey:       "derivatives",
                            TopicKey:          "calculus",
                            QuestionCount:     currentCount,
                            AverageDifficulty: 0.5,
                            BloomHistogram:    new Dictionary<int, int>(),
                            PublishedCount:    currentCount,
                            DraftCount:        0),
                    }),
            },
            Gaps: Array.Empty<TaxonomyGap>());

    private void SeedSources(params QuestionReadModel[] list) =>
        _sources.ResolveAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(list);

    private void SeedCoverage(ContentCoverageReport report) =>
        _coverage.BuildReportAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(report);

    private void SeedAi(BatchGenerateResponse response) =>
        _ai.BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(response));

    private static BatchGenerateResponse AiResponse(
        int total = 3, int passed = 3, int dropped = 0) =>
        new(Success: true,
            Results: Array.Empty<BatchGenerateResult>(),
            TotalGenerated: total,
            PassedQualityGate: passed,
            NeedsReview: 0,
            AutoRejected: 0,
            ModelUsed: "claude-sonnet-4.5",
            Error: null,
            DroppedForCasFailure: dropped);

    // ------------------------------------------------------------------
    [Fact]
    public async Task DryRun_Returns_Plan_Without_LLM_Calls()
    {
        SeedSources(Q("q1"), Q("q2"));
        SeedCoverage(Coverage(currentCount: 0));

        var result = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[] { new DifficultyBandConfig(0.3f, 0.5f, 2) },
                DryRun: true),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        Assert.True(result.DryRun);
        Assert.Equal(2, result.Plan.Count);
        Assert.All(result.Plan, p => Assert.Null(p.SkipReason));
        Assert.Null(result.Outcomes);
        Assert.Equal(0, result.TotalAttempted);
        await _ai.DidNotReceive().BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>());
    }

    [Fact]
    public async Task Wet_Run_Invokes_BatchGenerate_Per_Source_Per_Band()
    {
        SeedSources(Q("q1"), Q("q2"));
        SeedCoverage(Coverage(currentCount: 0));
        SeedAi(AiResponse(total: 2, passed: 2));
        _querySession.LoadAsync<QuestionReadModel>("q1", Arg.Any<CancellationToken>()).Returns(Q("q1"));
        _querySession.LoadAsync<QuestionReadModel>("q2", Arg.Any<CancellationToken>()).Returns(Q("q2"));

        var result = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[]
                {
                    new DifficultyBandConfig(0.3f, 0.5f, 2),
                    new DifficultyBandConfig(0.6f, 0.8f, 2),
                },
                DryRun: false),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        Assert.False(result.DryRun);
        Assert.NotNull(result.Outcomes);
        Assert.Equal(2, result.Outcomes!.Count);

        // 2 sources × 2 bands = 4 BatchGenerateAsync calls
        await _ai.Received(4).BatchGenerateAsync(
            Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>());
    }

    [Fact]
    public async Task Planner_Skips_Sources_In_Full_Leaves()
    {
        SeedSources(Q("q1"), Q("q2"));
        // Leaf already at 5 items, stopAfterLeafFull default = 5 → skip both.
        SeedCoverage(Coverage(currentCount: 5));

        var result = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "concept:CAL-003",
                DifficultyBands: new[] { new DifficultyBandConfig(0.4f, 0.6f, 3) },
                StopAfterLeafFull: 5,
                DryRun: true),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        Assert.All(result.Plan, p =>
        {
            Assert.Equal(0, p.WouldGenerate);
            Assert.StartsWith("leaf_full:", p.SkipReason);
        });
        Assert.Equal(0, result.TotalPlannedCandidates);
    }

    [Fact]
    public async Task Planner_Budget_Exhausted_After_MaxTotal_Reached()
    {
        SeedSources(Q("q1"), Q("q2"), Q("q3"), Q("q4"));
        SeedCoverage(Coverage(currentCount: 0));

        var result = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "all",
                DifficultyBands: new[] { new DifficultyBandConfig(0.4f, 0.6f, 4) },  // 4/source
                MaxTotalCandidates: 9,   // allows 2 full sources (8 candidates), 3rd exhausts
                DryRun: true),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        var scheduled = result.Plan.Count(p => p.WouldGenerate > 0);
        var skipped = result.Plan.Count(p => p.SkipReason == "budget_exhausted");

        Assert.Equal(2, scheduled);
        Assert.Equal(2, skipped);
        Assert.Equal(8, result.TotalPlannedCandidates);
    }

    [Fact]
    public async Task Error_In_One_Source_Does_Not_Abort_Run()
    {
        SeedSources(Q("q1"), Q("q2"), Q("q3"));
        SeedCoverage(Coverage(currentCount: 0));

        _querySession.LoadAsync<QuestionReadModel>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Q(ci.Arg<string>()));

        // q2's batch fails; q1 + q3 succeed. Using a counter since NSubstitute
        // can't mix a direct-value return with a throwing lambda in one
        // Returns(...) call.
        var callCount = 0;
        _ai.BatchGenerateAsync(
            Arg.Any<BatchGenerateRequest>(),
            Arg.Any<IQualityGateService>())
            .Returns(ci =>
            {
                callCount++;
                if (callCount == 2) throw new InvalidOperationException("q2 boom");
                return Task.FromResult(AiResponse(total: 3, passed: 3));
            });

        var result = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[] { new DifficultyBandConfig(0.4f, 0.6f, 3) },
                DryRun: false),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        Assert.NotNull(result.Outcomes);
        Assert.Equal(3, result.Outcomes!.Count);
        // Two successes + one error row — exact ordering depends on source
        // iteration so just count.
        Assert.Equal(2, result.Outcomes.Count(o => o.Error is null && o.Attempted > 0));
        Assert.Equal(1, result.Outcomes.Count(o => o.Error is not null));
    }

    [Fact]
    public async Task Aggregates_Attempted_Passed_And_Dropped_Across_Sources()
    {
        SeedSources(Q("q1"), Q("q2"));
        SeedCoverage(Coverage(currentCount: 0));

        _querySession.LoadAsync<QuestionReadModel>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Q(ci.Arg<string>()));

        SeedAi(AiResponse(total: 5, passed: 3, dropped: 2));

        var result = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[] { new DifficultyBandConfig(0.4f, 0.6f, 5) },
                DryRun: false),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        // 2 sources × 5 attempted = 10; 2 × 3 passed = 6; 2 × 2 dropped = 4
        Assert.Equal(10, result.TotalAttempted);
        Assert.Equal(6, result.TotalPassedCas);
        Assert.Equal(4, result.TotalDropped);
    }

    [Fact]
    public async Task Empty_DifficultyBands_Throws_Validation()
    {
        SeedSources(Q("q1"));
        SeedCoverage(Coverage());

        await Assert.ThrowsAsync<ArgumentException>(() => CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: Array.Empty<DifficultyBandConfig>()),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance));
    }

    [Fact]
    public async Task DifficultyBand_OutOfRange_Throws()
    {
        SeedSources(Q("q1"));
        SeedCoverage(Coverage());

        await Assert.ThrowsAsync<ArgumentException>(() => CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[] { new DifficultyBandConfig(-0.1f, 0.5f, 3) }),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance));
    }

    [Fact]
    public async Task DifficultyBand_Count_Zero_Throws()
    {
        SeedSources(Q("q1"));
        SeedCoverage(Coverage());

        await Assert.ThrowsAsync<ArgumentException>(() => CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[] { new DifficultyBandConfig(0.3f, 0.5f, 0) }),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance));
    }

    [Fact]
    public async Task MaxTotalCandidates_Zero_Throws()
    {
        SeedSources(Q("q1"));
        SeedCoverage(Coverage());

        await Assert.ThrowsAsync<ArgumentException>(() => CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[] { new DifficultyBandConfig(0.3f, 0.5f, 2) },
                MaxTotalCandidates: 0),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance));
    }

    [Fact]
    public async Task Empty_Selector_Returns_Empty_Plan()
    {
        SeedSources(/* none */);
        SeedCoverage(Coverage());

        var result = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest(
                SourceSelector: "seed",
                DifficultyBands: new[] { new DifficultyBandConfig(0.3f, 0.5f, 2) },
                DryRun: true),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        Assert.Empty(result.Plan);
        Assert.Equal(0, result.TotalPlannedCandidates);
    }

    [Fact]
    public async Task RunId_Is_Unique_Per_Invocation()
    {
        SeedSources(Q("q1"));
        SeedCoverage(Coverage());

        var a = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest("seed", new[] { new DifficultyBandConfig(0.3f, 0.5f, 2) }, DryRun: true),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);
        var b = await CorpusExpanderHandler.RunAsync(
            new CorpusExpansionRequest("seed", new[] { new DifficultyBandConfig(0.3f, 0.5f, 2) }, DryRun: true),
            _sources, _coverage, _store, _ai, _quality, "admin-1", NullLogger.Instance);

        Assert.NotEqual(a.RunId, b.RunId);
        Assert.StartsWith("run-", a.RunId);
    }
}
