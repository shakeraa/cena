// =============================================================================
// Cena Platform — BankTransferExpiryWorker (EPIC-PRR-I PRR-304)
//
// Daily IHostedService that expires bank-transfer reservations that have
// passed their 14-day window without admin confirmation. The
// BankTransferReservationService owns the per-row transition; the worker
// owns the schedule.
//
// Cadence: once at startup, then daily at 02:00 UTC. 02:00 is outside
// Israeli school-hours (04-23 UTC for IL timezone during school year) so
// any log volume from a large expiry batch doesn't overlap with peak
// student traffic. The parent's reservation silently dies at this
// instant; the parent sees "expired" next time they hit the
// /api/me/subscription/bank-transfer/{ref} status endpoint. Reservations
// lost via expiry are non-recoverable — the parent simply starts over,
// which is the honest posture (we didn't take money; we had nothing to
// return).
//
// TimeProvider-driven so tests can drive the clock deterministically.
// Cancellation-cooperative: the outer OperationCanceledException from
// Task.Delay is the hosted-service shutdown signal, propagate and exit.
// =============================================================================

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Subscriptions;

/// <summary>Knobs for <see cref="BankTransferExpiryWorker"/> tick cadence.</summary>
public sealed class BankTransferExpiryWorkerOptions
{
    /// <summary>
    /// Configuration-binding section name. Hosts that want to override the
    /// default UTC tick hour bind <c>BankTransferExpiryWorker:TickHourUtc</c>.
    /// </summary>
    public const string SectionName = "BankTransferExpiryWorker";

    /// <summary>UTC hour of day at which the worker runs. Default 02:00.</summary>
    public int TickHourUtc { get; set; } = 2;

    /// <summary>
    /// When true, the worker runs once immediately at startup before the
    /// first scheduled tick. Useful when the pod is restarted during a
    /// long weekend — catches anything that went past the window while
    /// the pod was down. Default true.
    /// </summary>
    public bool RunOnStartup { get; set; } = true;
}

/// <summary>
/// IHostedService that invokes
/// <see cref="BankTransferReservationService.ExpirePastDueAsync"/> once a day.
/// </summary>
public sealed class BankTransferExpiryWorker : BackgroundService
{
    private readonly BankTransferReservationService _service;
    private readonly TimeProvider _clock;
    private readonly BankTransferExpiryWorkerOptions _options;
    private readonly ILogger<BankTransferExpiryWorker> _logger;

    public BankTransferExpiryWorker(
        BankTransferReservationService service,
        TimeProvider clock,
        IOptions<BankTransferExpiryWorkerOptions> options,
        ILogger<BankTransferExpiryWorker> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value
            ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.TickHourUtc is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), _options.TickHourUtc,
                "TickHourUtc must be in [0, 23].");
        }
    }

    /// <summary>
    /// Core loop. Exposed as <c>ExecuteAsync</c> override; also invokable
    /// directly via <see cref="RunOnceAsync"/> for tests and admin
    /// /run-now endpoints if ops add one later.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RunOnStartup)
        {
            await SafeRunOnceAsync(stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextTick(_clock.GetUtcNow(), _options.TickHourUtc);
            try
            {
                await Task.Delay(delay, _clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await SafeRunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Run a single expiry pass. Returns the count of reservations
    /// transitioned to Expired. Never throws — exceptions are logged and
    /// swallowed so one bad row does not kill the worker. Tests call this
    /// directly to drive deterministic expiry.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var expired = await _service.ExpirePastDueAsync(ct).ConfigureAwait(false);
            if (expired > 0)
            {
                _logger.LogInformation(
                    "BankTransferExpiryWorker: expired {Count} past-due reservations.",
                    expired);
            }
            return expired;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BankTransferExpiryWorker: expiry pass failed. Will retry at next tick.");
            return 0;
        }
    }

    private Task SafeRunOnceAsync(CancellationToken ct) => RunOnceAsync(ct);

    /// <summary>
    /// Compute the delay until the next UTC hour boundary equal to
    /// <paramref name="tickHourUtc"/>. Exposed internal for tests.
    /// </summary>
    internal static TimeSpan TimeUntilNextTick(DateTimeOffset nowUtc, int tickHourUtc)
    {
        // Build "today at tickHour:00:00 UTC".
        var today = new DateTimeOffset(
            nowUtc.Year, nowUtc.Month, nowUtc.Day,
            tickHourUtc, 0, 0, TimeSpan.Zero);
        var target = nowUtc < today ? today : today.AddDays(1);
        var delay = target - nowUtc;
        // Floor at 1 minute so a misconfigured clock does not bring the
        // worker into a tight loop.
        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }
}
