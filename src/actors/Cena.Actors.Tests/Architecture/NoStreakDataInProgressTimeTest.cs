// =============================================================================
// Cena Platform — No-Streak-Data-in-Progress-Time Architecture Ratchet.
//
// PRR-225 removed the pre-existing dayStreakCount / kpiDayStreak leak from
// src/student/full-version/src/pages/progress/time.vue + locales (en/he/ar).
// This ratchet prevents the leak from regressing.
//
// Contract:
//   1. progress/time.vue must NOT reference the banned identifiers
//      (dayStreakCount, streakDays, streakCount, consecutiveDays).
//   2. progress/time.vue must NOT reference the banned i18n key
//      (progress.time.kpiDayStreak).
//   3. Locale files (en/he/ar) must NOT contain the banned i18n key
//      (kpiDayStreak) under progress.time.*.
//
// Rationale:
//   GD-004 (hard ban on streak counters) + ADR-0048 (exam-prep positive
//   framing). The CI shipgate scanner (scripts/shipgate/) enforces the
//   broader ban across src/; this test is the architectural side of the
//   same contract, so full-sln build fails loudly if someone reintroduces
//   the leak in progress/time surfaces specifically. This test complements
//   but does not replace the shipgate scanner — it gives the reviewer one
//   clear failure signal when the regression is in the exact file PRR-225
//   cleaned up.
//
// See:
//   - tasks/pre-release-review/TASK-PRR-225-remove-streak-leak-progress-time.md
//   - docs/adr/0048-exam-prep-time-framing.md
//   - docs/adr/0050-multi-target-student-exam-plan.md §10
//   - scripts/shipgate/multi-target-mechanics.yml (PRR-224)
// =============================================================================

namespace Cena.Actors.Tests.Architecture;

public sealed class NoStreakDataInProgressTimeTest
{
    private static readonly string[] BannedIdentifiers =
    {
        "dayStreakCount",
        "dayStreak",
        "streakCount",
        "streakDays",
        "consecutiveDays",
        "consecutive_days",
        "consecutiveSessions",
    };

    private static readonly string BannedI18nKey = "kpiDayStreak";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string ProgressTimeVuePath()
    {
        var root = FindRepoRoot();
        return Path.Combine(
            root,
            "src", "student", "full-version", "src", "pages", "progress", "time.vue");
    }

    private static string LocaleJsonPath(string locale)
    {
        var root = FindRepoRoot();
        return Path.Combine(
            root,
            "src", "student", "full-version", "src", "plugins", "i18n", "locales", $"{locale}.json");
    }

    [Fact]
    public void ProgressTimeVueDoesNotContainStreakIdentifiers()
    {
        var path = ProgressTimeVuePath();
        Assert.True(File.Exists(path), $"progress/time.vue missing at {path}");
        var content = File.ReadAllText(path);

        foreach (var banned in BannedIdentifiers)
        {
            Assert.DoesNotContain(
                banned,
                content,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProgressTimeVueDoesNotReferenceBannedI18nKey()
    {
        var path = ProgressTimeVuePath();
        var content = File.ReadAllText(path);
        Assert.DoesNotContain(
            BannedI18nKey,
            content,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("he")]
    [InlineData("ar")]
    public void LocaleDoesNotContainBannedI18nKey(string locale)
    {
        var path = LocaleJsonPath(locale);
        Assert.True(File.Exists(path), $"locale bundle missing at {path}");
        var content = File.ReadAllText(path);

        // The banned key is scoped to progress.time.*; we check for the exact
        // JSON key marker `"kpiDayStreak":` which uniquely identifies it
        // (no false positives on nested objects named 'streak' elsewhere in
        // the file — those are tracked by a separate GD-004 cleanup epic).
        var marker = $"\"{BannedI18nKey}\":";
        Assert.DoesNotContain(
            marker,
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MultiTargetMechanicsRulepackEnforcesIdentifierBans()
    {
        // Architectural proof that the shipgate scanner side of the contract
        // is wired in. If someone deletes the multi-target-mechanics rulepack
        // or its identifier rules, this test fails.
        var root = FindRepoRoot();
        var yamlPath = Path.Combine(root, "scripts", "shipgate", "multi-target-mechanics.yml");
        Assert.True(File.Exists(yamlPath), $"multi-target-mechanics rulepack missing at {yamlPath}");
        var yaml = File.ReadAllText(yamlPath);

        // Must name the ADR-0050 §10 identifier-ban rules explicitly.
        Assert.Contains("id: ident-streak", yaml);
        Assert.Contains("id: ident-countdown", yaml);
        Assert.Contains("id: ident-days-until", yaml);
        Assert.Contains("id: ident-days-left", yaml);
        Assert.Contains("id: ident-deadline-pressure", yaml);
    }

    [Fact]
    public void MultiTargetMechanicsRulepackIsRegisteredInScanner()
    {
        // Sanity check that rulepack-scan.mjs actually registers the new pack,
        // so the CI step `node scripts/shipgate/rulepack-scan.mjs` includes it.
        var root = FindRepoRoot();
        var scanner = Path.Combine(root, "scripts", "shipgate", "rulepack-scan.mjs");
        Assert.True(File.Exists(scanner), $"rulepack scanner missing at {scanner}");
        var content = File.ReadAllText(scanner);

        Assert.Contains(
            "name: \"multi-target-mechanics\"",
            content);
        Assert.Contains(
            "scripts/shipgate/multi-target-mechanics.yml",
            content);
    }
}
