// =============================================================================
// Cena Platform — AccuracyAuditSampler (EPIC-PRR-J PRR-423)
//
// Deterministic 5% sampler for retrospective SME accuracy review. The
// sampler decides — by stable hash of diagnosticId — whether this
// particular diagnostic should be flagged for human review. The decision
// is stable (same id → same verdict) so re-running the pipeline never
// drops an already-sampled diagnostic, and audit ids don't shift around
// between replays.
//
// Always-sample bias: low-confidence outcomes or "no template match"
// fallbacks bypass the sampler — those are always reviewed. Low-quality
// signal is precisely what SMEs need to eyeball, so 5% random + 100% of
// low-quality is the right policy.
//
// Storage is a seam (IPhotoDiagnosticAuditLog). The default
// implementation writes a structured log entry; a Marten-backed impl is
// a straightforward follow-up (PRR-424 tail).
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>One diagnostic under consideration for accuracy review.</summary>
public sealed record AccuracyAuditCandidate(
    string DiagnosticId,
    string StudentSubjectIdHash,
    MisconceptionBreakType? BreakType,
    double OcrConfidence,
    double? TemplateScore,
    bool TemplateMatched,
    bool StepChainVerificationSucceeded);

/// <summary>Sampler verdict + reason string for observability.</summary>
public sealed record AccuracyAuditDecision(bool Sampled, string Reason);

/// <summary>Port for the sampler.</summary>
public interface IAccuracyAuditSampler
{
    AccuracyAuditDecision Decide(AccuracyAuditCandidate candidate);
}

/// <summary>Downstream seam for recording sampled diagnostics.</summary>
public interface IPhotoDiagnosticAuditLog
{
    Task WriteAsync(AccuracyAuditCandidate candidate, AccuracyAuditDecision decision, CancellationToken ct);
}

/// <summary>Logger-backed default audit log.</summary>
public sealed class LoggingPhotoDiagnosticAuditLog : IPhotoDiagnosticAuditLog
{
    private readonly ILogger<LoggingPhotoDiagnosticAuditLog> _logger;

    public LoggingPhotoDiagnosticAuditLog(ILogger<LoggingPhotoDiagnosticAuditLog> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task WriteAsync(AccuracyAuditCandidate candidate, AccuracyAuditDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(decision);
        _logger.LogInformation(
            "PhotoDiagnostic accuracy-audit sample: diagId={DiagId} student={StudentHash} breakType={BreakType} ocrConfidence={OcrConf} templateScore={TplScore} templateMatched={TplMatch} chainOk={ChainOk} sampled={Sampled} reason={Reason}",
            candidate.DiagnosticId,
            candidate.StudentSubjectIdHash,
            candidate.BreakType,
            candidate.OcrConfidence,
            candidate.TemplateScore,
            candidate.TemplateMatched,
            candidate.StepChainVerificationSucceeded,
            decision.Sampled,
            decision.Reason);
        return Task.CompletedTask;
    }
}

/// <summary>Deterministic 5%-random + 100%-of-low-quality sampler.</summary>
public sealed class AccuracyAuditSampler : IAccuracyAuditSampler
{
    /// <summary>Target random sample rate (5%).</summary>
    public const int SampleRatePermille = 50; // out of 1000

    /// <summary>OCR confidence below which we always sample.</summary>
    public const double LowOcrConfidence = 0.60;

    private readonly PhotoDiagnosticMetrics _metrics;

    public AccuracyAuditSampler(PhotoDiagnosticMetrics metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public AccuracyAuditDecision Decide(AccuracyAuditCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(candidate.DiagnosticId))
            throw new ArgumentException("DiagnosticId is required.", nameof(candidate));

        if (candidate.OcrConfidence < LowOcrConfidence)
        {
            _metrics.RecordAuditSampled("low_ocr_confidence");
            return new AccuracyAuditDecision(true, "low_ocr_confidence");
        }
        if (!candidate.TemplateMatched)
        {
            _metrics.RecordAuditSampled("no_template_match");
            return new AccuracyAuditDecision(true, "no_template_match");
        }
        if (!candidate.StepChainVerificationSucceeded)
        {
            // Succeeded=false means we found a wrong step — that's the happy-path
            // diagnostic output. Don't always-sample those; fall through to random.
        }

        if (HashBucketPermille(candidate.DiagnosticId) < SampleRatePermille)
        {
            _metrics.RecordAuditSampled("random");
            return new AccuracyAuditDecision(true, "random");
        }
        return new AccuracyAuditDecision(false, "not_sampled");
    }

    /// <summary>
    /// Map the diagnostic id to a stable bucket in [0, 1000) so 5% of
    /// diagnostics across the whole population fall below 50.
    /// </summary>
    internal static int HashBucketPermille(string diagnosticId)
    {
        var bytes = Encoding.UTF8.GetBytes(diagnosticId);
        var hash = SHA256.HashData(bytes);
        // Take the first 4 bytes unsigned, mod 1000.
        var n = ((uint)hash[0] << 24) | ((uint)hash[1] << 16) | ((uint)hash[2] << 8) | hash[3];
        return (int)(n % 1000u);
    }
}
