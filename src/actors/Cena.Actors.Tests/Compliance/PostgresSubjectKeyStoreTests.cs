// =============================================================================
// Cena Platform -- Postgres Subject Key Store Unit Tests (ADR-0038, prr-003b)
//
// SCOPE: This is a pure unit-test suite that verifies the SQL-generation
// path, DI wiring, and contract semantics that do NOT require a live
// Postgres instance.
//
// FOLLOW-UP: integration tests against a real Postgres container require
// Testcontainers.PostgreSql (not currently referenced by this test project).
// See db/migrations/V0002__cena_subject_keys.sql for the expected schema.
// Once Testcontainers is adopted, add an integration companion that:
//   1. Spins up a Postgres container,
//   2. Applies V0001 + V0002,
//   3. Round-trips encrypt → tombstone → encrypt-refused against
//      PostgresSubjectKeyStore using the shared NpgsqlDataSource.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cena.Actors.Tests.Compliance;

public sealed class PostgresSubjectKeyStoreTests
{
    // -------------------------------------------------------------------------
    // Part 1 — SQL shape assertions.
    //   The SQL statements PostgresSubjectKeyStore issues are the ADR-0038
    //   write-path contract. Flipping any of these without updating the
    //   migration is a compliance regression.
    // -------------------------------------------------------------------------

    [Fact]
    public void Select_targets_cena_subject_keys_table()
    {
        Assert.Contains("FROM cena.cena_subject_keys", PostgresSubjectKeyStore.SqlSelect);
        Assert.Contains("WHERE subject_id = @id", PostgresSubjectKeyStore.SqlSelect);
    }

    [Fact]
    public void Insert_uses_on_conflict_do_nothing_for_idempotency()
    {
        Assert.Contains("INSERT INTO cena.cena_subject_keys", PostgresSubjectKeyStore.SqlInsert);
        Assert.Contains("ON CONFLICT (subject_id) DO NOTHING", PostgresSubjectKeyStore.SqlInsert);
    }

    [Fact]
    public void Delete_flips_encrypted_key_to_null_and_stamps_tombstoned_at()
    {
        Assert.Contains("UPDATE cena.cena_subject_keys", PostgresSubjectKeyStore.SqlDelete);
        Assert.Contains("encrypted_key = NULL", PostgresSubjectKeyStore.SqlDelete);
        Assert.Contains("tombstoned_at = NOW()", PostgresSubjectKeyStore.SqlDelete);
        // Refuses to overwrite an already-tombstoned row — guarantees
        // DeleteAsync returns `existed=false` for a second call.
        Assert.Contains("AND encrypted_key IS NOT NULL", PostgresSubjectKeyStore.SqlDelete);
    }

    [Fact]
    public void Exists_filters_out_tombstoned_rows()
    {
        Assert.Contains("WHERE subject_id = @id", PostgresSubjectKeyStore.SqlExists);
        Assert.Contains("AND encrypted_key IS NOT NULL", PostgresSubjectKeyStore.SqlExists);
    }

    [Fact]
    public void ListActive_excludes_tombstoned_rows()
    {
        Assert.Contains("WHERE encrypted_key IS NOT NULL", PostgresSubjectKeyStore.SqlListActive);
        Assert.Contains("ORDER BY subject_id", PostgresSubjectKeyStore.SqlListActive);
    }

    // -------------------------------------------------------------------------
    // Part 2 — Audit-log hashing is consistent across implementations.
    //   Both InMemorySubjectKeyStore and PostgresSubjectKeyStore must emit
    //   the same 16-char hex hash for a given subject ID so SIEM queries
    //   don't split across storage backings.
    // -------------------------------------------------------------------------

    [Fact]
    public void Audit_hash_matches_in_memory_implementation()
    {
        const string subjectId = "student-postgres-001";
        var inMem = InMemorySubjectKeyStore.HashSubjectForLog(subjectId);
        var pg = PostgresSubjectKeyStore.HashSubjectForLog(subjectId);

        Assert.Equal(inMem, pg);
        Assert.Equal(16, pg.Length);
        Assert.DoesNotContain(subjectId, pg, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Part 3 — Null-safety contract.
    //   Empty subject IDs short-circuit to (null / false) WITHOUT opening
    //   a DB connection. The constructor requires a real NpgsqlDataSource
    //   so we use a parked one — the methods must not call into it.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrCreate_with_empty_subject_returns_null_without_db()
    {
        var sut = BuildSut();
        Assert.Null(await sut.GetOrCreateAsync(""));
        Assert.Null(await sut.GetOrCreateAsync(null!));
    }

    [Fact]
    public async Task Exists_with_empty_subject_returns_false_without_db()
    {
        var sut = BuildSut();
        Assert.False(await sut.ExistsAsync(""));
        Assert.False(await sut.ExistsAsync(null!));
    }

    [Fact]
    public async Task Delete_with_empty_subject_returns_false_without_db()
    {
        var sut = BuildSut();
        Assert.False(await sut.DeleteAsync(""));
        Assert.False(await sut.DeleteAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dataSource()
    {
        var derivation = BuildDerivation();
        Assert.Throws<ArgumentNullException>(
            () => new PostgresSubjectKeyStore(null!, derivation));
    }

    [Fact]
    public void Constructor_rejects_null_derivation()
    {
        var dataSource = BuildParkedDataSource();
        Assert.Throws<ArgumentNullException>(
            () => new PostgresSubjectKeyStore(dataSource, null!));
    }

    // -------------------------------------------------------------------------
    // Part 4 — DI registration selects the right backing.
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSubjectKeyStore_in_production_selects_postgres_backing()
    {
        var env = new StubHostEnvironment("Production");
        var config = BuildConfig(backing: null);

        var backing = SubjectKeyStoreRegistration_InvokeResolve(config, env);
        Assert.Equal("Postgres", backing);
    }

    [Fact]
    public void AddSubjectKeyStore_in_development_selects_in_memory_backing()
    {
        var env = new StubHostEnvironment(Environments.Development);
        var config = BuildConfig(backing: null);

        var backing = SubjectKeyStoreRegistration_InvokeResolve(config, env);
        Assert.Equal("InMemory", backing);
    }

    [Fact]
    public void AddSubjectKeyStore_in_testing_selects_in_memory_backing()
    {
        var env = new StubHostEnvironment("Testing");
        var config = BuildConfig(backing: null);

        var backing = SubjectKeyStoreRegistration_InvokeResolve(config, env);
        Assert.Equal("InMemory", backing);
    }

    [Fact]
    public void AddSubjectKeyStore_config_override_takes_precedence_over_environment()
    {
        var env = new StubHostEnvironment("Production");
        var config = BuildConfig(backing: "in-memory");

        var backing = SubjectKeyStoreRegistration_InvokeResolve(config, env);
        Assert.Equal("InMemory", backing);
    }

    [Fact]
    public void AddSubjectKeyStore_config_override_postgres_takes_precedence()
    {
        var env = new StubHostEnvironment(Environments.Development);
        var config = BuildConfig(backing: "postgres");

        var backing = SubjectKeyStoreRegistration_InvokeResolve(config, env);
        Assert.Equal("Postgres", backing);
    }

    [Fact]
    public void AddSubjectKeyStore_falls_back_to_in_memory_when_postgres_selected_but_no_datasource()
    {
        // Production backing selected, but no NpgsqlDataSource in DI.
        // Registration must fall back to in-memory rather than failing
        // DI resolution mid-request. A warning is logged (see production code).
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(backing: "postgres");
        var env = new StubHostEnvironment("Production");

        services.AddSubjectKeyStore(config, env);

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<ISubjectKeyStore>();
        Assert.IsType<InMemorySubjectKeyStore>(store);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PostgresSubjectKeyStore BuildSut()
        => new(BuildParkedDataSource(), BuildDerivation());

    private static SubjectKeyDerivation BuildDerivation()
    {
        var rootKey = new byte[32];
        for (var i = 0; i < rootKey.Length; i++) rootKey[i] = (byte)i;
        return new SubjectKeyDerivation(rootKey, "unit-test-install", isDevFallback: false);
    }

    private static Npgsql.NpgsqlDataSource BuildParkedDataSource()
    {
        // Not opened, not connected — every null-safety test must short-circuit
        // before hitting the connection. If we accidentally try to connect,
        // Npgsql will fail loudly with a connect-refused error.
        var builder = new Npgsql.NpgsqlDataSourceBuilder(
            "Host=127.0.0.1;Port=1;Database=_parked;Username=_parked;Password=_parked;Timeout=1");
        return builder.Build();
    }

    private static IConfiguration BuildConfig(string? backing)
    {
        var dict = new Dictionary<string, string?>();
        if (backing is not null)
        {
            dict[SubjectKeyStoreRegistration.BackingConfigKey] = backing;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // Calls the internal resolver via the public DI path — we build a DI
    // container and ask for the store, then report which concrete type
    // showed up. This verifies the public contract end-to-end.
    private static string SubjectKeyStoreRegistration_InvokeResolve(
        IConfiguration config, IHostEnvironment env)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Mimic real wiring: AddCenaDataSource would register NpgsqlDataSource
        // before AddSubjectKeyStore. We skip AddCenaDataSource (no real DB)
        // and register a parked NpgsqlDataSource directly so Postgres backing
        // can resolve.
        services.AddSingleton(BuildParkedDataSource());

        services.AddSubjectKeyStore(config, env);

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<ISubjectKeyStore>();
        return store switch
        {
            PostgresSubjectKeyStore => "Postgres",
            InMemorySubjectKeyStore => "InMemory",
            _ => store.GetType().Name
        };
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Cena.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
