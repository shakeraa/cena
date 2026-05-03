// =============================================================================
// Cena Platform — Peer Explanation CAS-gated Architecture Test (prr-025, ADR-0002)
//
// Invariant: every code path that delivers a peer-authored explanation to
// another student's feed MUST route the submission through
// ICasRouterService.VerifyAsync FIRST, and MUST have a moderation-queue
// fallback for CAS-disagreed / CAS-unavailable outcomes.
//
// Enforcement is text-scan (same convention as NoPiiFieldInLlmPromptTest,
// SocraticCapEnforcedTest, and CostMetricEmittedTest):
//
//   1. The PeerExplanationPipeline class must exist under
//      src/actors/Cena.Actors/Collaboration/.
//   2. The class must declare ICasRouterService as a collaborator.
//   3. The class must declare IPeerExplanationModerationQueue.
//   4. It must declare IPeerExplanationFeed.
//   5. Ordering: the first IPeerExplanationFeed.PublishAsync call MUST appear
//      AFTER the first ICasRouterService.VerifyAsync call. A regression that
//      publishes before verifying would bypass the CAS gate entirely.
//   6. No OTHER class under src/actors/ and src/api/ may call
//      IPeerExplanationFeed.PublishAsync — only the pipeline is allowed to
//      deliver. Direct calls from endpoints would bypass the gate.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class PeerExplanationCasGatedTest
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
        throw new InvalidOperationException(
            "Repo root not found — looked for CLAUDE.md or src/actors/Cena.Actors/.");
    }

    private static string PipelinePath(string repoRoot) =>
        Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Collaboration", "PeerExplanationPipeline.cs");

    [Fact]
    public void PeerExplanationPipeline_Exists_AndDeclaresCasCollaborators()
    {
        var repoRoot = FindRepoRoot();
        var file = PipelinePath(repoRoot);
        Assert.True(File.Exists(file),
            $"prr-025 violation: PeerExplanationPipeline.cs missing at {file}. " +
            "Every peer-authored explanation must pass through a CAS-gated pipeline " +
            "before delivery (ADR-0002).");

        var src = File.ReadAllText(file);

        Assert.Matches(new Regex(@"\bICasRouterService\b"), src);
        Assert.Matches(new Regex(@"\bIPeerExplanationFeed\b"), src);
        Assert.Matches(new Regex(@"\bIPeerExplanationModerationQueue\b"), src);
    }

    [Fact]
    public void PeerExplanationPipeline_VerifiesBeforePublishing_NeverAfter()
    {
        var repoRoot = FindRepoRoot();
        var file = PipelinePath(repoRoot);
        Assert.True(File.Exists(file),
            "prr-025 baseline: PeerExplanationPipeline.cs missing.");

        var src = File.ReadAllText(file);

        // Token-level match: the exact call sites are the seams we guard.
        var verifyIdx = src.IndexOf("_cas.VerifyAsync", StringComparison.Ordinal);
        var publishIdx = src.IndexOf("_feed.PublishAsync", StringComparison.Ordinal);

        Assert.True(verifyIdx > 0,
            "prr-025 violation: expected a call to ICasRouterService.VerifyAsync " +
            "inside PeerExplanationPipeline. Without it, the CAS gate is missing.");
        Assert.True(publishIdx > 0,
            "prr-025 baseline: expected a call to IPeerExplanationFeed.PublishAsync " +
            "inside PeerExplanationPipeline.");
        Assert.True(verifyIdx < publishIdx,
            "prr-025 violation: IPeerExplanationFeed.PublishAsync appears BEFORE " +
            "ICasRouterService.VerifyAsync in PeerExplanationPipeline. The feed " +
            "publish must happen AFTER CAS verify — otherwise a peer explanation " +
            "reaches other students without CAS confirmation (ADR-0002 breach).");

        // Also ensure the moderation queue is enqueued-to somewhere (the
        // fail-closed fallback). Token match on the property name we expose.
        Assert.Matches(new Regex(@"_moderation\.EnqueueAsync"), src);
    }

    [Fact]
    public void NoOtherProductionClass_CallsPeerExplanationFeedPublish()
    {
        // Only the pipeline may publish to the peer feed. Any other caller
        // bypasses the CAS gate.
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src/ not found at {srcRoot}");

        var pipelineRel = Path.Combine("src", "actors", "Cena.Actors", "Collaboration",
            "PeerExplanationPipeline.cs");
        var publishRegex = new Regex(@"IPeerExplanationFeed[^;]{0,80}PublishAsync|\bPublishAsync\s*\([^)]*PeerExplanationSubmission");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            // The pipeline itself is allowed to publish.
            if (rel.EndsWith(pipelineRel, StringComparison.Ordinal)) continue;
            // The interface declaration itself contains the name.
            if (rel.EndsWith($"{sep}PeerExplanationPipeline.cs", StringComparison.Ordinal)) continue;

            var content = File.ReadAllText(file);
            // Cheap pre-filter: must mention the interface at all.
            if (!content.Contains("IPeerExplanationFeed", StringComparison.Ordinal)) continue;

            foreach (Match m in publishRegex.Matches(content))
            {
                // Allow interface / abstract declarations (the interface file
                // itself is permitted to have the method name — but it would
                // live in PeerExplanationPipeline.cs which we already excluded).
                violations.Add($"{rel.Replace(sep, '/')} — calls IPeerExplanationFeed.PublishAsync " +
                               "outside the PeerExplanationPipeline. This bypasses the CAS gate " +
                               "(prr-025, ADR-0002). Route through IPeerExplanationPipeline.ProcessAsync.");
                _ = m; // suppress unused
            }
        }

        if (violations.Count == 0) return;

        Assert.Fail(
            $"prr-025 violation: {violations.Count} call-site(s) publish to the peer feed " +
            "without going through the CAS-gated pipeline:\n  " +
            string.Join("\n  ", violations));
    }
}
