// =============================================================================
// Cena Platform — Misconception PII Store Registry unit tests (prr-015)
//
// Covers:
//   • Registration succeeds with valid input; invariants enforced.
//   • Duplicate registration rejected.
//   • Registration after freeze rejected.
//   • GetAll returns deterministic ordered snapshot.
//   • Purge callback lookup resolves + throws for unknown names.
//   • Retention clamping: declared >90d → effective=90d.
// =============================================================================

using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Tests.Compliance;

public sealed class MisconceptionPiiStoreRegistryTests
{
    private static RegisteredMisconceptionStore NewStore(
        string name,
        int days = 30,
        MisconceptionPurgeStrategy strategy = MisconceptionPurgeStrategy.Delete,
        bool verified = true) =>
        new(
            StoreName: name,
            RetentionDays: days,
            PurgeStrategy: strategy,
            SessionScopeVerified: verified,
            OwningModule: "Cena.Actors.Tests");

    private static MisconceptionPurgeCallback NoOp() =>
        (_, __) => Task.FromResult(0);

    [Fact]
    public void Register_ValidStore_CanBeRetrievedByName()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        var store = NewStore("alpha");

        registry.Register(store, NoOp());

        Assert.Same(store, registry.Get("alpha"));
    }

    [Fact]
    public void Register_Duplicate_ThrowsInvalidOperation()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        registry.Register(NewStore("alpha"), NoOp());

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register(NewStore("alpha"), NoOp()));
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void Register_AfterFreeze_Throws()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        registry.Register(NewStore("alpha"), NoOp());

        // Freeze by enumerating.
        _ = registry.GetAll();

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register(NewStore("beta"), NoOp()));
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void GetAll_ReturnsStoresInDeterministicOrder()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        registry.Register(NewStore("charlie"), NoOp());
        registry.Register(NewStore("alpha"), NoOp());
        registry.Register(NewStore("bravo"), NoOp());

        var all = registry.GetAll();

        Assert.Equal(new[] { "alpha", "bravo", "charlie" }, all.Select(s => s.StoreName));
    }

    [Fact]
    public void Get_UnknownStore_ReturnsNull()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        Assert.Null(registry.Get("nonexistent"));
    }

    [Fact]
    public void GetPurgeCallback_UnknownStore_Throws()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetPurgeCallback("nope"));
    }

    [Fact]
    public void RegisteredStore_BlankName_Throws()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        Assert.Throws<ArgumentException>(() =>
            registry.Register(NewStore(""), NoOp()));
    }

    [Fact]
    public void RegisteredStore_ZeroRetention_Throws()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            registry.Register(NewStore("alpha", days: 0), NoOp()));
    }

    [Fact]
    public void RegisteredStore_BlankOwningModule_Throws()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        var store = new RegisteredMisconceptionStore(
            StoreName: "alpha",
            RetentionDays: 30,
            PurgeStrategy: MisconceptionPurgeStrategy.Delete,
            SessionScopeVerified: true,
            OwningModule: "   ");

        Assert.Throws<ArgumentException>(() => registry.Register(store, NoOp()));
    }

    [Fact]
    public void EffectiveRetentionDays_Under90d_ReturnsDeclared()
    {
        var store = NewStore("alpha", days: 30);
        Assert.Equal(30, store.EffectiveRetentionDays);
    }

    [Fact]
    public void EffectiveRetentionDays_Over90d_ClampedTo90()
    {
        var store = NewStore("alpha", days: 365);
        Assert.Equal(90, store.EffectiveRetentionDays);
    }

    [Fact]
    public void EffectiveRetentionDays_Exactly90d_Unchanged()
    {
        var store = NewStore("alpha", days: 90);
        Assert.Equal(90, store.EffectiveRetentionDays);
    }

    [Fact]
    public void Register_BeforeFreeze_ConcurrentRegistrationsAllSucceed()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        var names = Enumerable.Range(0, 32).Select(i => $"store-{i:D2}").ToArray();

        Parallel.ForEach(names, n => registry.Register(NewStore(n), NoOp()));

        var all = registry.GetAll();
        Assert.Equal(32, all.Count);
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal),
                     all.Select(s => s.StoreName));
    }

    [Fact]
    public void GetPurgeCallback_RegisteredStore_InvokesWithProvidedArguments()
    {
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        DateTimeOffset? seenCutoff = null;

        MisconceptionPurgeCallback cb = (cutoff, _) =>
        {
            seenCutoff = cutoff;
            return Task.FromResult(7);
        };

        registry.Register(NewStore("alpha"), cb);
        var resolved = registry.GetPurgeCallback("alpha");

        var fakeCutoff = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var purged = resolved(fakeCutoff, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(7, purged);
        Assert.Equal(fakeCutoff, seenCutoff);
    }
}
