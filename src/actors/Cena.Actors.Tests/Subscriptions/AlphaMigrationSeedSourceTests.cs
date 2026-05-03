// =============================================================================
// Cena Platform — AlphaMigrationSeedSource tests (EPIC-PRR-I PRR-344)
//
// Locks in the operator-seam contract of IAlphaMigrationSeedSource. The
// in-memory implementation is exercised end-to-end; the Marten variant
// shares the same overwrite / clean / empty semantics and is covered by
// the full-sln build (type-check) + the AlphaUserMigrationWorker tests
// that drive it through a fake wrapper (no live Postgres here — Marten
// behaviour is covered by MartenSubscriptionAggregateStoreTests).
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class AlphaMigrationSeedSourceTests
{
    private const string AdminId = "enc::admin::op-01";

    [Fact]
    public async Task InMemory_stores_and_retrieves_seed_list()
    {
        var source = new InMemoryAlphaMigrationSeedSource();

        await source.SeedAsync(
            new[] { "enc::parent::a", "enc::parent::b", "enc::parent::c" },
            AdminId,
            CancellationToken.None);

        var got = await source.GetSeedParentIdsAsync(CancellationToken.None);
        Assert.Equal(3, got.Count);
        Assert.Contains("enc::parent::a", got);
        Assert.Contains("enc::parent::b", got);
        Assert.Contains("enc::parent::c", got);
    }

    [Fact]
    public async Task InMemory_overwrite_replaces_previous_list_not_appends()
    {
        var source = new InMemoryAlphaMigrationSeedSource();

        // First upload — three ids
        await source.SeedAsync(
            new[] { "enc::parent::a", "enc::parent::b", "enc::parent::c" },
            AdminId,
            CancellationToken.None);

        // Second upload — different two ids. Must REPLACE the first list,
        // not merge with it.
        await source.SeedAsync(
            new[] { "enc::parent::x", "enc::parent::y" },
            AdminId,
            CancellationToken.None);

        var got = await source.GetSeedParentIdsAsync(CancellationToken.None);
        Assert.Equal(2, got.Count);
        Assert.DoesNotContain("enc::parent::a", got);
        Assert.DoesNotContain("enc::parent::b", got);
        Assert.DoesNotContain("enc::parent::c", got);
        Assert.Contains("enc::parent::x", got);
        Assert.Contains("enc::parent::y", got);
    }

    [Fact]
    public async Task InMemory_empty_seed_before_any_upload()
    {
        var source = new InMemoryAlphaMigrationSeedSource();

        var got = await source.GetSeedParentIdsAsync(CancellationToken.None);
        Assert.Empty(got);
    }

    [Fact]
    public async Task InMemory_filters_whitespace_and_duplicates()
    {
        var source = new InMemoryAlphaMigrationSeedSource();

        // Dirty input: empty strings, whitespace-only, duplicates.
        await source.SeedAsync(
            new[] { "enc::parent::a", "", "  ", "enc::parent::a", "enc::parent::b" },
            AdminId,
            CancellationToken.None);

        var got = await source.GetSeedParentIdsAsync(CancellationToken.None);
        Assert.Equal(2, got.Count);
        Assert.Contains("enc::parent::a", got);
        Assert.Contains("enc::parent::b", got);
    }

    [Fact]
    public async Task InMemory_records_audit_fields()
    {
        var source = new InMemoryAlphaMigrationSeedSource();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await source.SeedAsync(
            new[] { "enc::parent::a" },
            AdminId,
            CancellationToken.None);

        Assert.Equal(AdminId, source.LastUploadedBy);
        Assert.True(source.LastUploadedAt >= before);
    }

    [Fact]
    public async Task InMemory_rejects_empty_uploader_audit()
    {
        var source = new InMemoryAlphaMigrationSeedSource();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.SeedAsync(
                new[] { "enc::parent::a" },
                uploadedByAdminSubjectId: "",
                CancellationToken.None));
    }

    [Fact]
    public async Task InMemory_empty_upload_leaves_empty_list()
    {
        var source = new InMemoryAlphaMigrationSeedSource();
        // Seed something first so we can verify it's cleared.
        await source.SeedAsync(
            new[] { "enc::parent::a" }, AdminId, CancellationToken.None);

        await source.SeedAsync(Array.Empty<string>(), AdminId, CancellationToken.None);

        var got = await source.GetSeedParentIdsAsync(CancellationToken.None);
        Assert.Empty(got);
    }
}
