// =============================================================================
// Cena Platform — AbuseDetectionWorker (EPIC-PRR-I PRR-314, EPIC-PRR-J PRR-403)
//
// Daily pass over subscription streams. Flags Premium subscribers with
// >200 diagnostic uploads in a rolling 30-day window for human review. No
// auto-action — a Review queue lands in admin dashboard, human decides.
//
// Counts come from the subscription stream's EntitlementSoftCapReached_V1
// events (photo_diagnostic_monthly cap type) per-parent-per-period. A
// dedicated UploadCounter store could serve this more directly; v1 uses
// the existing events so no new persistence is introduced.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Runs once daily. Walks subscription streams and emits a structured
/// warning log entry for any parent whose students exceeded the abuse
/// threshold in the last 30 days. Production use: Admin dashboard read
/// model + support review queue (follow-up task).
/// </summary>
public sealed class AbuseDetectionWorker : BackgroundService
{
    /// <summary>Threshold per student per rolling 30-day window.</summary>
    public const int AbuseThreshold = 200;

    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<AbuseDetectionWorker> _logger;

    public AbuseDetectionWorker(
        IDocumentStore store,
        TimeProvider clock,
        ILogger<AbuseDetectionWorker> logger)
    {
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "AbuseDetectionWorker pass failed; retrying in 1h.");
            }
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var since = _clock.GetUtcNow().AddDays(-30);
        await using var session = _store.QuerySession();
        var events = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.Timestamp >= since)
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .ToListAsync(ct);

        var byStudent = events
            .Select(e => e.Data)
            .OfType<EntitlementSoftCapReached_V1>()
            .Where(e => e.CapType == EntitlementSoftCapReached_V1.CapTypes.PhotoDiagnosticMonthly)
            .GroupBy(e => e.StudentSubjectIdEncrypted)
            .ToList();

        var flagged = 0;
        foreach (var group in byStudent)
        {
            var max = group.Max(e => e.UsageCount);
            if (max >= AbuseThreshold)
            {
                _logger.LogWarning(
                    "Abuse-detection: student {StudentIdHash} hit {Usage} photo diagnostics in 30d (threshold {Threshold}). Parent={ParentIdHash}. Human review required.",
                    HashForLog(group.Key),
                    max,
                    AbuseThreshold,
                    HashForLog(group.First().ParentSubjectIdEncrypted));
                flagged++;
            }
        }
        return flagged;
    }

    /// <summary>Truncate encrypted id to 8 chars for log correlation without leaking ciphertext.</summary>
    private static string HashForLog(string encryptedId) =>
        string.IsNullOrEmpty(encryptedId)
            ? "∅"
            : encryptedId.Length <= 8 ? encryptedId : encryptedId[..8] + "…";
}
