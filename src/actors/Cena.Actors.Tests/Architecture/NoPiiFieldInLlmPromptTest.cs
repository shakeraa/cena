// =============================================================================
// Cena Platform — "No PII in LLM prompts" architecture ratchet (ADR-0047, prr-022)
//
// Two invariants enforced against every production class that constructs or
// consumes an LLM call:
//
//   INVARIANT 1 — no banned field identifier may appear in a [TaskRouting]-
//                 tagged file. The banned vocabulary is ADR-0047 §Decision 1;
//                 the regex below is the single machine-enforced source of
//                 truth. Adding a new root is a code-only change — there is
//                 NO YAML allowlist and the in-test allowlist array is
//                 intentionally empty (ADR-0047 §Enforcement "strict
//                 TODO-free policy").
//
//   INVARIANT 2 — every [TaskRouting]-tagged production class either injects
//                 IPiiPromptScrubber OR carries [PiiPreScrubbed("<reason>")]
//                 documenting the upstream scrub seam.
//
// Scanner shape mirrors CostMetricEmittedTest.cs and BagrutRecreationOnlyTest.cs
// (text-scan, comment/string-stripped, repo-root anchored). This matches the
// "simple, fast, review-visible" approach the existing architecture tests
// established — a Roslyn-based scan would be more precise but 100× slower and
// far harder to debug at 03:00 on Bagrut morning.
//
// Exit behaviour:
//   - Any banned-field hit → Assert.Fail with file:line + remediation hint.
//   - Any untagged service missing IPiiPromptScrubber and missing
//     [PiiPreScrubbed] → Assert.Fail with injection hint.
//   - Scanner finding zero [TaskRouting] classes → Assert.Fail (scanner
//     self-broke; same loud-failure convention as CostMetricEmittedTest).
//
// See docs/adr/0047-no-pii-in-llm-prompts.md.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoPiiFieldInLlmPromptTest
{
    // ── Banned-identifier regex (ADR-0047 Decision 1) ───────────────────────
    //
    // Grouped roots with word-boundary anchoring. Case-insensitive matching
    // keeps the rule symmetrical across C# (`StudentEmail`), JSON
    // (`studentEmail`), and snake_case leaks (`student_email` — disallowed
    // even though C# wouldn't normally use it).
    //
    // Deliberately does NOT list every camelCase/PascalCase variant —
    // RegexOptions.IgnoreCase covers both. The roots below are semantic
    // categories; the trailing `[A-Za-z0-9_]*` allows a field to be a
    // suffixed variant (e.g. `studentEmailEncrypted` is still a leak).
    private static readonly Regex BannedIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>" +
        // 1 — full name
        @"studentFullName[A-Za-z0-9_]*" +
        @"|fullName[A-Za-z0-9_]*" +
        // 2 — first name
        @"|studentFirstName[A-Za-z0-9_]*" +
        // 3 — last name / surname
        @"|studentLastName[A-Za-z0-9_]*" +
        @"|studentSurname[A-Za-z0-9_]*" +
        // 4 — email
        @"|studentEmail[A-Za-z0-9_]*" +
        @"|studentEmailAddress[A-Za-z0-9_]*" +
        // 5 — phone / mobile
        @"|studentPhone[A-Za-z0-9_]*" +
        @"|studentPhoneNumber[A-Za-z0-9_]*" +
        @"|studentMobile[A-Za-z0-9_]*" +
        // 6 — government ID (israeli ID, SSN, national ID)
        @"|governmentId[A-Za-z0-9_]*" +
        @"|israeliId[A-Za-z0-9_]*" +
        @"|nationalId[A-Za-z0-9_]*" +
        @"|teudatZehut[A-Za-z0-9_]*" +
        @"|\bSSN\b" +
        // 7 — exact birthdate
        @"|birthDate[A-Za-z0-9_]*" +
        @"|dateOfBirth[A-Za-z0-9_]*" +
        @"|studentDob[A-Za-z0-9_]*" +
        // 8 — home address
        @"|homeAddress[A-Za-z0-9_]*" +
        @"|streetAddress[A-Za-z0-9_]*" +
        @"|postalAddress[A-Za-z0-9_]*" +
        // 9 — parent / guardian PII
        @"|parentName[A-Za-z0-9_]*" +
        @"|parentEmail[A-Za-z0-9_]*" +
        @"|parentPhone[A-Za-z0-9_]*" +
        @"|guardianName[A-Za-z0-9_]*" +
        @"|guardianEmail[A-Za-z0-9_]*" +
        @"|guardianPhone[A-Za-z0-9_]*" +
        // 10 — school name/address (quasi-identifier for small districts)
        @"|schoolName[A-Za-z0-9_]*" +
        @"|schoolAddress[A-Za-z0-9_]*" +
        @")(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TaskRoutingAttribute = new(
        @"\[TaskRouting\s*\(", RegexOptions.Compiled);

    private static readonly Regex PiiPreScrubbedAttribute = new(
        @"\[PiiPreScrubbed\s*\(", RegexOptions.Compiled);

    // ── In-source allowlist (ADR-0047: empty by design) ─────────────────────
    //
    // Adding an entry here is a code-only change, visible in PR review. Any
    // non-empty allowlist must be paired with an ADR supersede or addendum —
    // see ADR-0047 §Enforcement.
    //
    // Each entry is a relative path (forward-slash, repo-root-anchored). The
    // file is exempted from the banned-identifier scan ONLY (it still
    // participates in the scrubber-injection check).
    private static readonly string[] BannedVocabularyAllowlist = Array.Empty<string>();

    // ── Repo root discovery (same convention as CostMetricEmittedTest) ──────
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Repo root not found — looked for CLAUDE.md or src/actors/Cena.Actors/.");
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
            // The attribute declarations + this test's own regex source contain
            // the banned tokens as documentation. Exempt them.
            if (rel.EndsWith($"{sep}TaskRoutingAttribute.cs")) continue;
            if (rel.EndsWith($"{sep}FeatureTagAttribute.cs")) continue;
            if (rel.EndsWith($"{sep}PiiPreScrubbedAttribute.cs")) continue;
            if (rel.EndsWith($"{sep}PiiPromptScrubber.cs")) continue;
            if (rel.EndsWith($"{sep}PiiPromptScrubberRegistration.cs")) continue;
            // prr-143: new attribute + propagator declarations reference
            // [TaskRouting] in XML-doc examples only.
            if (rel.EndsWith($"{sep}DelegatesTraceIdToAttribute.cs")) continue;
            if (rel.EndsWith($"{sep}LlmTraceContext.cs")) continue;
            yield return f;
        }
    }

    // ── Comment + string-literal stripping (text-scan fidelity) ─────────────
    //
    // The banned identifiers are field names, not prose. Comments that say
    // "never use studentEmail" must not false-positive. This is the same
    // pass as CostMetricEmittedTest's StripCommentsAndStrings, lifted to keep
    // the two ratchets consistent.
    private static string StripCommentsAndStrings(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var sb = new StringBuilder(text.Length);
        var inStr = false;
        var inLineComment = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inLineComment)
            {
                if (c == '\n') { inLineComment = false; sb.Append(c); }
                continue;
            }
            if (inStr)
            {
                if (c == '"' && (i == 0 || text[i - 1] != '\\')) { inStr = false; sb.Append('"'); }
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (c == '"') { inStr = true; sb.Append('"'); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ── Scrubber-injection detection ────────────────────────────────────────
    //
    // We accept any of:
    //   - A constructor parameter typed IPiiPromptScrubber / PiiPromptScrubber
    //   - A field typed IPiiPromptScrubber (common pattern: `private readonly
    //     IPiiPromptScrubber _piiScrubber;`)
    //   - A call to `.Scrub(` on an IPiiPromptScrubber (last-ditch — catches
    //     services that construct the scrubber internally)
    //
    // Same regex-style detection as CostMetricEmittedTest's "Record(" tokens.
    private static readonly string[] ScrubberInjectionTokens =
    {
        "IPiiPromptScrubber",
        "PiiPromptScrubber ",
    };

    [Fact]
    public void NoTaskRoutingClass_ReferencesBannedPiiIdentifiers()
    {
        var repoRoot = FindRepoRoot();
        var allowlist = new HashSet<string>(BannedVocabularyAllowlist, StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();
        var withTaskRouting = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            var raw = File.ReadAllText(file);
            if (!TaskRoutingAttribute.IsMatch(raw)) continue;
            withTaskRouting++;

            var rel = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            if (allowlist.Contains(rel)) continue;

            var stripped = StripCommentsAndStrings(raw);
            var lines = stripped.Split('\n');

            // Scrubber-configuration seam exemption. A file that instantiates
            // `new StudentPiiContext(... FirstName: ..., Email: ...)` uses the
            // banned identifiers as named constructor parameters — the point is
            // to FEED the scrubber, not to leak. Record-style construction
            // spans multiple source lines (one named parameter per line), so
            // a line-local exemption is insufficient — we check a 12-line
            // preceding window for the `StudentPiiContext` marker.
            const int scrubberConfigWindow = 12;

            // Line-by-line so the error message can cite the exact line.
            for (var lineNo = 0; lineNo < lines.Length; lineNo++)
            {
                var line = lines[lineNo];
                var match = BannedIdentifier.Match(line);
                if (!match.Success) continue;

                var inScrubberConfig = false;
                for (var j = Math.Max(0, lineNo - scrubberConfigWindow); j <= lineNo; j++)
                {
                    if (lines[j].Contains("StudentPiiContext", StringComparison.Ordinal))
                    {
                        inScrubberConfig = true;
                        break;
                    }
                }
                if (inScrubberConfig) continue;

                var name = match.Groups["name"].Value;
                violations.Add(
                    $"{rel}:{lineNo + 1} — banned PII identifier `{name}` appears in a " +
                    "[TaskRouting]-tagged file. Replace with the structured placeholder " +
                    "defined in ADR-0047 §Decision 2 (e.g. {{student_pseudonym}}, " +
                    "{{age_band}}, {{subject}}).");
            }
        }

        Assert.True(
            withTaskRouting > 0,
            "NoPiiFieldInLlmPromptTest found zero [TaskRouting] classes under src/. " +
            "Scanner likely broken — check the TaskRoutingAttribute regex and the scan root.");

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"ADR-0047 violation: {violations.Count} banned-PII-identifier hit(s) in {withTaskRouting} [TaskRouting]-tagged file(s).");
        sb.AppendLine();
        sb.AppendLine("The banned vocabulary is locked by ADR-0047 §Decision 1. Fix options:");
        sb.AppendLine("  (a) Replace the raw field with a structured placeholder (§Decision 2).");
        sb.AppendLine("  (b) Remove the field from the prompt entirely; rely on free-text user input.");
        sb.AppendLine("  (c) Raise an ADR addendum if the ban is genuinely wrong for this seam —");
        sb.AppendLine("      do NOT silently add the file to BannedVocabularyAllowlist.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }

    [Fact]
    public void EveryTaskRoutingClass_InjectsPiiScrubber_OrCarriesPiiPreScrubbedAttribute()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var withTaskRouting = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            var raw = File.ReadAllText(file);
            if (!TaskRoutingAttribute.IsMatch(raw)) continue;
            withTaskRouting++;

            var rel = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');

            // Opt-out path: [PiiPreScrubbed("<reason>")]
            if (PiiPreScrubbedAttribute.IsMatch(raw)) continue;

            // Injection path: references IPiiPromptScrubber somewhere in the file.
            var stripped = StripCommentsAndStrings(raw);
            if (ScrubberInjectionTokens.Any(t => stripped.Contains(t, StringComparison.Ordinal)))
                continue;

            violations.Add(
                $"{rel} — carries [TaskRouting(...)] but neither injects " +
                "IPiiPromptScrubber nor declares [PiiPreScrubbed(\"<reason>\")]. " +
                "Either inject the scrubber and call it on the outgoing prompt, or " +
                "(if an upstream collaborator already scrubs) annotate the class " +
                "with [PiiPreScrubbed(\"<upstream-seam-name>\")]. See ADR-0047 §Decision 3.");
        }

        Assert.True(
            withTaskRouting > 0,
            "NoPiiFieldInLlmPromptTest scrubber-injection scan found zero [TaskRouting] classes. " +
            "Scanner broken.");

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"ADR-0047 violation: {violations.Count} [TaskRouting] class(es) miss PII scrubber wiring.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }
}
