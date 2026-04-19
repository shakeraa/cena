// =============================================================================
// Cena Platform — Admin SPA fast-polling guard (RDY-060)
//
// Regression guard: new admin-side pages tend to copy-paste a
// `setInterval(fn, 3000)` block and re-introduce the polling cost that
// RDY-060's SignalR work was supposed to replace. This test fails CI
// whenever an admin SPA page contains a setInterval callback firing
// under 30 seconds UNLESS the line above carries the allowlist token.
//
// To allowlist a tight poll (rare — reach for the hub first):
//
//   // arch-test-allow: setInterval-fast  (reason here)
//   setInterval(fn, 5000)
//
// The comment must literally contain `arch-test-allow: setInterval-fast`
// on the same line or the line immediately above the setInterval call.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Admin.Api.Tests.Hubs;

public class AdminSpaFastPollingTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    // setInterval(callback, <NUMBER_MS>) — group 1 captures the interval value.
    // Accepts underscores (e.g. 30_000) and scientific notation is rejected
    // (rare in practice; if it happens we'd fail closed).
    private static readonly Regex SetIntervalCall = new(
        @"setInterval\s*\([^,]+,\s*([0-9_]+)\s*\)",
        RegexOptions.Compiled);

    private const int FastPollThresholdMs = 30_000;
    private const string AllowlistToken = "arch-test-allow: setInterval-fast";

    [Fact]
    public void AdminSpaPages_DoNotPollFasterThan30Seconds_WithoutAllowlist()
    {
        var repoRoot = FindRepoRoot();
        var pagesDir = Path.Combine(
            repoRoot, "src", "admin", "full-version", "src", "pages");
        if (!Directory.Exists(pagesDir))
        {
            // CI mirrors sometimes prune frontend; skip cleanly in that case.
            return;
        }

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(pagesDir, "*.vue", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"))
                continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var match = SetIntervalCall.Match(lines[i]);
                if (!match.Success) continue;

                var raw = match.Groups[1].Value.Replace("_", "");
                if (!int.TryParse(raw, out var ms) || ms >= FastPollThresholdMs)
                    continue;

                // Fast poll — require the allowlist token on this line or
                // the line above.
                var sameLine = lines[i].Contains(AllowlistToken);
                var prevLine = i > 0 && lines[i - 1].Contains(AllowlistToken);
                if (sameLine || prevLine)
                    continue;

                var rel = Path.GetRelativePath(repoRoot, file);
                violations.Add($"{rel}:{i + 1}  setInterval @ {ms}ms (no allowlist)");
            }
        }

        Assert.True(violations.Count == 0,
            "Admin SPA pages must not poll faster than 30 seconds without the " +
            $"`{AllowlistToken}` comment. RDY-060 replaced 3-second polling with " +
            "SignalR streaming; new tight polls reintroduce the cost this task removed.\n" +
            "Violations:\n  " + string.Join("\n  ", violations));
    }
}
