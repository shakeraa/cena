// =============================================================================
// Cena Platform — wipe-questions command (RDY-036)
//
// Deletes ALL QuestionState streams + QuestionCasBinding documents.
// Double-gated: env CENA_ALLOW_PREPILOT_WIPE=true AND --confirm "I UNDERSTAND".
// Pre-pilot only — refuses if production-looking env unless flag set.
// =============================================================================

using Cena.Actors.Questions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cena.Tools.DbAdmin;

public static class WipeQuestionsCommand
{
    public const string ConfirmPhrase = "I UNDERSTAND";
    public const string EnvFlag = "CENA_ALLOW_PREPILOT_WIPE";

    public static async Task<int> RunAsync(string[] args, IConfiguration config, ILogger logger)
    {
        if (Environment.GetEnvironmentVariable(EnvFlag) != "true")
        {
            logger.LogError("[WIPE_REFUSED] {Env} is not set to \"true\". Refusing.", EnvFlag);
            return 3;
        }

        string? confirm = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--confirm") { confirm = args[i + 1]; break; }
        }

        if (confirm != ConfirmPhrase)
        {
            logger.LogError("[WIPE_REFUSED] --confirm must equal \"{Phrase}\". Got: {Got}",
                ConfirmPhrase, confirm ?? "<missing>");
            return 3;
        }

        var pg = config.GetConnectionString("Postgres")
                 ?? Environment.GetEnvironmentVariable("CENA_PG_CONN")
                 ?? "Host=localhost;Port=5432;Database=cena;Username=postgres;Password=postgres";

        logger.LogWarning("[WIPE_START] Connecting to Postgres for destructive wipe...");

        await using var conn = new NpgsqlConnection(pg);
        await conn.OpenAsync();

        // 1) Delete QuestionCasBinding documents via Marten's conventional table.
        //    Marten stores doc types in tables named mt_doc_<typename_lowercased>.
        await using (var bCmd = conn.CreateCommand())
        {
            bCmd.CommandText = @"
                DO $$
                DECLARE tbl text;
                BEGIN
                    SELECT c.relname INTO tbl
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.relkind = 'r' AND c.relname ILIKE 'mt_doc_questioncasbinding';
                    IF tbl IS NOT NULL THEN
                        EXECUTE format('DELETE FROM %I', tbl);
                    END IF;
                END $$;
            ";
            await bCmd.ExecuteNonQueryAsync();
        }
        logger.LogWarning("[WIPE_BINDINGS_DONE] QuestionCasBinding documents deleted");

        // 2) Delete QuestionState streams via Marten's event-store schema.
        var streamType = typeof(QuestionState).FullName ?? "QuestionState";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM mt_events WHERE stream_id IN (
                SELECT id FROM mt_streams WHERE type = @type
            );
            DELETE FROM mt_streams WHERE type = @type;
        ";
        cmd.Parameters.AddWithValue("type", streamType);
        var affected = await cmd.ExecuteNonQueryAsync();
        logger.LogWarning("[WIPE_STREAMS_DONE] streamType={Type} rowsAffected={N}", streamType, affected);

        logger.LogWarning("[WIPE_COMPLETE] All QuestionState streams + CasBinding docs removed.");
        return 0;
    }
}
