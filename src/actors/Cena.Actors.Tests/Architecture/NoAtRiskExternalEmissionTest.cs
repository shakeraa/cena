// =============================================================================
// Cena Platform — No at-risk/readiness fields on outbound external adapters
// (prr-013, RDY-080, ADR-0003)
//
// RDY-080 + ADR-0003 demand that the session-scoped risk/readiness number
// NEVER leaves the in-session surface. This test enforces that seal at the
// adapter boundary: no outbound-integration payload, parent-notification body,
// webhook contract, SIS passback, SMS/email/WhatsApp outbound, or gradebook
// export may reference `SessionRiskAssessment` or carry a field matching the
// risk/readiness naming pattern.
//
// Surfaces scanned (outbound-adapter boundary):
//   1. src/actors/Cena.Actors/Outreach/*.cs
//   2. src/actors/Cena.Actors/Notifications/*.cs
//   3. any file anywhere under src/ matching *WebhookPayload*.cs,
//      *SmsPayload*.cs, *EmailPayload*.cs, *GradebookPayload*.cs,
//      *ClassroomPayload*.cs, *Passback*.cs
//
// If a future outbound adapter NEEDS a risk field, the answer is no.
// The teacher and the student see the number in-session. Nobody else, ever.
// See prr-013 and RDY-080 for the product rationale.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoAtRiskExternalEmissionTest
{
    private static readonly Regex BannedIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>(At)?Risk[A-Za-z0-9_]*|BagrutRisk[A-Za-z0-9_]*|PredictedScore[A-Za-z0-9_]*|Readiness[A-Za-z0-9_]*|SessionRiskAssessment)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        var actors = Path.Combine(repoRoot, "src", "actors", "Cena.Actors");

        // 1. Outreach directory (parent-notification scheduling)
        var outreachDir = Path.Combine(actors, "Outreach");
        if (Directory.Exists(outreachDir))
            foreach (var f in Directory.EnumerateFiles(outreachDir, "*.cs", SearchOption.AllDirectories))
                yield return f;

        // 2. Notifications directory (SMS/email/WebPush dispatchers)
        var notifDir = Path.Combine(actors, "Notifications");
        if (Directory.Exists(notifDir))
            foreach (var f in Directory.EnumerateFiles(notifDir, "*.cs", SearchOption.AllDirectories))
                yield return f;

        // 3. Payload-contract files anywhere under src/ (webhook, SMS, email,
        //    gradebook, classroom, SIS passback). Future adapters must add
        //    their file here OR follow the suffix convention to stay caught.
        var srcDir = Path.Combine(repoRoot, "src");
        if (Directory.Exists(srcDir))
        {
            var suffixGlobs = new[]
            {
                "*WebhookPayload*.cs",
                "*SmsPayload*.cs",
                "*EmailPayload*.cs",
                "*GradebookPayload*.cs",
                "*ClassroomPayload*.cs",
                "*Passback*.cs",
            };
            foreach (var glob in suffixGlobs)
            {
                foreach (var f in Directory.EnumerateFiles(srcDir, glob, SearchOption.AllDirectories))
                {
                    if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                    if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                    if (f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")) continue;
                    yield return f;
                }
            }
        }
    }

    [Fact]
    public void OutboundAdapters_DoNotEmitRiskOrReadinessFields()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var distinctFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in ScannedFiles(repoRoot))
        {
            if (!distinctFiles.Add(file)) continue;
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
                        $"{rel}:{lineNumber} — outbound-adapter surface references `{name}`. " +
                        "Session-risk data is in-surface-only (RDY-080): it must never leave the " +
                        "session actor to a parent SMS, gradebook passback, webhook, or SIS " +
                        "integration. Remove the field or re-route the caller. See prr-013.");
                    break;
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-013 external-emission ban violated. Outbound adapters must never carry");
            sb.AppendLine("risk/readiness fields or reference SessionRiskAssessment. The honest-reality");
            sb.AppendLine("number stays in-session (student + teacher during the session, nobody else).");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }
}
