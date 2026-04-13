// =============================================================================
// Cena Platform — CSAM Incident Report Service (PP-001 Stage 3)
// Sealed audit log + queue for safety officer NCMEC CyberTipline submission.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Moderation;

/// <summary>
/// Incident report for CSAM detection. Stored in a sealed (append-only) audit log.
/// Contains only hashes and metadata — never stores the flagged content itself.
/// </summary>
public sealed class CsamIncidentReport
{
    public string Id { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string UploaderId { get; set; } = "";
    public string? UploaderIpAddress { get; set; }
    public string? PhotoDnaMatchId { get; set; }
    public string? PhotoDnaHashValue { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset UploadTimestamp { get; set; }
    public string Status { get; set; } = "pending_review";  // pending_review | reported | dismissed_false_positive
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? NcmecReportId { get; set; }
}

public interface IIncidentReportService
{
    Task FileIncidentAsync(
        string contentHash,
        string uploaderId,
        string? ipAddress,
        PhotoDnaMatch match,
        CancellationToken ct = default);
}

/// <summary>
/// Files CSAM incident reports to a sealed Marten document store.
/// The safety officer reviews queued reports and submits to NCMEC CyberTipline.
/// </summary>
public sealed class IncidentReportService : IIncidentReportService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<IncidentReportService> _logger;

    public IncidentReportService(
        IDocumentStore store,
        ILogger<IncidentReportService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task FileIncidentAsync(
        string contentHash,
        string uploaderId,
        string? ipAddress,
        PhotoDnaMatch match,
        CancellationToken ct = default)
    {
        var incident = new CsamIncidentReport
        {
            Id = $"csam-{Guid.NewGuid():N}",
            ContentHash = contentHash,
            UploaderId = uploaderId,
            UploaderIpAddress = ipAddress,
            PhotoDnaMatchId = match.MatchId,
            PhotoDnaHashValue = match.HashValue,
            DetectedAt = DateTimeOffset.UtcNow,
            UploadTimestamp = DateTimeOffset.UtcNow,
            Status = "pending_review"
        };

        await using var session = _store.LightweightSession();
        session.Store(incident);
        await session.SaveChangesAsync(ct);

        _logger.LogCritical(
            "CSAM incident report filed. IncidentId={IncidentId}, ContentHash={ContentHash}, " +
            "Uploader={Uploader}, PhotoDnaMatchId={MatchId}. " +
            "SAFETY OFFICER: Review and submit to NCMEC CyberTipline.",
            incident.Id, contentHash, uploaderId, match.MatchId);
    }
}
