// =============================================================================
// Cena Platform — prr-102 emotional-state profile ban
//
// Invariant: emotional-state signals never live on a student's long-lived
// profile. This test is preventive — the 2026-04-20 pre-release review
// (prr-102, persona-ethics + persona-privacy consensus) verified that
// `StudentState` already carries no emotional-state fields. This arch test
// prevents the next engineer from adding one.
//
// ADR-0003 rationale: "emotional state" is an affective signal about a minor.
// Persisting it on a profile is a per-student behavioural profile of a child
// — the exact class of artefact the FTC v. Edmodo "Affected Work Product"
// decree flagged (2023), reinforced by ICO v. Reddit £14.47M (Feb 2026) under
// GDPR Art. 22, and further by the FTC 2025 COPPA Final Rule's explicit data
// minimisation requirement. Any legitimate use of an affective signal
// (safeguarding, distress detection) must be in-session only, ephemeral, and
// never projected onto a student-lifecycle surface.
//
// Surfaces scanned (the "ban zone"):
//   1. src/actors/Cena.Actors/Students/StudentState.cs
//   2. src/actors/Cena.Actors/Events/*.cs                 (event DTOs)
//   3. src/api/**/*Dto*.cs and *Response*.cs              (outbound contracts)
//
// Banned identifier prefixes (PascalCase token, case-sensitive):
//   EmotionalState, EmotionState, AffectiveState, MoodState,
//   EmotionalProfile, MoodProfile, FeelingState
//
// We deliberately do NOT ban bare "mood", "feeling", or "affect" — they
// appear in safeguarding-classifier category names (e.g. `emotional_distress`)
// and in prose comments about pedagogy. The ban pattern targets identifier
// names that would appear only as a property/field on a profile/DTO shape.
//
// How to fix a violation: if the field is a session-only signal, move it to
// the session actor (SessionRiskAssessment pattern). If it's a safeguarding
// classification result, route it through SafeguardingEscalation — which is
// session-scoped and already in compliance. Never put it on StudentState or
// any outbound profile DTO.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoEmotionalStateOnProfileTest
{
    private static readonly Regex BannedIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>" +
        @"EmotionalState[A-Za-z0-9_]*|" +
        @"EmotionState[A-Za-z0-9_]*|" +
        @"AffectiveState[A-Za-z0-9_]*|" +
        @"MoodState[A-Za-z0-9_]*|" +
        @"EmotionalProfile[A-Za-z0-9_]*|" +
        @"MoodProfile[A-Za-z0-9_]*|" +
        @"FeelingState[A-Za-z0-9_]*" +
        @")\b",
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
    public void PersistenceAndOutboundDtos_HaveNoEmotionalStateFields()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in ScannedFiles(repoRoot))
        {
            var rel = Path.GetRelativePath(repoRoot, file);

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
                        $"{rel}:{lineNumber} — field/type `{name}` matches emotional-state ban. " +
                        "Emotional/affective signals must NOT live on profile or outbound surfaces. " +
                        "ADR-0003 + prr-102: session-only, ephemeral, routed through " +
                        "SafeguardingEscalation (session-scoped) for safeguarding use-cases.");
                    break;
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-102 emotional-state profile ban violated. New emotional/affective-state fields");
            sb.AppendLine("appeared on persistence or outbound-DTO types. Emotional signals about minors must");
            sb.AppendLine("never cross the session boundary onto a profile (ADR-0003; FTC v. Edmodo 2023; ICO");
            sb.AppendLine("v. Reddit 2026; FTC COPPA Final Rule 2025).");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }
}
