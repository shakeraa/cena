// =============================================================================
// Cena Platform — NoAgeBandBranchingOutsidePolicyTest (prr-052)
//
// Architecture ratchet for ADR-0041 + prr-052:
//
//   Any `switch` / `if` that branches on `AgeBand.Under13`, `AgeBand.Teen13to15`,
//   `AgeBand.Teen16to17`, or `AgeBand.Adult` MUST live inside one of the
//   approved seams:
//
//     - src/actors/Cena.Actors/Consent/AgeBand.cs
//         (the enum definition + AgeBandComputation classifier)
//     - src/actors/Cena.Actors/Consent/AgeBandAuthorizationRules.cs
//         (ADR-0041 grant/revoke matrix; prr-155)
//     - src/actors/Cena.Actors/Consent/AgeBandPolicy.cs
//         (parent-dashboard visibility + student veto; prr-052)
//     - src/actors/Cena.Actors/Consent/ConsentCommands.cs
//         (command handler gating via AgeBandPolicy — it is allowed to
//         name Initiator / Student bands in argument checks)
//     - test files under src/actors/Cena.Actors.Tests/Consent/ and
//       src/api/*/ParentConsole/*Tests.cs
//
// Anything else that hard-codes a band value is age-based business logic
// leaking outside the policy seam — which is exactly what ADR-0041 and
// prr-052 require a single source of truth for.
//
// Scan strategy:
//   Walk every .cs file under src/. Any line that literally contains
//   `AgeBand.Under13`, `AgeBand.Teen13to15`, `AgeBand.Teen16to17`, or
//   `AgeBand.Adult` (case-sensitive) fails unless its file path is in
//   the approved list.
//
// Test files and XML doc comments / string literals are exempted because
// tests MUST name the bands they exercise and doc text legitimately
// references the band names.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class NoAgeBandBranchingOutsidePolicyTest
{
    /// <summary>
    /// Absolute-suffix match: a file under any of these relative paths is
    /// allowed to branch on AgeBand values.
    /// </summary>
    private static readonly string[] ApprovedSuffixes = new[]
    {
        // Canonical policy seams
        Path.Combine("src", "actors", "Cena.Actors", "Consent", "AgeBand.cs"),
        Path.Combine("src", "actors", "Cena.Actors", "Consent", "AgeBandAuthorizationRules.cs"),
        Path.Combine("src", "actors", "Cena.Actors", "Consent", "AgeBandPolicy.cs"),
        Path.Combine("src", "actors", "Cena.Actors", "Consent", "ConsentCommands.cs"),
        Path.Combine("src", "actors", "Cena.Actors", "Consent", "ConsentAggregateWriterAdapter.cs"),
        // PRR-230 parent-visibility default policy — the ONLY place in
        // StudentPlan that branches on AgeBand.
        Path.Combine("src", "actors", "Cena.Actors", "StudentPlan", "ParentVisibilityDefaults.cs"),
        // PRR-230 student-facing toggle endpoint — coarse Under13 gate
        // (authority check). Any finer branching inside the endpoint is
        // forbidden; this suffix only authorises the Under13 === no-veto
        // check.
        Path.Combine(
            "src", "api", "Cena.Student.Api.Host", "Endpoints",
            "ExamTargetParentVisibilityEndpoints.cs"),
    };

    /// <summary>
    /// Directory suffixes that are test code; band references there are
    /// legitimate test assertions, not production business logic.
    /// </summary>
    private static readonly string[] TestDirSuffixes = new[]
    {
        Path.Combine("Cena.Actors.Tests") + Path.DirectorySeparatorChar,
        Path.Combine("Cena.Admin.Api.Tests") + Path.DirectorySeparatorChar,
        Path.Combine("Cena.Student.Api.Host.Tests") + Path.DirectorySeparatorChar,
        Path.Combine("Cena.Infrastructure.Tests") + Path.DirectorySeparatorChar,
    };

    private static readonly Regex BandReference = new(
        @"\bAgeBand\.(Under13|Teen13to15|Teen16to17|Adult)\b",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    [Fact]
    public void AgeBand_Branching_LivesOnlyInApprovedSeams()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src root not found at {srcRoot}");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip test files — test assertions legitimately reference bands.
            if (TestDirSuffixes.Any(sfx => file.Contains(sfx, StringComparison.Ordinal)))
            {
                continue;
            }
            // Skip approved production seams.
            if (ApprovedSuffixes.Any(sfx => file.EndsWith(sfx, StringComparison.Ordinal)))
            {
                continue;
            }
            // Skip obj/bin.
            if (file.Contains(Path.Combine("bin", "Debug"), StringComparison.Ordinal)
                || file.Contains(Path.Combine("bin", "Release"), StringComparison.Ordinal)
                || file.Contains(Path.Combine("obj"), StringComparison.Ordinal))
            {
                continue;
            }

            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }

            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Tolerate doc comments that mention band names in prose.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }
                if (BandReference.IsMatch(line))
                {
                    violations.Add($"{file}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "AgeBand branching detected outside the approved policy seams.\n"
            + "Move the decision into AgeBandPolicy or AgeBandAuthorizationRules:\n  "
            + string.Join("\n  ", violations));
    }
}
