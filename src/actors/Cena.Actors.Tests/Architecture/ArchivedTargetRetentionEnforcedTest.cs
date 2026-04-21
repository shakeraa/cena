// =============================================================================
// Cena Platform — Archived-target retention enforcement ratchet (prr-229)
//
// Invariant: the ExamTargetRetentionWorker must actually process every
// target with ArchivedAtUtc past the 24-month window. Enforced
// structurally by asserting:
//
//   1. ExamTargetRetentionWorker exists and calls
//      ExamTargetRetentionPolicy.IsBeyondRetention on every row it
//      inspects (so policy is the ONLY place the horizon is computed).
//   2. ExamTargetRetentionPolicy.DefaultRetentionMonths == 24 and
//      MaxExtendedRetentionMonths == 60 — these are ADR-0050 §6 locked
//      values; any change requires a new ADR, not a source-only edit.
//   3. The worker is registered as IHostedService in the DI wiring.
//   4. No other source file recomputes the 24-month window on its own —
//      policy is the single source of truth.
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.Retention;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class ArchivedTargetRetentionEnforcedTest
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
        throw new InvalidOperationException("Repo root not found.");
    }

    [Fact]
    public void Retention_policy_constants_match_adr_0050_section_6()
    {
        Assert.Equal(24, ExamTargetRetentionPolicy.DefaultRetentionMonths);
        Assert.Equal(60, ExamTargetRetentionPolicy.MaxExtendedRetentionMonths);
        Assert.Equal(TimeSpan.FromDays(60),
            ExamTargetRetentionPolicy.ExpiringSoonWindow);
    }

    [Fact]
    public void Retention_worker_uses_policy_as_single_source_of_truth()
    {
        var repoRoot = FindRepoRoot();
        var workerPath = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Retention",
            "ExamTargetRetentionWorker.cs");

        Assert.True(File.Exists(workerPath),
            "Expected worker at " + workerPath);

        var src = File.ReadAllText(workerPath);

        // The worker must call IsBeyondRetention — otherwise it's
        // computing the horizon somewhere else.
        Assert.Matches(
            new Regex(@"ExamTargetRetentionPolicy\.IsBeyondRetention",
                RegexOptions.Compiled),
            src);

        // The worker must NOT hard-code "24" months anywhere. Any
        // literal 24 that looks like a month constant is a red flag —
        // policy constants are the only legal source.
        var suspiciousMonthLiterals = Regex.Matches(
            src,
            @"\bAddMonths\s*\(\s*24\b|\.Months\s*>=\s*24\b",
            RegexOptions.Compiled);
        Assert.True(
            suspiciousMonthLiterals.Count == 0,
            "Found hard-coded 24-month literal in worker source; all " +
            "month arithmetic must go through ExamTargetRetentionPolicy.");
    }

    [Fact]
    public void Worker_is_registered_as_hosted_service()
    {
        var repoRoot = FindRepoRoot();
        var regPath = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Retention",
            "ExamTargetRetentionServiceRegistration.cs");

        Assert.True(File.Exists(regPath),
            "Expected DI registration at " + regPath);
        var src = File.ReadAllText(regPath);

        Assert.Contains(
            "AddHostedService<ExamTargetRetentionWorker>", src,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_notifies_student_on_shred()
    {
        var repoRoot = FindRepoRoot();
        var workerPath = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Retention",
            "ExamTargetRetentionWorker.cs");
        var src = File.ReadAllText(workerPath);

        // NotifyShreddedAsync must be called so students are informed —
        // task body §Integration test asserts "student notified".
        Assert.Contains(
            "NotifyShreddedAsync", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Retention_policy_only_source_of_month_math()
    {
        // Scan the entire Retention folder for `AddMonths(` — every call
        // site should be inside ExamTargetRetentionPolicy.cs OR pass a
        // named constant from the policy, never a raw literal 24 / 60.
        var repoRoot = FindRepoRoot();
        var retentionFolder = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Retention");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     retentionFolder, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            if (rel.Contains("ExamTargetRetentionPolicy.cs"))
            {
                // The policy file itself is the only sanctioned source
                // of AddMonths(24/60) and is exempt.
                continue;
            }
            var src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(
                         src, @"AddMonths\s*\(\s*(24|60)\s*\)"))
            {
                violations.Add(
                    $"{rel}: literal AddMonths({m.Groups[1].Value}) — " +
                    "use ExamTargetRetentionPolicy.{Default|MaxExtended}" +
                    "RetentionMonths instead.");
            }
        }

        if (violations.Count > 0)
        {
            Assert.Fail(
                "prr-229 violation:\n  " + string.Join("\n  ", violations));
        }
    }
}
