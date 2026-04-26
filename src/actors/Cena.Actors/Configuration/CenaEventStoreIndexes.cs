// =============================================================================
// Cena Platform — Custom Marten event-store indexes (IFeatureSchema)
//
// Marten in CreateOrUpdate mode reconciles the schema for tables it owns
// (mt_events, mt_streams, mt_doc_*) on host startup via
// ApplyAllConfiguredChangesToDatabaseAsync(). During that step it DROPS
// any indexes on those tables that aren't part of its tracked schema —
// proven on 2026-04-26 when V0003's ix_mt_events_active_stream_seq was
// silently removed on every actor-host / admin-api / student-api boot.
//
// To make supplementary read-side indexes survive Marten reconciliation
// we register them through Marten's own IFeatureSchema mechanism so the
// schema diff treats them as Marten-managed objects. Marten then keeps
// them in sync (creates if missing, leaves alone if present) instead of
// dropping them as foreign artifacts.
//
// Indexes declared here:
//
//   ix_mt_events_active_stream_seq  — partial covering index on
//     (stream_id, seq_id) WHERE is_archived = false. Targets the
//     dominant Marten event-stream read query in mt_events. See V0003
//     migration header for the full EXPLAIN evidence and stress numbers.
//
// Add new event-store indexes to schemaObjects() below. Always pair them
// with a brief rationale + a pg_stat_statements measurement so the
// performance contribution stays auditable.
// =============================================================================

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Cena.Actors.Configuration;

internal sealed class CenaEventStoreIndexes : FeatureSchemaBase
{
    public CenaEventStoreIndexes(StoreOptions options)
        : base(nameof(CenaEventStoreIndexes), options.Advanced.Migrator)
    {
    }

    protected override IEnumerable<ISchemaObject> schemaObjects()
    {
        yield return new ActiveStreamSeqIndex();
    }
}

internal sealed class ActiveStreamSeqIndex : ISchemaObject
{
    private static readonly DbObjectName _identifier =
        new PostgresqlObjectName("cena", "ix_mt_events_active_stream_seq");

    // pg_advisory_xact_lock keyed off a stable hash of the index identifier
    // serializes concurrent apply attempts across actor-host / admin-api /
    // student-api. CREATE INDEX IF NOT EXISTS is not atomic with respect
    // to its existence check — two concurrent transactions can both see
    // "missing" and both attempt to insert into pg_class, and the loser
    // gets a 23505 duplicate-key on pg_class_relname_nsp_index. The
    // advisory lock ensures only one host holds the create-window at a
    // time; later arrivals see the index already exists and CREATE IF NOT
    // EXISTS is a true no-op for them.
    //
    // Lock key: deterministic int8 derived from the qualified index name.
    // Picked once and frozen so all hosts contend on the same key.
    private const long AdvisoryLockKey = -1963886295L; // hashtext('cena.ix_mt_events_active_stream_seq')

    private const string CreateSql = @"
SELECT pg_advisory_xact_lock(-1963886295);
CREATE INDEX IF NOT EXISTS ix_mt_events_active_stream_seq
    ON cena.mt_events (stream_id, seq_id)
    WHERE is_archived = false;
";

    private const string DropSql =
        "DROP INDEX IF EXISTS cena.ix_mt_events_active_stream_seq;";

    public DbObjectName Identifier => _identifier;

    public void WriteCreateStatement(Migrator rules, TextWriter writer)
        => writer.WriteLine(CreateSql);

    public void WriteDropStatement(Migrator rules, TextWriter writer)
        => writer.WriteLine(DropSql);

    public void ConfigureQueryCommand(Weasel.Core.DbCommandBuilder builder)
    {
        // Probe pg_indexes for the index. CreateDeltaAsync reads the
        // result: 1 row → exists, 0 rows → missing.
        builder.Append(
            "SELECT indexname FROM pg_indexes " +
            "WHERE schemaname = 'cena' AND indexname = 'ix_mt_events_active_stream_seq';");
    }

    public async Task<ISchemaObjectDelta> CreateDeltaAsync(
        DbDataReader reader, CancellationToken ct)
    {
        var exists = await reader.ReadAsync(ct).ConfigureAwait(false);
        // Use Create (not Update) so Marten emits only the CREATE statement
        // — never the DROP. Update would emit DROP+CREATE which not only
        // wastes work on every boot but also opens a window where other
        // queries momentarily see no index.
        var diff = exists
            ? SchemaPatchDifference.None
            : SchemaPatchDifference.Create;
        return new SchemaObjectDelta(this, diff);
    }

    public IEnumerable<DbObjectName> AllNames()
    {
        yield return _identifier;
    }
}
