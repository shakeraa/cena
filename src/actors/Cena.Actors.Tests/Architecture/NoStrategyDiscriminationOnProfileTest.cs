// =============================================================================
// Cena Platform — prr-065 strategy-discrimination persistence ban
//
// Invariant: strategy-discrimination scores are session-scoped only. The
// single authorised home is the `StrategyDiscriminationScore` value object
// in src/actors/Cena.Actors/Sessions. No NEW field matching the
// strategy-discrimination naming pattern may appear on:
//
//   1. src/actors/Cena.Actors/Students/StudentState.cs          (profile state)
//   2. src/actors/Cena.Actors/Events/*.cs                       (event DTOs)
//   3. src/api/**/*Dto*.cs and src/api/**/*Response*.cs         (outbound contracts)
//
// ADR-0003 rationale: per-session error-pattern signals, when aggregated
// across sessions into a student profile, become behavioural profiling of a
// minor (FTC v. Edmodo "Affected Work Product"; ICO v. Reddit £14.47M,
// Feb 2026, under GDPR Art. 22). Mastery signals (BKT, Elo) are aggregate
// competence measures and are allowed to persist; strategy-discrimination
// names the specific mistake pattern — same ADR-0003 boundary as misconception
// tags.
//
// Ban pattern (case-sensitive on PascalCase token boundary):
//   StrategyDiscrim*   — e.g. StrategyDiscriminationScore as a property
//   DiscriminationScore* — e.g. DiscriminationScoreForTopic
//   StrategyScore*     — e.g. StrategyScoreByConcept
//
// Allowlist: the value-object itself in Sessions/StrategyDiscriminationScore.cs
// is where these identifiers are legitimately declared. We skip that one file.
//
// Pattern mirror: this test follows NoAtRiskPersistenceTest exactly so the
// two prediction-surface bans age together.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoStrategyDiscriminationOnProfileTest
{
    private static readonly Regex BannedIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>StrategyDiscrim[A-Za-z0-9_]*|DiscriminationScore[A-Za-z0-9_]*|StrategyScore[A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex CommentLine = new(
        @"^\s*(//|\*|/\*|\*/)",
        RegexOptions.Compiled);

    private static string StripCommentsAndStrings(string line)
    {
        var slashSlash = line.IndexOf("//", StringComparison.Ordinal);
        if (slashSlash >= 0) line = line[..slashSlash];

        var sb = new StringBuilder(line.Length);
        var inStr = false;
        foreach (var c in line)
        {
            if (c == '"') { inStr = !inStr; sb.Append('"'); continue; }
            if (inStr) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        var studentState = Path.Combine(
            repoRoot, "src", "actors", "Cena.Actors", "Students", "StudentState.cs");
        if (File.Exists(studentState)) yield return studentState;

        var eventsDir = Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Events");
        if (Directory.Exists(eventsDir))
            foreach (var f in Directory.EnumerateFiles(eventsDir, "*.cs", SearchOption.TopDirectoryOnly))
                yield return f;

        var apiDir = Path.Combine(repoRoot, "src", "api");
        if (Directory.Exists(apiDir))
        {
            foreach (var f in Directory.EnumerateFiles(apiDir, "*Dto*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}")) continue;
                if (f.EndsWith(".Tests.cs", StringComparison.Ordinal)) continue;
                yield return f;
            }
            foreach (var f in Directory.EnumerateFiles(apiDir, "*Response*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}")) continue;
                yield return f;
            }
        }
    }

    [Fact]
    public void PersistenceAndOutboundDtos_HaveNoStrategyDiscriminationFields()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in ScannedFiles(repoRoot))
        {
            var rel = Path.GetRelativePath(repoRoot, file);

            // The single authorised seam is not in the scanned set, but skip
            // defensively in case conventions shift.
            if (rel.EndsWith("StrategyDiscriminationScore.cs", StringComparison.Ordinal))
                continue;

            var lineNumber = 0;
            foreach (var rawLine in File.ReadLines(file))
            {
                lineNumber++;
                if (CommentLine.IsMatch(rawLine)) continue;
                var line = StripCommentsAndStrings(rawLine);

                foreach (Match m in BannedIdentifier.Matches(line))
                {
                    var name = m.Groups["name"].Value;
                    violations.Add(
                        $"{rel}:{lineNumber} — field/type `{name}` matches strategy-discrimination ban. " +
                        "Strategy-discrimination signals must live on " +
                        "`StrategyDiscriminationScore` (session-scoped, see " +
                        "src/actors/Cena.Actors/Sessions/StrategyDiscriminationScore.cs). " +
                        "ADR-0003: per-student error-pattern signals are session-only, never on profile.");
                    break;
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-065 persistence-surface ban violated. New strategy-discrimination-named fields");
            sb.AppendLine("appeared on persistence or outbound-DTO types. These signals must live on");
            sb.AppendLine("`StrategyDiscriminationScore` inside the session actor (ADR-0003), never persisted.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }
}
