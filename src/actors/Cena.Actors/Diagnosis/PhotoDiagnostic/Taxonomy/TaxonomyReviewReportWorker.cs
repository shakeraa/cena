// =============================================================================
// Cena Platform — TaxonomyReviewReportWorker (EPIC-PRR-J PRR-392)
//
// Weekly hosted service that produces the "disputed-diagnosis → taxonomy
// review" feedback loop (PRR-392). Calls
// ITaxonomyGovernanceService.FlagHighDisputeTemplatesAsync (shipped in
// PRR-375) on the last 7 days + emits a structured report log that
// ops / SMEs read in Slack-or-email. Per memory "Honest not
// complimentary" — the report surfaces the raw flagged keys with no
// smoothing; downstream UI can apply min-sample gates if the stream is
// noisy in the first weeks.
//
// Intentionally standalone: this file does NOT extend
// PhotoDiagnosticServiceRegistration.cs (concurrent in-flight task
// PRR-402 modifies that file; touching it here would force a merge
// conflict for the coordinator). Hosts wire the worker via
// AddTaxonomyReviewReporting() from this file directly. That extension
// method lives here so a single-file import pulls in the worker + DI.
//
// Schedule: Mondays at 07:00 UTC. Mondays so the SME inbox isn't
// cluttered mid-week when school districts are most active; 07:00 UTC
// = 10:00 local Asia/Jerusalem, i.e. first thing in the morning for
// the support team. Timezone-safe scheduling identical to
// WeeklyParentDigestWorker.TimeUntilNextSundayMorning (the bug-free
// UTC-only rewrite shipped in commit f59cfcb9).
//
// Regression-test auto-generation called out in the PRR-392 DoD is
// explicitly deferred — it requires a template→regression-corpus
// correlation we don't have yet. The present worker ships the
// FEEDBACK SIGNAL the SMEs consume; the regression auto-gen is a
// follow-up that reads this worker's output.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;

/// <summary>Knobs for <see cref="TaxonomyReviewReportWorker"/>. Tunable via DI.</summary>
public sealed record TaxonomyReviewReportOptions(
    /// <summary>Upheld-rate threshold that flags a template for review.</summary>
    double DisputeUpheldRateThreshold = 0.05,
    /// <summary>Hour of day (UTC) the weekly report fires.</summary>
    int WeeklyHourUtc = 7)
{
    /// <summary>Default: 5% upheld-rate threshold, 07:00 UTC Monday.</summary>
    public static readonly TaxonomyReviewReportOptions Default = new();
}

/// <summary>
/// Weekly hosted service. Runs once per Monday 07:00 UTC; each run calls
/// <see cref="ITaxonomyGovernanceService.FlagHighDisputeTemplatesAsync"/>
/// and emits a structured warning log with a stable alert code
/// <c>taxonomy_review_needed</c> so the ops alert pipeline can pivot on
/// it.
/// </summary>
public sealed class TaxonomyReviewReportWorker : BackgroundService
{
    /// <summary>Stable log-alert code ops can route on.</summary>
    public const string AlertCode = "taxonomy_review_needed";

    private readonly ITaxonomyGovernanceService _governance;
    private readonly TimeProvider _clock;
    private readonly TaxonomyReviewReportOptions _options;
    private readonly ILogger<TaxonomyReviewReportWorker> _logger;

    /// <summary>Construct the worker; all deps TryAdd-registered via DI.</summary>
    public TaxonomyReviewReportWorker(
        ITaxonomyGovernanceService governance,
        TimeProvider clock,
        TaxonomyReviewReportOptions options,
        ILogger<TaxonomyReviewReportWorker> logger)
    {
        _governance = governance ?? throw new ArgumentNullException(nameof(governance));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var until = TimeUntilNextMondayMorning(_clock.GetUtcNow(), _options.WeeklyHourUtc);
            try
            {
                await Task.Delay(until, stoppingToken);
            }
            catch (TaskCanceledException) { break; }
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var flagged = await RunOnceAsync(stoppingToken);
                if (flagged > 0)
                {
                    _logger.LogInformation(
                        "TaxonomyReviewReportWorker: {Count} templates flagged this week.",
                        flagged);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "TaxonomyReviewReportWorker pass failed; retrying next week.");
            }
        }
    }

    /// <summary>
    /// Fire one pass immediately. Exposed for tests. Returns the number
    /// of template keys above the dispute threshold. Logs each flagged
    /// key at Warning with the alert code so dashboards + Slack routes
    /// can both subscribe.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var flagged = await _governance
            .FlagHighDisputeTemplatesAsync(_options.DisputeUpheldRateThreshold, ct)
            .ConfigureAwait(false);

        foreach (var key in flagged)
        {
            _logger.LogWarning(
                "[PRR-392] [{AlertCode}] templateKey={Key} threshold={Threshold} — "
                + "dispute upheld-rate exceeded weekly threshold; SME review required.",
                AlertCode,
                key,
                _options.DisputeUpheldRateThreshold);
        }
        return flagged.Count;
    }

    /// <summary>
    /// Time until the next Monday at <paramref name="hourUtc"/>:00. Pure
    /// function, UTC-anchored throughout (no DateTime-Kind-Unspecified
    /// traps — see WeeklyParentDigestWorker.TimeUntilNextSundayMorning
    /// commit f59cfcb9 for the lesson). Returns a floored non-negative
    /// TimeSpan; if scheduling math would produce zero/negative, returns
    /// 1 minute so Task.Delay never throws on a corner case.
    /// </summary>
    public static TimeSpan TimeUntilNextMondayMorning(DateTimeOffset now, int hourUtc)
    {
        if (hourUtc < 0 || hourUtc > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hourUtc));
        }
        var nowUtc = now.ToUniversalTime();
        var todayMidnightUtc = new DateTimeOffset(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
        var todayHourUtc = todayMidnightUtc.AddHours(hourUtc);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)todayMidnightUtc.DayOfWeek + 7) % 7;
        var nextMonday = todayHourUtc.AddDays(
            daysUntilMonday == 0 && nowUtc.TimeOfDay >= TimeSpan.FromHours(hourUtc)
                ? 7
                : daysUntilMonday);
        var result = nextMonday - nowUtc;
        return result < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : result;
    }
}

/// <summary>
/// DI helpers for the PRR-392 worker. Hosts that want the weekly feedback
/// loop call <see cref="AddTaxonomyReviewReporting"/> after registering
/// the PRR-375 governance surface (<c>AddPhotoDiagnosticMarten</c> etc.).
/// </summary>
public static class TaxonomyReviewReportingServiceRegistration
{
    /// <summary>
    /// Register the weekly worker + options. Requires
    /// <see cref="ITaxonomyGovernanceService"/> + <see cref="TimeProvider"/>
    /// already registered upstream (both ship by PRR-375 / host base).
    /// Idempotent via TryAdd for the options; the hosted service itself is
    /// registered via <see cref="ServiceCollectionHostedServiceExtensions.AddHostedService{T}"/>
    /// (not TryAdd because host-service registration is additive).
    /// </summary>
    public static IServiceCollection AddTaxonomyReviewReporting(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TaxonomyReviewReportOptions.Default);
        services.AddHostedService<TaxonomyReviewReportWorker>();
        return services;
    }
}
