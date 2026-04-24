// =============================================================================
// Cena Platform — ExamCatalogService tests (prr-220, ADR-0050)
//
// Five scenarios from the task DoD:
//   (a) Tenant-filtered list — overlay DisabledExamCodes subtracts from
//       the global set.
//   (b) Unknown target → GetTopics returns null (→ 404 at endpoint layer).
//   (c) Offline fallback — RenderFallbackJson produces a stable bundle
//       whose version matches the snapshot.
//   (d) CDN outage simulation — monotonicity refuses a version roll-back
//       so a stale CDN can't overwrite a newer snapshot.
//   (e) Catalog-version monotonicity — rebuild rejects same-version and
//       older-version candidates.
//
// Uses the real YAML under contracts/exam-catalog/ (discovered by walking
// up from AppContext.BaseDirectory) so the taxonomy stays in sync with
// what the service actually ships.
// =============================================================================

using System.Text.Json;
using Cena.Student.Api.Host.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class ExamCatalogServiceTests
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

    private static ExamCatalogService NewService(ITenantCatalogOverlayStore? overlay = null) =>
        new(
            catalogDir: CatalogDir(),
            overlayStore: overlay ?? new NullTenantCatalogOverlayStore(),
            logger: NullLogger<ExamCatalogService>.Instance);

    [Fact]
    public void Load_reads_every_yaml_target_and_dedupes_families()
    {
        var svc = NewService();
        var snap = svc.Current;

        Assert.NotEmpty(snap.TargetsByCode);
        Assert.Contains("BAGRUT_MATH_5U", snap.TargetsByCode.Keys);
        Assert.Contains("BAGRUT_MATH_5U_ARAB_STREAM", snap.TargetsByCode.Keys);
        Assert.Contains("PET_QUANTITATIVE", snap.TargetsByCode.Keys);
        Assert.Contains("IB_MATH_HL_AA", snap.TargetsByCode.Keys);

        Assert.Contains("BAGRUT", snap.FamilyOrder);
        Assert.True(snap.Families["BAGRUT"].Count > 0);

        // Ministry codes must survive the parse — ADR-0050 §2.
        var math5u = snap.TargetsByCode["BAGRUT_MATH_5U"];
        Assert.Equal("035", math5u.MinistrySubjectCode);
        Assert.Contains("035581", math5u.MinistryQuestionPaperCodes);
    }

    // (a) Tenant-filtered list — overlay DisabledExamCodes subtracts from global.
    [Fact]
    public void GetForTenant_respects_overlay_disabled_codes()
    {
        var overlay = new StubOverlay(new CatalogTenantOverlay(
            TenantId: "school-42",
            EnabledExamCodes: null,
            DisabledExamCodes: new[] { "PET_QUANTITATIVE", "PET_VERBAL_RUSSIAN" }));
        var svc = NewService(overlay);

        var dto = svc.GetForTenant("school-42", "en", overlay.Resolve("school-42"));

        var allCodes = dto.Groups.SelectMany(g => g.Targets.Select(t => t.ExamCode)).ToArray();
        Assert.DoesNotContain("PET_QUANTITATIVE", allCodes);
        Assert.DoesNotContain("PET_VERBAL_RUSSIAN", allCodes);
        Assert.Contains("BAGRUT_MATH_5U", allCodes);
    }

    // (a2) Allow-list semantics — explicit EnabledExamCodes intersects.
    [Fact]
    public void GetForTenant_respects_overlay_enabled_allowlist()
    {
        var overlay = new StubOverlay(new CatalogTenantOverlay(
            TenantId: "school-7",
            EnabledExamCodes: new[] { "BAGRUT_MATH_5U", "BAGRUT_MATH_4U" },
            DisabledExamCodes: Array.Empty<string>()));
        var svc = NewService(overlay);

        var dto = svc.GetForTenant("school-7", "he", overlay.Resolve("school-7"));

        var allCodes = dto.Groups.SelectMany(g => g.Targets.Select(t => t.ExamCode)).ToArray();
        Assert.Equal(new[] { "BAGRUT_MATH_5U", "BAGRUT_MATH_4U" }.OrderBy(x => x),
                     allCodes.OrderBy(x => x));
    }

    // (b) Unknown target → null (→ 404 in endpoint).
    [Fact]
    public void GetTopics_returns_null_for_unknown_code()
    {
        var svc = NewService();
        var dto = svc.GetTopics("DEFINITELY_NOT_A_REAL_TARGET_123", "en");
        Assert.Null(dto);
    }

    [Fact]
    public void GetTopics_returns_localized_display_for_arabic()
    {
        var svc = NewService();
        var dto = svc.GetTopics("BAGRUT_MATH_5U", "ar");
        Assert.NotNull(dto);
        Assert.Equal("ar", dto!.Locale);
        // Topics must carry Arabic names.
        Assert.All(dto.Topics, t => Assert.False(string.IsNullOrWhiteSpace(t.Display.Name)));
    }

    [Fact]
    public void GetTopics_falls_back_to_english_for_unsupported_locale()
    {
        var svc = NewService();
        var dto = svc.GetTopics("BAGRUT_MATH_5U", "zz");
        Assert.NotNull(dto);
        Assert.Equal("en", dto!.Locale);
    }

    // (c) Offline fallback — bundle is parseable JSON whose version matches.
    [Fact]
    public void RenderFallbackJson_produces_bundle_with_matching_version()
    {
        var svc = NewService();
        var json = svc.RenderFallbackJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(svc.Current.CatalogVersion,
                     root.GetProperty("catalog_version").GetString());
        Assert.True(root.GetProperty("groups").GetArrayLength() > 0);
    }

    // (c2) Offline fallback mimics the CDN-outage code path — service returns
    // its rendered bundle when the SPA degrades to fallback.
    [Fact]
    public void RenderFallbackJson_is_stable_across_calls()
    {
        var svc = NewService();
        var a = svc.RenderFallbackJson();
        var b = svc.RenderFallbackJson();
        Assert.Equal(a, b);
    }

    // (d) CDN outage simulation: the SPA may be holding a cached catalog with
    // an OLDER version; when the CDN comes back online, the service must
    // refuse to publish a snapshot whose version is NOT strictly greater.
    // This is modeled by RebuildAsync with the current on-disk state — it
    // should refuse because the on-disk version equals the loaded one.
    [Fact]
    public async Task Rebuild_refuses_same_version_rebuild()
    {
        var svc = NewService();
        var before = svc.Current.CatalogVersion;

        var outcome = await svc.RebuildAsync();

        Assert.False(outcome.Accepted);
        Assert.Contains(outcome.Warnings, w => w.Contains("non_monotonic_version", StringComparison.Ordinal));
        Assert.Equal(before, svc.Current.CatalogVersion);
    }

    // (e) Monotonicity guard — equal version refuses, strictly greater wins.
    [Theory]
    [InlineData("2026.04.21-01", "2026.04.21-01", false)]
    [InlineData("2026.04.21-01", "2026.04.21-02", true)]
    [InlineData("2026.04.20-03", "2026.04.21-01", true)]
    [InlineData("2026.04.21-02", "2026.04.21-01", false)]
    [InlineData("2026.04.21-01", "2026.04.20-99", false)]
    [InlineData("", "2026.04.21-01", true)]   // baseline blank → accept
    [InlineData("2026.04.21-01", "", false)]  // candidate blank → refuse
    public void Monotonicity_guard_enforces_strict_greater(
        string baseline, string candidate, bool expected)
    {
        Assert.Equal(expected,
            ExamCatalogService.IsStrictlyGreater(candidate, baseline));
    }

    private sealed class StubOverlay : ITenantCatalogOverlayStore
    {
        private readonly CatalogTenantOverlay _o;
        public StubOverlay(CatalogTenantOverlay o) { _o = o; }
        public CatalogTenantOverlay Resolve(string? tenantId) => _o;
    }
}
