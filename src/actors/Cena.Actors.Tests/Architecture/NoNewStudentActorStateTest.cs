// =============================================================================
// Cena Platform — No-new-event-handler-in-StudentActor gate
// (EPIC-PRR-A Sprint 1 architecture gate, effective 2026-04-27 per ADR-0012)
//
// Prevents the StudentActor god-aggregate from growing by pinning the count of
// event-handler `case X_V<N>:` lines inside src/actors/Cena.Actors/Students/
// StudentActor*.cs at the 2026-04-20 baseline. New event types must land in
// LearningSessionActor (available post Sprint 1, 2026-05-03) or be documented
// as a temporary `StudentActor.Pending` seam in the PR description.
//
// Counting rule (mirrored by NoNewStudentActorStateBaseline.yml):
//   A line counts if its trimmed content matches the regex
//     ^case\s+[A-Za-z_][A-Za-z0-9_]*_V\d+\b
//   — i.e. the dispatch case for a versioned event type inside the actor's
//   event-fan-out switch. Lifecycle hooks (OnStarted/OnStopping/etc.) are
//   NOT counted: they are per-actor infrastructure, not event-type state.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class NoNewStudentActorStateTest
{
    private static readonly Regex EventHandlerCaseLine = new(
        @"^\s*case\s+[A-Za-z_][A-Za-z0-9_]*_V\d+\b",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string BaselineYamlPath(string repoRoot) => Path.Combine(
        repoRoot, "src", "actors", "Cena.Actors.Tests", "Architecture",
        "NoNewStudentActorStateBaseline.yml");

    private static string StudentActorDir(string repoRoot) => Path.Combine(
        repoRoot, "src", "actors", "Cena.Actors", "Students");

    /// <summary>
    /// Minimal parser for NoNewStudentActorStateBaseline.yml. Only pulls the
    /// top-level `baseline_count:` scalar; everything else (per-file
    /// breakdown, events_at_baseline, etc.) is documentation.
    /// </summary>
    internal static int LoadBaselineCount(string yamlPath)
    {
        if (!File.Exists(yamlPath))
            throw new FileNotFoundException(
                $"Baseline YAML missing: {yamlPath}. This test file cannot run without it.");

        foreach (var raw in File.ReadAllLines(yamlPath))
        {
            var line = raw;
            var hashIdx = line.IndexOf('#');
            if (hashIdx >= 0) line = line[..hashIdx];
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("baseline_count:", StringComparison.Ordinal)) continue;
            var value = trimmed["baseline_count:".Length..].Trim();
            if (int.TryParse(value, out var count))
                return count;
        }
        throw new InvalidDataException(
            $"baseline_count scalar not found in {yamlPath}");
    }

    internal static int CountEventHandlersIn(string file)
    {
        var count = 0;
        foreach (var line in File.ReadLines(file))
        {
            if (EventHandlerCaseLine.IsMatch(line))
                count++;
        }
        return count;
    }

    [Fact]
    public void StudentActor_EventHandlerCount_IsAtOrBelowBaseline()
    {
        var repoRoot = FindRepoRoot();
        var baseline = LoadBaselineCount(BaselineYamlPath(repoRoot));
        var dir = StudentActorDir(repoRoot);
        Assert.True(Directory.Exists(dir), $"Students dir missing: {dir}");

        var total = 0;
        var perFile = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "StudentActor*.cs", SearchOption.TopDirectoryOnly))
        {
            var n = CountEventHandlersIn(file);
            perFile[Path.GetFileName(file)] = n;
            total += n;
        }

        if (total > baseline)
        {
            var breakdown = new StringBuilder();
            foreach (var (name, n) in perFile.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                breakdown.AppendLine($"    {name}: {n}");

            Assert.Fail(
                $"StudentActor*.cs now contains {total} event-handler case lines, " +
                $"above the 2026-04-20 baseline of {baseline}.\n\n" +
                $"  Per-file breakdown:\n{breakdown}\n" +
                "  Adding event handlers to StudentActor is blocked per ADR-0012. " +
                "New handlers must route to LearningSessionActor (available post Sprint 1, " +
                "2026-05-03) or to a documented `StudentActor.Pending` seam noted in the " +
                "PR description. If you are migrating handlers out (count went down relative " +
                "to a prior local state), lower the baseline_count in " +
                "src/actors/Cena.Actors.Tests/Architecture/NoNewStudentActorStateBaseline.yml " +
                "in the same PR.");
        }
    }

    [Fact]
    public void StudentActorStateBaseline_IsNotStaleAboveCurrent()
    {
        // If the current count dropped below baseline (events migrated out
        // to LearningSessionActor), the PR that moved them should ALSO have
        // lowered baseline_count in the same commit. This test catches
        // stale baselines: the ratchet must snap to the new low, otherwise
        // a later regression could silently climb back up to the old
        // higher number without tripping the primary guard.
        var repoRoot = FindRepoRoot();
        var baseline = LoadBaselineCount(BaselineYamlPath(repoRoot));
        var dir = StudentActorDir(repoRoot);

        var total = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "StudentActor*.cs", SearchOption.TopDirectoryOnly))
            total += CountEventHandlersIn(file);

        Assert.True(total >= baseline,
            $"StudentActor*.cs event-handler count is {total}, below the recorded baseline of " +
            $"{baseline}. Lower baseline_count in " +
            "src/actors/Cena.Actors.Tests/Architecture/NoNewStudentActorStateBaseline.yml " +
            $"to {total} in the same PR that migrated the handlers out — the ratchet must " +
            "snap to every downward step so a later regression cannot silently climb back.");
    }
}
