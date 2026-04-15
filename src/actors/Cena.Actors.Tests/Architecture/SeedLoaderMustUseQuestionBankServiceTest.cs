// =============================================================================
// Cena Platform — Architecture test (RDY-034 §12 / ADR-0032)
//
// Fails the build if any source file OUTSIDE the approved ingestion surface
// writes QuestionState events directly. All question ingestion must go through
// QuestionBankService.CreateQuestion (which runs the CAS gate) or the operator
// CasBackfillEndpoint (which also runs the gate on already-persisted state).
// =============================================================================

using System.Text.RegularExpressions;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public class SeedLoaderMustUseQuestionBankServiceTest
{
    // Writers that must be routed through the CAS-gated service. We look only
    // for the unambiguous "direct write" signal: StartStream<QuestionState>.
    // Constructing an event record inside a projection/upcaster is legitimate
    // (read-side consumption, not a write) so we don't pattern-match on that.
    private static readonly Regex ForbiddenPattern = new(
        @"\bStartStream\s*<\s*QuestionState\s*>",
        RegexOptions.Compiled);

    // Files allowed to write QuestionState streams directly.
    // RDY-037 / ADR-0032: the CAS-gated persister is the single legitimate
    // StartStream<QuestionState> site in the repository. No other file is
    // permitted to append directly to a new QuestionState stream — every
    // creation path (admin UI, AI generation, ingestion, seed, test fixtures)
    // routes through ICasGatedQuestionPersister.PersistAsync so the ADR-0002
    // correctness invariant is globally enforced, not locally suggested.
    private static readonly string[] AllowList = new[]
    {
        // THE gated writer.
        Path.Combine("Cena.Actors", "Cas", "CasGatedQuestionPersister.cs"),
        // This test.
        "SeedLoaderMustUseQuestionBankServiceTest.cs",
    };

    [Fact]
    public void NoFileOutsideApprovedSurfaceWritesQuestionStateDirectly()
    {
        var repoRoot = FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"src dir not found under {repoRoot}");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip bin/obj
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            if (AllowList.Any(a => file.EndsWith(a, StringComparison.Ordinal))) continue;

            var text = File.ReadAllText(file);
            if (ForbiddenPattern.IsMatch(text))
            {
                violations.Add(Path.GetRelativePath(repoRoot, file));
            }
        }

        Assert.True(violations.Count == 0,
            "The following files write QuestionState events directly, bypassing the CAS gate. " +
            "Route through ICasGatedQuestionPersister.PersistAsync (Cena.Actors.Cas) instead:\n  - " +
            string.Join("\n  - ", violations));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }
}
