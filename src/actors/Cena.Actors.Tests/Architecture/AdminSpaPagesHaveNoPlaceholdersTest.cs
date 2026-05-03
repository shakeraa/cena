// =============================================================================
// Cena Platform — Architecture test (RDY-058)
//
// Fails the build if any .vue file under
// src/admin/full-version/src/pages/ or src/student/full-version/src/pages/
// contains a placeholder marker string like "coming soon", "TODO"-comment
// banners, or "placeholder text". Catches the ADM-016-class regression
// where a page lives under tasks/done/ while the actual view renders
// two words of placeholder copy.
//
// The allowlist keeps genuine mentions (e.g. UX strings talking about
// "coming soon events" in a feature-flag list) out of the net. Add a
// context comment when you allowlist a match.
// =============================================================================

using System.Text.RegularExpressions;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public class AdminSpaPagesHaveNoPlaceholdersTest
{
    // Any of these substrings in a page's template / script-setup block is
    // a placeholder smell. Case-insensitive. The "not implemented" variants
    // catch the common templating language people reach for when stubbing.
    private static readonly string[] ForbiddenSubstrings = new[]
    {
        "coming soon",
        "placeholder text",
        "not implemented yet",
        "todo: implement this",
    };

    // File names where the forbidden strings are legitimate UX copy
    // ABOUT upcoming features (not self-description). Keep this list
    // tiny; every entry needs a context comment.
    private static readonly string[] AllowList = Array.Empty<string>();

    [Fact]
    public void NoAdminOrStudentPageRendersPlaceholderCopy()
    {
        var repoRoot = FindRepoRoot();
        var pageDirs = new[]
        {
            Path.Combine(repoRoot, "src", "admin", "full-version", "src", "pages"),
            Path.Combine(repoRoot, "src", "student", "full-version", "src", "pages"),
        };

        var violations = new List<string>();

        foreach (var root in pageDirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.vue", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(repoRoot, file);
                if (AllowList.Any(a => rel.EndsWith(a, StringComparison.Ordinal))) continue;

                var text = File.ReadAllText(file);
                foreach (var forbid in ForbiddenSubstrings)
                {
                    if (text.Contains(forbid, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{rel} — contains \"{forbid}\"");
                        break;
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "The following SPA pages render placeholder copy instead of a real implementation. " +
            "Either implement the feature or remove the page:\n  - " +
            string.Join("\n  - ", violations));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }
}
