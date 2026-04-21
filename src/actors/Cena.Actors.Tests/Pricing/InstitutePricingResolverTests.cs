// =============================================================================
// Cena Platform — InstitutePricingResolver unit tests (prr-244)
//
// Pins the three resolver behaviours required by PRR-244:
//   (a) No override → resolver returns YAML defaults.
//   (b) Override applied → resolver returns override values.
//   (c) Override with EffectiveUntilUtc in the past → falls back to defaults.
//
// Also verifies the cache seam is honoured (cache hit skips store lookup).
// =============================================================================

using Cena.Actors.Pricing;

namespace Cena.Actors.Tests.Pricing;

public sealed class InstitutePricingResolverTests
{
    private static DefaultPricingYaml LoadCanonicalDefaults()
    {
        const string yaml = """
            version: 1
            effective_from_utc: "2026-04-21T00:00:00Z"
            source: "test-default"
            student_monthly_price_usd: 19.00
            institutional_per_seat_price_usd: 14.00
            min_seats_for_institutional: 20
            free_tier_session_cap: 10
            bounds:
              student_monthly_price_usd_min: 3.30
              student_monthly_price_usd_max: 99.00
              institutional_per_seat_price_usd_min: 2.31
              institutional_per_seat_price_usd_max: 99.00
              min_seats_for_institutional_min: 1
              min_seats_for_institutional_max: 1000
              free_tier_session_cap_min: 0
              free_tier_session_cap_max: 500
            """;
        return DefaultPricingYaml.LoadFromYaml(yaml);
    }

    [Fact]
    public async Task Resolve_NoOverride_ReturnsYamlDefaults()
    {
        var defaults = LoadCanonicalDefaults();
        var store = new InMemoryInstitutePricingOverrideStore();
        var resolver = new InstitutePricingResolver(defaults, store);

        var result = await resolver.ResolveAsync("institute-test");

        Assert.Equal(PricingSource.Default, result.Source);
        Assert.Equal(19.00m, result.StudentMonthlyPriceUsd);
        Assert.Equal(14.00m, result.InstitutionalPerSeatPriceUsd);
        Assert.Equal(20, result.MinSeatsForInstitutional);
        Assert.Equal(10, result.FreeTierSessionCap);
    }

    [Fact]
    public async Task Resolve_WithActiveOverride_ReturnsOverrideValues()
    {
        var defaults = LoadCanonicalDefaults();
        var store = new InMemoryInstitutePricingOverrideStore();
        await store.UpsertAsync(new InstitutePricingOverrideDocument
        {
            Id = InstitutePricingOverrideDocument.IdFor("institute-subsidy"),
            InstituteId = "institute-subsidy",
            StudentMonthlyPriceUsd = 7.50m,
            InstitutionalPerSeatPriceUsd = 5.00m,
            MinSeatsForInstitutional = 10,
            FreeTierSessionCap = 50,
            EffectiveFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
            OverriddenBySuperAdminId = "super-admin-1",
            JustificationText = "Regional subsidy pilot — 2026 Q2 funded by grant",
        });
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var resolver = new InstitutePricingResolver(defaults, store, clock: clock.Now);

        var result = await resolver.ResolveAsync("institute-subsidy");

        Assert.Equal(PricingSource.Override, result.Source);
        Assert.Equal(7.50m, result.StudentMonthlyPriceUsd);
        Assert.Equal(5.00m, result.InstitutionalPerSeatPriceUsd);
        Assert.Equal(10, result.MinSeatsForInstitutional);
        Assert.Equal(50, result.FreeTierSessionCap);
    }

    [Fact]
    public async Task Resolve_ExpiredOverride_FallsBackToDefaults()
    {
        var defaults = LoadCanonicalDefaults();
        var store = new InMemoryInstitutePricingOverrideStore();
        var clock = new TestClock(DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await store.UpsertAsync(new InstitutePricingOverrideDocument
        {
            Id = InstitutePricingOverrideDocument.IdFor("institute-expired"),
            InstituteId = "institute-expired",
            StudentMonthlyPriceUsd = 9.99m,
            InstitutionalPerSeatPriceUsd = 7.00m,
            MinSeatsForInstitutional = 5,
            FreeTierSessionCap = 100,
            EffectiveFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            EffectiveUntilUtc = DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
            OverriddenBySuperAdminId = "super-admin-1",
            JustificationText = "Temporary Q1 promotional pricing for pilot",
        });
        var resolver = new InstitutePricingResolver(defaults, store, clock: clock.Now);

        var result = await resolver.ResolveAsync("institute-expired");

        Assert.Equal(PricingSource.Default, result.Source);
        Assert.Equal(19.00m, result.StudentMonthlyPriceUsd);
    }

    [Fact]
    public async Task Resolve_FutureOverride_FallsBackToDefaults()
    {
        var defaults = LoadCanonicalDefaults();
        var store = new InMemoryInstitutePricingOverrideStore();
        var clock = new TestClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await store.UpsertAsync(new InstitutePricingOverrideDocument
        {
            Id = InstitutePricingOverrideDocument.IdFor("institute-future"),
            InstituteId = "institute-future",
            StudentMonthlyPriceUsd = 5.00m,
            InstitutionalPerSeatPriceUsd = 4.00m,
            MinSeatsForInstitutional = 10,
            FreeTierSessionCap = 30,
            EffectiveFromUtc = DateTimeOffset.Parse("2026-12-01T00:00:00Z"),
            OverriddenBySuperAdminId = "super-admin-1",
            JustificationText = "Pre-scheduled end-of-year pricing for fiscal cycle",
        });
        var resolver = new InstitutePricingResolver(defaults, store, clock: clock.Now);

        var result = await resolver.ResolveAsync("institute-future");

        Assert.Equal(PricingSource.Default, result.Source);
    }

    [Fact]
    public async Task Resolve_CacheHit_BypassesStore()
    {
        var defaults = LoadCanonicalDefaults();
        var store = new CountingStore();
        var cache = new MemoryPricingCache();
        var resolver = new InstitutePricingResolver(defaults, store, cache);

        var first = await resolver.ResolveAsync("institute-cache");
        var second = await resolver.ResolveAsync("institute-cache");

        Assert.Equal(first, second);
        Assert.Equal(1, store.FindCalls);
    }

    [Fact]
    public void DefaultPricingYaml_LoadsCanonicalFile()
    {
        // Pin that the real YAML file parses + validates. Walks up to
        // the repo root from AppContext.BaseDirectory so the test runs
        // from any CI layout.
        var root = FindRepoRoot();
        var file = Path.Combine(root, "contracts", "pricing", "default-pricing.yml");
        Assert.True(File.Exists(file), $"Expected default-pricing.yml at {file}");

        var defaults = DefaultPricingYaml.LoadFromFile(file);
        Assert.Equal(1, defaults.Version);
        // Values must match the YAML at rest — if someone changes defaults
        // without also updating the test, the review surface is explicit.
        Assert.Equal(19.00m, defaults.StudentMonthlyPriceUsd);
        Assert.Equal(14.00m, defaults.InstitutionalPerSeatPriceUsd);
        Assert.Equal(20, defaults.MinSeatsForInstitutional);
        Assert.Equal(10, defaults.FreeTierSessionCap);
    }

    [Fact]
    public void DefaultPricingYaml_RejectsOutOfBoundsValues()
    {
        const string badYaml = """
            version: 1
            effective_from_utc: "2026-04-21T00:00:00Z"
            source: "test-bad"
            student_monthly_price_usd: 1.00
            institutional_per_seat_price_usd: 0.50
            min_seats_for_institutional: 20
            free_tier_session_cap: 10
            bounds:
              student_monthly_price_usd_min: 3.30
              student_monthly_price_usd_max: 99.00
              institutional_per_seat_price_usd_min: 2.31
              institutional_per_seat_price_usd_max: 99.00
              min_seats_for_institutional_min: 1
              min_seats_for_institutional_max: 1000
              free_tier_session_cap_min: 0
              free_tier_session_cap_max: 500
            """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => DefaultPricingYaml.LoadFromYaml(badYaml));
        Assert.Contains("student_monthly_price_usd", ex.Message);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found from test base directory.");
    }

    private sealed class TestClock
    {
        private DateTimeOffset _now;
        public TestClock(DateTimeOffset start) { _now = start; }
        public DateTimeOffset Now() => _now;
    }

    private sealed class CountingStore : IInstitutePricingOverrideStore
    {
        public int FindCalls { get; private set; }
        public Task<InstitutePricingOverrideDocument?> FindAsync(string instituteId, CancellationToken ct = default)
        {
            FindCalls++;
            return Task.FromResult<InstitutePricingOverrideDocument?>(null);
        }
        public Task UpsertAsync(InstitutePricingOverrideDocument document, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class MemoryPricingCache : IPricingCache
    {
        private readonly Dictionary<string, ResolvedPricing> _map = new(StringComparer.Ordinal);
        public Task<ResolvedPricing?> GetAsync(string instituteId, CancellationToken ct = default)
        {
            _map.TryGetValue(instituteId, out var v);
            return Task.FromResult<ResolvedPricing?>(v);
        }
        public Task SetAsync(string instituteId, ResolvedPricing value, CancellationToken ct = default)
        {
            _map[instituteId] = value;
            return Task.CompletedTask;
        }
    }
}
