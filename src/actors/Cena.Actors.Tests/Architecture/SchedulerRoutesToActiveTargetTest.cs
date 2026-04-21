// =============================================================================
// Cena Platform — SchedulerRoutesToActiveTargetTest (prr-226, ADR-0050 §10)
//
// Architectural ratchet: any session-start path that builds a
// SchedulerInputs MUST consult ActiveExamTargetId (either stamping it on
// the inputs or at minimum reading the field from a resolved inputs record).
//
// Rationale: ADR-0050 §1 makes a StudentPlan a LIST of ExamTargets. Running
// the scheduler against a multi-target student WITHOUT resolving which
// target this session runs against produces nonsense — the plan would mix
// topics across Bagrut-Math-5U, PET-Quant, and SAT-Math with no cue to the
// downstream selector which scope it's in. The only sanctioned way to drop
// that resolution is for a legacy path that constructs SchedulerInputs
// without a reader wired (null ActiveExamTargetId) and the arch test
// recognises that case.
//
// Enforcement is textual: every *.cs under Sessions/ that references
// `new SchedulerInputs(` or `SchedulerInputs(` in a non-test-double context
// must also reference `ActiveExamTargetId` (the field or the resolver) in
// the same file. If a new session-start path is added and forgets the
// wiring, the build fails.
//
// This is deliberately a BROADER scan than the unit tests on
// ActiveExamTargetPolicy — those prove the policy is correct; this proves
// the CALL SITES keep using it.
// =============================================================================

using System.Text;

namespace Cena.Actors.Tests.Architecture;

public sealed class SchedulerRoutesToActiveTargetTest
{
    private static readonly string[] ScannedDirs =
    {
        Path.Combine("src", "actors", "Cena.Actors", "Sessions"),
    };

    // Files explicitly exempt from the arch check — the file that DECLARES
    // SchedulerInputs (if it ever ends up under Sessions), and interface
    // definitions that document the field as a comment but do not construct.
    // Kept empty by default; add only with a written justification.
    private static readonly HashSet<string> ExemptFileNames = new(StringComparer.Ordinal)
    {
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found from test base directory.");
    }

    [Fact]
    public void EverySessionStartCallSite_ConsultsActiveExamTargetId()
    {
        var repoRoot = FindRepoRoot();
        var violations = new StringBuilder();

        foreach (var relDir in ScannedDirs)
        {
            var dir = Path.Combine(repoRoot, relDir);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                var fileName = Path.GetFileName(file);
                if (ExemptFileNames.Contains(fileName)) continue;

                var text = File.ReadAllText(file);

                // Heuristic: a file is a session-start CALL SITE iff it
                // constructs a SchedulerInputs (the constructor is the
                // only way to hand scheduler a multi-target context, so
                // it is the right pivot). The same file must then name
                // ActiveExamTargetId at least once (either the parameter,
                // the policy, or the resolver — all three reference the
                // identifier textually).
                var isCallSite = text.Contains("new SchedulerInputs(", StringComparison.Ordinal);
                if (!isCallSite) continue;

                var consultsActive = text.Contains("ActiveExamTargetId", StringComparison.Ordinal)
                    || text.Contains("ActiveExamTargetPolicy", StringComparison.Ordinal)
                    || text.Contains("ISittingCanonicalDateResolver", StringComparison.Ordinal);

                if (!consultsActive)
                {
                    violations.AppendLine(
                        $"  {Path.GetRelativePath(repoRoot, file)}: constructs `SchedulerInputs` " +
                        "but does not reference `ActiveExamTargetId` / " +
                        "`ActiveExamTargetPolicy` / `ISittingCanonicalDateResolver`. " +
                        "Session-start paths MUST resolve the active target per " +
                        "ADR-0050 §10 and prr-226.");
                }
            }
        }

        Assert.True(violations.Length == 0,
            "prr-226 architectural violation: one or more session-start call " +
            "sites build SchedulerInputs without consulting the multi-target " +
            "resolver. See ADR-0050 §10 — a multi-target student's session " +
            "cannot run scheduler without picking an ActiveExamTargetId.\n"
            + violations);
    }

    [Fact]
    public void ActiveTargetPolicy_WindowConstant_DoesNotLeakIntoUxCopy()
    {
        // Belt-and-braces: ensure no file outside the policy itself hardcodes
        // the 14-day window. The shipgate scanner (PRR-224) will catch the
        // UX surface; this arch test catches stray actor-layer code that
        // might accidentally introduce `daysUntil` / `timeLeft` semantics
        // via the window constant.
        var repoRoot = FindRepoRoot();
        var offenders = new StringBuilder();

        var actorsDir = Path.Combine(repoRoot, "src", "actors", "Cena.Actors");
        if (!Directory.Exists(actorsDir)) return;

        foreach (var file in Directory.EnumerateFiles(actorsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.EndsWith("ActiveExamTargetPolicy.cs", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            // Identifier-level checks only — literal comment copy is fine.
            // The banned identifiers are camelCase / PascalCase forms that
            // would indicate a caller leaking "days until" semantics.
            foreach (var banned in new[] { "daysUntil", "timeLeft", "examCountdown" })
            {
                // Skip matches inside obvious comments (lines starting with `//`).
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (trimmed.StartsWith("*", StringComparison.Ordinal)) continue;
                    if (line.Contains(banned, StringComparison.Ordinal))
                    {
                        offenders.AppendLine(
                            $"  {Path.GetRelativePath(repoRoot, file)}: contains identifier `{banned}`.");
                        break;
                    }
                }
            }
        }

        Assert.True(offenders.Length == 0,
            "prr-226 / ADR-0048 violation: one or more actor-layer files " +
            "introduce a `daysUntil` / `timeLeft` / `examCountdown` identifier. " +
            "ADR-0048 bans countdown framing; the 14-day exam-week lock is " +
            "scheduler-internal and has no outbound surface.\n" + offenders);
    }
}
