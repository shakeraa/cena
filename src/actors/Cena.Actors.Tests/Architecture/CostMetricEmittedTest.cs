// =============================================================================
// Cena Platform — prr-046 cost-metric ratchet
//
// Invariant: every class carrying [TaskRouting(...)] in production code must
// emit the per-feature cost counter on its success path, UNLESS it carries
// [DelegatesLlmCost("...")] (the LLM call is owned by a collaborator that
// does emit).
//
// Same textual-scan approach used by SocraticCapEnforcedTest + PromptCacheUsedTest:
//   1. Walk every *.cs under src/, excluding bin/obj/worktrees/tests.
//   2. For each file containing [TaskRouting:
//        a. If the file also contains [DelegatesLlmCost( → ok (delegation).
//        b. Otherwise require at least one of:
//             - `_costMetric.Record(` or similar field names ending in
//               `_cost` / `_costMetric` / `_featureCost`
//             - direct `ILlmCostMetric.Record(` call
//             - `CostCounterName` reference (programmatic emission)
//           If none found → violation.
//
// Reason why grep-style rather than Roslyn:
//   Consistent with existing architecture tests, fast (<200ms), and catches
//   the realistic failure mode — a dev adds a new [TaskRouting]-tagged
//   service but forgets to wire the cost metric. Reflection/Roslyn would
//   miss the textual requirement "emission must be a visible call site".
//
// Deliberate safety net: the test also asserts that ≥1 [TaskRouting] class
// was scanned. If the codebase ever reshapes so zero tagged classes exist
// under src/, the scanner is broken — we fail loudly rather than silently
// pass.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class CostMetricEmittedTest
{
    private static readonly Regex TaskRoutingAttribute = new(
        @"\[TaskRouting\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex DelegatesLlmCostAttribute = new(
        @"\[DelegatesLlmCost\s*\(",
        RegexOptions.Compiled);

    // Any of these tokens in the same file satisfies the "cost emitted" rule.
    // We are not trying to prove that every success path carries an emission —
    // only that the service is wired to emit at all. The realistic failure
    // mode (new service forgot to inject ILlmCostMetric) is caught by this.
    private static readonly string[] CostEmissionTokens =
    {
        "_costMetric.Record(",
        "_featureCost.Record(",
        "_featureCost?.Record(",
        "_cost.Record(",
        "ILlmCostMetric.Record(",
        // Paranoid belt-and-braces: callers that expose a helper.
        "EmitLlmCost(",
    };

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

        // NOTE: we intentionally exclude by RELATIVE path rather than raw
        // substring. When this test runs inside a `.claude/worktrees/…`
        // checkout, every absolute path contains "worktrees" — a
        // substring-based exclusion on that token would match EVERY file and
        // we'd silently pass with zero scanned classes. Relative path is the
        // only reliable anchor: the nested-worktree directory (`worktrees/`
        // under `.claude/`) does not appear inside `src/` under ANY valid
        // checkout, so the scan root is self-contained.
        foreach (var f in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, f);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            // Tests + fixtures — not production call sites.
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            if (rel.Contains($"{sep}fixtures{sep}")) continue;
            // The attribute declarations themselves (they reference the
            // [TaskRouting] token in XML-doc or as a pattern, not as a
            // class attribute).
            if (rel.EndsWith($"{sep}TaskRoutingAttribute.cs")) continue;
            if (rel.EndsWith($"{sep}FeatureTagAttribute.cs")) continue;
            yield return f;
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
    public void EveryTaskRoutingClass_EmitsCostMetric_OrDelegatesToOneThatDoes()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var scanned = 0;
        var withTaskRouting = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            scanned++;
            var raw = File.ReadAllText(file);
            // Check attribute presence in the raw source (attributes sometimes
            // sit inside XML-doc regions which the strip pass removes).
            if (!TaskRoutingAttribute.IsMatch(raw)) continue;

            withTaskRouting++;
            var rel = Path.GetRelativePath(repoRoot, file);

            // Delegation path: explicit [DelegatesLlmCost(...)] opt-out.
            if (DelegatesLlmCostAttribute.IsMatch(raw)) continue;

            var stripped = StripCommentsAndStrings(raw);
            if (CostEmissionTokens.Any(t => stripped.Contains(t, StringComparison.Ordinal))) continue;

            violations.Add(
                $"{rel} — carries [TaskRouting(...)] but does not call " +
                "ILlmCostMetric.Record(...) anywhere in the file, and is not " +
                "annotated [DelegatesLlmCost(\"<inner-service>\")]. " +
                "See prr-046 / ADR-0026 §7.");
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"prr-046 violation: {violations.Count} [TaskRouting] class(es) do not emit the per-feature cost metric.");
            sb.AppendLine(
                $"(scanned {scanned} production .cs files, {withTaskRouting} carry [TaskRouting])");
            sb.AppendLine();
            sb.AppendLine("Fix: inject ILlmCostMetric and call _costMetric.Record(feature, tier, task, modelId, inputTokens, outputTokens)");
            sb.AppendLine("on the LLM call's success path, OR annotate the class with:");
            sb.AppendLine("    [DelegatesLlmCost(\"<InnerServiceName>\")]");
            sb.AppendLine("if the LLM call is owned by a collaborator that emits.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }

        // Loud-failure sanity check: zero scanned tagged classes means the
        // file-layout changed or the regex broke.
        Assert.True(
            withTaskRouting > 0,
            "CostMetricEmittedTest found zero [TaskRouting] classes under src/. " +
            "Scanner likely broken — check the TaskRoutingAttribute regex and the scan root.");
    }
}
