// =============================================================================
// Cena Platform — PromptCacheKeyCarriesTargetContextTest (prr-233)
//
// Architectural ratchet (EPIC-PRR-F / ADR-0050 §10): any code path that
// CONSTRUCTS a prompt-cache key in a TARGET-SCOPED context must route
// through IPromptCacheKeyContext — either implicitly via the ambient-context
// overload of PromptCacheKeyBuilder.For{Explanation,StudentContext}, or
// explicitly via the 4-arg overload with an examTargetCode sourced from the
// scheduler's resolved ActiveExamTargetId.
//
// Rationale
// ---------
// ADR-0050 §10 makes a StudentPlan a LIST of ExamTargets. A cached
// L2 explanation authored under (BAGRUT_MATH_5U) context would be
// mis-contextualised if served under (SAT_MATH) context — SAT questions
// have a different prompt template, a different difficulty curve, and the
// cache key must separate them. Without this rule, a target switch inside a
// session would silently serve stale cross-target LLM output.
//
// Enforcement model (mirrors PromptCacheUsedTest's approach)
// ---------------------------------------------------------
// 1. Find every *.cs under src/actors/Cena.Actors/ and src/api/ that
//    REFERENCES `PromptCacheKeyBuilder.ForExplanation(` OR
//    `PromptCacheKeyBuilder.ForStudentContext(`.
// 2. For each such file, require ONE of:
//      (a) The file also references `IPromptCacheKeyContext` (the ambient
//          overload takes this as a parameter), OR
//      (b) The file also references `ActiveExamTargetId` / `ExamTargetCode` /
//          `ExamCode.Value` (proving the call site threads a target
//          explicitly into the 4-arg overload), OR
//      (c) The class carries
//          `[PromptCacheKeyBypassesTargetContext("<reason>")]`.
// 3. Otherwise, build fails with a pointer to the missing wiring.
//
// Why only call sites, not the builder itself
// -------------------------------------------
// PromptCacheKeyBuilder is the construction primitive — it is always the
// thing that emits a key, so the builder trivially "references" itself.
// Asking "does the CALLER have target context?" is the right pivot. The
// scan therefore excludes PromptCacheKeyBuilder.cs.
//
// Allowlist maintenance
// ---------------------
// `[PromptCacheKeyBypassesTargetContext("...")]` requires a written reason,
// validated at construction, so every bypass is reviewable in PR. There is no
// parallel YAML allowlist — this avoids the drift mode where a YAML entry
// stops matching the file it used to cover.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class PromptCacheKeyCarriesTargetContextTest
{
    // Matches either PromptCacheKeyBuilder.ForExplanation( or
    // PromptCacheKeyBuilder.ForStudentContext( — the two target-scoped
    // construction entry points. ForSystemPrompt is intentionally NOT
    // matched: system prompts are target-independent by contract.
    private static readonly Regex KeyBuilderCallSite = new(
        @"PromptCacheKeyBuilder\.(ForExplanation|ForStudentContext)\s*\(",
        RegexOptions.Compiled);

    // Any of these tokens in the same file satisfies the "threads target
    // context" rule.
    private static readonly string[] TargetContextTokens =
    {
        "IPromptCacheKeyContext",
        "ActiveExamTargetId",
        "ExamTargetCode",
        "ExamCode.Value",
        "examTargetCode",
    };

    private const string OptOutMarker = "[PromptCacheKeyBypassesTargetContext";

    private static readonly string[] ScannedRoots =
    {
        Path.Combine("src", "actors", "Cena.Actors"),
        Path.Combine("src", "api"),
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Repo root not found — looked for CLAUDE.md or src/actors/Cena.Actors/.");
    }

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        foreach (var root in ScannedRoots)
        {
            var dir = Path.Combine(repoRoot, root);
            if (!Directory.Exists(dir)) continue;

            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(repoRoot, f);
                var sep = Path.DirectorySeparatorChar;
                if (rel.Contains($"{sep}bin{sep}")) continue;
                if (rel.Contains($"{sep}obj{sep}")) continue;
                if (rel.Contains($"{sep}Tests{sep}")) continue;
                if (rel.Contains($".Tests{sep}")) continue;
                // The builder itself trivially references itself; excluding it
                // keeps the "caller" pivot meaningful.
                if (rel.EndsWith($"{sep}PromptCacheKeyBuilder.cs", StringComparison.Ordinal)) continue;
                yield return f;
            }
        }
    }

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

    [Fact]
    public void EveryCacheKeyConstructionCallSite_RoutesThroughTargetContext_OrIsAllowlisted()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var scanned = 0;
        var withCalls = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            scanned++;
            var raw = File.ReadAllText(file);
            var stripped = StripCommentsAndStrings(raw);

            if (!KeyBuilderCallSite.IsMatch(stripped))
                continue;

            withCalls++;
            var rel = Path.GetRelativePath(repoRoot, file);

            // Class-level opt-out (reason required by attribute ctor).
            if (raw.Contains(OptOutMarker, StringComparison.Ordinal))
                continue;

            if (TargetContextTokens.Any(tok => stripped.Contains(tok, StringComparison.Ordinal)))
                continue;

            violations.Add(
                $"{rel} — constructs a prompt-cache key via PromptCacheKeyBuilder.For{{Explanation,StudentContext}} " +
                "but does not reference IPromptCacheKeyContext / ActiveExamTargetId / ExamTargetCode / " +
                "ExamCode.Value / examTargetCode, and is not annotated " +
                "[PromptCacheKeyBypassesTargetContext(\"reason\")]. See prr-233.");
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"prr-233 violation: {violations.Count} cache-key construction site(s) do not route " +
                "through IPromptCacheKeyContext.");
            sb.AppendLine(
                $"(scanned {scanned} files under src/actors/Cena.Actors and src/api, " +
                $"{withCalls} construct target-scoped cache keys)");
            sb.AppendLine();
            sb.AppendLine("Fix: either inject IPromptCacheKeyContext and use");
            sb.AppendLine("    PromptCacheKeyBuilder.ForExplanation(questionId, errorType, context)");
            sb.AppendLine("or thread the resolved target explicitly into the 4-arg overload");
            sb.AppendLine("    PromptCacheKeyBuilder.ForExplanation(..., tenantId, examCode.Value).");
            sb.AppendLine("If the call site legitimately constructs a key without an active target,");
            sb.AppendLine("annotate the class with:");
            sb.AppendLine("    [PromptCacheKeyBypassesTargetContext(\"<one-sentence justification>\")]");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }

        // Loud-failure sanity: the scan SHOULD find ≥ 1 call site once
        // callers migrate to PromptCacheKeyBuilder. Today (pre-migration of
        // ExplanationOrchestrator / PersonalizedExplanationService into the
        // unified seam) that number can legitimately be zero — we DO NOT
        // fail on zero so this test rolls out cleanly. When a caller
        // migrates, the ratchet begins gating new regressions automatically.
        _ = scanned; // document that scanned is intentionally unread on the zero-path.
    }
}
