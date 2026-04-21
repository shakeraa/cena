// =============================================================================
// Cena Platform — CoverageTargetManifest parser tests (prr-209)
//
// Locks the in-process YAML loader against the live contract file so
// contracts/coverage/coverage-targets.yml stays in lockstep with the
// heatmap endpoint. The parser purposely matches the narrow subset used by
// the shipgate node script; these tests catch drift early.
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;
using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

public sealed class CoverageTargetManifestTests
{
    [Fact]
    public void Parse_FixtureWithDefaultsAndCells_PopulatesEveryField()
    {
        var yaml = """
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
                notes: "Foundational rung."
              - topic: physics.free_body_diagrams
                difficulty: Medium
                methodology: Halabi
                track: FiveUnit
                questionType: fbd-construct
                min: 3
                active: false
            """;

        var m = CoverageTargetManifest.Parse(yaml);

        Assert.Equal(1, m.Version);
        Assert.Equal(5, m.GlobalMin);
        Assert.Equal(5, m.MethodologyDefaults["Halabi"]);
        Assert.Equal(10, m.QuestionTypeDefaults["multiple-choice"]);
        Assert.Equal(2, m.Cells.Count);

        var easy = m.Cells[0];
        Assert.Equal("algebra.linear_equations", easy.Topic);
        Assert.Equal("Easy", easy.Difficulty);
        Assert.Equal("Halabi", easy.Methodology);
        Assert.Equal("FourUnit", easy.Track);
        Assert.Equal("multiple-choice", easy.QuestionType);
        Assert.Equal("en", easy.Language);
        Assert.Equal(10, easy.Min);
        Assert.True(easy.Active);
        Assert.Equal("Foundational rung.", easy.Notes);

        var fbd = m.Cells[1];
        Assert.Equal("physics.free_body_diagrams", fbd.Topic);
        Assert.False(fbd.Active);
    }

    [Fact]
    public void ResolveRequiredN_PrefersExplicitMin()
    {
        var m = CoverageTargetManifest.Parse(BasicYaml(globalMin: 5));
        var cell = new CoverageTargetCell(
            Topic: "algebra.linear_equations",
            Difficulty: "Easy",
            Methodology: "Halabi",
            Track: "FourUnit",
            QuestionType: "multiple-choice",
            Language: "en",
            Min: 42,
            Active: true,
            Notes: null);

        Assert.Equal(42, m.ResolveRequiredN(cell));
    }

    [Fact]
    public void ResolveRequiredN_FallsThroughQuestionType()
    {
        var m = CoverageTargetManifest.Parse(BasicYaml(globalMin: 5));
        var cell = new CoverageTargetCell(
            Topic: "algebra.linear_equations",
            Difficulty: "Easy",
            Methodology: "Halabi",
            Track: "FourUnit",
            QuestionType: "multiple-choice",
            Language: "en",
            Min: null,
            Active: true,
            Notes: null);

        // questionType override (10) beats methodology default (5).
        Assert.Equal(10, m.ResolveRequiredN(cell));
    }

    [Fact]
    public void ResolveRequiredN_FallsThroughMethodologyThenGlobal()
    {
        var m = CoverageTargetManifest.Parse(BasicYaml(globalMin: 7));
        var cell = new CoverageTargetCell(
            Topic: "algebra.linear_equations",
            Difficulty: "Easy",
            Methodology: "Halabi",
            Track: "FourUnit",
            QuestionType: "free-text", // not in questionType defaults
            Language: "en",
            Min: null,
            Active: true,
            Notes: null);

        // methodology default (Halabi=5) beats global (7).
        Assert.Equal(5, m.ResolveRequiredN(cell));
    }

    [Fact]
    public void ResolveRequiredN_FallsToGlobal_WhenNothingMatches()
    {
        var m = CoverageTargetManifest.Parse(
            "version: 1\ndefaults:\n  global:\n    min: 9\ncells: []\n");
        var cell = new CoverageTargetCell(
            Topic: "x", Difficulty: "Easy", Methodology: "Unknown",
            Track: "FourUnit", QuestionType: "unknown", Language: "en",
            Min: null, Active: true, Notes: null);

        Assert.Equal(9, m.ResolveRequiredN(cell));
    }

    [Fact]
    public void MatchKeyFor_CoverageCell_MatchesDeclaredKey()
    {
        var m = CoverageTargetManifest.Parse(BasicYaml(globalMin: 5));
        var declared = m.Cells.Single(c => c.Topic == "algebra.linear_equations"
                                           && c.Difficulty == "Easy"
                                           && c.Methodology == "Halabi");

        var live = new CoverageCell
        {
            Track = TemplateTrack.FourUnit,
            Subject = "math",  // subject deliberately differs — must be ignored
            Topic = "algebra.linear_equations",
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            QuestionType = "multiple-choice",
            Language = "en",
        };

        var key = CoverageTargetManifest.MatchKeyFor(live);
        Assert.Equal(declared.MatchKey, key);

        // FindByMatchKey / FindFor both resolve via the same key.
        Assert.Same(declared, m.FindByMatchKey(key));
        Assert.Same(declared, m.FindFor(live));
    }

    [Fact]
    public void Parse_RejectsZeroGlobalMin()
    {
        var bad = "version: 1\ndefaults:\n  global:\n    min: 0\ncells: []\n";
        var ex = Assert.Throws<InvalidDataException>(() => CoverageTargetManifest.Parse(bad));
        Assert.Contains("global.min", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_HandlesCommentsAndBlankLines()
    {
        var yaml = """
            # top comment
            version: 1

            defaults:
              global:
                min: 5 # inline
              methodology:
                Halabi: 5

            cells:
              # first rung
              - topic: x
                difficulty: Easy
                methodology: Halabi
                track: FourUnit
                questionType: multiple-choice
                active: true
            """;

        var m = CoverageTargetManifest.Parse(yaml);
        Assert.Single(m.Cells);
        Assert.Equal("x", m.Cells[0].Topic);
    }

    [Fact]
    public void LivesContract_Parses_WithoutError_AndMatchesSnapshotRows()
    {
        // Lock the parser against the real, committed contract file. If the
        // YAML schema drifts beyond what the parser handles, this test fails
        // before the heatmap endpoint silently drops cells.
        var repoRoot = FindRepoRoot();
        var yamlPath = Path.Combine(repoRoot, "contracts", "coverage", "coverage-targets.yml");
        Assert.True(File.Exists(yamlPath), $"missing contract file at {yamlPath}");

        var m = CoverageTargetManifest.Load(yamlPath);

        Assert.True(m.Version >= 1);
        Assert.True(m.GlobalMin > 0);
        Assert.NotEmpty(m.Cells);

        // At least one active cell so the manifest can gate something.
        Assert.Contains(m.Cells, c => c.Active);

        // Every cell must have all required axes populated.
        foreach (var c in m.Cells)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Topic), $"cell missing topic: {c}");
            Assert.False(string.IsNullOrWhiteSpace(c.Difficulty), $"cell missing difficulty: {c}");
            Assert.False(string.IsNullOrWhiteSpace(c.Methodology), $"cell missing methodology: {c}");
            Assert.False(string.IsNullOrWhiteSpace(c.Track), $"cell missing track: {c}");
            Assert.False(string.IsNullOrWhiteSpace(c.QuestionType), $"cell missing questionType: {c}");
        }
    }

    private static string BasicYaml(int globalMin) => $$"""
        version: 1
        defaults:
          global:
            min: {{globalMin}}
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
        """;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".gitignore"))
                && Directory.Exists(Path.Combine(dir.FullName, "contracts")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("could not find repo root from " + AppContext.BaseDirectory);
    }
}
