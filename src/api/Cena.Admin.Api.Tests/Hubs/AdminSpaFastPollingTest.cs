// =============================================================================
// Cena Platform — Admin SPA fast-polling guard (RDY-060)
//
// Regression guard: new admin-side pages tend to copy-paste a
// `setInterval(fn, 3000)` block and re-introduce the polling cost that
// RDY-060's SignalR work was supposed to replace. This test fails CI
// whenever an admin SPA page contains a setInterval call firing under
// 30 seconds UNLESS the line (or line above) carries the allowlist token.
//
// To allowlist a tight poll (rare — reach for the hub first):
//
//   // arch-test-allow: setInterval-fast  (reason here)
//   setInterval(fn, 5000)
//
// The comment must literally contain `arch-test-allow: setInterval-fast`
// on the same line or the line immediately above the setInterval call.
//
// Parser notes (tightened in Phase 5c): the original regex failed on
// multi-line arrow-function first args like:
//
//     setInterval(() => {
//       if (autoRefresh.value) fetchStats()
//     }, 3000)
//
// because `[^,]+` stops at the first newline. The current walker
// tracks paren depth + string-literal state so it catches multi-line
// callbacks and nested-comma arguments.
// =============================================================================

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

            var source = File.ReadAllText(file);
            foreach (var hit in FindSetIntervalCalls(source))
            {
                if (hit.IntervalMs >= FastPollThresholdMs) continue;

                var lineNumber = CountLines(source, hit.MatchStart);

                // Allowlist token must be on the same line OR the
                // immediately preceding line.
                var lineStart = hit.MatchStart == 0
                    ? 0
                    : source.LastIndexOf('\n', hit.MatchStart - 1) + 1;
                var currentLineEnd = source.IndexOf('\n', lineStart);
                if (currentLineEnd < 0) currentLineEnd = source.Length;
                var currentLine = source.Substring(lineStart, currentLineEnd - lineStart);

                string prevLine = "";
                if (lineStart > 1)
                {
                    var prevEnd = lineStart - 1;
                    var prevStart = source.LastIndexOf('\n', prevEnd - 1) + 1;
                    prevLine = source.Substring(prevStart, prevEnd - prevStart);
                }

                if (currentLine.Contains(AllowlistToken) || prevLine.Contains(AllowlistToken))
                    continue;

                var rel = Path.GetRelativePath(repoRoot, file);
                violations.Add($"{rel}:{lineNumber}  setInterval @ {hit.IntervalMs}ms (no allowlist)");
            }
        }

        Assert.True(violations.Count == 0,
            "Admin SPA pages must not poll faster than 30 seconds without the " +
            $"`{AllowlistToken}` comment. RDY-060 replaced 3-second polling with " +
            "SignalR streaming; new tight polls reintroduce the cost this task removed.\n" +
            "Violations:\n  " + string.Join("\n  ", violations));
    }

    private static int CountLines(string source, int offset)
    {
        int n = 1;
        for (int i = 0; i < offset && i < source.Length; i++)
            if (source[i] == '\n') n++;
        return n;
    }

    /// <summary>
    /// Walks `source` finding every `setInterval(...)` call where the
    /// last argument (at paren depth 1) is a numeric literal. Tracks
    /// paren depth + string-literal state so multi-line arrow-function
    /// first args + nested-comma arguments parse correctly. Underscore
    /// separators in numeric literals (e.g. 30_000) are allowed.
    /// </summary>
    internal static IEnumerable<SetIntervalHit> FindSetIntervalCalls(string source)
    {
        const string token = "setInterval";
        int i = 0;
        while (i + token.Length < source.Length)
        {
            var idx = source.IndexOf(token, i, StringComparison.Ordinal);
            if (idx < 0) yield break;
            i = idx + token.Length;

            // Word-boundary check: preceding char must not be a letter /
            // digit / underscore (rejects e.g. `mySetInterval`).
            if (idx > 0)
            {
                var prev = source[idx - 1];
                if (char.IsLetterOrDigit(prev) || prev == '_') continue;
            }

            // Skip whitespace between "setInterval" and "(".
            int j = i;
            while (j < source.Length && char.IsWhiteSpace(source[j])) j++;
            if (j >= source.Length || source[j] != '(') continue;

            int openParen = j;
            int depth = 1;
            int k = openParen + 1;
            int lastCommaAtDepth1 = -1;
            bool inSingle = false, inDouble = false, inBack = false;

            while (k < source.Length && depth > 0)
            {
                var c = source[k];
                if (inSingle || inDouble || inBack)
                {
                    if (c == '\\' && k + 1 < source.Length) { k += 2; continue; }
                    if (inSingle && c == '\'') inSingle = false;
                    else if (inDouble && c == '"') inDouble = false;
                    else if (inBack && c == '`') inBack = false;
                }
                else
                {
                    switch (c)
                    {
                        case '(': depth++; break;
                        case ')':
                            depth--;
                            if (depth == 0) break;
                            break;
                        case '\'': inSingle = true; break;
                        case '"': inDouble = true; break;
                        case '`': inBack = true; break;
                        case ',' when depth == 1: lastCommaAtDepth1 = k; break;
                    }
                }
                k++;
                if (depth == 0) break;
            }

            if (depth != 0 || lastCommaAtDepth1 < 0)
            {
                i = openParen + 1;
                continue;
            }

            int closeParen = k - 1;
            var lastArg = source.Substring(
                lastCommaAtDepth1 + 1, closeParen - lastCommaAtDepth1 - 1).Trim();

            var cleaned = lastArg.Replace("_", "");
            if (int.TryParse(cleaned, out var ms))
                yield return new SetIntervalHit(idx, ms);

            i = closeParen + 1;
        }
    }

    internal readonly record struct SetIntervalHit(int MatchStart, int IntervalMs);
}
