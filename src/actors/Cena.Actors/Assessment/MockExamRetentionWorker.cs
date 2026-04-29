// =============================================================================
// Cena Platform — MockExamRetentionWorker (Phase 1F)
//
// 180-day retention floor for ExamSimulationState rows. ADR-0059 §15.7
// mandates a 180-day horizon on rendered/practiced events; the
// mock-exam runner's per-run state (PartA/B IDs, answers, visibility
// events) is the analogous artifact.
//
// Safety properties:
//   - Only deletes rows where SubmittedAt is non-null AND
//     SubmittedAt < (now - 180d). In-flight runs (SubmittedAt == null)
//     are never deleted by this worker — they're closed by deadline
//     enforcement at the service layer or, on host crash, by an
//     orphan-sweep that's out of Phase 1 scope.
//   - Delete is hard (Marten doc deletion). The aggregate event
//     stream (ExamSimulationStarted_V1 / Submitted_V2 on the student
//     stream) is preserved — those carry only counts, no answers, so
//     they're privacy-safe and useful for longitudinal analytics.
//
// Cadence: hourly tick. The worker is cheap (single LINQ delete);
// running it more often is wasteful, less often risks queue depth
// spikes during heavy exam-prep periods.
// =============================================================================

using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment;

public sealed class MockExamRetentionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MockExamRetentionWorker> _logger;
    private readonly TimeSpan _retentionFloor;
    private readonly TimeSpan _tickPeriod;
    private readonly bool _enabled;

    public MockExamRetentionWorker(
        IServiceProvider services,
        ILogger<MockExamRetentionWorker> logger,
        IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _retentionFloor = TimeSpan.FromDays(
            config.GetValue<int?>("Cena:ExamPrep:Retention:Days") ?? 180);
        _tickPeriod = TimeSpan.FromMinutes(
            config.GetValue<int?>("Cena:ExamPrep:Retention:TickMinutes") ?? 60);
        _enabled = config.GetValue<bool?>("Cena:ExamPrep:Retention:Enabled") ?? true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("[EXAM-PREP-RETENTION] disabled by config; worker idle");
            return;
        }

        _logger.LogInformation(
            "[EXAM-PREP-RETENTION] worker started; retention={Days}d tickPeriod={Tick}m",
            _retentionFloor.TotalDays, _tickPeriod.TotalMinutes);

        // First tick after a short delay so host startup isn't blocked.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { /* host stopping */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EXAM-PREP-RETENTION] sweep failed; retrying next tick");
            }

            try
            {
                await Task.Delay(_tickPeriod, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - _retentionFloor;

        using var scope = _services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession();
        var stale = await session.Query<ExamSimulationState>()
            .Where(s => s.SubmittedAt != null && s.SubmittedAt < cutoff)
            .Select(s => s.SimulationId)
            .Take(500)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        foreach (var id in stale)
            session.Delete<ExamSimulationState>(id);

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[EXAM-PREP-RETENTION] purged {Count} ExamSimulationState rows submitted before {Cutoff:o}",
            stale.Count, cutoff);
    }
}
