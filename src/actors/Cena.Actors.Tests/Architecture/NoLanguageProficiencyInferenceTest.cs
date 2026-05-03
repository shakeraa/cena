// =============================================================================
// Cena Platform — No language-proficiency inference of students (prr-167)
//
// Policy: Cena is a mathematics platform. It has no business scoring a
// student's English / Hebrew / Arabic / Ivrit-language PROFICIENCY, FLUENCY,
// LEVEL, or GRADE from their math-problem responses. Inferring language level
// from math text is a special-category data use that:
//
//   1. Violates the minimum-necessary principle — language fluency is not
//      required to grade a math response, so the platform has no legitimate
//      interest in inferring it.
//   2. Risks mis-inferring disability — a student with dyslexia may produce
//      math-response text that a naive NLP classifier scores as "low
//      English proficiency", which is a clinical-adjacent inference
//      banned by the therapeutic-claims rule pack.
//   3. Erodes the CAS oracle's correctness framing — a student's math
//      answer is EITHER correct or not (ADR-0002); whether their surrounding
//      prose is B1 or C1 is irrelevant to that verdict.
//
// The ship-gate rule pack `positive-framing-extended.yml` flags the copy-level
// variants (banned phrases in UI strings). This architecture test flags the
// CODE-LEVEL variants: field names, class names, method names, DTO properties,
// and typed enums that would enable proficiency scoring even if no UI string
// exposed it.
//
// Scanned surface: all C# production code under src/ (tests excluded).
//
// Banned identifier patterns (all case-insensitive, word-boundary anchored):
//   * englishProficiency / EnglishProficiency
//   * hebrewProficiency  / HebrewProficiency
//   * arabicProficiency  / ArabicProficiency
//   * ivritProficiency   / IvritProficiency
//   * l1Proficiency / l2Proficiency (language-identity proficiency scoring)
//   * languageLevelScore / LanguageLevelScore
//   * cefrLevel          / CefrLevel (CEFR is the proficiency framework used
//                          for A1/A2/B1/B2/C1/C2 scoring)
//
// How to fix a violation: remove the field. If a legitimate seam genuinely
// needs to know the student's UI language, use the existing
// `AccommodationProfile` accessor set and the session-locale header — those
// describe the RENDERING target, not the student's proficiency.
//
// Allowlist: empty by design. Adding a field that SCORES language proficiency
// requires a new ADR that supersedes ADR-0003 (special-category-data minimum-
// necessary principle). See NoPiiFieldInLlmPromptTest.cs for the same
// "strict TODO-free policy" enforcement pattern.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoLanguageProficiencyInferenceTest
{
    // Banned-identifier regex (case-insensitive). Each root covers the
    // PascalCase / camelCase / snake_case permutations via the trailing
    // [A-Za-z0-9_]* continuation and the IgnoreCase flag. The regex deliberately
    // does NOT flag bare "language" or "level" — those are used legitimately
    // across the codebase (UI locale, difficulty level, grade level). It flags
    // only the COMPOUND pattern: a natural-language-identifier root concatenated
    // with a proficiency-framework root.
    private static readonly Regex BannedIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>"
        // 1 — language + proficiency/fluency/level-score compounds
        + @"english(Proficiency|Fluency|LevelScore|GradeLevel|CefrLevel)[A-Za-z0-9_]*"
        + @"|hebrew(Proficiency|Fluency|LevelScore|GradeLevel|CefrLevel)[A-Za-z0-9_]*"
        + @"|arabic(Proficiency|Fluency|LevelScore|GradeLevel|CefrLevel)[A-Za-z0-9_]*"
        + @"|ivrit(Proficiency|Fluency|LevelScore|GradeLevel|CefrLevel)[A-Za-z0-9_]*"
        + @"|msa(Proficiency|Fluency|LevelScore|CefrLevel)[A-Za-z0-9_]*"
        // 2 — L1/L2 language-identity proficiency scoring
        + @"|l1Proficiency[A-Za-z0-9_]*"
        + @"|l2Proficiency[A-Za-z0-9_]*"
        + @"|L1Proficiency[A-Za-z0-9_]*"
        + @"|L2Proficiency[A-Za-z0-9_]*"
        // 3 — Generic language-level/proficiency scoring fields
        + @"|languageProficiency[A-Za-z0-9_]*"
        + @"|languageLevelScore[A-Za-z0-9_]*"
        + @"|languageFluencyScore[A-Za-z0-9_]*"
        // 4 — CEFR framework references applied to STUDENTS (student-scoped
        // CEFR scoring is the specific banned shape; translating UI strings
        // against a CEFR locale identifier is a separate thing).
        + @"|studentCefr[A-Za-z0-9_]*"
        + @"|studentLanguageLevel[A-Za-z0-9_]*"
        + @"|studentEnglishLevel[A-Za-z0-9_]*"
        + @"|studentHebrewLevel[A-Za-z0-9_]*"
        + @"|studentArabicLevel[A-Za-z0-9_]*"
        + @")(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Allowlist: empty by design. See header.
    private static readonly string[] Allowlist = Array.Empty<string>();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcRoot)) yield break;

        foreach (var f in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, f);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            if (rel.Contains($"{sep}fixtures{sep}")) continue;
            yield return f;
        }
    }

    // Strip // line comments and "..." string literals from each line so the
    // scan matches identifiers in source code only, not in documentation or
    // user-facing strings (the rule-pack scanner already covers strings).
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

    [Fact]
    public void NoCSharpProductionFile_DeclaresLanguageProficiencyField()
    {
        var repoRoot = FindRepoRoot();
        var allowlist = new HashSet<string>(Allowlist, StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();
        var filesScanned = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            filesScanned++;
            var rel = Path.GetRelativePath(repoRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (allowlist.Contains(rel)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var stripped = StripCommentsAndStrings(lines[i]);
                var match = BannedIdentifier.Match(stripped);
                if (!match.Success) continue;

                var name = match.Groups["name"].Value;
                violations.Add(
                    $"{rel}:{i + 1} — banned language-proficiency identifier `{name}`. " +
                    "Cena is a math platform; scoring a student's language proficiency " +
                    "violates the minimum-necessary principle (ADR-0003) and risks " +
                    "mis-inferring disability. If you need the UI locale, use " +
                    "AccommodationProfile + session-locale header instead — those describe " +
                    "the RENDERING target, not the student's proficiency.");
            }
        }

        Assert.True(
            filesScanned > 0,
            "NoLanguageProficiencyInferenceTest scanned zero .cs files under src/. Scanner broken.");

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"prr-167 violation: {violations.Count} language-proficiency-inference identifier(s) in production code.");
        sb.AppendLine();
        sb.AppendLine("Cena does not score a student's language proficiency. Fix options:");
        sb.AppendLine("  (a) Remove the field. Language proficiency is not in scope for a math platform.");
        sb.AppendLine("  (b) If you need the RENDERING locale (UI language), use the existing");
        sb.AppendLine("      session-locale header + AccommodationProfile accessors — those do not");
        sb.AppendLine("      SCORE the student, they select a rendering channel.");
        sb.AppendLine("  (c) If you genuinely need to score proficiency for a legitimate product");
        sb.AppendLine("      reason, raise an ADR that supersedes ADR-0003 §minimum-necessary.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }
}
