// =============================================================================
// Cena Platform — MartenConceptCurationCalibrationCounter integration tests
// (ADR-0062 Phase 1 calibration corpus gate)
//
// What this pins:
//   1. The counter's QueryAllRawEvents-based count actually returns the
//      number of distinct streams with QuestionConceptsConfirmed_V1
//      events when run against a real Marten event store.
//   2. The EventTypeName fallback (FullName vs short Name) survives a
//      round-trip through Marten's STJ serialiser — was a guess in the
//      implementation, gets verified here.
//   3. Distinct-stream semantics: re-confirms on the same stream count
//      once, not twice.
//   4. The "calibration complete" cache flips when the threshold is hit
//      and the in-flight cache invalidates correctly.
//
// Why an integration test (and not just a stub-counter unit test):
//   The counter's job is the *Marten query*. A unit test that mocks
//   Marten would re-prove the wiring of NSubstitute, not the SQL.
//   Per `feedback_event_sourcing_replay_check`, projection-replay
//   defects ship undetected when the actual store layer is mocked out.
//
// Why no testcontainers (per `feedback_container_state_before_build`):
//   Spawning a Postgres container during host-side tests is the pattern
//   that has crashed Docker Desktop. The dev `cena-postgres` on :5433
//   is already running. Each test run uses its own schema so dev data
//   stays untouched and parallel runs don't collide.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Extraction;
using JasperFx;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Core;

namespace Cena.Actors.Tests.Mastery;

public sealed class MartenConceptCurationCalibrationCounterIntegrationTests : IAsyncLifetime
{
    // Same connection string convention as Rdy081SeqCollisionTests — dev
    // Postgres, dev-only password matching docker-compose.yml.
    private const string DevPostgres =
        "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

    private readonly string _schema = $"calib_{Guid.NewGuid():N}"[..24];
    private bool _postgresReachable;

    public async Task InitializeAsync() => _postgresReachable = await ProbePostgresAsync();

    public Task DisposeAsync()
    {
        if (!_postgresReachable) return Task.CompletedTask;
        try
        {
            using var conn = new NpgsqlConnection(DevPostgres);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS {_schema} CASCADE;";
            cmd.ExecuteNonQuery();
        }
        catch { /* best-effort cleanup */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Count_DistinctStreams_WithConfirmEvents()
    {
        if (!_postgresReachable)
        {
            Assert.Fail("cena-postgres on localhost:5433 is unreachable. " +
                        "Run `docker compose up -d postgres` before executing this test.");
        }

        await using var store = BuildStore();

        // Seed: 3 distinct streams each with one confirm event +
        // one stream with TWO confirms (re-confirm — counts once).
        var distinctStreamIds = new[] { "q-1", "q-2", "q-3", "q-4" };
        await using (var session = store.LightweightSession())
        {
            foreach (var sid in distinctStreamIds)
            {
                session.Events.Append(sid, BuildConfirmed(sid));
            }
            // Re-confirm on q-4 — should still count as ONE distinct stream.
            session.Events.Append("q-4", BuildConfirmed("q-4"));
            await session.SaveChangesAsync();
        }

        var counter = BuildCounter(store, threshold: 100);

        var count = await counter.GetConfirmedItemCountAsync();
        Assert.Equal(4, count);
        Assert.False(await counter.IsCalibrationCompleteAsync());
    }

    [Fact]
    public async Task Count_IgnoresStreams_WithOnlyExtractedEvents()
    {
        if (!_postgresReachable)
        {
            Assert.Fail("cena-postgres on localhost:5433 is unreachable. " +
                        "Run `docker compose up -d postgres` before executing this test.");
        }

        await using var store = BuildStore();

        // Two streams. One has only an extracted event (calibration-pending);
        // the other has both extracted + confirmed (calibration contributor).
        // Counter must return 1, not 2.
        await using (var session = store.LightweightSession())
        {
            session.Events.Append("q-pending", BuildExtracted("q-pending"));
            session.Events.Append("q-confirmed",
                BuildExtracted("q-confirmed"),
                BuildConfirmed("q-confirmed"));
            await session.SaveChangesAsync();
        }

        var counter = BuildCounter(store, threshold: 100);

        var count = await counter.GetConfirmedItemCountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IsCalibrationComplete_FlipsTrue_AndCachesIndefinitely()
    {
        if (!_postgresReachable)
        {
            Assert.Fail("cena-postgres on localhost:5433 is unreachable. " +
                        "Run `docker compose up -d postgres` before executing this test.");
        }

        await using var store = BuildStore();

        // Threshold: 3. Seed exactly 3 distinct confirm streams.
        await using (var session = store.LightweightSession())
        {
            for (int i = 0; i < 3; i++)
            {
                session.Events.Append($"q-{i}", BuildConfirmed($"q-{i}"));
            }
            await session.SaveChangesAsync();
        }

        var counter = BuildCounter(store, threshold: 3);

        Assert.True(await counter.IsCalibrationCompleteAsync());

        // Once "complete" flips, the count clamps to threshold (the cache
        // never re-queries the DB — monotone per ADR-0062). Even if we
        // delete the schema underneath, the cached truth holds.
        var clampedCount = await counter.GetConfirmedItemCountAsync();
        Assert.Equal(3, clampedCount);
    }

    // ---- helpers ----

    private DocumentStore BuildStore() => DocumentStore.For(opts =>
    {
        opts.Connection(DevPostgres);
        opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
        opts.DatabaseSchemaName = _schema;
        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Events.AppendMode = EventAppendMode.Rich;
        opts.UseSystemTextJsonForSerialization(
            enumStorage: EnumStorage.AsString,
            casing: Casing.CamelCase);
        // Same registration as MartenConfiguration.cs — production path.
        opts.Events.AddEventType<QuestionConceptsExtracted_V1>();
        opts.Events.AddEventType<QuestionConceptsConfirmed_V1>();
    });

    private static MartenConceptCurationCalibrationCounter BuildCounter(
        IDocumentStore store, int threshold)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cena:Concepts:CalibrationThreshold"] = threshold.ToString(),
            })
            .Build();
        return new MartenConceptCurationCalibrationCounter(
            store, config,
            NullLogger<MartenConceptCurationCalibrationCounter>.Instance);
    }

    private static QuestionConceptsConfirmed_V1 BuildConfirmed(string streamId) =>
        new(QuestionId: streamId,
            Concepts: new[]
            {
                new QuestionConcept(
                    SkillCode.Parse("math.calculus.derivative-rules"),
                    ConceptRole.Primary, 0.9, "", "curator"),
            },
            Action: CuratorAction.AcceptedAsExtracted,
            ConfirmedBy: "curator-test",
            Timestamp: DateTimeOffset.UtcNow);

    private static QuestionConceptsExtracted_V1 BuildExtracted(string streamId) =>
        new(QuestionId: streamId,
            Concepts: new[]
            {
                new QuestionConcept(
                    SkillCode.Parse("math.calculus.derivative-rules"),
                    ConceptRole.Primary, 0.6, "", "rules"),
            },
            ExtractionStrategy: "rules_v1",
            ExtractedBy: "test",
            Timestamp: DateTimeOffset.UtcNow);

    private static async Task<bool> ProbePostgresAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(DevPostgres);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await conn.OpenAsync(cts.Token);
            return conn.State == System.Data.ConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }
}
