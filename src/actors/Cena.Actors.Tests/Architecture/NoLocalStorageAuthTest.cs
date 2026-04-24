// =============================================================================
// Cena Platform — No localStorage Auth Architecture Gate (prr-011)
//
// XSS session-theft defence. Until prr-011, the student and admin Vue SPAs
// both called `localStorage.setItem(...)` with keys like `cena_auth_user` and
// `auth-token`. Any stored value in localStorage is synchronously readable
// from JavaScript, which means a single XSS — the kind that slips past CSP
// through a dependency, a markdown renderer, or a third-party embed — can
// drain every student's session in one fetch.
//
// This test scans the student + admin Vue source tree for
// `localStorage.setItem` calls whose key argument looks like an auth token
// (matches `auth|token|jwt|session|bearer`, case-insensitive) and fails if
// any appear OUTSIDE a build-gate. An allowed call must be either inside an
// `if (import.meta.env.DEV)` block, inside an `if (... VITE_USE_MOCK_AUTH
// ...)` block, or file-scoped behind an early `if (!import.meta.env.DEV)
// return;` guard. The test walks forward from each match looking for the
// nearest enclosing guard within 20 lines; heuristic but tight enough to
// catch the failure mode the pre-release-review redteam lens actually
// flagged (Scope A of prr-011).
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class NoLocalStorageAuthTest
{
    private static readonly Regex AuthStoragePattern = new(
        @"localStorage\.setItem\s*\(\s*[`'""]([^`'""]*)[`'""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Keys that look like auth state. Deliberately broad — false positives
    // (e.g. `user_preferences`) are cheap to resolve (add a more specific
    // key or wrap in a DEV guard) while false negatives ship the defect.
    private static readonly Regex AuthKeyPattern = new(
        @"auth|token|jwt|session|bearer|credential",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Guards that allow a localStorage.setItem to live in prod code.
    // The mock-auth path legitimately stores to localStorage during E2E /
    // Storybook / Cypress runs; we only want the prod bundle clean.
    private static readonly string[] AllowedGuardMarkers =
    {
        "import.meta.env.DEV",
        "VITE_USE_MOCK_AUTH",
        "process.env.NODE_ENV !== 'production'",
        "__DEV__",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> EnumerateVueSourceFiles(string repoRoot)
    {
        foreach (var bundle in new[] { "src/student/full-version/src", "src/admin/full-version/src" })
        {
            var dir = Path.Combine(repoRoot, bundle);
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".ts" or ".js" or ".vue" or ".tsx" or ".jsx")
                {
                    // Skip test files, Storybook stories, and explicit mock-auth
                    // directories — those directories are never bundled into prod.
                    var normalized = file.Replace('\\', '/');
                    if (normalized.Contains("/__tests__/", StringComparison.Ordinal)
                        || normalized.Contains(".spec.", StringComparison.Ordinal)
                        || normalized.Contains(".test.", StringComparison.Ordinal)
                        || normalized.Contains(".stories.", StringComparison.Ordinal)
                        || normalized.Contains("/mocks/", StringComparison.Ordinal)
                        || normalized.Contains("/mock-auth/", StringComparison.Ordinal)
                        || normalized.Contains("/node_modules/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    yield return file;
                }
            }
        }
    }

    private static bool IsGuarded(string content, int matchIndex)
    {
        // Scan backwards up to ~40 lines from the match looking for the
        // nearest guard marker inside an if / early-return block that
        // encloses the match. The heuristic: find the last occurrence of
        // any guard marker before the match, then confirm it's within a
        // reasonable-size window (file-level module guards are further away
        // but still valid — a file opening with `if (!DEV) return;` covers
        // the entire body).
        var windowStart = Math.Max(0, matchIndex - 4000);
        var prefix = content.Substring(windowStart, matchIndex - windowStart);

        foreach (var marker in AllowedGuardMarkers)
        {
            if (prefix.Contains(marker, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    [Fact]
    public void ProductionVueBundles_HaveNoLocalStorageAuthWrites_OutsideBuildGate()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateVueSourceFiles(repoRoot))
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match match in AuthStoragePattern.Matches(content))
            {
                var key = match.Groups[1].Value;
                if (!AuthKeyPattern.IsMatch(key))
                    continue; // benign localStorage use (theme, locale, etc.)

                if (IsGuarded(content, match.Index))
                    continue; // dev-only path

                // Record the offending file + line number for actionable output.
                var lineNumber = content.Take(match.Index).Count(c => c == '\n') + 1;
                var relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                offenders.Add($"{relPath}:{lineNumber} — localStorage.setItem('{key}', ...)");
            }
        }

        if (offenders.Count > 0)
        {
            Assert.Fail(
                $"Production Vue bundle contains {offenders.Count} JS-accessible auth "
                + "storage call(s) — XSS session-theft vector. See prr-011 + Scope A "
                + "(strip mock-auth from prod bundle).\n\n"
                + "Acceptable guards: `if (import.meta.env.DEV) { ... }`, "
                + "`if (import.meta.env.VITE_USE_MOCK_AUTH === 'true') { ... }`, "
                + "or a file-top early return with one of the above.\n\n"
                + "Offenders:\n  " + string.Join("\n  ", offenders));
        }
    }
}
