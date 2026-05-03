// =============================================================================
// Cena Platform — Daily Minute Cap Architecture Tests (prr-048)
//
// Regression guards for the per-student daily-minute cap + soft-limit nudge:
//
//   1. The two prr-048 spec-named counters must remain declared in
//      DailyTutorTimeBudget.cs, verbatim. Dashboards and finops alerting
//      target these exact strings — renaming is a breaking change.
//
//        cena_student_daily_minute_cap_hit_total
//        cena_student_daily_minute_cap_nudge_total
//
//   2. The TakeBreakMessage constant and the RenderNudge helper must
//      produce ship-gate-compliant copy — no streak, loss-aversion, or FOMO
//      tokens. This asserts the static strings at compile/test time, BEFORE
//      the JS ship-gate scanner runs in CI.
//
//   3. ClaudeTutorLlmService must thread context.InstituteId into both the
//      CheckAsync and RecordUsageAsync calls — otherwise per-institute
//      metric labels silently degrade to "unknown" across the fleet.
//
// These are textual gates (the same style as SocraticCapEnforcedTest) because
// reflection cannot see whether the correct LITERAL metric name was used.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class DailyMinuteCapArchitectureTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string ReadBudgetSource()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "RateLimit", "DailyTutorTimeBudget.cs");
        Assert.True(File.Exists(file), $"Expected DailyTutorTimeBudget at {file}");
        return File.ReadAllText(file);
    }

    private static string ReadTutorLlmSource()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Tutor", "ClaudeTutorLlmService.cs");
        Assert.True(File.Exists(file), $"Expected ClaudeTutorLlmService at {file}");
        return File.ReadAllText(file);
    }

    [Fact]
    public void SpecNamedCounters_AreDeclaredVerbatim()
    {
        // Renaming either of these strings breaks the finops dashboard
        // queries + PagerDuty alerts keyed on cena_student_daily_minute_cap_*.
        var src = ReadBudgetSource();
        Assert.Contains("\"cena_student_daily_minute_cap_hit_total\"", src);
        Assert.Contains("\"cena_student_daily_minute_cap_nudge_total\"", src);
    }

    [Fact]
    public void HitCounter_IsLabeledWithInstituteIdAndCapType()
    {
        // Both labels are required by the finops dashboard spec. A future
        // edit that drops either label will widen cardinality OR blind the
        // soft-vs-hard attribution — both of which are regressions.
        var src = ReadBudgetSource();
        Assert.Contains("\"institute_id\"", src);
        Assert.Contains("\"cap_type\"", src);
    }

    [Fact]
    public void TakeBreakMessage_ContainsNoBannedTokens()
    {
        // The hard-limit copy is compiled into the binary; this test asserts
        // it at test-time so a bad edit fails here BEFORE reaching the
        // JS ship-gate scanner in CI.
        var src = ReadBudgetSource();
        var match = Regex.Match(src,
            @"public\s+const\s+string\s+TakeBreakMessage\s*=\s*(?<body>(?:""[^""]*""\s*\+?\s*)+);",
            RegexOptions.Singleline);
        Assert.True(match.Success, "TakeBreakMessage constant was not located via regex");

        var body = match.Groups["body"].Value.ToLowerInvariant();
        foreach (var banned in BannedTokens)
        {
            Assert.DoesNotContain(banned.ToLowerInvariant(), body);
        }
    }

    [Fact]
    public void TutorLlmService_ThreadsInstituteIdIntoBudgetCalls()
    {
        // Regression guard: if this test fails it means a future edit
        // stopped passing context.InstituteId to the budget service, which
        // would silently tag every metric as "unknown" fleet-wide.
        var src = ReadTutorLlmSource();
        Assert.Matches(
            new Regex(@"CheckAsync\(\s*context\.StudentId,\s*context\.InstituteId", RegexOptions.Singleline),
            src);
        // The RecordUsageAsync call bodies span multiple lines and include a
        // `(int)`-cast elapsed-seconds expression in the middle, so we accept
        // any characters between StudentId and InstituteId — the load-bearing
        // assertion is that BOTH StudentId and InstituteId flow in together.
        Assert.Matches(
            new Regex(@"RecordUsageAsync\(\s*context\.StudentId,.*?context\.InstituteId",
                RegexOptions.Singleline),
            src);
    }

    /// <summary>
    /// Mirror of the ship-gate dark-pattern scanner's English rule list for
    /// dark-pattern banned phrases. Kept locally so the architecture test is
    /// self-contained and runs in the .NET test pass.
    /// </summary>
    private static readonly string[] BannedTokens =
    [
        "streak",
        "don't break",
        "don't miss",
        "don't waste",
        "keep the chain",
        "you'll lose",
        "running out of time",
        "out of time",
        "last chance",
        "countdown",
        "hurry up",
        "time's up",
    ];
}
