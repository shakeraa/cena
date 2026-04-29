// =============================================================================
// Cena Platform — TrialExpiryWorker (Phase 1D, trial-then-paywall §5.5)
//
// Hosted background service that scans subscription streams in Trialing
// status and fires SubscriptionCommands.ExpireTrial when the pinned
// TrialEndsAt has passed. The command itself is idempotent on already-
// Expired streams (Phase 1A SubscriptionCommands.ExpireTrial design), so
// a duplicate tick is harmless.
//
// Cadence: every 60 seconds by default. Smaller than the bank-transfer
// expiry (which is daily at 02:00 UTC) because trials are USER-FACING —
// a Trialing student whose calendar passed needs the paywall to engage
// promptly, not at the next nightly batch. The 60s window also caps the
// "extra free turn" leakage per StudentEntitlementResolver §5.5.1 (the
// resolver IS already past-end aware via IsTrialingAsOf, so users do not
// actually get an extra turn — but state and projection lag is bounded
// to this tick).
//
// Cap-only trials (TrialEndsAt == TrialStartedAt) are NEVER expired by
// this worker — they end on cap-hit telemetry only (Phase 1E enforcer).
//
// TimeProvider-driven so tests can drive the clock deterministically.
// Per-stream exception isolation: a single bad stream's exception is
// logged and swallowed so the worker keeps making progress on the rest.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Subscriptions;

/// <summary>Knobs for <see cref="TrialExpiryWorker"/> tick cadence.</summary>
public sealed class TrialExpiryWorkerOptions
{
    /// <summary>Configuration-binding section name.</summary>
    public const string SectionName = "TrialExpiryWorker";

    /// <summary>
    /// Tick interval in seconds. Default 60. Lower bound 5 enforced at
    /// construction so a misconfigured value cannot degenerate into a tight
    /// loop. Upper bound 3600 enforced so a large-typo value still keeps
    /// trial expirations bounded.
    /// </summary>
    public int TickIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// When true, runs one immediate pass at startup before the first
    /// scheduled tick. Catches anything that went past expiry while the
    /// pod was down. Default true.
    /// </summary>
    public bool RunOnStartup { get; set; } = true;
}

/// <summary>
/// IHostedService that periodically scans subscription streams in Trialing
/// status and fires <see cref="SubscriptionCommands.ExpireTrial"/> on those
/// whose pinned end has passed. Idempotent.
/// </summary>
public sealed class TrialExpiryWorker : BackgroundService
{
    private readonly ISubscriptionStreamEnumerator _enumerator;
    private readonly ISubscriptionAggregateStore _store;
    private readonly IStudentTrialConsumptionStore _consumption;
    private readonly TimeProvider _clock;
    private readonly TrialExpiryWorkerOptions _options;
    private readonly ILogger<TrialExpiryWorker> _logger;

    public TrialExpiryWorker(
        ISubscriptionStreamEnumerator enumerator,
        ISubscriptionAggregateStore store,
        IStudentTrialConsumptionStore consumption,
        TimeProvider clock,
        IOptions<TrialExpiryWorkerOptions> options,
        ILogger<TrialExpiryWorker> logger)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _consumption = consumption ?? throw new ArgumentNullException(nameof(consumption));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.TickIntervalSeconds is < 5 or > 3600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), _options.TickIntervalSeconds,
                "TickIntervalSeconds must be in [5, 3600].");
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RunOnStartup)
        {
            await SafeRunOnceAsync(stoppingToken).ConfigureAwait(false);
        }

        var interval = TimeSpan.FromSeconds(_options.TickIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await SafeRunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Run a single expiry pass. Returns the count of trials transitioned
    /// to Expired. Never throws on per-stream errors; only OperationCancelled
    /// propagates. Tests call this directly to drive deterministic expiry.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var expired = 0;
        try
        {
            await foreach (var parentId in _enumerator.EnumerateParentIdsAsync(ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (await TryExpireOneAsync(parentId, now, ct).ConfigureAwait(false))
                    {
                        expired++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Per-stream exception isolation. Do NOT log the parent
                    // id verbatim — it is encrypted but treat as PII per
                    // ADR-0038 conservative posture; the stream-key prefix
                    // gives ops enough to grep for in the audit log without
                    // leaking user identity.
                    _logger.LogWarning(ex,
                        "TrialExpiryWorker: failed to expire one stream; continuing.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TrialExpiryWorker: enumeration failed; will retry at next tick.");
            return expired;
        }

        if (expired > 0)
        {
            _logger.LogInformation(
                "TrialExpiryWorker: expired {Count} trial(s) at {Now:o}.",
                expired, now);
        }
        return expired;
    }

    /// <summary>
    /// Decide whether a single stream needs expiry; if so, append the
    /// TrialExpired_V1 event. Returns true when an expiration was written.
    /// </summary>
    /// <remarks>
    /// Eligibility:
    ///   * Status == Trialing
    ///   * TrialStartedAt + TrialEndsAt populated
    ///   * TrialEndsAt > TrialStartedAt (cap-only trials are excluded)
    ///   * now >= TrialEndsAt
    /// </remarks>
    private async Task<bool> TryExpireOneAsync(
        string parentId, DateTimeOffset now, CancellationToken ct)
    {
        var aggregate = await _store.LoadAsync(parentId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        if (state.Status != SubscriptionStatus.Trialing) return false;
        if (!state.TrialStartedAt.HasValue || !state.TrialEndsAt.HasValue) return false;

        // Cap-only trials never expire on the calendar — they end on cap
        // telemetry. Worker leaves them alone.
        if (state.TrialEndsAt.Value <= state.TrialStartedAt.Value) return false;
        if (now < state.TrialEndsAt.Value) return false;

        // Build the utilisation snapshot from the per-student counter.
        var primary = state.LinkedStudents.FirstOrDefault();
        var utilisation = TrialUtilization.NoConsumption;
        if (primary is not null)
        {
            var consumption = await _consumption
                .GetAsync(primary.StudentSubjectIdEncrypted, ct)
                .ConfigureAwait(false);
            utilisation = new TrialUtilization(
                TutorTurnsUsed: consumption.TutorTurnsUsed,
                PhotoDiagnosticsUsed: consumption.PhotoDiagnosticsUsed,
                SessionsStarted: consumption.SessionsStarted,
                DaysActive: consumption.DaysActive,
                HitCapBeforeExpiry: false);
        }

        var evt = SubscriptionCommands.ExpireTrial(state, utilisation, now);
        await _store.AppendAsync(parentId, evt, ct).ConfigureAwait(false);
        return true;
    }

    private Task SafeRunOnceAsync(CancellationToken ct) => RunOnceAsync(ct);
}
