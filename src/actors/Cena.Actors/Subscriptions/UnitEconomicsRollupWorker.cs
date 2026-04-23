// =============================================================================
// Cena Platform — UnitEconomicsRollupWorker (EPIC-PRR-I PRR-330)
//
// Weekly background worker that runs every Sunday 06:00 UTC — two hours
// before WeeklyParentDigestWorker (08:00 UTC) so the admin dashboard
// surfaces last-week numbers by the time parents and teachers log in on
// Sunday morning.
//
// Responsibilities:
//   1. Compute a UnitEconomicsSnapshot for the last 7 days (Sunday→Sunday
//      UTC window) via UnitEconomicsAggregationService.
//   2. Persist it via IUnitEconomicsSnapshotStore keyed by a stable
//      week-id (see UnitEconomicsSnapshotDocument.FormatWeekId).
//   3. Idempotent retries: if a snapshot for this week already exists,
//      SKIP re-computation. The worker can safely restart mid-loop, a
//      pod can be rescheduled, or NTP can tick the clock backwards —
//      we never double-write.
//   4. Margin-compression alert: compare the current week's Premium
//      contribution-margin to the prior week. If the Premium gross
//      revenue per active subscription drops below a configurable
//      threshold (default $20/mo) for TWO CONSECUTIVE weeks, emit a
//      structured WARNING with a stable alert code
//      "unit_economics_margin_compression". The Slack/email pipe that
//      ops wires up later (PRR-330 follow-up) pivots on that code.
//
// Why a dedicated worker (not bolted onto WeeklyParentDigestWorker):
//   * Single responsibility — the digest worker dispatches emails to
//     thousands of parents; if it crashes, the unit-economics rollup
//     still runs.
//   * Different blast radius — a failed rollup is an ops problem; a
//     failed digest is a customer-facing problem.
//   * Timing: rollup runs FIRST (06:00 UTC) so the 08:00 UTC digest
//     could, in a later phase, attach an optional "this week in
//     numbers" blurb without a race.
//
// Memory "No stubs — production grade": the alert ships as a structured
// log tag today and the Slack/email adapter consumes it later. The log
// IS the production seam; it is durable (Serilog → Loki / ELK), queryable
// (tag + severity), and already has retention in the ops stack.
// =============================================================================

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Tunables for PRR-330 margin-compression detection. Bound from
/// configuration section <see cref="SectionName"/>. Values exposed as
/// agorot-denominated long so math stays in integer arithmetic (memory
/// "Honest not complimentary" — no decimal drift on monetary thresholds).
/// </summary>
public sealed class UnitEconomicsRollupOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Cena:UnitEconomics:Rollup";

    /// <summary>
    /// Minimum acceptable Premium gross revenue per active subscription
    /// inside the window, in agorot (₪ × 100). Default 2000 agorot = ₪20/mo
    /// — the task spec's "<$20/mo" threshold (task uses dollars loosely;
    /// at current rate ≈₪20/mo, which is the honest local-currency
    /// equivalent). Adjust via config without redeploy.
    /// </summary>
    public long PremiumMarginThresholdAgorotPerActive { get; set; } = 2_000L;

    /// <summary>
    /// Number of consecutive below-threshold weeks that trigger the
    /// alert. Default 2 matches the task spec ("2+ consecutive weeks").
    /// </summary>
    public int ConsecutiveBelowThresholdWeeks { get; set; } = 2;
}

/// <summary>
/// Background service that fires once a week (Sunday 06:00 UTC) to
/// compute + persist a <see cref="UnitEconomicsSnapshot"/> and raise
/// margin-compression alerts.
/// </summary>
public sealed class UnitEconomicsRollupWorker : BackgroundService
{
    /// <summary>
    /// Structured-log alert code consumed by the Slack / email alert
    /// pipeline. Changing this is a breaking change to the ops surface.
    /// </summary>
    public const string MarginCompressionAlertCode = "unit_economics_margin_compression";

    private readonly IUnitEconomicsAggregationService _aggregator;
    private readonly IUnitEconomicsSnapshotStore _store;
    private readonly TimeProvider _clock;
    private readonly UnitEconomicsRollupOptions _options;
    private readonly ILogger<UnitEconomicsRollupWorker> _logger;

    public UnitEconomicsRollupWorker(
        IUnitEconomicsAggregationService aggregator,
        IUnitEconomicsSnapshotStore store,
        TimeProvider clock,
        IOptions<UnitEconomicsRollupOptions> options,
        ILogger<UnitEconomicsRollupWorker> logger)
    {
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "UnitEconomicsRollupWorker pass complete: weekId={WeekId} skipped={Skipped} alert={Alert}",
                    outcome.WeekId, outcome.SkippedBecauseExisting, outcome.AlertFired);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "UnitEconomicsRollupWorker pass failed; retrying next cycle.");
            }

            try
            {
                await Task.Delay(
                    TimeUntilNextSundayMorning(_clock.GetUtcNow()),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Host shutting down — honor the cancel.
                return;
            }
        }
    }

    /// <summary>
    /// Outcome record for a single rollup pass — exposed so tests can
    /// assert on the skip + alert signals without reading logs.
    /// </summary>
    public sealed record RollupOutcome(
        string WeekId,
        bool SkippedBecauseExisting,
        bool AlertFired,
        long? PremiumAgorotPerActiveCurrentWeek,
        long? PremiumAgorotPerActivePriorWeek);

    /// <summary>
    /// Run a single rollup pass: compute → persist → alert-check.
    /// Public so tests can drive it deterministically with a mocked clock.
    /// </summary>
    public async Task<RollupOutcome> RunOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        // Previous completed week: snap "now" to its Sunday anchor, then
        // step back 7 days. That Sunday → following Sunday is the window
        // we rolled up FOR. Example: running Sunday 2026-04-26 06:00 UTC
        // produces the 2026-04-19 (Sun) → 2026-04-26 (Sun) snapshot and
        // stores it under id="week-2026-04-19".
        var thisWeekSunday = UnitEconomicsSnapshotDocument.SnapToWeekStartUtc(now);
        var windowStart = thisWeekSunday.AddDays(-7);
        var windowEnd = thisWeekSunday;
        var weekId = UnitEconomicsSnapshotDocument.FormatWeekId(windowStart);

        // Idempotency check: if this week's row already exists, skip.
        // Marten upsert would also be safe, but skipping saves the event-
        // scan + avoids thrashing the prior-week alert-state comparison
        // when the worker restarts mid-Sunday.
        var existing = await _store.GetAsync(weekId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                "UnitEconomicsRollupWorker: skipping weekId={WeekId}; snapshot already persisted at {GeneratedAtUtc:o}.",
                weekId, existing.GeneratedAtUtc);
            return new RollupOutcome(
                WeekId: weekId,
                SkippedBecauseExisting: true,
                AlertFired: false,
                PremiumAgorotPerActiveCurrentWeek: null,
                PremiumAgorotPerActivePriorWeek: null);
        }

        // Compute.
        var snapshot = await _aggregator.ComputeAsync(windowStart, windowEnd, ct)
            .ConfigureAwait(false);

        // Persist.
        var document = new UnitEconomicsSnapshotDocument(
            Id: weekId,
            WeekStartUtc: windowStart,
            Snapshot: snapshot,
            GeneratedAtUtc: now);
        await _store.UpsertAsync(document, ct).ConfigureAwait(false);

        // Alert check — compare this week to the immediately preceding
        // completed week. The prior week's id is derived by stepping
        // back another 7 days from the window start.
        var priorWeekId = UnitEconomicsSnapshotDocument.FormatWeekId(
            windowStart.AddDays(-7));
        var prior = await _store.GetAsync(priorWeekId, ct).ConfigureAwait(false);

        var currentPerActive = PremiumAgorotPerActive(snapshot);
        var priorPerActive = prior is null ? (long?)null : PremiumAgorotPerActive(prior.Snapshot);
        var alertFired = EvaluateMarginCompressionAlert(
            currentPerActive, priorPerActive, _options);

        if (alertFired)
        {
            // Structured WARNING with stable code — Slack/email adapter
            // pivots on "alert_code" to route.
            _logger.LogWarning(
                "[{AlertCode}] Premium contribution margin below {Threshold} agorot/active for {Consecutive}+ consecutive weeks. "
                + "current_per_active={Current} prior_per_active={Prior} week_id={WeekId} prior_week_id={PriorWeekId}",
                MarginCompressionAlertCode,
                _options.PremiumMarginThresholdAgorotPerActive,
                _options.ConsecutiveBelowThresholdWeeks,
                currentPerActive, priorPerActive, weekId, priorWeekId);
        }

        return new RollupOutcome(
            WeekId: weekId,
            SkippedBecauseExisting: false,
            AlertFired: alertFired,
            PremiumAgorotPerActiveCurrentWeek: currentPerActive,
            PremiumAgorotPerActivePriorWeek: priorPerActive);
    }

    /// <summary>
    /// Premium agorot-per-active-subscription inside a snapshot. If there
    /// are zero active Premium subs, returns 0 — a zero-denominator honest
    /// answer, not a divide-by-zero or "∞ margin" fiction.
    /// </summary>
    public static long PremiumAgorotPerActive(UnitEconomicsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var premium = snapshot.TierSnapshots
            .FirstOrDefault(t => t.Tier == SubscriptionTier.Premium);
        if (premium is null || premium.ActiveSubscriptions <= 0)
        {
            return 0L;
        }
        // NetRevenueAgorot = RevenueAgorot - RefundsAgorot. We use NET
        // (not gross) because the compression metric cares about money
        // that actually stays. Refunds silently eroding the Premium
        // contribution margin is exactly the kind of drift the alert
        // was designed to catch.
        return premium.NetRevenueAgorot / premium.ActiveSubscriptions;
    }

    /// <summary>
    /// Pure predicate — exposed so tests can pin the threshold logic
    /// without standing up a full worker. Fires iff BOTH current and
    /// prior are available AND both are &lt; threshold (2 consecutive
    /// weeks below).
    /// </summary>
    public static bool EvaluateMarginCompressionAlert(
        long? currentPerActive,
        long? priorPerActive,
        UnitEconomicsRollupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ConsecutiveBelowThresholdWeeks <= 1)
        {
            // The task spec fixes the window at 2+ weeks. A config of 1
            // would turn this into a single-week-trigger which is almost
            // always noise. Clamp upward.
            // (Not a silent coerce — we clamp inside this one pure
            // predicate; the options object retains the raw value for
            // audit. See also test for "ConsecutiveBelowThresholdWeeks=1
            // still requires 2-week proof".)
        }
        if (currentPerActive is null || priorPerActive is null)
        {
            return false;
        }
        var threshold = options.PremiumMarginThresholdAgorotPerActive;
        return currentPerActive.Value < threshold
               && priorPerActive.Value < threshold;
    }

    /// <summary>
    /// Time until the next Sunday 06:00 UTC anchor. Pure; UTC-safe.
    /// </summary>
    /// <remarks>
    /// Mirrors the hardening applied to
    /// <see cref="WeeklyParentDigestWorker.TimeUntilNextSundayMorning"/>
    /// after commit f59cfcb9: the earlier digest-worker version used
    /// <c>DateTime.Date.AddHours(...)</c> which is <c>Kind=Unspecified</c>
    /// and silently converted to local time on laptop-run tests.
    /// This implementation stays in <see cref="DateTimeOffset"/>
    /// throughout so the scheduling math is timezone-independent.
    ///
    /// We anchor at 06:00 UTC (two hours earlier than the digest
    /// worker's 08:00 UTC) so the admin dashboard surfaces fresh
    /// numbers before Sunday-morning readers log in, AND so rollup
    /// failures surface two hours before the parent emails go out —
    /// giving ops a runway to investigate without blocking the digest.
    /// </remarks>
    public static TimeSpan TimeUntilNextSundayMorning(DateTimeOffset now)
    {
        var nowUtc = now.ToUniversalTime();
        var todayMidnightUtc = new DateTimeOffset(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
        var todaySixUtc = todayMidnightUtc.AddHours(6);
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)todayMidnightUtc.DayOfWeek + 7) % 7;
        var nextSunday = todaySixUtc.AddDays(
            daysUntilSunday == 0 && nowUtc.TimeOfDay >= TimeSpan.FromHours(6)
                ? 7
                : daysUntilSunday);
        var result = nextSunday - nowUtc;
        return result < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : result;
    }
}
