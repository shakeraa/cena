// =============================================================================
// Cena Platform — CatalogRoadmapTargetTest (PRR-240)
//
// Any catalog entry with `availability: roadmap` MUST declare
// `available_from` (e.g. "2026-Q3"). The student SPA renders the field as
// a "Coming …" badge on the exam-target card; a roadmap entry with an
// empty badge would reach the student as a gray-out card with no cohort
// explanation, which defeats the purpose of surfacing the roadmap at all.
//
// Enforcement vectors:
//
//   1. The YAML loader fails loudly at parse time
//      (`ExamCatalogYamlLoader.MapTarget` — see "availability=roadmap
//      requires available_from" in the source).
//   2. This test re-asserts the invariant against the on-disk YAML at
//      test-time so the content team sees the failure in CI rather than
//      at the next admin rebuild.
//   3. A companion text gate confirms the loader keeps the enforcement
//      branch; deleting it would silently weaken the invariant.
// =============================================================================

using Cena.Student.Api.Host.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Student.Api.Host.Tests.Catalog;

public sealed class CatalogRoadmapTargetTest
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
        return dir.FullName;
    }

    private static string CatalogDir() =>
        Path.Combine(RepoRoot(), "contracts", "exam-catalog");

    private static ExamCatalogService NewService() =>
        new(CatalogDir(), new NullTenantCatalogOverlayStore(),
            NullLogger<ExamCatalogService>.Instance);

    [Fact]
    public void Every_roadmap_target_has_available_from_populated()
    {
        var svc = NewService();
        var snap = svc.Current;

        var offenders = snap.TargetsByCode.Values
            .Where(t => string.Equals(t.Availability, "roadmap", StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrWhiteSpace(t.AvailableFrom))
            .Select(t => t.ExamCode)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void At_least_one_roadmap_target_exists_to_exercise_the_invariant()
    {
        // Guards against the invariant being vacuously satisfied when the
        // roadmap cohort is emptied out by mistake. PRR-240 ships
        // PET_VERBAL_RUSSIAN and SAT_MATH as roadmap.
        var svc = NewService();
        var snap = svc.Current;

        var roadmapCodes = snap.TargetsByCode.Values
            .Where(t => string.Equals(t.Availability, "roadmap", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.ExamCode)
            .ToArray();

        Assert.NotEmpty(roadmapCodes);
    }

    [Fact]
    public void Loader_enforces_roadmap_requires_available_from_at_parse_time()
    {
        // Textual gate: the loader contains the enforcement branch. If a
        // future edit removes it, this test fails before a bad YAML can
        // slip through.
        var loaderSrc = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src", "api", "Cena.Student.Api.Host", "Catalog", "ExamCatalogYamlLoader.cs"));
        Assert.Contains("availability=roadmap requires available_from", loaderSrc);
    }
}
