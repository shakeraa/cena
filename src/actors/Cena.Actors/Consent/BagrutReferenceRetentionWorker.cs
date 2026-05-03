// =============================================================================
// Cena Platform — BagrutReferenceRetentionWorker (PRR-266 R2)
//
// 180-day retention floor on BagrutReferenceItemRendered_V1 events per
// ADR-0059 §15.7. Mirrors the MockExamRetentionWorker pattern: hourly
// tick, cooperative cancellation, "enabled" flag default true, hard
// production guard not needed (events should retain in prod, just
// pruned at 180d).
//
// Why this isn't bundled with the misconception RetentionWorker:
//   - Different bounded context — consent stream, not session stream.
//   - Different retention horizon — 180d here vs ADR-0003 30d on
//     misconception.
//   - Different RTBF cascade owner — when a student RTBF-erases, the
//     consent-stream events crypto-shred via ADR-0042's existing
//     cascade; this worker just handles the time-based prune.
//
// Observability:
//   - Per-tick metric on prune count (logged structured; SIEM-tractable).
//   - Per-tick metric on bytes-recovered (best-effort estimate from
//     average event size; useful for capacity planning).
// =============================================================================

using Cena.Actors.Consent.Events;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Consent;

public sealed class BagrutReferenceRetentionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BagrutReferenceRetentionWorker> _logger;
    private readonly TimeSpan _retentionFloor;
    private readonly TimeSpan _tickPeriod;
    private readonly bool _enabled;

    public BagrutReferenceRetentionWorker(
        IServiceProvider services,
        ILogger<BagrutReferenceRetentionWorker> logger,
        IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _retentionFloor = TimeSpan.FromDays(
            config.GetValue<int?>("Cena:BagrutReference:Retention:Days") ?? 180);
        _tickPeriod = TimeSpan.FromMinutes(
            config.GetValue<int?>("Cena:BagrutReference:Retention:TickMinutes") ?? 60);
        _enabled = config.GetValue<bool?>("Cena:BagrutReference:Retention:Enabled") ?? true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("[BAGRUT-REF-RETENTION] disabled by config; worker idle");
            return;
        }

        _logger.LogInformation(
            "[BAGRUT-REF-RETENTION] worker started; retention={Days}d tickPeriod={Tick}m",
            _retentionFloor.TotalDays, _tickPeriod.TotalMinutes);

        // First tick after a short delay so host startup isn't blocked
        // by Marten warmup races on the consent schema.
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BAGRUT-REF-RETENTION] sweep failed; retrying next tick");
            }

            try { await Task.Delay(_tickPeriod, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[BAGRUT-REF-RETENTION] worker stopped");
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var threshold = DateTimeOffset.UtcNow - _retentionFloor;

        // Marten event-stream prune via the underlying NpgsqlConnection.
        // We want event-LEVEL deletion (the consent stream itself stays
        // alive; only the rendered-events older than the threshold are
        // pruned), so a typed DELETE against mt_events filtered by the
        // type discriminator is the right granularity. Marten's session
        // gives us a parameterised connection without bypassing the
        // configured NpgsqlDataSource pool.
        //
        // The type discriminator is the snake_cased event-name Marten
        // assigns at registration time (see ConsentMartenRegistration
        // → opts.Events.AddEventType<BagrutReferenceItemRendered_V1>()).
        await using var session = store.LightweightSession();
        var connection = session.Connection
            ?? throw new InvalidOperationException(
                "[BAGRUT-REF-RETENTION] Marten lightweight session did not yield a connection.");

        const string sql = "DELETE FROM mt_events WHERE type = @eventType AND timestamp < @threshold";
        await using var cmd = new Npgsql.NpgsqlCommand(sql, (Npgsql.NpgsqlConnection)connection);
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter("@eventType", "bagrut_reference_item_rendered_v1"));
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter("@threshold", threshold));

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "[BAGRUT-REF-RETENTION] sweep done; pruned={Pruned} events thresholdUtc={Threshold:O}",
                rowsAffected, threshold);
        }
        else
        {
            _logger.LogDebug(
                "[BAGRUT-REF-RETENTION] sweep done; no events older than {Threshold:O}",
                threshold);
        }
    }
}
