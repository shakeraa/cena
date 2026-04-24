// =============================================================================
// Cena Platform — File-size 500-LOC rule with grandfather whitelist
// (EPIC-PRR-A Sprint 1 architecture gate, effective 2026-04-27 per ADR-0012)
//
// Two invariants enforced mechanically:
//
//   1. Any .cs file under src/ that is NOT in FileSize500LocBaseline.yml must
//      be at most 500 LOC.
//
//   2. Any .cs file that IS in the grandfather list must be at most its
//      recorded baseline_loc. PRs MAY lower a baseline (the same PR must
//      update the YAML to the new lower number); PRs MUST NOT raise one.
//
// LOC counting heuristic (documented in baseline YAML):
//   - Count = total lines − blank lines − single-line-comment-only lines.
//   - A line is a "single-line-comment-only" line iff its trimmed content
//     starts with "//". Block comments are NOT handled — close-enough.
//   - This matches how `wc -l` minus common comment noise tracks "real code"
//     without pulling in a Roslyn dependency.
//
// The grandfather baseline YAML is a tiny hand-written format; this test
// parses it inline to avoid adding a YAML NuGet dependency.
// =============================================================================

using System.Text;

namespace Cena.Actors.Tests.Architecture;

public class FileSize500LocTest
{
    private const int LocLimit = 500;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string BaselineYamlPath(string repoRoot) => Path.Combine(
        repoRoot, "src", "actors", "Cena.Actors.Tests", "Architecture",
        "FileSize500LocBaseline.yml");

    /// <summary>
    /// LOC count: total lines minus blank lines and single-line-comment-only
    /// lines. Block comments are intentionally not handled (simple heuristic).
    /// </summary>
    internal static int CountLoc(string path)
    {
        var loc = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            loc++;
        }
        return loc;
    }

    /// <summary>
    /// Minimal parser for the FileSize500LocBaseline.yml format. Accepts
    /// a top-level `grandfathered:` key followed by a sequence of mappings
    /// with `path:` and `baseline_loc:` fields. Ignores `reason:` and any
    /// other keys. Strings may be quoted or unquoted.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> LoadBaseline(string yamlPath)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(yamlPath))
            return result;

        string? currentPath = null;
        foreach (var raw in File.ReadAllLines(yamlPath))
        {
            var line = raw;
            // Strip trailing comments (safe because our YAML values don't
            // contain "#" outside of the leading comment header which is
            // unconditionally skipped by the key-matching below).
            var hashIdx = line.IndexOf('#');
            if (hashIdx >= 0) line = line[..hashIdx];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            // Start of a new list entry: "- path: <value>"
            if (trimmed.StartsWith("- path:", StringComparison.Ordinal))
            {
                currentPath = Unquote(trimmed["- path:".Length..].Trim());
                continue;
            }

            if (currentPath is null) continue;

            if (trimmed.StartsWith("path:", StringComparison.Ordinal))
            {
                currentPath = Unquote(trimmed["path:".Length..].Trim());
                continue;
            }

            if (trimmed.StartsWith("baseline_loc:", StringComparison.Ordinal))
            {
                var value = trimmed["baseline_loc:".Length..].Trim();
                if (int.TryParse(value, out var loc))
                {
                    // Normalize path separators to match filesystem
                    var norm = currentPath!.Replace('/', Path.DirectorySeparatorChar);
                    result[norm] = loc;
                }
                currentPath = null;
            }
        }
        return result;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1];
        return s;
    }

    [Fact]
    public void EveryCsFile_UnderSrc_IsEitherAtMost500Loc_OrGrandfathered()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src/ directory not found at {srcRoot}");

        var baseline = LoadBaseline(BaselineYamlPath(repoRoot));
        var failures = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip build output, auto-generated code, and agent worktrees.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}.claude-flow{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}.agentdb{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")) continue;

            var rel = Path.GetRelativePath(repoRoot, file);
            var loc = CountLoc(file);

            if (baseline.TryGetValue(rel, out var grandfathered))
            {
                if (loc > grandfathered)
                {
                    failures.AppendLine(
                        $"  {rel}: {loc} LOC exceeds grandfather baseline of {grandfathered}. " +
                        "Either refactor the file below the baseline OR (if the file legitimately " +
                        "grew past its baseline) this is exactly the regression ADR-0012 blocks — " +
                        "the baseline is a ratchet, it only goes down. " +
                        "If the file is a StudentActor successor, move new code into that successor " +
                        "(LearningSessionActor post Sprint 1, 2026-05-03).");
                }
            }
            else if (loc > LocLimit)
            {
                failures.AppendLine(
                    $"  {rel}: {loc} LOC exceeds the {LocLimit}-LOC rule and is not grandfathered. " +
                    "Refactor into smaller modules (extract helpers, split by concern, or move to a " +
                    "domain submodule). If this file was already over the limit before 2026-04-20 " +
                    "and was missed by the grandfather baseline, add it to " +
                    "src/actors/Cena.Actors.Tests/Architecture/FileSize500LocBaseline.yml — but " +
                    "net-new files over 500 LOC are blocked by policy.");
            }
        }

        Assert.True(failures.Length == 0,
            "File-size 500-LOC rule violated (ADR-0012 Schedule Lock, effective 2026-04-27):\n" +
            failures);
    }

    [Fact]
    public void GrandfatherBaseline_DoesNotContainFilesUnder500Loc()
    {
        // If a grandfathered file has shrunk below 500 LOC, the PR that
        // shrank it should also have removed it from the baseline. This
        // test catches stale entries that would otherwise permit a
        // later regression back up to the old baseline.
        var repoRoot = FindRepoRoot();
        var baseline = LoadBaseline(BaselineYamlPath(repoRoot));
        var failures = new StringBuilder();

        foreach (var (relPath, baselineLoc) in baseline)
        {
            var abs = Path.Combine(repoRoot, relPath);
            if (!File.Exists(abs))
            {
                failures.AppendLine(
                    $"  {relPath}: listed in grandfather baseline but the file no longer exists. " +
                    "Remove the entry from FileSize500LocBaseline.yml.");
                continue;
            }

            var loc = CountLoc(abs);
            if (loc <= LocLimit)
            {
                failures.AppendLine(
                    $"  {relPath}: now {loc} LOC (<= {LocLimit}); grandfather entry is stale " +
                    $"(recorded baseline_loc={baselineLoc}). " +
                    "Remove the entry from FileSize500LocBaseline.yml so the file is subject to " +
                    "the standard rule going forward.");
            }
        }

        Assert.True(failures.Length == 0,
            "FileSize500LocBaseline.yml contains stale entries for files now under the 500-LOC limit:\n" +
            failures);
    }

    [Fact]
    public void GrandfatherBaseline_EveryEntry_HasCurrentLocAtOrBelowBaseline()
    {
        // Redundant with the main test for violations, but useful as a
        // standalone sanity check that the YAML is in a coherent state
        // on a green main. Fails if someone lands a commit that grew a
        // grandfathered file without lowering its baseline in the same PR.
        var repoRoot = FindRepoRoot();
        var baseline = LoadBaseline(BaselineYamlPath(repoRoot));
        Assert.NotEmpty(baseline); // Sanity: YAML parsed something.

        var failures = new StringBuilder();
        foreach (var (relPath, baselineLoc) in baseline)
        {
            var abs = Path.Combine(repoRoot, relPath);
            if (!File.Exists(abs)) continue; // handled by stale-entry test
            var loc = CountLoc(abs);
            if (loc > baselineLoc)
                failures.AppendLine($"  {relPath}: {loc} > baseline {baselineLoc}");
        }

        Assert.True(failures.Length == 0,
            "Grandfathered files grew past their baselines:\n" + failures);
    }
}
