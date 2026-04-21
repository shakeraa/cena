// =============================================================================
// Cena Platform — CoverageHeatmapService behaviour tests (prr-209)
//
// Covers the task's integration-test matrix:
//   (a) cell below target  → red / dots
//   (b) cell at target     → green / solid
//   (c) cell over target   → amber / diagonal  (over-coverage = wasted effort)
//   (d) cross-tenant fails → ForbiddenException (403)
//   (e) unknown track      → BuildHeatmap returns null (endpoint → 404)
//
// Plus:
//   - drilldown returns 404 (null) for an untracked cell
//   - drilldown surfaces curator-queued count + per-variant summaries
//   - summary counters roll up deficit / surplus per status bucket
// =============================================================================

using System.Security.Claims;
using Cena.Actors.QuestionBank.Coverage;
using Cena.Actors.QuestionBank.Templates;
using Cena.Admin.Api.Coverage;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api.Tests.Coverage;

public sealed class CoverageHeatmapServiceTests
{
    // ── Fixtures ───────────────────────────────────────────────────────

    private const string DeclaredYaml = """
        version: 1
        defaults:
          global:
            min: 5
          methodology:
            Halabi: 5
            Rabinovitch: 5
          questionType:
            multiple-choice: 10
            step-solver: 5
        cells:
          - topic: algebra.linear_equations
            difficulty: Easy
            methodology: Halabi
            track: FourUnit
            questionType: multiple-choice
            min: 10
            active: true
            notes: "At-target cell under test."
          - topic: algebra.linear_equations
            difficulty: Medium
            methodology: Halabi
            track: FourUnit
            questionType: multiple-choice
            min: 10
            active: true
          - topic: algebra.linear_equations
            difficulty: Hard
            methodology: Halabi
            track: FourUnit
            questionType: multiple-choice
            min: 10
            active: true
          - topic: algebra.quadratic_equations
            difficulty: Medium
            methodology: Halabi
            track: FiveUnit
            questionType: multiple-choice
            min: 10
            active: true
        """;

    private const string Institute = "inst-haifa-01";
    private const string OtherInstitute = "inst-nazareth-01";

    private static CoverageCell Cell(
        string topic = "algebra.linear_equations",
        TemplateDifficulty difficulty = TemplateDifficulty.Easy,
        TemplateMethodology methodology = TemplateMethodology.Halabi,
        TemplateTrack track = TemplateTrack.FourUnit,
        string questionType = "multiple-choice",
        string language = "en",
        string subject = "math") => new()
    {
        Track = track,
        Subject = subject,
        Topic = topic,
        Difficulty = difficulty,
        Methodology = methodology,
        QuestionType = questionType,
        Language = language,
    };

    private static (CoverageHeatmapService svc, CoverageCellVariantCounter counter, RecordingDrilldownSource drilldown)
        BuildService()
    {
        var counter = new CoverageCellVariantCounter();
        var manifest = CoverageTargetManifest.Parse(DeclaredYaml);
        var provider = new InMemoryCoverageTargetManifestProvider(manifest);
        var drilldown = new RecordingDrilldownSource();
        var svc = new CoverageHeatmapService(counter, provider, drilldown);
        return (svc, counter, drilldown);
    }

    private static ClaimsPrincipal Admin(string instituteId) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Role, "ADMIN"),
        new Claim("institute_id", instituteId),
        new Claim("school_id", instituteId),
    }, "test"));

    private static ClaimsPrincipal SuperAdmin() => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Role, "SUPER_ADMIN"),
    }, "test"));

    // ── (a) below target → red / dots ─────────────────────────────────

    [Fact]
    public void BuildHeatmap_CellBelowTarget_YieldsRedDots()
    {
        var (svc, counter, _) = BuildService();
        counter.Record(Cell(difficulty: TemplateDifficulty.Easy), 3); // target 10
        counter.MarkBelowSlo(Cell(difficulty: TemplateDifficulty.Easy), true);

        var heatmap = svc.BuildHeatmap("FourUnit", Institute, Admin(Institute));
        Assert.NotNull(heatmap);

        var cell = heatmap!.Cells.Single(c =>
            c.Difficulty == "Easy" && c.Methodology == "Halabi");
        Assert.Equal(3, cell.VariantCount);
        Assert.Equal(10, cell.RequiredCount);
        Assert.Equal(7, cell.Deficit);
        Assert.Equal(0, cell.Surplus);
        Assert.Equal(CoverageHeatmapStatus.Red, cell.Status);
        Assert.Equal(CoverageHeatmapPatternKey.Dots, cell.PatternKey);
        Assert.True(cell.BelowSlo);
    }

    // ── (b) at target → green / solid ──────────────────────────────────

    [Fact]
    public void BuildHeatmap_CellAtTarget_YieldsGreenSolid()
    {
        var (svc, counter, _) = BuildService();
        counter.Record(Cell(difficulty: TemplateDifficulty.Medium), 10); // target 10

        var heatmap = svc.BuildHeatmap("FourUnit", Institute, Admin(Institute));

        var cell = heatmap!.Cells.Single(c =>
            c.Difficulty == "Medium" && c.Methodology == "Halabi");
        Assert.Equal(10, cell.VariantCount);
        Assert.Equal(10, cell.RequiredCount);
        Assert.Equal(0, cell.Deficit);
        Assert.Equal(0, cell.Surplus);
        Assert.Equal(CoverageHeatmapStatus.Green, cell.Status);
        Assert.Equal(CoverageHeatmapPatternKey.Solid, cell.PatternKey);
    }

    // ── (c) over target → amber / diagonal ─────────────────────────────

    [Fact]
    public void BuildHeatmap_CellOverTarget_YieldsAmberDiagonal()
    {
        var (svc, counter, _) = BuildService();
        counter.Record(Cell(difficulty: TemplateDifficulty.Hard), 15); // target 10

        var heatmap = svc.BuildHeatmap("FourUnit", Institute, Admin(Institute));

        var cell = heatmap!.Cells.Single(c =>
            c.Difficulty == "Hard" && c.Methodology == "Halabi");
        Assert.Equal(15, cell.VariantCount);
        Assert.Equal(10, cell.RequiredCount);
        Assert.Equal(0, cell.Deficit);
        Assert.Equal(5, cell.Surplus);
        Assert.Equal(CoverageHeatmapStatus.Amber, cell.Status);
        Assert.Equal(CoverageHeatmapPatternKey.Diagonal, cell.PatternKey);
    }

    // ── (d) cross-tenant fails ─────────────────────────────────────────

    [Fact]
    public void BuildHeatmap_CrossTenant_Throws403()
    {
        var (svc, _, _) = BuildService();
        var user = Admin(Institute);

        var ex = Assert.Throws<ForbiddenException>(() =>
            svc.BuildHeatmap("FourUnit", OtherInstitute, user));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public void BuildHeatmap_SuperAdminCrossInstitute_Allowed()
    {
        var (svc, counter, _) = BuildService();
        counter.Record(Cell(difficulty: TemplateDifficulty.Easy), 10);

        // SUPER_ADMIN can query any institute — returns the same snapshot
        // regardless of the institute param (Phase 1 projection is global).
        var heatmap = svc.BuildHeatmap("FourUnit", OtherInstitute, SuperAdmin());
        Assert.NotNull(heatmap);
        Assert.Equal(OtherInstitute, heatmap!.InstituteId);
    }

    [Fact]
    public void BuildHeatmap_UserWithoutInstituteClaim_Throws403()
    {
        var (svc, _, _) = BuildService();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "ADMIN"),
            // No institute_id claim!
        }, "test"));

        var ex = Assert.Throws<ForbiddenException>(() =>
            svc.BuildHeatmap("FourUnit", Institute, user));

        Assert.Equal(ErrorCodes.CENA_AUTH_INSUFFICIENT_ROLE, ex.ErrorCode);
    }

    // ── (e) unknown track → null → endpoint 404 ────────────────────────

    [Fact]
    public void BuildHeatmap_UnknownTrack_ReturnsNull()
    {
        var (svc, _, _) = BuildService();
        var result = svc.BuildHeatmap("Martian8Unit", Institute, Admin(Institute));
        Assert.Null(result);
    }

    // ── Track filter filters live cells ────────────────────────────────

    [Fact]
    public void BuildHeatmap_FiltersByTrack()
    {
        var (svc, counter, _) = BuildService();
        counter.Record(Cell(track: TemplateTrack.FourUnit), 10);
        counter.Record(Cell(
            topic: "algebra.quadratic_equations",
            difficulty: TemplateDifficulty.Medium,
            track: TemplateTrack.FiveUnit), 10);

        var four = svc.BuildHeatmap("FourUnit", Institute, Admin(Institute));
        Assert.NotNull(four);
        Assert.All(four!.Cells, c => Assert.Equal("FourUnit", c.Track));

        var five = svc.BuildHeatmap("FiveUnit", Institute, Admin(Institute));
        Assert.NotNull(five);
        Assert.All(five!.Cells, c => Assert.Equal("FiveUnit", c.Track));
    }

    // ── Declared-but-empty cells still appear as 0 variants ────────────

    [Fact]
    public void BuildHeatmap_DeclaredCellsWithNoLiveCount_Appear_AsRed()
    {
        var (svc, counter, _) = BuildService();
        // Only record one of the three declared FourUnit cells.
        counter.Record(Cell(difficulty: TemplateDifficulty.Easy), 10);

        var heatmap = svc.BuildHeatmap("FourUnit", Institute, Admin(Institute));

        Assert.NotNull(heatmap);
        Assert.Equal(3, heatmap!.Cells.Count);

        var missingMedium = heatmap.Cells.Single(c =>
            c.Difficulty == "Medium" && c.Methodology == "Halabi");
        Assert.Equal(0, missingMedium.VariantCount);
        Assert.Equal(CoverageHeatmapStatus.Red, missingMedium.Status);
        Assert.Equal(CoverageHeatmapPatternKey.Dots, missingMedium.PatternKey);
        Assert.True(missingMedium.Declared);
        Assert.True(missingMedium.BelowSlo);
    }

    // ── Undeclared-but-tracked cell surfaces as declared=false ────────

    [Fact]
    public void BuildHeatmap_UndeclaredLiveCell_AppearsWithDeclaredFalse()
    {
        var (svc, counter, _) = BuildService();
        // A cell the manifest knows nothing about, but is being tracked.
        counter.Record(Cell(
            topic: "algebra.inequalities",
            difficulty: TemplateDifficulty.Easy,
            methodology: TemplateMethodology.Rabinovitch,
            track: TemplateTrack.FourUnit), 4);

        var heatmap = svc.BuildHeatmap("FourUnit", Institute, Admin(Institute));
        var stray = heatmap!.Cells.Single(c => c.Topic == "algebra.inequalities");
        Assert.False(stray.Declared);
        Assert.False(stray.Active);
    }

    // ── Summary roll-up ────────────────────────────────────────────────

    [Fact]
    public void BuildHeatmap_SummaryRollsUpStatusBuckets()
    {
        var (svc, counter, _) = BuildService();
        counter.Record(Cell(difficulty: TemplateDifficulty.Easy), 3);      // red
        counter.Record(Cell(difficulty: TemplateDifficulty.Medium), 10);   // green
        counter.Record(Cell(difficulty: TemplateDifficulty.Hard), 15);     // amber

        var heatmap = svc.BuildHeatmap("FourUnit", Institute, Admin(Institute));
        var s = heatmap!.Summary;

        Assert.Equal(3, s.TotalCells);
        Assert.Equal(1, s.GreenCount);
        Assert.Equal(1, s.AmberCount);
        Assert.Equal(1, s.RedCount);
        Assert.Equal(7, s.DeficitTotal);  // easy 10 - 3 = 7
        Assert.Equal(5, s.SurplusTotal);  // hard 15 - 10 = 5
    }

    // ── Drilldown ──────────────────────────────────────────────────────

    [Fact]
    public void BuildRungDrilldown_AtTarget_ReturnsGreenSolidAndVariants()
    {
        var (svc, counter, drilldown) = BuildService();
        var live = Cell(difficulty: TemplateDifficulty.Medium);
        counter.Record(live, 10);

        drilldown.Queued[CoverageTargetManifest.MatchKeyFor(live)] = 2;
        drilldown.Variants[CoverageTargetManifest.MatchKeyFor(live)] = new[]
        {
            new CoverageRungVariantSummary("v1", "t_med", 1, DateTimeOffset.UtcNow),
            new CoverageRungVariantSummary("v2", "t_med", 1, DateTimeOffset.UtcNow),
        };

        var rung = svc.BuildRungDrilldown(
            topic: "algebra.linear_equations",
            difficulty: "Medium",
            methodology: "Halabi",
            track: "FourUnit",
            questionType: "multiple-choice",
            language: "en",
            instituteId: Institute,
            user: Admin(Institute));

        Assert.NotNull(rung);
        Assert.Equal(CoverageHeatmapStatus.Green, rung!.Cell.Status);
        Assert.Equal(CoverageHeatmapPatternKey.Solid, rung.Cell.PatternKey);
        Assert.Equal(2, rung.CuratorQueuedCount);
        Assert.Equal(2, rung.Variants.Count);
    }

    [Fact]
    public void BuildRungDrilldown_UntrackedCell_ReturnsNull()
    {
        var (svc, _, _) = BuildService();

        var rung = svc.BuildRungDrilldown(
            topic: "algebra.transcendental",
            difficulty: "Easy",
            methodology: "Halabi",
            track: "FourUnit",
            questionType: "multiple-choice",
            language: "en",
            instituteId: Institute,
            user: Admin(Institute));

        Assert.Null(rung);
    }

    [Fact]
    public void BuildRungDrilldown_CrossTenant_Throws403()
    {
        var (svc, counter, _) = BuildService();
        counter.Record(Cell(), 10); // Make the cell tracked so tenant check runs.

        Assert.Throws<ForbiddenException>(() => svc.BuildRungDrilldown(
            topic: "algebra.linear_equations",
            difficulty: "Easy",
            methodology: "Halabi",
            track: "FourUnit",
            questionType: "multiple-choice",
            language: "en",
            instituteId: OtherInstitute,
            user: Admin(Institute)));
    }

    [Fact]
    public void BuildRungDrilldown_UnknownTrack_ReturnsNull()
    {
        var (svc, _, _) = BuildService();
        var rung = svc.BuildRungDrilldown(
            topic: "algebra.linear_equations",
            difficulty: "Easy",
            methodology: "Halabi",
            track: "Martian8Unit",
            questionType: "multiple-choice",
            language: "en",
            instituteId: Institute,
            user: Admin(Institute));
        Assert.Null(rung);
    }

    // ── DeriveStatusAndPattern is pure; lock the mapping ──────────────

    [Theory]
    [InlineData(0, 10, "red", "dots")]
    [InlineData(5, 10, "red", "dots")]
    [InlineData(9, 10, "red", "dots")]
    [InlineData(10, 10, "green", "solid")]
    [InlineData(11, 10, "amber", "diagonal")]
    [InlineData(100, 10, "amber", "diagonal")]
    public void DeriveStatusAndPattern_LocksMapping(
        int variantCount, int required, string expectedStatus, string expectedPattern)
    {
        var (status, pattern) = CoverageHeatmapService.DeriveStatusAndPattern(variantCount, required);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedPattern, pattern);
    }

    // ── Recording fake for drilldown source ───────────────────────────

    private sealed class RecordingDrilldownSource : ICoverageRungDrilldownSource
    {
        public Dictionary<string, int> Queued { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<CoverageRungVariantSummary>> Variants { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<CoverageRungVariantSummary> GetVariants(CoverageCell cell)
        {
            var key = CoverageTargetManifest.MatchKeyFor(cell);
            return Variants.TryGetValue(key, out var list)
                ? list
                : Array.Empty<CoverageRungVariantSummary>();
        }

        public int GetCuratorQueuedCount(CoverageCell cell)
        {
            var key = CoverageTargetManifest.MatchKeyFor(cell);
            return Queued.TryGetValue(key, out var n) ? n : 0;
        }
    }
}
