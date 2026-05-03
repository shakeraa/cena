// =============================================================================
// Cena Platform — HintLadderEndpointUsesLadderTest (prr-203)
//
// Architecture ratchet that locks the prr-203 wiring in place. The new
// POST /api/sessions/{sid}/question/{qid}/hint/next endpoint (in
// src/api/Cena.Student.Api.Host/Endpoints/HintLadderEndpoint.cs) MUST:
//
//   1. Invoke the IHintLadderOrchestrator (the tier-dispatching layer).
//   2. NOT invoke the old inline IHintGenerator.Generate(...) path used
//      by the deprecated single-hint endpoint in SessionEndpoints.cs.
//   3. NOT import Anthropic.SDK / ITutorLlmService / any direct LLM seam
//      — all LLM traffic must flow through the orchestrator's tier-2 /
//      tier-3 generators (which each carry [TaskRouting]).
//
// Separately, L1TemplateHintGenerator.cs MUST NOT import ILlmClient or
// any LLM seam — ADR-0045 §3 pins L1 to tier 1 (no LLM). A refactor
// that folds an LLM call into L1 would be a ship-blocker.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class HintLadderEndpointUsesLadderTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found");
    }

    private static string ReadFile(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"Expected file at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Read file with comment lines stripped. We grep code, not prose —
    /// rationale comments legitimately name the types we are forbidding
    /// as imports (e.g. "must NOT import ILlmClient") and those mentions
    /// must not trip the ratchet.
    /// </summary>
    private static string ReadFileCodeOnly(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"Expected file at {path}");

        var lines = File.ReadAllLines(path);
        var buffer = new System.Text.StringBuilder(capacity: 4096);
        var inBlockComment = false;
        foreach (var raw in lines)
        {
            var line = raw;

            // Strip /* ... */ block comments (may span lines).
            if (inBlockComment)
            {
                var end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) continue;
                line = line[(end + 2)..];
                inBlockComment = false;
            }
            while (true)
            {
                var start = line.IndexOf("/*", StringComparison.Ordinal);
                if (start < 0) break;
                var end = line.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    line = line[..start];
                    inBlockComment = true;
                    break;
                }
                line = line[..start] + line[(end + 2)..];
            }

            // Strip // line comments (and skip full-line comments).
            var slashIdx = line.IndexOf("//", StringComparison.Ordinal);
            if (slashIdx >= 0) line = line[..slashIdx];

            if (line.Trim().Length == 0) continue;
            buffer.AppendLine(line);
        }
        return buffer.ToString();
    }

    [Fact]
    public void HintLadderEndpoint_invokes_orchestrator_and_declares_next_route()
    {
        var src = ReadFile(Path.Combine(
            "src", "api", "Cena.Student.Api.Host",
            "Endpoints", "HintLadderEndpoint.cs"));

        // 1. Endpoint declares the /hint/next sub-route.
        Assert.Matches(new Regex(@"/\{sessionId\}/question/\{questionId\}/hint/next"), src);

        // 2. Endpoint injects the orchestrator.
        Assert.Matches(new Regex(@"\bIHintLadderOrchestrator\b"), src);

        // 3. Endpoint invokes AdvanceAsync.
        Assert.Matches(new Regex(@"\borchestrator\s*\.\s*AdvanceAsync\s*\("), src);
    }

    [Fact]
    public void HintLadderEndpoint_does_not_invoke_inline_IHintGenerator()
    {
        // Deprecation ratchet: the new ladder route must NOT fall through
        // to the old inline-hint generator seam. The inline /hint endpoint
        // in SessionEndpoints.cs keeps that path for the deprecated route;
        // the new /hint/next route goes through the tier-dispatch
        // orchestrator only.
        var src = ReadFileCodeOnly(Path.Combine(
            "src", "api", "Cena.Student.Api.Host",
            "Endpoints", "HintLadderEndpoint.cs"));

        Assert.DoesNotMatch(new Regex(@"\bIHintGenerator\b"), src);
        Assert.DoesNotMatch(new Regex(@"\bhintGenerator\s*\.\s*Generate\s*\("), src);
    }

    [Fact]
    public void HintLadderEndpoint_does_not_import_LLM_seams_directly()
    {
        // LLM traffic must flow through the orchestrator's tier-2 / tier-3
        // generators, each [TaskRouting]-tagged. The endpoint itself must
        // not know about ILlmClient or ITutorLlmService — any direct import
        // here would bypass the routing ratchet for the ladder.
        var src = ReadFileCodeOnly(Path.Combine(
            "src", "api", "Cena.Student.Api.Host",
            "Endpoints", "HintLadderEndpoint.cs"));

        Assert.DoesNotMatch(new Regex(@"\bILlmClient\b"), src);
        Assert.DoesNotMatch(new Regex(@"\bITutorLlmService\b"), src);
        Assert.DoesNotMatch(new Regex(@"\bAnthropic\.SDK\b"), src);
        Assert.DoesNotMatch(new Regex(@"\busing\s+Anthropic\b"), src);
    }

    [Fact]
    public void L1TemplateHintGenerator_does_not_import_LLM_seams()
    {
        // ADR-0045 §3 invariant: L1 is strictly no-LLM. This ratchet catches
        // a refactor that "improves" L1 by adding a Sonnet call — the
        // $6.3k/month regression Dr. Kenji's review flagged.
        var src = ReadFileCodeOnly(Path.Combine(
            "src", "actors", "Cena.Actors",
            "Hints", "L1TemplateHintGenerator.cs"));

        Assert.DoesNotMatch(new Regex(@"\bILlmClient\b"), src);
        Assert.DoesNotMatch(new Regex(@"\bITutorLlmService\b"), src);
        Assert.DoesNotMatch(new Regex(@"\busing\s+Anthropic"), src);
        // Must NOT carry a [TaskRouting] attribute — tier 1 means no LLM,
        // which means no routing row.
        Assert.DoesNotMatch(new Regex(@"\[TaskRouting\("), src);
    }

    [Fact]
    public void L2HaikuHintGenerator_carries_tier2_routing_tag()
    {
        // Locks the L2 tier assignment so a refactor cannot silently promote
        // L2 to Sonnet. ADR-0045 §3 is the source of truth; the attribute
        // text is the ratchet.
        var src = ReadFile(Path.Combine(
            "src", "actors", "Cena.Actors",
            "Hints", "L2HaikuHintGenerator.cs"));

        Assert.Matches(new Regex(@"\[TaskRouting\(\s*""tier2""\s*,\s*""ideation_l2_hint""\s*\)\]"), src);
        Assert.Matches(new Regex(@"\[FeatureTag\(\s*""hint-l2""\s*\)\]"), src);
    }

    [Fact]
    public void L3WorkedExampleHintGenerator_carries_tier3_routing_tag()
    {
        // Locks the L3 tier assignment. ADR-0045 §3 pins L3 to Sonnet; the
        // attribute text is the ratchet that makes a silent tier demotion
        // (which would regress pedagogical quality) visible in CI.
        var src = ReadFile(Path.Combine(
            "src", "actors", "Cena.Actors",
            "Hints", "L3WorkedExampleHintGenerator.cs"));

        Assert.Matches(new Regex(@"\[TaskRouting\(\s*""tier3""\s*,\s*""worked_example_l3_hint""\s*\)\]"), src);
        Assert.Matches(new Regex(@"\[FeatureTag\(\s*""hint-l3""\s*\)\]"), src);
    }
}
