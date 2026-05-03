// =============================================================================
// Cena Platform — BKT Parameters Locked (prr-041 architecture gate)
//
// Prevents silent tuning of the four BKT parameters (pInit, pLearn, pSlip,
// pGuess). The four constants live in exactly one authorized file —
// src/actors/Cena.Actors/Mastery/BktParameters.cs — and all BKT code paths
// must consume them from there. Any other .cs file that pairs a BKT-
// parameter name fragment with one of the locked literal values fails this
// test, forcing a new ADR + human sign-off before the change can land.
//
// Detection heuristic (deliberately loose — false positives are fixable by
// adding the file to the exemption list with a justification):
//   - A line-scanning match: the trimmed line contains
//     (one BKT-parameter-name fragment) AND (one locked numeric literal).
//   - Name fragments: "guess", "slip", "learn", "transit", "pInit", "pLearn",
//     "pSlip", "pGuess", "bkt", "BKT". Case-insensitive for English words.
//   - Locked literals (as literal decimal text): "0.3", "0.15", "0.10", "0.15"
//     (dedup to {0.3, 0.10, 0.15}; expressed as prefixes that tolerate an
//     optional trailing 'f' suffix common to float constants in this repo).
//
// Exemptions:
//   - The authorized file BktParameters.cs (the single source of truth).
//   - Any `*.Tests/` or `*Tests.cs` file (tests legitimately reference the
//     constants by value to assert behaviour).
//   - obj/, bin/, worktree, and .agentdb generated-code paths.
//
// Tolerance for the parallel agent race:
//   - The parallel prr-041 agent owns BktParameters.cs. If that file is
//     missing when this test runs, the test still passes the pure file-scan
//     (it doesn't reference the type), but emits a debug note so the
//     coordinator can revisit once the parallel branch lands.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class BktParametersLockedTest
{
    // Identifier-like fragments that must appear on the same line as a
    // locked literal for the line to be flagged. Matched case-insensitively
    // against word boundaries to avoid false positives like "SteadyLearner"
    // (archetype name containing "learn") or prose like "learning zone".
    private static readonly Regex ContextFragmentRegex = new(
        @"\b(pInit|pLearn|pLearning|pSlip|pGuess|pInitial|PLearning|PSlip|PGuess|PInit|guess|slip|transit|bkt|BKT)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Locked numeric literal prefixes — tolerate optional trailing 'f' (e.g. 0.15f).
    // Koedinger lab defaults per prr-041: pInit=0.3, pLearn=0.15, pSlip=0.10, pGuess=0.15.
    // Dedup set: {0.3, 0.30, 0.15, 0.10}. Non-digit boundary asserts on both
    // sides prevent matching inside longer literals like 0.155 or 10.15.
    private static readonly Regex LockedLiteralRegex = new(
        @"(?<![\d.])(0\.3|0\.30|0\.15|0\.10)f?(?![\d])",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static bool IsExempt(string relPath)
    {
        // Normalize separators for cross-platform matching.
        var p = relPath.Replace('\\', '/');
        // Single authorized seam.
        if (p.EndsWith("src/actors/Cena.Actors/Mastery/BktParameters.cs", StringComparison.Ordinal))
            return true;
        // Previous TODO exemptions for BktService.cs and BktCalibrationOptions.cs
        // are now removed (2026-04-20). Both files consume the authorized seam
        // (Cena.Actors.Mastery.BktParameters.PInit/PLearn/PSlip/PGuess/PForget)
        // directly, so the prr-041 scanner has a single source of truth.
        // Tests legitimately reference the constants.
        if (p.Contains("/Cena.Actors.Tests/", StringComparison.Ordinal)) return true;
        if (p.EndsWith("Tests.cs", StringComparison.Ordinal)) return true;
        // Build output and generated / agent scratch areas.
        if (p.Contains("/bin/", StringComparison.Ordinal)) return true;
        if (p.Contains("/obj/", StringComparison.Ordinal)) return true;
        if (p.Contains("/worktrees/", StringComparison.Ordinal)) return true;
        if (p.Contains("/.claude-flow/", StringComparison.Ordinal)) return true;
        if (p.Contains("/.agentdb/", StringComparison.Ordinal)) return true;
        if (p.Contains("/node_modules/", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool LineContainsBktContext(string line)
        => ContextFragmentRegex.IsMatch(line);

    [Fact]
    public void No_file_outside_BktParameters_cs_overrides_BKT_parameters_with_literals()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src/ directory not found at {srcRoot}");

        var failures = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            if (IsExempt(rel)) continue;

            var lineNo = 0;
            foreach (var raw in File.ReadLines(file))
            {
                lineNo++;
                var line = raw;
                // Strip line-ending comments conservatively — but only for
                // detection; comments themselves may legitimately mention
                // parameter values in prose, and we don't want to flag those.
                var commentIdx = line.IndexOf("//", StringComparison.Ordinal);
                var codeOnly = commentIdx >= 0 ? line[..commentIdx] : line;

                if (codeOnly.Trim().Length == 0) continue;
                if (!LineContainsBktContext(codeOnly)) continue;
                if (!LockedLiteralRegex.IsMatch(codeOnly)) continue;

                failures.AppendLine(
                    $"  {rel}:{lineNo}: BKT parameter override detected.");
                failures.AppendLine(
                    $"      Line: {codeOnly.Trim()}");
                failures.AppendLine(
                    "      Parameters are locked at prr-041 ADR; use BktParameters.P{Init|Learn|Slip|Guess} " +
                    "(or the existing P_L0/P_T/P_S/P_G fields) instead. Changes require a new ADR + human sign-off.");
            }
        }

        Assert.True(failures.Length == 0,
            "BKT parameters may only be defined in src/actors/Cena.Actors/Mastery/BktParameters.cs. " +
            "Silent tuning is blocked per prr-041 (authored by parallel ADR agent on 2026-04-20).\n" +
            failures);
    }

    [Fact]
    public void Authorized_BktParameters_file_exists_or_is_being_authored_by_parallel_agent()
    {
        // The authorized BKT seam file. Hard existence check — prr-041's parallel
        // ADR work landed 2026-04-20 so the soft-pass tolerance is no longer
        // needed. If this fails, someone deleted the authorized seam; the
        // file-scanner test above won't catch that alone.
        var repoRoot = FindRepoRoot();
        var authorized = Path.Combine(
            repoRoot, "src", "actors", "Cena.Actors", "Mastery", "BktParameters.cs");

        Assert.True(File.Exists(authorized),
            $"Authorized BKT seam missing: {authorized}. prr-041 requires a single named file " +
            "that defines pInit, pLearn, pSlip, pGuess.");
    }
}
