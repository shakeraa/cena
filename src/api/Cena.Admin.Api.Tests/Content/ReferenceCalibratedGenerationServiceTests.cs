// =============================================================================
// Cena Platform — ReferenceCalibratedGenerationService tests (RDY-019b, Phase 3.2)
//
// Covers:
//   • missing analysis.json → FileNotFoundException (no silent fallback)
//   • dry-run returns plan with correct cluster bundles + budget halt
//     WITHOUT calling IAiGenerationService
//   • wet-run drives BatchGenerateAsync per cluster with correct
//     (subject, topic, grade, bloom, difficulty, language) wire format
//   • per-cluster error isolation (one cluster throws → other clusters still run)
//   • MaxTotalCandidates budget clamp produces partial + skip reasons
//   • Language override flows into the batch request
//   • Track filter respects TargetTracks ordering
//   • Topic filter respects TargetTopics allow-list
//   • Budget exhaustion yields budget_exhausted skip reason
//   • Bloom inference: proof=5, computation=3, multiple_choice=2
//   • Event appended once per run (best-effort) via IDocumentStore
//
// IAiGenerationService + IDocumentStore + IQualityGateService are NSubstitute
// fakes; the service's own logic (planner + analysis parser) runs for real.
// =============================================================================

using Cena.Admin.Api.Content;
using Cena.Admin.Api.QualityGate;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cena.Admin.Api.Tests.Content;

public sealed class ReferenceCalibratedGenerationServiceTests
{
    private readonly IAiGenerationService _ai = Substitute.For<IAiGenerationService>();
    private readonly IQualityGateService _quality = Substitute.For<IQualityGateService>();
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _writeSession = Substitute.For<IDocumentSession>();

    public ReferenceCalibratedGenerationServiceTests()
    {
        _store.LightweightSession().Returns(_writeSession);
    }

    private ReferenceCalibratedGenerationService CreateSut() =>
        new(_ai, _quality, _store,
            NullLogger<ReferenceCalibratedGenerationService>.Instance);

    private static BatchGenerateResponse AiResponse(int total = 3, int passed = 3, int dropped = 0) =>
        new(Success: true,
            Results: Array.Empty<BatchGenerateResult>(),
            TotalGenerated: total,
            PassedQualityGate: passed,
            NeedsReview: 0,
            AutoRejected: 0,
            ModelUsed: "claude-sonnet-4.5",
            Error: null,
            DroppedForCasFailure: dropped);

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Content", "fixtures", "analysis-seed.json");

    // ── Fixture presence sanity check ────────────────────────────────────

    [Fact]
    public void Fixture_Exists_AtExpectedPath()
    {
        // If this fails, check the .csproj copies Content/fixtures/*.json.
        Assert.True(File.Exists(FixturePath),
            $"Seed fixture not found at {FixturePath}");
    }

    // ── Validation errors ────────────────────────────────────────────────

    [Fact]
    public async Task RecreateAsync_MissingAnalysis_ThrowsFileNotFoundException()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: "/tmp/does-not-exist-analysis.json",
            DryRun: true);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            sut.RecreateAsync(request, "test-super-admin"));
    }

    [Fact]
    public async Task RecreateAsync_NullStartedBy_Throws()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(AnalysisJsonPath: FixturePath, DryRun: true);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.RecreateAsync(request, ""));
    }

    // ── Dry-run returns plan, never calls AI ─────────────────────────────

    [Fact]
    public async Task RecreateAsync_DryRun_ReturnsPlanAndDoesNotCallAi()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 2,
            MaxTotalCandidates: 200,
            DryRun: true);

        var response = await sut.RecreateAsync(request, "admin");

        Assert.True(response.DryRun);
        Assert.Null(response.Outcomes);
        // 4u: 2 topics × 2 formats = 4 clusters
        // 5u: 2 topics × 2 formats = 4 clusters
        // Total: 8 clusters × 2 candidates each = 16 planned
        Assert.Equal(8, response.Plan.Count);
        Assert.Equal(16, response.TotalPlannedCandidates);
        Assert.Equal(6, response.PapersAnalyzed);

        await _ai.DidNotReceiveWithAnyArgs()
            .BatchGenerateAsync(default!, default!);
    }

    // ── Wet-run drives BatchGenerateAsync per cluster ────────────────────

    [Fact]
    public async Task RecreateAsync_WetRun_CallsAiPerClusterWithCorrectParameters()
    {
        _ai.BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(AiResponse(total: 2, passed: 2)));

        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 2,
            MaxTotalCandidates: 200,
            Language: "en",
            DryRun: false);

        var response = await sut.RecreateAsync(request, "admin");

        Assert.False(response.DryRun);
        Assert.NotNull(response.Outcomes);
        Assert.Equal(8, response.Outcomes!.Count);
        Assert.Equal(16, response.TotalAttempted);
        Assert.Equal(16, response.TotalPassedCas);

        // Verify 4u clusters used the 0.5–0.7 difficulty band and 5u
        // clusters used the 0.7–0.9 band.
        await _ai.Received().BatchGenerateAsync(
            Arg.Is<BatchGenerateRequest>(r =>
                r.Subject == "math"
                && r.Grade == "12"
                && r.Language == "en"
                && r.MinDifficulty >= 0.49f && r.MinDifficulty <= 0.51f),
            _quality);
        await _ai.Received().BatchGenerateAsync(
            Arg.Is<BatchGenerateRequest>(r =>
                r.MinDifficulty >= 0.69f && r.MinDifficulty <= 0.71f),
            _quality);
    }

    // ── Per-cluster error isolation ──────────────────────────────────────

    [Fact]
    public async Task RecreateAsync_WetRun_OneClusterThrows_OthersStillRun()
    {
        var callCount = 0;
        _ai.BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>())
            .Returns(ci =>
            {
                callCount++;
                if (callCount == 2) throw new InvalidOperationException("cluster 2 boom");
                return Task.FromResult(AiResponse(total: 2, passed: 2));
            });

        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 2,
            MaxTotalCandidates: 200,
            DryRun: false);

        var response = await sut.RecreateAsync(request, "admin");

        Assert.NotNull(response.Outcomes);
        // One cluster has an error populated.
        Assert.Contains(response.Outcomes!, o => o.Error != null);
        // Remaining 7 clusters ran (2 attempted each) → 14 attempted total.
        Assert.True(response.TotalAttempted >= 14);
    }

    // ── Budget clamp + exhaustion ────────────────────────────────────────

    [Fact]
    public async Task RecreateAsync_BudgetSmallerThanPlan_ClampsLastClusters()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 3,
            MaxTotalCandidates: 7,      // hard budget
            DryRun: true);

        var response = await sut.RecreateAsync(request, "admin");

        // Running count: 3 + 3 + (1, clamped) + 0 + 0 + … = 7 exactly.
        Assert.Equal(7, response.TotalPlannedCandidates);
        Assert.Contains(response.Plan, p => p.SkipReason is not null && p.SkipReason.StartsWith("budget"));
    }

    [Fact]
    public async Task RecreateAsync_ZeroRemainingBudget_EmitsBudgetExhausted()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 3,
            MaxTotalCandidates: 6,      // exactly 2 clusters fit
            DryRun: true);

        var response = await sut.RecreateAsync(request, "admin");

        Assert.Equal(6, response.TotalPlannedCandidates);
        var exhausted = response.Plan.Where(p => p.SkipReason == "budget_exhausted").ToList();
        Assert.NotEmpty(exhausted);
    }

    // ── TargetTracks / TargetTopics filtering ────────────────────────────

    [Fact]
    public async Task RecreateAsync_TargetTracks_FiltersTrackSelection()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            TargetTracks: new[] { "5u" },
            MaxCandidatesPerCluster: 2,
            MaxTotalCandidates: 200,
            DryRun: true);

        var response = await sut.RecreateAsync(request, "admin");

        // All clusters must belong to 5u.
        Assert.All(response.Plan, p => Assert.Equal("5u", p.Track));
        // 5u has 2 topics × 2 formats = 4 clusters.
        Assert.Equal(4, response.Plan.Count);
    }

    [Fact]
    public async Task RecreateAsync_TargetTopics_FiltersTopicSelection()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            TargetTopics: new[] { "calculus.derivatives" },
            MaxCandidatesPerCluster: 2,
            MaxTotalCandidates: 200,
            DryRun: true);

        var response = await sut.RecreateAsync(request, "admin");

        // Only the derivatives topic survives (appears only in 4u).
        Assert.All(response.Plan, p => Assert.Equal("calculus.derivatives", p.Topic));
    }

    // ── Language override ────────────────────────────────────────────────

    [Fact]
    public async Task RecreateAsync_LanguageOverride_FlowsIntoBatchRequest()
    {
        _ai.BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(AiResponse()));

        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 1,
            MaxTotalCandidates: 200,
            Language: "he",
            DryRun: false);

        await sut.RecreateAsync(request, "admin");

        await _ai.Received().BatchGenerateAsync(
            Arg.Is<BatchGenerateRequest>(r => r.Language == "he"),
            _quality);
    }

    // ── Bloom inference ──────────────────────────────────────────────────

    [Theory]
    [InlineData("proof", 5)]
    [InlineData("computation", 3)]
    [InlineData("multiple_choice", 2)]
    [InlineData("unknown_fmt", 2)]
    public void BloomForFormat_ReturnsExpected(string format, int expectedBloom)
    {
        Assert.Equal(expectedBloom, ReferenceCalibratedGenerationService.BloomForFormat(format));
    }

    // ── Difficulty bands per track ───────────────────────────────────────

    [Theory]
    [InlineData("3u", 0.3f, 0.5f)]
    [InlineData("4u", 0.5f, 0.7f)]
    [InlineData("5u", 0.7f, 0.9f)]
    [InlineData("unknown", 0.4f, 0.6f)]
    public void DifficultyBands_PerTrack_AreCanonical(string track, float expectedMin, float expectedMax)
    {
        Assert.Equal(expectedMin, ReferenceCalibratedGenerationService.DifficultyFloorForTrack(track));
        Assert.Equal(expectedMax, ReferenceCalibratedGenerationService.DifficultyCeilingForTrack(track));
    }

    // ── Event append: best-effort (failure must not bubble) ─────────────

    [Fact]
    public async Task RecreateAsync_DryRun_AppendsRunEventBestEffort()
    {
        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 1,
            MaxTotalCandidates: 200,
            DryRun: true);

        var response = await sut.RecreateAsync(request, "admin");

        Assert.NotNull(response);
        _store.Received().LightweightSession();
        await _writeSession.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecreateAsync_EventAppendFailure_IsSwallowed()
    {
        _writeSession.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("marten down"));

        var sut = CreateSut();
        var request = new ReferenceRecreationRequest(
            AnalysisJsonPath: FixturePath,
            MaxCandidatesPerCluster: 1,
            MaxTotalCandidates: 200,
            DryRun: true);

        // Must not throw.
        var response = await sut.RecreateAsync(request, "admin");
        Assert.NotNull(response);
    }
}
