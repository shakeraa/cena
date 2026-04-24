// =============================================================================
// Cena Platform — No-LLM-in-parametric-pipeline architecture test (prr-200)
//
// Enforces the Strategy-1 non-negotiable: the deterministic parametric
// template engine path MUST NOT import any LLM client. This is the
// architectural contribution of prr-200 — $0/call content generation,
// zero hallucination surface, 100% CAS-authoritative.
//
// Scope:
//   src/actors/Cena.Actors/QuestionBank/Templates/**.cs
//   src/tools/Cena.Tools.QuestionGen/**.cs
//
// Ban list (substring, case-sensitive):
//   * using Anthropic
//   * using OpenAI
//   * using Google.(AI|Ai|GenerativeAI)
//   * using Azure.AI
//   * ITutorLlmService
//   * IAiGenerationService
//   * AnthropicClient
//   * Claude* client types (ClaudeTutorLlmService etc.)
//
// Allowlist: files whose only reference to a banned token is in THIS TEST
// itself (we grep for the token string here, so exclude the test file).
// =============================================================================

using System.Text;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoLlmInParametricPipelineTest
{
    private static readonly string[] BannedTokens = new[]
    {
        "using Anthropic",
        "using OpenAI",
        "using Google.AI",
        "using Google.Ai",
        "using Google.GenerativeAI",
        "using Azure.AI",
        "ITutorLlmService",
        "IAiGenerationService",
        "AnthropicClient",
        "ClaudeTutorLlmService"
    };

    [Fact]
    public void ParametricPipeline_ContainsNoLlmImports()
    {
        var repoRoot = FindRepoRoot();

        var scopes = new[]
        {
            Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "QuestionBank", "Templates"),
            Path.Combine(repoRoot, "src", "tools", "Cena.Tools.QuestionGen")
        };

        var violations = new List<string>();

        foreach (var scope in scopes)
        {
            if (!Directory.Exists(scope))
                throw new DirectoryNotFoundException(
                    $"prr-200: expected parametric-pipeline scope directory missing: {scope}");

            foreach (var file in Directory.EnumerateFiles(scope, "*.cs", SearchOption.AllDirectories))
            {
                if (IsExcluded(file)) continue;
                if (IsThisTestFile(file)) continue;

                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    foreach (var banned in BannedTokens)
                    {
                        if (line.Contains(banned, StringComparison.Ordinal))
                        {
                            violations.Add(
                                $"{Path.GetRelativePath(repoRoot, file)}:{lineNumber} — " +
                                $"banned token '{banned}'. Strategy 1 is deterministic no-LLM (prr-200, ADR-0002).");
                        }
                    }
                }
            }
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prr-200 NoLlmInParametricPipelineTest failed — the deterministic");
            sb.AppendLine("template engine must not import or reference any LLM client.");
            sb.AppendLine("If you need LLM-based variant generation, that is prr-201's");
            sb.AppendLine("coverage-waterfall orchestrator, NOT Strategy 1.");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }
    }

    private static bool IsExcluded(string f) =>
        f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsThisTestFile(string f) =>
        f.EndsWith("NoLlmInParametricPipelineTest.cs", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }
}
