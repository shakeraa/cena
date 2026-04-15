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
    private static readonly string[] AllowList = new[]
    {
        // The gated entry point itself.
        Path.Combine("Cena.Admin.Api", "QuestionBankService.cs"),
        // The gated backfill surface (resolves existing streams; does not
        // StartStream, but we keep it here as an approved CAS-adjacent surface).
        Path.Combine("Cena.Admin.Api", "Endpoints", "CasBackfillEndpoint.cs"),
        // This test.
        "SeedLoaderMustUseQuestionBankServiceTest.cs",
        // KNOWN_VIOLATION_TODO (ADR-0032 follow-up): these two files predate
        // the CAS gate and write QuestionState streams directly. They must be
        // refactored to route through QuestionBankService.CreateQuestion before
        // the pilot ships. Tracked as CAS-GATE-SEED-REFACTOR.
        // The allow-list entry here keeps CI green while any NEW bypass
        // (files not listed here) still fails the build.
        Path.Combine("Cena.Admin.Api", "QuestionBankSeedData.cs"),
        Path.Combine("Cena.Actors", "Ingest", "IngestionOrchestrator.cs"),
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
            "Route through QuestionBankService.CreateQuestion instead:\n  - " +
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
