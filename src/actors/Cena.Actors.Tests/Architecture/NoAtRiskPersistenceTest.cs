// =============================================================================
// Cena Platform — No at-risk/readiness fields on persistence or outbound DTOs
// (prr-013, ADR-0003, RDY-080, RDY-020)
//
// prr-013 locks the "At-Risk Student" concept to a session-scoped value object
// (SessionRiskAssessment). This test prevents regression: no NEW field matching
// the prediction-surface naming pattern may appear on any of the surfaces this
// test scans.
//
// Surfaces scanned (the "ban zone"):
//   1. src/actors/Cena.Actors/Students/StudentState.cs
//      — event-sourced aggregate state for StudentActor
//   2. src/actors/Cena.Actors/Events/*.cs
//      — every event DTO (versioned `*_V<N>` records)
//   3. src/api/**/*Dto.cs and *Response.cs (outbound admin + student API)
//
// The ban pattern (case-insensitive, matches a full token):
//     risk | atRisk | bagrutRisk | predictedScore | readiness
// prefixed by whitespace or `(` or `,` (record/ctor parameter position) and
// followed by `Name`/`Score`/`Level`/`Count`/`Upper`/`Lower`/etc.
//
// Allowlist: one narrow exception for SessionRiskAssessment (the authorised
// single-seam home), plus a pinned list of pre-2026-04-20 violations that are
// explicit tech debt scheduled for retirement by the follow-up task tracked in
// prr-013's broader DoD (retire MasteryDtos at-risk surface + exam-sim
// Readiness* fields). Every allowlisted entry carries a TODO comment naming
// the follow-up.
//
// How to fix a new violation (what the failure message tells you):
//   Either rename the field (use session-local language, e.g. "AccuracyRate"
//   with a separate "ConfidenceIntervalHalfWidth" field) OR — if this truly
//   is a risk/readiness number — move the calculation into
//   SessionRiskAssessment where it is confined to the session actor and is
//   never persisted or externalised.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoAtRiskPersistenceTest
{
    // Captures identifier names on their own line that begin with a banned
    // token. We deliberately match ONLY at the start of a record-member or
    // property declaration line so that words inside comments, strings, or
    // method bodies do not false-positive. The identifier may be preceded by
    // whitespace, an access modifier, a type, and any other parameter-list
    // ceremony; the regex accepts "everything up to and including the last
    // whitespace before the identifier" so that patterns like
    //     public int AtRiskCount { get; set; }
    //     double ReadinessLowerBound,
    //     string RiskLevel,
    // all match. Identifier must be the final token of interest on the line
    // (up to a punctuation boundary).
    private static readonly Regex BannedIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>(At)?Risk[A-Za-z0-9_]*|BagrutRisk[A-Za-z0-9_]*|PredictedScore[A-Za-z0-9_]*|Readiness[A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Lines that look like code declarations we should scan. We avoid matching
    // inside XML doc comments (/// lines) and // comments.
    private static readonly Regex CommentLine = new(
        @"^\s*(//|\*|/\*|\*/)",
        RegexOptions.Compiled);

    // Strips trailing `// ...` inline comments and collapses any double-quoted
    // string literal to `""`. The arch scan only cares about identifier
    // tokens in declaration context, not about comment prose or string
    // values (e.g. enum-literal comments like `// 'high' | 'medium'`).
    private static string StripCommentsAndStrings(string line)
    {
        // Strip trailing // comment (naive: doesn't handle `//` inside a
        // string, but C# event/DTO declarations in this repo never use `//`
        // inside a string literal — if they ever do, the stripper just
        // under-redacts a bit, which is safe for this ban test).
        var slashSlash = line.IndexOf("//", StringComparison.Ordinal);
        if (slashSlash >= 0) line = line[..slashSlash];

        // Collapse double-quoted literals. Again naive (no escape handling),
        // but deliberately so: any identifier that mysteriously appears only
        // inside a string literal on a declaration line is not a field name,
        // and we want the scan to ignore it.
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
        // 1. StudentState.cs
        var studentState = Path.Combine(
            repoRoot, "src", "actors", "Cena.Actors", "Students", "StudentState.cs");
        if (File.Exists(studentState)) yield return studentState;

        // 2. Events directory (every event DTO)
        var eventsDir = Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Events");
        if (Directory.Exists(eventsDir))
            foreach (var f in Directory.EnumerateFiles(eventsDir, "*.cs", SearchOption.TopDirectoryOnly))
                yield return f;

        // 3. API DTO + Response files under src/api/**
        var apiDir = Path.Combine(repoRoot, "src", "api");
        if (Directory.Exists(apiDir))
        {
            foreach (var f in Directory.EnumerateFiles(apiDir, "*Dto*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                // Exclude test fixtures — they intentionally exercise historical shapes.
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

    // Legacy allowlist — entries here are READ-ONLY legacy that the codebase
    // has retired from new writes but must still tolerate on-stream replay.
    //
    // Posture change 2026-04-20 (prr-013 backend retirement):
    //   * V1 readiness fields on `ExamSimulationSubmitted_V1` stay
    //     allowlisted because the record is `[Obsolete]` and retained
    //     solely for historical Marten replay. New emitters use
    //     `ExamSimulationSubmitted_V2` (no readiness bounds); V2 is NOT on
    //     this allowlist, so any regression that puts a readiness-shaped
    //     field back on V2 fails the build. See ADR-0043 "Sibling change
    //     2026-04-20: V1→V2 readiness field migration" for the full rationale.
    //   * MasteryDtos at-risk fields were physically removed from
    //     MasteryDtos.cs the same day, so they no longer need allowlisting.
    //   * ClassMasteryRollupDocument.AtRiskCount was removed from persistence
    //     and AtRiskStudentDocument was moved to Documents/Legacy/ as
    //     [Obsolete] + schema registration dropped from MartenConfiguration.
    //     None of those types are scanned by this arch test (persistence
    //     documents are not event DTOs / outbound contracts), so no
    //     allowlist entry is required.
    //   * Remaining Vue SPA at-risk pages are Phase 2 (separate task); they
    //     are not in this test's scan zone either.
    //
    // Do NOT add to this list without a follow-up task and an explicit
    // decision note. The default posture is "rename or move into
    // SessionRiskAssessment".
    private static readonly HashSet<(string RelPath, string Identifier)> LegacyAllowlist =
        new(new TupleComparer())
    {
        // ── src/actors/Cena.Actors/Events/ExamSimulationEvents.cs ──
        // Read-only historical replay only. V1 is [Obsolete]; V2 is the
        // clean replacement and is NOT allowlisted. See ADR-0043 sibling
        // change note (prr-013, 2026-04-20).
        (Rel("src/actors/Cena.Actors/Events/ExamSimulationEvents.cs"), "ReadinessLowerBound"),
        (Rel("src/actors/Cena.Actors/Events/ExamSimulationEvents.cs"), "ReadinessUpperBound"),
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
    public void PersistenceAndOutboundDtos_HaveNoRiskNamedFields()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in ScannedFiles(repoRoot))
        {
            var rel = Path.GetRelativePath(repoRoot, file);

            // The single authorised seam — SessionRiskAssessment — is not in
            // the scanned set, but skip it defensively in case Events or
            // Sessions conventions shift.
            if (rel.EndsWith("SessionRiskAssessment.cs", StringComparison.Ordinal))
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
                    if (LegacyAllowlist.Contains((rel, name))) continue;
                    violations.Add(
                        $"{rel}:{lineNumber} — field/type `{name}` matches prediction-surface ban. " +
                        "Either rename the field, or move the calculation into " +
                        "`SessionRiskAssessment` (session-scoped, see " +
                        "src/actors/Cena.Actors/Sessions/SessionRiskAssessment.cs). " +
                        "See prr-013 + RDY-020.");
                    break; // one violation per line is enough
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-013 persistence-surface ban violated. New risk/readiness-named fields appeared on");
            sb.AppendLine("persistence or outbound-DTO types. The honest-reality number + CI + N must live on");
            sb.AppendLine("`SessionRiskAssessment` inside the session actor, never on persisted/external surfaces.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }
}
