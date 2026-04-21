// =============================================================================
// Cena Platform — prr-143 trace-id ratchet
//
// Invariant: every class carrying [TaskRouting(...)] in production code must
// either (a) reference IActivityPropagator / GetTraceId( in its own source,
// OR (b) carry [DelegatesTraceIdTo("<collaborator>")].
//
// Rationale (prr-143, persona-sre + persona-finops, 2026-04-20 pre-release
// review): when a Bagrut-morning incident fires at 03:00, the on-call
// engineer must pivot from a single failed student turn to the full LLM
// fan-out (hint ladder → Socratic tutor → CAS gate → explanation cache)
// without reconstructing session IDs. Trace-id is the cheapest stitching key
// — one label on every metric, one tag on every span. Without this ratchet a
// new [TaskRouting] service can ship that is invisible to cross-call tracing.
//
// Scan approach mirrors CostMetricEmittedTest exactly so the two ratchets
// age in lock-step:
//   1. Walk every *.cs under src/, excluding bin/obj/tests/fixtures.
//   2. Skip the attribute declarations themselves (TaskRoutingAttribute.cs,
//      DelegatesTraceIdToAttribute.cs, LlmTraceContext.cs).
//   3. For each file carrying [TaskRouting:
//        a. If [DelegatesTraceIdTo(…)] present → ok (delegation path).
//        b. Otherwise require at least one token:
//             - IActivityPropagator  (injection)
//             - GetTraceId(          (direct call)
//           If none found → violation.
//
// Loud-failure safety net: we also assert ≥1 [TaskRouting] class was
// scanned — zero means the file layout changed and the scanner is broken.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class EveryLlmServiceEmitsTraceIdTest
{
    private static readonly Regex TaskRoutingAttribute = new(
        @"\[TaskRouting\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex DelegatesTraceIdAttribute = new(
        @"\[DelegatesTraceIdTo\s*\(",
        RegexOptions.Compiled);

    private static readonly string[] TracePropagationTokens =
    {
        "IActivityPropagator",
        "GetTraceId(",
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

        foreach (var f in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, f);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            if (rel.Contains($"{sep}fixtures{sep}")) continue;
            if (rel.EndsWith($"{sep}TaskRoutingAttribute.cs")) continue;
            if (rel.EndsWith($"{sep}FeatureTagAttribute.cs")) continue;
            if (rel.EndsWith($"{sep}DelegatesTraceIdToAttribute.cs")) continue;
            // The propagator declaration itself references IActivityPropagator
            // and GetTraceId — exclude it so we don't tautologically count it
            // as a scanned TaskRouting file (it isn't tagged anyway, but
            // keep the exclusion explicit for future readers).
            if (rel.EndsWith($"{sep}LlmTraceContext.cs")) continue;
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
    public void EveryTaskRoutingClass_StampsTraceId_OrDelegatesToOneThatDoes()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var scanned = 0;
        var withTaskRouting = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            scanned++;
            var raw = File.ReadAllText(file);
            if (!TaskRoutingAttribute.IsMatch(raw)) continue;

            withTaskRouting++;
            var rel = Path.GetRelativePath(repoRoot, file);

            // Explicit opt-out via [DelegatesTraceIdTo("collaborator")].
            if (DelegatesTraceIdAttribute.IsMatch(raw)) continue;

            var stripped = StripCommentsAndStrings(raw);
            if (TracePropagationTokens.Any(t => stripped.Contains(t, StringComparison.Ordinal)))
                continue;

            violations.Add(
                $"{rel} — carries [TaskRouting(...)] but does not reference " +
                "IActivityPropagator / GetTraceId(...) anywhere in the file, and is " +
                "not annotated [DelegatesTraceIdTo(\"<inner-service>\")]. " +
                "See prr-143 / ADR-0026 §7.");
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"prr-143 violation: {violations.Count} [TaskRouting] class(es) do not stamp trace_id on their LLM calls.");
            sb.AppendLine(
                $"(scanned {scanned} production .cs files, {withTaskRouting} carry [TaskRouting])");
            sb.AppendLine();
            sb.AppendLine("Fix: inject IActivityPropagator and call _activityPropagator.GetTraceId()");
            sb.AppendLine("on the LLM call attempt, OR annotate the class with:");
            sb.AppendLine("    [DelegatesTraceIdTo(\"<InnerServiceName>\")]");
            sb.AppendLine("if the LLM call is owned by a collaborator that stamps.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }

        // Loud-failure sanity check: zero scanned tagged classes means the
        // file-layout changed or the regex broke.
        Assert.True(
            withTaskRouting > 0,
            "EveryLlmServiceEmitsTraceIdTest found zero [TaskRouting] classes under src/. " +
            "Scanner likely broken — check the TaskRoutingAttribute regex and the scan root.");
    }
}
