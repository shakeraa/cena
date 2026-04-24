// =============================================================================
// Cena Platform — Reflective Text PII Scrubbed Architecture Test (prr-036)
//
// Invariant: every class under src/ decorated with [HandlesReflectiveText]
// MUST either inject IReflectiveTextScrubber directly OR declare its upstream
// scrub seam via [ReflectiveTextPreScrubbed("<reason>")]. The pattern mirrors
// NoPiiFieldInLlmPromptTest's ADR-0047 scrubber-injection invariant, adapted
// for the reflective-text surface.
//
// Also asserts the scrubber itself exists at the expected path and exposes
// both persistence and LLM-egress entrypoints — a regression that deletes
// ScrubForPersistence would silently turn the invariant into a no-op.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class ReflectiveTextPiiScrubbedTest
{
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
        throw new InvalidOperationException("Repo root not found.");
    }

    private static readonly Regex HandlesReflectiveTextAttr = new(
        @"\[HandlesReflectiveText\s*\(", RegexOptions.Compiled);

    private static readonly Regex PreScrubbedAttr = new(
        @"\[ReflectiveTextPreScrubbed\s*\(", RegexOptions.Compiled);

    private static readonly string[] ScrubberInjectionTokens =
    {
        "IReflectiveTextScrubber",
        "ReflectiveTextScrubber ",
    };

    // Comment/string stripper lifted verbatim from NoPiiFieldInLlmPromptTest so
    // both ratchets use the same text-fidelity pass.
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
            // The scrubber + attribute declarations themselves mention the
            // tokens in their own source; exempt them from the injection check.
            if (rel.EndsWith($"{sep}ReflectiveTextScrubber.cs")) continue;
            if (rel.EndsWith($"{sep}ReflectiveTextScrubberRegistration.cs")) continue;
            yield return f;
        }
    }

    [Fact]
    public void ReflectiveTextScrubber_Exists_ExposesBothEntrypoints()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot,
            "src", "shared", "Cena.Infrastructure", "Privacy", "ReflectiveTextScrubber.cs");
        Assert.True(File.Exists(file),
            $"prr-036 violation: ReflectiveTextScrubber.cs missing at {file}. " +
            "Student reflective text must pass through this scrubber before " +
            "persistence OR LLM send.");

        var src = File.ReadAllText(file);
        Assert.Matches(new Regex(@"\bInterface\b|\binterface\s+IReflectiveTextScrubber\b"), src);
        Assert.Matches(new Regex(@"\bScrubForPersistence\s*\("), src);
        Assert.Matches(new Regex(@"\bScrubForLlm\s*\("), src);

        // Both the persistence and the LLM-egress counter names must be
        // present — they are the observability contract on-call relies on.
        Assert.Contains("cena_reflective_text_pii_scrubbed_total", src);
        Assert.Contains("cena_reflective_text_llm_scrubbed_total", src);
    }

    [Fact]
    public void EveryHandlesReflectiveTextClass_InjectsScrubber_OrCarriesPreScrubbedAttribute()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var seen = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            var raw = File.ReadAllText(file);
            if (!HandlesReflectiveTextAttr.IsMatch(raw)) continue;
            seen++;

            var rel = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');

            // Opt-out path: [ReflectiveTextPreScrubbed("<reason>")]
            if (PreScrubbedAttr.IsMatch(raw)) continue;

            var stripped = StripCommentsAndStrings(raw);
            if (ScrubberInjectionTokens.Any(t => stripped.Contains(t, StringComparison.Ordinal)))
                continue;

            violations.Add(
                $"{rel} — carries [HandlesReflectiveText(...)] but neither injects " +
                "IReflectiveTextScrubber nor declares [ReflectiveTextPreScrubbed(\"<reason>\")]. " +
                "Fix: add `IReflectiveTextScrubber` to the constructor and call " +
                "`ScrubForPersistence` before Marten writes / `ScrubForLlm` before " +
                "LLM egress. If an upstream already scrubs, annotate with " +
                "[ReflectiveTextPreScrubbed(\"<seam>\")] instead. See prr-036.");
        }

        // The seen-count guard is deliberately low (≥ 0) — this invariant is
        // opt-in by attribute, and a fresh branch may have zero annotated
        // classes. The arch test guards the wiring, not the annotation count.
        _ = seen;

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"prr-036 violation: {violations.Count} [HandlesReflectiveText] class(es) " +
            "miss reflective-text scrubber wiring.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }
}
