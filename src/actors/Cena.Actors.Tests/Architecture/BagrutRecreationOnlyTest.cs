// =============================================================================
// Cena Platform — Bagrut-reference-only outbound-DTO ban (prr-008)
//
// Locks the 2026-04-15 decision (CLAUDE.md non-negotiable "Bagrut reference-only",
// ADR-bagrut-reference-only-enforcement): Ministry-published Bagrut items are
// reference-only. They never appear on student-facing DTOs — only AI-recreated
// items (produced via `BagrutRecreationAggregate` + CAS gate, ADR-0032) do.
//
// This test's "hard" version would check reachability from a `MinistryBagrut`-
// provenanced source. Threading provenance through the whole pipeline is
// Sprint-2 (see prr-008 "scope cuts"), so until then we enforce the
// "relaxed" shape from the task spec:
//
//   No student-facing DTO field name may match any of the banned identifiers
//   that would leak the Ministry reference into the student surface:
//     * `ministryBagrut` / `MinistryBagrut*`
//     * `BagrutReferenceId` / `bagrutReferenceId`
//     * `ministryExamId` / `MinistryExamId`
//     * `ministryCode` / `MinistryCode` (the raw Ministry item identifier)
//     * `moedSlug` / `MoedSlug` (only meaningful as part of a Ministry-source
//       reference; if a student-facing surface ever carries this, it is
//       leaking the reference link)
//
// Allowlist philosophy: the admin moderation / review-queue surface is NOT
// a student-facing surface. Any DTO under `src/api/Cena.Admin.Api.*` or
// `src/api/Cena.Api.Contracts/Admin/**` is explicitly out of scope — the
// Ministry reference is the whole point of expert review there.
//
// Surfaces scanned (the student-facing outbound zone):
//   1. src/api/Cena.Student.Api.Host/**/*Dto.cs / *Response.cs / *Payload.cs
//   2. src/api/Cena.Api.Contracts/Sessions/**/*.cs
//   3. src/api/Cena.Api.Contracts/Challenges/**/*.cs
//   4. src/api/Cena.Api.Contracts/Tutor/**/*.cs
//   5. src/api/Cena.Api.Contracts/Me/**/*.cs
//   6. src/api/Cena.Api.Contracts/Hub/**/*.cs
//
// How to fix a violation: run the item through `BagrutRecreationAggregate`
// (ADR-0032), surface the `RecreationId` / public attribution only, and
// keep `MinistryReference` buried inside the aggregate — never in an
// outbound DTO.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class BagrutRecreationOnlyTest
{
    // Identifier ban. Case-insensitive, anchored by non-identifier-char on both
    // sides so substrings embedded in unrelated names (e.g. `PanelCodeMinistry`)
    // don't false-positive, though the specific banned roots here are distinctive
    // enough that substring collision is unlikely.
    private static readonly Regex BannedIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>"
        + @"ministryBagrut[A-Za-z0-9_]*"
        + @"|MinistryBagrut[A-Za-z0-9_]*"
        + @"|bagrutReferenceId[A-Za-z0-9_]*"
        + @"|BagrutReferenceId[A-Za-z0-9_]*"
        + @"|ministryExamId[A-Za-z0-9_]*"
        + @"|MinistryExamId[A-Za-z0-9_]*"
        + @"|ministryCode[A-Za-z0-9_]*"
        + @"|MinistryCode[A-Za-z0-9_]*"
        + @"|moedSlug[A-Za-z0-9_]*"
        + @"|MoedSlug[A-Za-z0-9_]*"
        + @")\b",
        RegexOptions.Compiled);

    // Lines that look like pure comments (// / /// / block-comment interior).
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
        var apiDir = Path.Combine(repoRoot, "src", "api");
        if (!Directory.Exists(apiDir)) yield break;

        // Tier 1 — student-host suffix-matched files.
        var studentHost = Path.Combine(apiDir, "Cena.Student.Api.Host");
        if (Directory.Exists(studentHost))
        {
            foreach (var glob in new[] { "*Dto*.cs", "*Response*.cs", "*Payload*.cs" })
            {
                foreach (var f in Directory.EnumerateFiles(studentHost, glob, SearchOption.AllDirectories))
                {
                    if (IsExcluded(f)) continue;
                    yield return f;
                }
            }
        }

        // Tier 2 — explicitly student-facing Contracts namespaces.
        var contracts = Path.Combine(apiDir, "Cena.Api.Contracts");
        if (Directory.Exists(contracts))
        {
            foreach (var studentNs in new[] { "Sessions", "Challenges", "Tutor", "Me", "Hub" })
            {
                var subdir = Path.Combine(contracts, studentNs);
                if (!Directory.Exists(subdir)) continue;
                foreach (var f in Directory.EnumerateFiles(subdir, "*.cs", SearchOption.AllDirectories))
                {
                    if (IsExcluded(f)) continue;
                    yield return f;
                }
            }
        }
    }

    private static bool IsExcluded(string f) =>
        f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || f.EndsWith(".Tests.cs", StringComparison.Ordinal);

    [Fact]
    public void StudentFacingDtos_DoNotLeakMinistryBagrutReferenceFields()
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

                foreach (Match m in BannedIdentifier.Matches(line))
                {
                    var name = m.Groups["name"].Value;
                    violations.Add(
                        $"{rel}:{lineNumber} — field `{name}` leaks the Ministry-Bagrut "
                        + "reference onto a student-facing DTO. Route the item through "
                        + "BagrutRecreationAggregate (ADR-0032) and surface the recreation "
                        + "id / public attribution instead. See prr-008 + "
                        + "ADR-bagrut-reference-only-enforcement.");
                    break; // one violation per line is enough
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-008 Bagrut-reference-only outbound-DTO ban violated. A student-facing");
            sb.AppendLine("contract surface references a Ministry-source identifier. Per the 2026-04-15");
            sb.AppendLine("decision (CLAUDE.md non-negotiable 'Bagrut reference-only', ADR-0032), Ministry");
            sb.AppendLine("items are reference-only — students see AI-recreated items only.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }
}
