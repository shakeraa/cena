// =============================================================================
// Cena Platform — DisputeMetricsService (EPIC-PRR-J PRR-393)
//
// Thin application service that composes IDiagnosticDisputeRepository +
// DisputeRateAggregator + TimeProvider into a single read-side call. The
// admin observability endpoint depends only on this interface so it can
// ship behind a lightweight test fake without constructing a full Marten
// session (see NoopDisputeMetricsService).
//
// Why not a worker: the snapshot is cheap (integer folds over at most
// ~30 days of dispute rows; retention caps the population at ≤90 days
// of history per PRR-385 DiagnosticDispute.cs). Reading on demand keeps
// the dashboard live without a scheduled job; if the dispute volume ever
// grows past the load budget, swapping this for a worker + read-model
// projection is a 30-minute follow-up without changing callers.
//
// Reading page size:
//   MaxDisputesPerWindowFetch is a defensive cap on the ListRecentAsync
//   call. 10_000 comfortably covers the 30-day window at 10x our launch
//   volume forecast (PRR-393 expected v1 volume is <1k/week). If the fold
//   ever truncates because of this cap we log a warning so ops knows the
//   dashboard needs to move to a projection.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Read-side service that surfaces dispute metrics for the admin dashboard.</summary>
public interface IDisputeMetricsService
{
    /// <summary>Compute a snapshot for the given rolling window.</summary>
    Task<DisputeMetricsSnapshot> GetAsync(
        AggregationWindow window,
        CancellationToken ct);
}

/// <summary>
/// Production implementation: pulls recent disputes from the repository
/// and folds them through the pure aggregator.
/// </summary>
public sealed class MartenDisputeMetricsService : IDisputeMetricsService
{
    /// <summary>
    /// Defensive cap on the ListRecentAsync page size. Sized to cover 30
    /// days at 10x PRR-393 launch volume forecast.
    /// </summary>
    public const int MaxDisputesPerWindowFetch = 10_000;

    private readonly IDiagnosticDisputeRepository _repo;
    private readonly TimeProvider _clock;
    private readonly ILogger<MartenDisputeMetricsService> _logger;

    public MartenDisputeMetricsService(
        IDiagnosticDisputeRepository repo,
        TimeProvider clock,
        ILogger<MartenDisputeMetricsService> logger)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DisputeMetricsSnapshot> GetAsync(
        AggregationWindow window,
        CancellationToken ct)
    {
        // status=null fetches every dispute regardless of state; the
        // aggregator needs Withdrawn + New + InReview + terminal states
        // to compute denominators honestly.
        var docs = await _repo
            .ListRecentAsync(status: null, take: MaxDisputesPerWindowFetch, ct)
            .ConfigureAwait(false);

        if (docs.Count == MaxDisputesPerWindowFetch)
        {
            // If the fold hit the cap, the snapshot is no longer a faithful
            // window — log loud so ops can move us to a projection. This
            // is not a silent truncation.
            _logger.LogWarning(
                "DisputeMetricsService hit fetch cap of {Cap} rows; "
                + "window snapshot may be truncated. Consider moving to a projection.",
                MaxDisputesPerWindowFetch);
        }

        var views = new List<DiagnosticDisputeView>(docs.Count);
        foreach (var d in docs)
        {
            views.Add(ToView(d));
        }

        return DisputeRateAggregator.Aggregate(
            views,
            now: _clock.GetUtcNow(),
            window: window);
    }

    private static DiagnosticDisputeView ToView(DiagnosticDisputeDocument d) => new(
        DisputeId: d.Id,
        DiagnosticId: d.DiagnosticId,
        StudentSubjectIdHash: d.StudentSubjectIdHash,
        Reason: d.Reason,
        StudentComment: d.StudentComment,
        Status: d.Status,
        SubmittedAt: d.SubmittedAt,
        ReviewedAt: d.ReviewedAt,
        ReviewerNote: d.ReviewerNote);
}

/// <summary>
/// Test fixture that returns an empty snapshot. Explicitly labelled as a
/// test fixture — per memory "No stubs — production grade" (2026-04-11),
/// production wiring must always use <see cref="MartenDisputeMetricsService"/>.
/// Registered from test projects only.
/// </summary>
public sealed class NoopDisputeMetricsService : IDisputeMetricsService
{
    /// <inheritdoc />
    public Task<DisputeMetricsSnapshot> GetAsync(
        AggregationWindow window,
        CancellationToken ct)
    {
        var windowDays = DisputeRateAggregator.DaysFor(window);
        var empty = new Dictionary<DisputeReason, int>();
        var emptyRates = new Dictionary<DisputeReason, double>();
        foreach (var r in Enum.GetValues<DisputeReason>())
        {
            empty[r] = 0;
            emptyRates[r] = 0.0;
        }
        return Task.FromResult(new DisputeMetricsSnapshot(
            WindowDays: windowDays,
            TotalDisputes: 0,
            UpheldCount: 0,
            RejectedCount: 0,
            InReviewCount: 0,
            NewCount: 0,
            WithdrawnCount: 0,
            UpheldRate: 0.0,
            PerReasonCounts: empty,
            PerReasonUpheldRate: emptyRates,
            AlertThreshold: DisputeRateAggregator.DefaultAlertThreshold,
            IsAboveAlertThreshold: false));
    }
}
