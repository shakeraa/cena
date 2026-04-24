// =============================================================================
// Cena Platform — Scheduler-path must not call any LLM (prr-149, ADR-0026)
//
// Invariant: AdaptiveScheduler and everything it calls transitively must
// remain a pure heuristic. Per ADR-0026 (LLM routing / cost controls) and
// the explicit Definition of Done on prr-149, the scheduler is NOT on
// the LLM critical path — every session pays the plan-generation cost in
// microseconds, not cents.
//
// Enforcement: scan the source files in Cena.Actors/Mastery and
// Cena.Actors/Sessions for any reference (using, qualified name,
// namespace) to types that name an LLM vendor/service. The scan is
// textual because a compiled-time reflection crawl over the scheduler
// call graph is both heavier and fragile against interface-behind-DI
// obfuscation. The textual scan catches the realistic failure mode —
// someone adding `_claude.Complete(...)` inside the scheduler's helpers.
//
// False-positive control: the test only scans the two directories that
// host the live scheduler path (Mastery/ + Sessions/) and excludes
// rationale/copy strings by stripping comments + string literals per
// the same policy NoAtRiskPersistenceTest already uses.
//
// Failure message:
//     "Scheduler path calls LLM service; violates prr-149 + ADR-0026
//      routing policy. Heuristic-only."
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class SchedulerNoLlmCallTest
{
    // Any identifier token that starts with one of these banned prefixes is
    // a violation. Anchored with a non-word boundary on both sides so
    // substrings inside unrelated identifiers (e.g. "unclaimed") do not
    // fire. Case-insensitive to catch both `LlmClient` and `llmClient`.
    private static readonly Regex BannedType = new(
        @"(?<![A-Za-z0-9_])(?<name>" +
        @"[A-Za-z0-9_]*Llm[A-Za-z0-9_]*|" +
        @"Anthropic[A-Za-z0-9_]*|" +
        @"Claude[A-Za-z0-9_]*|" +
        @"OpenAi[A-Za-z0-9_]*|" +
        @"OpenAI[A-Za-z0-9_]*|" +
        @"GroqClient|" +
        @"GeminiClient|" +
        @"BedrockClient" +
        @")(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    // Identifiers that look like they might match but are explicitly not
    // LLM wrappers. Add here (with a comment naming the file + rationale)
    // only when the ban is genuinely a false positive.
    private static readonly HashSet<string> Whitelist = new(StringComparer.Ordinal)
    {
        // `MasteryLlm*` would be a violation; nothing in Mastery/ or
        // Sessions/ currently trips this. Keeping the set empty is the
        // whole point — add to it only with a written justification.
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        var dirs = new[]
        {
            Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Mastery"),
            Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Sessions"),
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                yield return f;
            }
        }
    }

    private static readonly Regex LineCommentStart = new(@"^\s*(//|\*|/\*|\*/)", RegexOptions.Compiled);

    private static string StripCommentsAndStrings(string line)
    {
        // Strip trailing `//` inline comment.
        var slashSlash = line.IndexOf("//", StringComparison.Ordinal);
        if (slashSlash >= 0) line = line[..slashSlash];

        // Collapse any `"..."` literal so rationale copy / log format strings
        // do not trigger the scan. Naive but safe — identifiers in string
        // literals are not callable in any case.
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
    public void SchedulerPath_DoesNotReferenceLlmTypes()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in ScannedFiles(repoRoot))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            var lineNumber = 0;
            var inBlockComment = false;
            foreach (var rawLine in File.ReadLines(file))
            {
                lineNumber++;

                // Naive block-comment tracker: lines wholly inside a
                // /* ... */ block are skipped.
                if (inBlockComment)
                {
                    if (rawLine.Contains("*/")) inBlockComment = false;
                    continue;
                }
                if (rawLine.Contains("/*") && !rawLine.Contains("*/"))
                {
                    inBlockComment = true;
                    continue;
                }

                if (LineCommentStart.IsMatch(rawLine)) continue;

                var line = StripCommentsAndStrings(rawLine);
                foreach (Match m in BannedType.Matches(line))
                {
                    var name = m.Groups["name"].Value;
                    if (Whitelist.Contains(name)) continue;
                    violations.Add(
                        $"{rel}:{lineNumber} — identifier `{name}` matches the LLM-vendor ban. " +
                        "Scheduler path calls LLM service; violates prr-149 + ADR-0026 routing policy. " +
                        "Heuristic-only.");
                    break; // one violation per line is enough
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-149 + ADR-0026 violation: AdaptiveScheduler path introduces an LLM dependency.");
            sb.AppendLine("The scheduler is a pure heuristic; plan generation must not pay per-session LLM cost.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }
}
