// =============================================================================
// Cena Platform — No-direct-system-timezone-calls gate (prr-157)
//
// Enforces that `TimeZoneInfo.FindSystemTimeZoneById` and
// `TimeZoneInfo.ConvertTimeBySystemTimeZoneId` are called from exactly ONE
// place in the repository: the Israel time-zone resolver at
// src/shared/Cena.Infrastructure/Time/IsraelTimeZoneResolver.cs.
//
// Background (actor-system-review L1, 2026-04-20):
//   PushNotificationTriggerService.cs used the Windows-only ID
//   "Israel Standard Time" which throws `TimeZoneNotFoundException` on every
//   Linux CI run and production container. OutreachSchedulerActor.cs had a
//   correct try/catch but duplicated the pattern. prr-157 extracted the
//   resolver; this test prevents future drift.
//
// Detection is a literal substring grep (no Roslyn dependency, matches the
// style of the other tests in this folder). Both method names are flagged;
// the resolver file is the only allowed call site. The test file itself is
// excluded because the method names appear in its error messages.
// =============================================================================

using System.Text;

namespace Cena.Actors.Tests.Architecture;

public class NoDirectSystemTimeZoneCallsTest
{
    private static readonly string[] ForbiddenCalls =
    {
        "FindSystemTimeZoneById",
        "ConvertTimeBySystemTimeZoneId",
    };

    // Files allowed to reference these APIs directly.
    private static readonly string[] AllowListSuffixes =
    {
        // THE Israel resolver — single source of truth for the Israel zone.
        Path.Combine("shared", "Cena.Infrastructure", "Time", "IsraelTimeZoneResolver.cs"),
        // Generic cross-platform resolver — sanctioned second seam for
        // arbitrary caller-supplied zone IDs (prr-018 quiet-hours).
        Path.Combine("shared", "Cena.Infrastructure", "Time", "SafeTimeZoneResolver.cs"),
        // This test file — its error message cites the forbidden symbols.
        Path.Combine("Architecture", "NoDirectSystemTimeZoneCallsTest.cs"),
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    [Fact]
    public void NoSourceFile_OutsideResolver_CallsSystemTimeZoneApisDirectly()
    {
        var repoRoot = FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"src/ directory not found at {srcDir}");

        var violations = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip build output and agent worktrees.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}.claude-flow{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}.agentdb{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")) continue;

            if (AllowListSuffixes.Any(s => file.EndsWith(s, StringComparison.Ordinal))) continue;

            var text = File.ReadAllText(file);
            foreach (var call in ForbiddenCalls)
            {
                if (text.Contains(call, StringComparison.Ordinal))
                {
                    violations.AppendLine(
                        $"  {Path.GetRelativePath(repoRoot, file)}: contains '{call}'");
                }
            }
        }

        Assert.True(violations.Length == 0,
            "Direct TimeZoneInfo system-ID calls are forbidden outside " +
            "src/shared/Cena.Infrastructure/Time/IsraelTimeZoneResolver.cs (prr-157). " +
            "These APIs accept platform-specific IDs (IANA on Linux/macOS, " +
            "legacy names on Windows) and crash on the wrong OS. Route through " +
            "IsraelTimeZoneResolver.Instance / IsraelTimeZoneResolver.ConvertFromUtc " +
            "(or extend the resolver if a different zone is needed):\n" + violations);
    }
}
