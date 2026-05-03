// =============================================================================
// Cena Platform — No raw Bagrut corpus text in student-facing DTOs (prr-242)
//
// ADR-0043 enforcement:
//   * Ministry-published past-Bagrut items are INTERNAL REFERENCE MATERIAL.
//   * The Marten document is BagrutCorpusItemDocument — its RawText field
//     is the OCR'd Ministry text, and it MUST NEVER appear on a student-
//     facing DTO.
//
// This arch test complements BagrutRecreationOnlyTest (which scans for
// Ministry-identifier field names). This one scans for any outbound-DTO
// surface that types a field as `BagrutCorpusItemDocument`, a collection
// of same, or a field named `RawText`/`RawBagrutText`/similar on a
// student-facing DTO.
//
// Scope mirrors BagrutRecreationOnlyTest:
//   * src/api/Cena.Student.Api.Host/**/*Dto*.cs + *Response*.cs + *Payload*.cs
//   * src/api/Cena.Api.Contracts/{Sessions,Challenges,Tutor,Me,Hub}/**/*.cs
//
// Admin surfaces (`Cena.Admin.Api*`, `Cena.Api.Contracts/Admin/**`) are
// out of scope — the admin curator queue IS the legitimate review surface
// for raw corpus text.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoRawBagrutTextInStudentResponseTest
{
    // Banned type references: a student-facing DTO must not declare a
    // property/field whose type is BagrutCorpusItemDocument (or a
    // collection / nullable variant thereof). We match conservatively on
    // the type name — false positives (e.g. a using-alias that shadows the
    // name) are acceptable, since the fix is cheap (rename or exclude).
    private static readonly Regex BannedType = new(
        @"\bBagrutCorpusItemDocument\b",
        RegexOptions.Compiled);

    // Banned field names on student-facing DTOs. These are the identifiers
    // a careless mapping might use to project the corpus row through.
    // Case-insensitive; must be delimited by non-identifier chars.
    private static readonly Regex BannedField = new(
        @"(?<![A-Za-z0-9_])(?<name>"
        + @"rawBagrutText[A-Za-z0-9_]*"
        + @"|RawBagrutText[A-Za-z0-9_]*"
        + @"|bagrutRawText[A-Za-z0-9_]*"
        + @"|BagrutRawText[A-Za-z0-9_]*"
        + @"|ministryRawText[A-Za-z0-9_]*"
        + @"|MinistryRawText[A-Za-z0-9_]*"
        + @"|bagrutCorpusText[A-Za-z0-9_]*"
        + @"|BagrutCorpusText[A-Za-z0-9_]*"
        + @"|bagrutCorpusItem[A-Za-z0-9_]*"
        + @"|BagrutCorpusItem[A-Za-z0-9_]*"
        + @")\b",
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
        var apiDir = Path.Combine(repoRoot, "src", "api");
        if (!Directory.Exists(apiDir)) yield break;

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

        var contracts = Path.Combine(apiDir, "Cena.Api.Contracts");
        if (Directory.Exists(contracts))
        {
            foreach (var ns in new[] { "Sessions", "Challenges", "Tutor", "Me", "Hub" })
            {
                var subdir = Path.Combine(contracts, ns);
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
    public void StudentFacingDtos_DoNotLeakRawBagrutCorpusText()
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

                if (BannedType.IsMatch(line))
                {
                    violations.Add(
                        $"{rel}:{lineNumber} — student-facing DTO references `BagrutCorpusItemDocument`. "
                        + "Raw Bagrut corpus text is internal per ADR-0043 — use the AiRecreation surface "
                        + "instead (see Cena.Actors.Content.BagrutRecreation + Deliverable<T>).");
                    continue;
                }

                foreach (Match m in BannedField.Matches(line))
                {
                    var name = m.Groups["name"].Value;
                    violations.Add(
                        $"{rel}:{lineNumber} — field `{name}` exposes raw Bagrut-corpus text on a "
                        + "student-facing DTO. Per ADR-0043, corpus text is reference-only and never "
                        + "reaches students; deliver the AI-recreated, CAS-verified form instead.");
                    break;
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-242 raw-Bagrut-text ban violated. A student-facing DTO references");
            sb.AppendLine("BagrutCorpusItemDocument or a banned raw-text field. Corpus text is");
            sb.AppendLine("INTERNAL reference material per ADR-0043; students see AI recreations.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }
}
