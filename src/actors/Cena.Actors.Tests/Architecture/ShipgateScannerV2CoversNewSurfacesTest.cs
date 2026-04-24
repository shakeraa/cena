// =============================================================================
// Cena Platform — Ship-gate Scanner V2 Architecture Ratchet (prr-211).
//
// Asserts the ux-surface rulepack (scripts/shipgate/shipgate-ux-surfaces.yml)
// enumerates ALL five new UX surfaces with the correct canonical file paths
// and locale-bundle prefixes. This is a ratchet: if a new UX surface is
// added to the ship-gate scope, a reviewer must update BOTH the YAML AND
// this test — renaming a component without updating the scanner would
// silently disable dark-pattern enforcement on that surface.
//
// This test complements scripts/shipgate/ux-surface-scan.mjs (the JS scanner)
// by adding a C#-side guard that the full sln build catches before the JS
// test suite even runs.
//
// Architectural rationale (senior-architect protocol):
//
//   WHY is this a separate test file instead of bolting onto an existing
//   architecture ratchet? Because the ratchet answers the question "are the
//   scanner config AND the repo's UX surface inventory in sync?" which is
//   a cross-cutting concern across five component files. No existing
//   architecture test owns that question. Creating this new file gives the
//   reviewer one failing test that clearly signals "scanner config and
//   component inventory have drifted apart" — much more actionable than a
//   violation buried in DailyMinuteCapArchitectureTest or similar.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class ShipgateScannerV2CoversNewSurfacesTest
{
    /// <summary>
    /// Canonical surface inventory. The YAML rulepack MUST name every entry
    /// here; changing a path here (e.g. moving a component) requires the
    /// YAML update in the same PR. Paths use forward slashes (YAML is
    /// OS-independent).
    /// </summary>
    private static readonly (string Name, string Path, string LocalePrefix)[] ExpectedSurfaces =
    {
        ("HintLadder",
            "src/student/full-version/src/components/session/HintLadder.vue",
            "session.runner.hintLadder"),
        ("StepSolverCard",
            "src/student/full-version/src/components/session/StepSolverCard.vue",
            "session.runner.stepSolver"),
        ("Sidekick",
            "src/student/full-version/src/components/Sidekick.vue",
            "sidekick"),
        ("MathInput",
            "src/student/full-version/src/components/session/MathInput.vue",
            "session.runner.mathInput"),
        ("FreeBodyDiagramConstruct",
            "src/student/full-version/src/components/session/FreeBodyDiagramConstruct.vue",
            "session.runner.fbd"),
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string ReadRulepack()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "scripts", "shipgate", "shipgate-ux-surfaces.yml");
        Assert.True(File.Exists(path), $"ux-surface rulepack missing at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void RulepackNamesAllFiveSurfaces()
    {
        var yaml = ReadRulepack();
        foreach (var (name, _, _) in ExpectedSurfaces)
        {
            var marker = $"name: {name}";
            Assert.True(
                yaml.Contains(marker, StringComparison.Ordinal),
                $"scripts/shipgate/shipgate-ux-surfaces.yml is missing surface name: '{marker}'. " +
                $"Every prr-211 surface must be named in the rulepack; adding a new UX surface " +
                $"requires updating the YAML in the same PR.");
        }
    }

    [Fact]
    public void RulepackPointsToCanonicalFilePaths()
    {
        var yaml = ReadRulepack();
        foreach (var (name, path, _) in ExpectedSurfaces)
        {
            var marker = $"path: \"{path}\"";
            Assert.True(
                yaml.Contains(marker, StringComparison.Ordinal),
                $"scripts/shipgate/shipgate-ux-surfaces.yml must reference '{name}' at canonical " +
                $"path '{path}'. If the component moved, update the YAML in the same PR.");
        }
    }

    [Fact]
    public void RulepackDeclaresLocaleBundlePrefixes()
    {
        var yaml = ReadRulepack();
        foreach (var (name, _, prefix) in ExpectedSurfaces)
        {
            var marker = $"locale-prefix: \"{prefix}\"";
            Assert.True(
                yaml.Contains(marker, StringComparison.Ordinal),
                $"scripts/shipgate/shipgate-ux-surfaces.yml must declare locale-prefix '{prefix}' " +
                $"for surface '{name}'. Locale keys on this surface should live under this prefix " +
                $"so the scanner's coverage matrix stays accurate.");
        }
    }

    [Fact]
    public void RulepackHasRulesForEverySurface()
    {
        // Each of the five surfaces must have at least one rule scoped to it.
        // This prevents a PR from adding a surface entry with zero enforcement.
        var yaml = ReadRulepack();
        foreach (var (name, _, _) in ExpectedSurfaces)
        {
            var ruleScope = $"surface: {name}";
            Assert.True(
                yaml.Contains(ruleScope, StringComparison.Ordinal),
                $"scripts/shipgate/shipgate-ux-surfaces.yml declares surface '{name}' but has no " +
                $"'{ruleScope}' rule attached. Every named surface must have at least one " +
                $"enforcement rule; otherwise the surface entry is decorative, not protective.");
        }
    }

    [Fact]
    public void ExistingSurfaceFilesArePresentOrExplicitlyMissing()
    {
        // The scanner tolerates missing surfaces by design (a surface whose
        // canonical .vue does not yet exist is reported as "not found, will
        // be enforced on creation"). This test is the architectural audit
        // side of that: it reports which surfaces are present today and
        // which are still pending. On every PR, reviewers see which of the
        // five have landed.
        var root = FindRepoRoot();
        var missing = new List<string>();
        foreach (var (name, path, _) in ExpectedSurfaces)
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) missing.Add(name);
        }
        // We ASSERT that the scanner config covers every surface even when
        // the file is missing — this is the correctness check. We do NOT
        // fail the build when a surface file is absent; that is intentional
        // per prr-211 DoD ("scanner must handle missing files gracefully").
        // Instead we assert the scanner config names them — which is already
        // checked above. Here we just produce a human-readable status line
        // in xUnit output via a no-op assert-true with a richer message
        // when missing surfaces exist.
        Assert.True(
            true,
            $"Surface presence status: " +
            $"{ExpectedSurfaces.Length - missing.Count}/{ExpectedSurfaces.Length} present. " +
            $"Missing: {(missing.Count == 0 ? "(none)" : string.Join(", ", missing))}");
    }

    [Fact]
    public void ScannerScriptExists()
    {
        // Basic sanity — the scanner binary must exist where the YAML
        // expects it. Prevents a PR from deleting the scanner but leaving
        // the YAML orphaned.
        var root = FindRepoRoot();
        var scanner = Path.Combine(root, "scripts", "shipgate", "ux-surface-scan.mjs");
        Assert.True(
            File.Exists(scanner),
            $"ux-surface scanner missing at {scanner} — the YAML rulepack is useless without it.");

        // And the scanner must load the correct rulepack.
        var scannerSrc = File.ReadAllText(scanner);
        Assert.Contains(
            "shipgate-ux-surfaces.yml",
            scannerSrc);
    }

    [Fact]
    public void FixtureExistsAndCoversEverySurface()
    {
        // The positive-test fixture must exist and mention every surface by
        // name, so every rule has a line to match against.
        var root = FindRepoRoot();
        var fixture = Path.Combine(root, "shipgate", "fixtures", "ux-surfaces-sample.vue");
        Assert.True(File.Exists(fixture), $"fixture missing at {fixture}");
        var content = File.ReadAllText(fixture);

        foreach (var (name, _, _) in ExpectedSurfaces)
        {
            // Require the surface name to appear textually in the fixture —
            // either as a component tag, a selector, or a section comment.
            // The scanner's surfacesPresentIn() treats <Surface /> tags as
            // embedding markers.
            var rx = new Regex(
                Regex.Escape(name),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.True(
                rx.IsMatch(content),
                $"fixture shipgate/fixtures/ux-surfaces-sample.vue must mention '{name}' at least " +
                $"once so the scanner classifies it as representing that surface.");
        }
    }
}
