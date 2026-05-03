// =============================================================================
// Cena Platform — Accommodations Wired Architecture Gate (PRR-151 R-22)
//
// Compliance-critical guard. The prr-151 Group-A caller audit identified
// R-22 (Accommodations bounded context) as ORPHANED: the parent-console
// endpoint persists AccommodationProfileAssignedV1 events + the endpoint
// validates with Phase1ADimensions.IsShipped, but NO session-rendering
// code path consults AccommodationProfile.IsEnabled(...) or any of the
// derived per-dimension flags. That means a parent can sign Ministry
// hatama consent for TTS / extended-time / distraction-reduced-layout
// and Cena will never technically render the accommodation — which a
// Ministry-of-Education audit will classify as "consent without
// render", a ship-blocker class defect.
//
// This test asserts that AccommodationProfile (and its per-dimension
// IsEnabled() / TtsForProblemStatementsEnabled / etc. accessors) is
// consulted by at least one session-rendering code path outside the
// Accommodations/ bounded context and outside test code. If the count
// regresses to zero, the compliance defect has returned and the test
// fails LOUDLY with the Ministry-reportable framing.
//
// The test is intentionally sturdy against refactors that rename the
// accessor: it looks for any reference to the AccommodationProfile
// type *OR* any of its session-pipeline accessors (IsEnabled,
// TtsForProblemStatementsEnabled, ExtendedTimeMultiplier,
// DistractionReducedLayoutEnabled, NoComparativeStatsRequired,
// IAccommodationProfileService), and counts the files that match.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class AccommodationsWiredTest
{
    private static readonly Regex AccommodationConsumerPattern = new(
        @"\b(AccommodationProfile\b" +
        @"|IAccommodationProfileService\b" +
        @"|IsEnabled\s*\(\s*AccommodationDimension\." +
        @"|TtsForProblemStatementsEnabled\b" +
        @"|ExtendedTimeMultiplier\b" +
        @"|DistractionReducedLayoutEnabled\b" +
        @"|NoComparativeStatsRequired\b" +
        @")",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found");
    }

    /// <summary>
    /// Locate every .cs file under src/ that references an accommodation
    /// consumer symbol (the profile type, the service, or any of its
    /// per-dimension accessors) while excluding:
    ///   • the Accommodations/ bounded context itself (internal self-reference
    ///     would mask the compliance defect — we need *external* consumers)
    ///   • all test projects (test-only references prove the unit test
    ///     compiles but not that production code wires the accommodation)
    /// </summary>
    private static IReadOnlyList<string> FindProductionConsumers(string repoRoot)
    {
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"src dir missing: {srcDir}");

        var consumers = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip all test projects
            if (file.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || file.Contains(".Tests.", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip obj/bin build artifacts
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                continue;

            // Skip the Accommodations/ bounded context itself — we want
            // external session-pipeline consumers, not the profile's own
            // self-references.
            if (file.Contains(Path.DirectorySeparatorChar + "Accommodations"
                    + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var content = File.ReadAllText(file);
            if (AccommodationConsumerPattern.IsMatch(content))
                consumers.Add(file);
        }
        return consumers;
    }

    [Fact]
    public void AccommodationProfile_HasAtLeastThreeProductionCallSites_OutsideBoundedContext()
    {
        var repoRoot = FindRepoRoot();
        var consumers = FindProductionConsumers(repoRoot);

        // We demand at least 3 external consumer files: one for the
        // session-question DTO population, one for the DI registration,
        // and one for the contract field. This is a floor — the real
        // wiring will likely grow over time as Phase 1C / 1D accommodations
        // ship. Fewer than 3 external files means the bounded context
        // is sitting alone again and the prr-151 R-22 defect has
        // returned.
        const int minimumExternalConsumers = 3;

        if (consumers.Count < minimumExternalConsumers)
        {
            Assert.Fail(
                $"AccommodationProfile (or derived accessors / IAccommodationProfileService) "
                + $"has only {consumers.Count} production call site(s) outside "
                + $"the Accommodations/ bounded context. Minimum required: "
                + $"{minimumExternalConsumers}.\n\n"
                + "  COMPLIANCE DEFECT: this means the platform records "
                + "AccommodationProfileAssignedV1 consent events but no "
                + "session-rendering code consults them — a Ministry-of-"
                + "Education audit will classify this as 'consent without "
                + "render'. Parents will have legal paper (consent signature "
                + "stored) that their disabled child received an accommodation "
                + "the platform never technically applied.\n\n"
                + "  Restore the wiring in SessionEndpoints (student API) / "
                + "the session-rendering DTOs. See prr-151 audit R-22 and "
                + "pre-release-review/reviews/audit/group-a-caller-audit.md:241-273.");
        }
    }

    [Fact]
    public void AccommodationProfile_IsConsultedByStudentSessionEndpoint()
    {
        // Specificity guard: at least ONE consumer must sit under the
        // student-facing session API (the live render path). Without
        // this targeted assertion, the "three consumers" gate could be
        // satisfied by three DI registrations and zero render-time
        // decisions — which is the exact pre-PRR-151 state that shipped
        // the compliance defect in the first place.
        var repoRoot = FindRepoRoot();
        var consumers = FindProductionConsumers(repoRoot);

        var sessionConsumers = consumers
            .Where(f => f.Contains(Path.DirectorySeparatorChar + "SessionEndpoints",
                StringComparison.Ordinal)
                || (f.Contains(Path.DirectorySeparatorChar + "Sessions"
                        + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && f.EndsWith("Dtos.cs", StringComparison.Ordinal)))
            .ToList();

        if (sessionConsumers.Count == 0)
        {
            Assert.Fail(
                "No student-session endpoint or session DTO file references "
                + "AccommodationProfile. The parent-console accepts "
                + "Ministry-hatama consent, but the session rendering path "
                + "does not consult the profile when serving questions. "
                + "This is the prr-151 R-22 compliance defect. See "
                + "pre-release-review/reviews/audit/group-a-caller-audit.md:241.");
        }
    }
}
