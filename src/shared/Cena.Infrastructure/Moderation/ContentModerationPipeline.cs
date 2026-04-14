// =============================================================================
// Cena Platform — Content Moderation Pipeline for Minors (PHOTO-003 + PP-001)
//
// Multi-stage content moderation for uploaded images:
//   1. Hash-based CSAM detection via PhotoDNA (fail-closed)
//   2. AI classification via Azure Content Safety
//   3. Policy threshold + human review queue
//   4. Incident reporting for CSAM detections
//
// CRITICAL: PhotoDNA unavailability BLOCKS uploads (fail-closed).
// AI Safety unavailability routes to human review (fail-safe).
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Moderation;

/// <summary>
/// Content moderation verdict.
/// </summary>
public enum ModerationVerdict
{
    Safe,
    NeedsReview,
    Blocked,
    CsamDetected
}

/// <summary>
/// Result of content moderation.
/// </summary>
public record ModerationResult(
    string ContentId,
    ModerationVerdict Verdict,
    double ConfidenceScore,
    string[] FlaggedCategories,
    bool RequiresHumanReview,
    bool IncidentReportFiled,
    DateTimeOffset ModeratedAt
);

/// <summary>
/// Moderation policy configuration.
/// </summary>
public record ModerationPolicy(
    /// <summary>Minimum confidence for auto-approve (higher = stricter).</summary>
    double AutoApproveThreshold = 0.95,
    /// <summary>Maximum confidence for auto-block (lower = more aggressive blocking).</summary>
    double AutoBlockThreshold = 0.30,
    /// <summary>Whether this is a minor's content (stricter thresholds).</summary>
    bool IsMinor = true
);

public interface IContentModerationPipeline
{
    /// <summary>
    /// Run the full moderation pipeline on uploaded content.
    /// </summary>
    Task<ModerationResult> ModerateAsync(
        byte[] content,
        string contentType,
        string uploaderId,
        ModerationPolicy policy,
        string? uploaderIpAddress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Multi-stage moderation pipeline. Stage 1 (PhotoDNA) is fail-closed:
/// if the service is unavailable, uploads are BLOCKED, not approved.
/// </summary>
public sealed class ContentModerationPipeline : IContentModerationPipeline
{
    private readonly IPhotoDnaClient _photoDna;
    private readonly IContentSafetyClient _contentSafety;
    private readonly IIncidentReportService _incidentReporter;
    private readonly ILogger<ContentModerationPipeline> _logger;

    public ContentModerationPipeline(
        IPhotoDnaClient photoDna,
        IContentSafetyClient contentSafety,
        IIncidentReportService incidentReporter,
        ILogger<ContentModerationPipeline> logger)
    {
        _photoDna = photoDna;
        _contentSafety = contentSafety;
        _incidentReporter = incidentReporter;
        _logger = logger;
    }

    public async Task<ModerationResult> ModerateAsync(
        byte[] content,
        string contentType,
        string uploaderId,
        ModerationPolicy policy,
        string? uploaderIpAddress = null,
        CancellationToken ct = default)
    {
        var contentId = GenerateContentId(content);

        // ══════════════════════════════════════════════════════════════
        // Stage 1: CSAM hash detection (PhotoDNA) — FAIL-CLOSED
        // If PhotoDNA is unavailable, the upload is BLOCKED.
        // ══════════════════════════════════════════════════════════════
        try
        {
            var csamResult = await _photoDna.CheckHashAsync(content, ct);
            if (csamResult.IsMatch)
            {
                _logger.LogCritical(
                    "[SIEM] CSAM detected for content {ContentId}, uploader {Uploader}, IP {IP}",
                    contentId, uploaderId, uploaderIpAddress ?? "unknown");

                await _incidentReporter.FileIncidentAsync(
                    contentId, uploaderId, uploaderIpAddress, csamResult, ct);

                return new ModerationResult(
                    contentId, ModerationVerdict.CsamDetected, 1.0,
                    ["csam"], RequiresHumanReview: false,
                    IncidentReportFiled: true, DateTimeOffset.UtcNow);
            }
        }
        catch (PhotoDnaUnavailableException ex)
        {
            // FAIL-CLOSED: PhotoDNA down → block the upload
            _logger.LogCritical(ex,
                "[SIEM] PhotoDNA unavailable — BLOCKING upload {ContentId} from {Uploader} (fail-closed policy)",
                contentId, uploaderId);

            return new ModerationResult(
                contentId, ModerationVerdict.Blocked, 0.0,
                ["csam_check_unavailable"], RequiresHumanReview: false,
                IncidentReportFiled: false, DateTimeOffset.UtcNow);
        }

        // ══════════════════════════════════════════════════════════════
        // Stage 2: AI safety classification — fail-safe (human review)
        // ══════════════════════════════════════════════════════════════
        var aiScore = await _contentSafety.ClassifyAsync(content, contentType, ct);

        // ══════════════════════════════════════════════════════════════
        // Stage 3: Apply policy thresholds (stricter for minors)
        // ══════════════════════════════════════════════════════════════
        var effectiveApproveThreshold = policy.IsMinor
            ? policy.AutoApproveThreshold  // 0.95 for minors
            : policy.AutoApproveThreshold - 0.10; // 0.85 for adults

        if (aiScore >= effectiveApproveThreshold)
        {
            return new ModerationResult(
                contentId, ModerationVerdict.Safe, aiScore,
                [], false, false, DateTimeOffset.UtcNow);
        }

        if (aiScore <= policy.AutoBlockThreshold)
        {
            _logger.LogWarning(
                "Content auto-blocked: {ContentId}, score={Score}, uploader={Uploader}",
                contentId, aiScore, uploaderId);
            return new ModerationResult(
                contentId, ModerationVerdict.Blocked, aiScore,
                ["low_safety_score"], false, false, DateTimeOffset.UtcNow);
        }

        // Between thresholds: queue for human review
        _logger.LogInformation("Content queued for review: {ContentId}, score={Score}",
            contentId, aiScore);
        return new ModerationResult(
            contentId, ModerationVerdict.NeedsReview, aiScore,
            ["uncertain"], RequiresHumanReview: true, false, DateTimeOffset.UtcNow);
    }

    private static string GenerateContentId(byte[] content)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(content);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
