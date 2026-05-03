// =============================================================================
// Cena Platform — DiagnosticDisputeRetentionWorker (EPIC-PRR-J PRR-410)
//
// Enforces the 90-day retention window on diagnostic dispute records
// (extended from the 30-day ADR-0003 misconception-session window
// because disputes need a wider SME-calibration tail).
//
// Runs once daily; walks the dispute repository and deletes records
// with SubmittedAt older than the retention window.
//
// Why not rely on Marten's built-in retention: the retention window is
// business-domain state, not a data-store concern, and this way the
// ADR-0003 contract is expressible + testable in domain code.
// =============================================================================

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class DiagnosticDisputeRetentionWorker : BackgroundService
{
    /// <summary>
    /// Retention window. 90 days keeps 2 sprint cycles of SME review tail;
    /// change only via PR reviewed by product + legal.
    /// </summary>
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(90);

    /// <summary>How often the worker runs.</summary>
    public static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

    private readonly IDiagnosticDisputeRepository _repo;
    private readonly TimeProvider _clock;
    private readonly ILogger<DiagnosticDisputeRetentionWorker> _logger;

    public DiagnosticDisputeRetentionWorker(
        IDiagnosticDisputeRepository repo,
        TimeProvider clock,
        ILogger<DiagnosticDisputeRetentionWorker> logger)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "DiagnosticDisputeRetentionWorker: deleted {Count} disputes older than {Window}.",
                        deleted, RetentionWindow);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "DiagnosticDisputeRetentionWorker pass failed; retrying next interval.");
            }
            await Task.Delay(RunInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Internal hook for tests + on-demand execution.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var threshold = _clock.GetUtcNow() - RetentionWindow;
        return await _repo.DeleteSubmittedBeforeAsync(threshold, ct).ConfigureAwait(false);
    }
}
