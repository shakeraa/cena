// =============================================================================
// Cena Platform — ConsentAggregate architectural boundary gate (prr-155)
//
// Enforces ADR-0012-aligned aggregate decomposition for the Consent
// bounded context. Asserts:
//   1. No non-Consent .cs file under src/actors/Cena.Actors/ references
//      the ConsentPurpose / ConsentGrantInfo / ConsentAggregate types
//      (the legacy facade in Cena.Infrastructure is allowed and expected;
//      the architectural rule is about downstream aggregates not reaching
//      in).
//   2. StudentActor*.cs and the StudentProfile* family hold NO consent-
//      shaped fields (field or property named *Consent* / *ConsentGrant*).
//      Consent state lives on ConsentAggregate; StudentActor may only
//      reference it via well-defined read-facade seams (IGdprConsentManager),
//      never as embedded state.
//
// Intent: if a future refactor accidentally re-embeds consent state on a
// student-side type, this test fails loudly so the PR author has to move
// it to the aggregate.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class ConsentAggregateNoProfileCouplingTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string StudentsDir(string repoRoot) =>
        Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Students");

    private static string ActorsRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "actors", "Cena.Actors");

    private static string ConsentDir(string repoRoot) =>
        Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Consent");

    // -------------------------------------------------------------------------
    // Test 1 — StudentActor + StudentProfile files have no consent-shaped
    //          fields/properties.
    // -------------------------------------------------------------------------

    private static readonly Regex ConsentFieldPattern = new(
        @"(?:public|private|protected|internal)\s+[A-Za-z_][A-Za-z0-9_<>?,\s]*?\s+(?:_?[A-Za-z]*Consent[A-Za-z]*|[Cc]onsentGrant[A-Za-z]*)\s*[{;=]",
        RegexOptions.Compiled);

    [Fact]
    public void StudentActor_and_StudentProfile_have_no_consent_state_fields()
    {
        var repoRoot = FindRepoRoot();
        var dir = StudentsDir(repoRoot);
        Assert.True(Directory.Exists(dir), $"Students dir missing: {dir}");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            // Only gate on StudentActor*.cs and StudentProfile*.cs — other
            // files in Students/ are scoped contexts (e.g. StudentMessages).
            if (!name.StartsWith("StudentActor", StringComparison.Ordinal)
                && !name.StartsWith("StudentProfile", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (ConsentFieldPattern.IsMatch(line))
                {
                    offenders.Add($"{name}:{i + 1}: {line.Trim()}");
                }
            }
        }

        if (offenders.Count > 0)
        {
            Assert.Fail(
                "Consent state field(s) detected on StudentActor/StudentProfile — must move to ConsentAggregate (prr-155 / ADR-0041):\n  "
                + string.Join("\n  ", offenders));
        }
    }

    // -------------------------------------------------------------------------
    // Test 2 — No non-Consent aggregate type under src/actors/Cena.Actors
    //          imports ConsentAggregate / ConsentCommandHandler / ConsentState.
    //          Exceptions: the Consent/ folder itself (obviously), and the DI
    //          registration entry points (Host Program.cs files, which live
    //          elsewhere so are not in scope of this walk).
    // -------------------------------------------------------------------------

    [Fact]
    public void No_non_consent_aggregate_references_ConsentAggregate_types()
    {
        var repoRoot = FindRepoRoot();
        var actorsRoot = ActorsRoot(repoRoot);
        var consentDir = ConsentDir(repoRoot);

        Assert.True(Directory.Exists(actorsRoot), $"Actors root missing: {actorsRoot}");

        var forbidden = new[]
        {
            "ConsentAggregate",
            "ConsentCommandHandler",
            "ConsentState",
            "ConsentGrantInfo",
        };

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(actorsRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip anything under the Consent/ bounded-context folder.
            if (file.StartsWith(consentDir, StringComparison.Ordinal)) continue;

            // Skip obj/ + bin/ build outputs.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            foreach (var forbiddenSymbol in forbidden)
            {
                // Match as a word-boundary token so we don't false-match on
                // substrings. Also tolerate namespace-qualified references:
                // "Cena.Actors.Consent.ConsentAggregate" is an explicit opt-in
                // and is allowed (e.g. Host DI composition). But any code in
                // Cena.Actors/ outside Consent/ shouldn't actually need those.
                var pattern = @"\b" + Regex.Escape(forbiddenSymbol) + @"\b";
                if (Regex.IsMatch(content, pattern))
                {
                    offenders.Add($"{Path.GetRelativePath(actorsRoot, file)}: references {forbiddenSymbol}");
                    break; // one offender per file is enough for the message
                }
            }
        }

        if (offenders.Count > 0)
        {
            Assert.Fail(
                "Non-Consent files in src/actors/Cena.Actors/ reference ConsentAggregate internals — "
                + "the aggregate is its own bounded context (prr-155). Use IGdprConsentManager or a "
                + "thin seam instead:\n  " + string.Join("\n  ", offenders));
        }
    }

    // -------------------------------------------------------------------------
    // Test 3 — The Consent folder itself is self-contained: every event is
    //          under Consent/Events/ and the aggregate root file exists.
    // -------------------------------------------------------------------------

    [Fact]
    public void Consent_folder_is_structured_per_prr155()
    {
        var repoRoot = FindRepoRoot();
        var consentDir = ConsentDir(repoRoot);
        Assert.True(Directory.Exists(consentDir), $"Consent folder missing: {consentDir}");

        var eventsDir = Path.Combine(consentDir, "Events");
        Assert.True(Directory.Exists(eventsDir), $"Consent/Events folder missing: {eventsDir}");

        var requiredEventFiles = new[]
        {
            "ConsentGranted_V1.cs",
            "ConsentRevoked_V1.cs",
            "ConsentPurposeAdded_V1.cs",
            "ConsentReviewedByParent_V1.cs",
        };
        foreach (var f in requiredEventFiles)
        {
            var path = Path.Combine(eventsDir, f);
            Assert.True(File.Exists(path), $"Required consent event file missing: {path}");
        }

        Assert.True(File.Exists(Path.Combine(consentDir, "ConsentAggregate.cs")));
        Assert.True(File.Exists(Path.Combine(consentDir, "ConsentCommands.cs")));
        Assert.True(File.Exists(Path.Combine(consentDir, "ConsentState.cs")));
        Assert.True(File.Exists(Path.Combine(consentDir, "AgeBandAuthorizationRules.cs")));
    }
}
