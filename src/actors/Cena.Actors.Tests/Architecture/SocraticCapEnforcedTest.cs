// =============================================================================
// Cena Platform — Socratic LLM Cap Architecture Test (prr-012)
//
// Invariant: every Socratic LLM call in ClaudeTutorLlmService MUST be gated by
// ISocraticCallBudget.CanMakeLlmCallAsync AND IDailyTutorTimeBudget.CheckAsync
// before the Anthropic SDK is touched. Without this invariant, a future edit
// could silently re-introduce the $480k/mo unbounded Sonnet spend documented
// in the 2026-04-20 finops review.
//
// This is a textual gate — same policy SchedulerNoLlmCallTest uses — because
// a compile-time reflection crawl misses the realistic failure modes (the
// dev adds `_client.Messages.Create(...)` above the gate). The test scans the
// file for ordering: the first Anthropic `Messages.Create` must appear AFTER
// both the budget check and the daily-cap check, and the LLM call site must
// be preceded by `CanMakeLlmCallAsync`.
//
// Failure message:
//   "prr-012 violation: ClaudeTutorLlmService calls Anthropic before the
//    3-call Socratic budget check. Restore the budget gate."
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class SocraticCapEnforcedTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    [Fact]
    public void ClaudeTutorLlmService_GatesAnthropicCallBehindBudget()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Tutor", "ClaudeTutorLlmService.cs");
        Assert.True(File.Exists(file), $"Expected ClaudeTutorLlmService at {file}");

        var src = File.ReadAllText(file);

        // 1. The class must declare an ISocraticCallBudget collaborator.
        Assert.Matches(new Regex(@"\bISocraticCallBudget\b"), src);

        // 2. The class must declare an IDailyTutorTimeBudget collaborator.
        Assert.Matches(new Regex(@"\bIDailyTutorTimeBudget\b"), src);

        // 3. The class must declare an IStaticHintLadderFallback collaborator
        //    (used as the no-LLM degradation path).
        Assert.Matches(new Regex(@"\bIStaticHintLadderFallback\b"), src);

        // 4. The budget check must appear in the source. Token-level match
        //    because the call site is the exact seam we are guarding.
        Assert.Matches(new Regex(@"\bCanMakeLlmCallAsync\("), src);

        // 5. The daily-cap check must appear too.
        Assert.Matches(new Regex(@"\bCheckAsync\("), src);

        // 6. Ordering: the first Anthropic call (`_client.Messages.Create`)
        //    must come AFTER the first budget check and daily-cap check.
        var anthropicIdx = src.IndexOf("_client.Messages.Create", StringComparison.Ordinal);
        var budgetIdx = src.IndexOf("CanMakeLlmCallAsync(", StringComparison.Ordinal);
        var dailyIdx = src.IndexOf("CheckAsync(", StringComparison.Ordinal);

        Assert.True(anthropicIdx > 0,
            "prr-012 baseline: expected an Anthropic SDK call site in ClaudeTutorLlmService");
        Assert.True(budgetIdx > 0 && budgetIdx < anthropicIdx,
            "prr-012 violation: SocraticCallBudget.CanMakeLlmCallAsync must be invoked BEFORE Anthropic Messages.Create. " +
            "Restore the budget gate or the service will silently re-enable unbounded Sonnet spend.");
        Assert.True(dailyIdx > 0 && dailyIdx < anthropicIdx,
            "prr-012 violation: DailyTutorTimeBudget.CheckAsync must be invoked BEFORE Anthropic Messages.Create. " +
            "Restore the daily-cap gate.");
    }

    [Fact]
    public void TutorMessageService_RoutesThroughITutorLlmService_NotDirectAnthropic()
    {
        // The non-streaming path (TutorMessageService) must keep going through
        // ITutorLlmService so the cap gates in ClaudeTutorLlmService apply to
        // /messages and /stream alike. A regression where TutorMessageService
        // invokes Anthropic directly would bypass prr-012 entirely.
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Tutor", "TutorMessageService.cs");
        Assert.True(File.Exists(file), $"Expected TutorMessageService at {file}");

        var src = File.ReadAllText(file);

        // Must still consume ITutorLlmService.
        Assert.Matches(new Regex(@"\bITutorLlmService\b"), src);

        // Must NOT construct an AnthropicClient directly.
        Assert.DoesNotMatch(new Regex(@"new\s+AnthropicClient\b"), src);
        Assert.DoesNotMatch(new Regex(@"Anthropic\.Models"), src);
    }
}
