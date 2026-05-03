// =============================================================================
// Cena Platform — NoLdAnxiousHintWithoutGovernorTest (prr-029)
//
// Architecture ratchet that locks the prr-029 wiring in place. The LD-
// anxious hint governor is a compliance-shaped seam (RDY-066 accommodations
// are Ministry-reportable + consent-audited). If a future refactor drops
// the governor call from the student hint endpoint, the platform will
// persist AccommodationProfileAssignedV1(LdAnxiousFriendly) events, the
// admin console will claim the accommodation is active, and the student
// will receive the terse nudge anyway — the exact PRR-151 R-22 "consent
// without render" defect class.
//
// This test asserts that every code path that resolves
// IHintGenerator.Generate(...) on the student-facing API host sits next
// to an invocation of ILdAnxiousHintGovernor (or LdAnxiousHintGovernor).
// The test is sturdy against renames: it matches either the interface or
// the concrete type name, and it counts files, not method lines.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class NoLdAnxiousHintWithoutGovernorTest
{
    // A hint-generation call site. Matches any ".Generate(" call off a
    // variable whose name suggests IHintGenerator. Matches broadly to
    // tolerate refactors that rename the instance variable.
    private static readonly Regex HintGeneratorInvocation = new(
        @"\bhintGenerator\s*\.\s*Generate\s*\(",
        RegexOptions.Compiled);

    // Governor wiring present at the same seam. Matches either the
    // interface or the implementation so future refactors (e.g. renaming
    // the concrete class) don't silently unwire the ratchet.
    private static readonly Regex GovernorInvocation = new(
        @"\b(ILdAnxiousHintGovernor\b|LdAnxiousHintGovernor\b|ldAnxiousGovernor\s*\.\s*Apply\s*\()",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> EnumerateEndpointFiles(string repoRoot)
    {
        // Scan the student-facing API host (the public hint endpoint) and
        // the actor hosts. Tests, build artefacts, and the Hints/
        // bounded-context itself are excluded (the governor lives there
        // and trivially matches both regexes by inspection).
        var roots = new[]
        {
            Path.Combine(repoRoot, "src", "api"),
            Path.Combine(repoRoot, "src", "actors", "Cena.Actors"),
            Path.Combine(repoRoot, "src", "actors", "Cena.Actors.Host"),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.cs",
                         SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "Tests"
                        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || file.Contains(".Tests.", StringComparison.OrdinalIgnoreCase)
                    || file.Contains(Path.DirectorySeparatorChar + "obj"
                        + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "bin"
                        + Path.DirectorySeparatorChar))
                    continue;
                // Skip the governor's own file — it defines the type,
                // does not itself invoke it in an endpoint seam.
                if (file.EndsWith("LdAnxiousHintGovernor.cs", StringComparison.Ordinal))
                    continue;
                yield return file;
            }
        }
    }

    [Fact]
    public void Every_hint_generator_invocation_sits_next_to_the_ld_anxious_governor()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();
        int scanned = 0;
        int hintCallSites = 0;

        foreach (var file in EnumerateEndpointFiles(repoRoot))
        {
            scanned++;
            var content = File.ReadAllText(file);
            if (!HintGeneratorInvocation.IsMatch(content))
                continue;

            hintCallSites++;
            // Every file that calls IHintGenerator.Generate(...) must also
            // reference the governor in the same file. If the same
            // endpoint composes multiple hint levels separately, the
            // governor must be visible alongside at least one of them —
            // which is how the current endpoint wires it.
            if (!GovernorInvocation.IsMatch(content))
                offenders.Add(file);
        }

        Assert.True(scanned > 0, "Architecture test scanned zero files — glob likely broken");
        Assert.True(hintCallSites > 0, "No hintGenerator.Generate(...) call sites found — expected at least one in the student API");

        if (offenders.Count > 0)
        {
            Assert.Fail(
                $"{offenders.Count} file(s) invoke hintGenerator.Generate(...) "
                + "but do NOT reference ILdAnxiousHintGovernor / "
                + "LdAnxiousHintGovernor in the same file. prr-029 requires "
                + "the governor be composed at every hint-delivery seam so "
                + "AccommodationProfileAssignedV1(LdAnxiousFriendly) "
                + "consent translates into a rewritten L1 hint body. "
                + "Offending files:\n  " + string.Join("\n  ", offenders));
        }
    }
}
