// =============================================================================
// Cena Platform — No raw IRT theta / ability / score / readiness doubles on
// student-visible outbound DTOs (prr-007, ADR-0012)
//
// prr-007 locks the IRT θ scalar inside Cena.Actors (the session actor,
// BKT/IRT/HLR math, the CAS pipeline). The student-visible surface must
// expose the ordinal ReadinessBucket seam instead. This test is the
// architecture-level guard: no NEW field matching the theta/ability/score/
// prediction/readiness naming pattern may appear on any outbound contract
// surface.
//
// Surfaces scanned (outbound DTO surfaces):
//   1. src/api/**/*Dto.cs
//   2. src/api/**/*Response.cs
//   3. src/api/**/*Payload.cs
//   4. src/api/Cena.Api.Contracts/**/*.cs
//
// The ban pattern (case-insensitive) catches two tiers of violation:
//
//   Tier 1 — NAME ban (any type, because the name itself leaks the concept):
//     Theta | AbilityEstimate | theta* | bagrutReadiness* |
//     predictedScore* | atRisk*
//
//   Tier 2 — TYPED ban (raw double / float whose name matches the pattern,
//   because a number through a pedagogy-loaded name *is* a leak):
//     theta* | ability* | score* | prediction* | readiness*
//
// Allowlist:
//   * `ReadinessBucket` enum type — the whole point of the seam.
//   * Every legacy violation shared with `NoAtRiskPersistenceTest` — those
//     are tracked under prr-013 follow-up for full retirement. Adding to this
//     list requires an explicit follow-up task.
//
// How to fix a NEW violation (what the failure message tells you):
//   Route the scalar through `ThetaMasteryMapper.ToReadinessBucket(theta, CI)`
//   and expose the ordinal bucket instead. The SessionRiskAssessment pattern
//   (src/actors/Cena.Actors/Sessions/SessionRiskAssessment.cs) is the
//   precedent. See ADR-0012 + prr-007.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoThetaInOutboundDtoTest
{
    // Tier 1 — name-only ban (no type required). Any field whose identifier
    // matches this shape is a leak by name alone, even if typed to an enum
    // or string: "Theta" as a string field still broadcasts the concept.
    private static readonly Regex NameOnlyBan = new(
        @"(?<![A-Za-z0-9_])(?<name>"
        + @"Theta[A-Za-z0-9_]*"
        + @"|AbilityEstimate[A-Za-z0-9_]*"
        + @"|bagrutReadiness[A-Za-z0-9_]*"
        + @"|predictedScore[A-Za-z0-9_]*"
        + @"|(At)?Risk[A-Za-z0-9_]*"
        + @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Tier 2 — typed ban. Captures a declaration of a raw double/float
    // whose IDENTIFIER name begins with theta / ability / score / prediction
    // / readiness. We scan the stripped-of-comments-and-strings line for a
    // token sequence that includes the type word immediately before the
    // identifier. The type word must be a standalone keyword boundary so
    // members typed as `ReadinessBucket` (the explicit allowlist) never
    // match — the regex only fires on `double` / `double?` / `float` /
    // `float?`.
    private static readonly Regex TypedBan = new(
        @"(?<![A-Za-z0-9_])(?<type>double\??|float\??)\s+"
        + @"(?<name>"
        + @"theta[A-Za-z0-9_]*"
        + @"|ability[A-Za-z0-9_]*"
        + @"|score[A-Za-z0-9_]*"
        + @"|prediction[A-Za-z0-9_]*"
        + @"|readiness[A-Za-z0-9_]*"
        + @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Skip XML/slash-star comments.
    private static readonly Regex CommentLine = new(
        @"^\s*(//|\*|/\*|\*/)",
        RegexOptions.Compiled);

    // Remove trailing // comments and collapse "..." string literals so we
    // don't false-positive on identifiers that only appear inside comments
    // or strings. Matches the approach in NoAtRiskPersistenceTest.
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
        var apiDir = Path.Combine(repoRoot, "src", "api");
        if (!Directory.Exists(apiDir)) yield break;

        var suffixGlobs = new[] { "*Dto*.cs", "*Response*.cs", "*Payload*.cs" };

        foreach (var glob in suffixGlobs)
        {
            foreach (var f in Directory.EnumerateFiles(apiDir, glob, SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}")) continue;
                if (f.EndsWith(".Tests.cs", StringComparison.Ordinal)) continue;
                yield return f;
            }
        }

        // Cena.Api.Contracts ships the outbound contract surface shared by
        // both Admin + Student hosts. Every file there is scanned, not just
        // the *Dto.cs / *Response.cs suffix subset.
        var contractsDir = Path.Combine(apiDir, "Cena.Api.Contracts");
        if (Directory.Exists(contractsDir))
        {
            foreach (var f in Directory.EnumerateFiles(contractsDir, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (f.EndsWith(".Tests.cs", StringComparison.Ordinal)) continue;
                yield return f;
            }
        }
    }

    // Legacy allowlist — shares entries with NoAtRiskPersistenceTest for
    // the types that overlap (MasteryDtos.cs at-risk surface, ExamSimulation
    // readiness fields). Those are tracked under prr-013 follow-up for full
    // retirement; until then we accept the known shapes and scan for NEW
    // violations only.
    //
    // TODO(prr-013 follow-up): retire ExamSimulationSubmitted_V1 readiness
    // bounds + MasteryDtos at-risk admin surface. Once those land the
    // entries below can go away — this test will start failing loudly if
    // they don't, which is the point.
    private static readonly HashSet<(string RelPath, string Identifier)> LegacyAllowlist =
        new(new TupleComparer())
    {
        // ── src/actors/Cena.Actors/Events/ExamSimulationEvents.cs ──
        // NOTE: not in this scan's surface (Events are not DTOs), kept
        // here only to document the cross-test alignment. If a future
        // refactor moves these fields into a *Dto.cs file, the entries
        // below already cover the migration.
        (Rel("src/actors/Cena.Actors/Events/ExamSimulationEvents.cs"), "ReadinessLowerBound"),
        (Rel("src/actors/Cena.Actors/Events/ExamSimulationEvents.cs"), "ReadinessUpperBound"),

        // ── MasteryDtos at-risk fields retired 2026-04-20 per prr-013 follow-up ──
        // Entries removed since the underlying fields were deleted from MasteryDtos.cs.
        // Remaining allowlist: ExamSimulationEvents readiness bounds (still present,
        // retirement deferred to preserve event-schema backward compat).
    };

    private static string Rel(string posixPath) =>
        posixPath.Replace('/', Path.DirectorySeparatorChar);

    private sealed class TupleComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.Ordinal)
            && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(obj.Item1, obj.Item2);
    }

    [Fact]
    public void OutboundDtos_HaveNoRawThetaOrReadinessScalars()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in ScannedFiles(repoRoot))
        {
            if (!seen.Add(file)) continue;
            var rel = Path.GetRelativePath(repoRoot, file);

            var lineNumber = 0;
            foreach (var rawLine in File.ReadLines(file))
            {
                lineNumber++;
                if (CommentLine.IsMatch(rawLine)) continue;
                var line = StripCommentsAndStrings(rawLine);

                foreach (Match m in NameOnlyBan.Matches(line))
                {
                    var name = m.Groups["name"].Value;
                    if (LegacyAllowlist.Contains((rel, name))) continue;
                    violations.Add(FailureFor(rel, lineNumber, name, tier: "name"));
                    goto nextLine; // one violation per line is enough
                }

                foreach (Match m in TypedBan.Matches(line))
                {
                    var name = m.Groups["name"].Value;
                    if (LegacyAllowlist.Contains((rel, name))) continue;
                    violations.Add(FailureFor(rel, lineNumber, name, tier: "typed-double"));
                    goto nextLine;
                }
                nextLine:;
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-007 outbound-DTO ban violated. Raw IRT θ / ability / score / prediction /");
            sb.AppendLine("readiness scalars appeared on a student-visible contract surface. Route through");
            sb.AppendLine("`ThetaMasteryMapper.ToReadinessBucket(theta, CI)` and expose the ordinal bucket");
            sb.AppendLine("instead. See ADR-0012 SessionRiskAssessment pattern + prr-007.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }

    private static string FailureFor(string rel, int line, string name, string tier) =>
        $"{rel}:{line} — field `{name}` ({tier} ban) exposes raw theta/readiness scalar externally. "
        + "Route through `ThetaMasteryMapper.ToReadinessBucket(theta, CI)` seam instead. "
        + "See ADR-0012 SessionRiskAssessment pattern + prr-007.";
}
