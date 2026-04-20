// =============================================================================
// Cena Platform — Outbound SMS quiet-hours policy (prr-018).
//
// Parent-nudge SMS is NOT allowed to ring the recipient's phone during the
// institute-configured quiet window (default 21:00–07:00 in the parent's local
// timezone). When a scheduled send falls inside the window the policy returns
// Defer(earliestSendAtUtc) so the dispatcher enqueues the message for the
// next safe moment — defer, never drop.
//
// WHY parent timezone (not institute timezone):
//   - An Israeli institute can have an expatriate parent in Europe, the US,
//     or the Gulf. The quiet hours are about the RECIPIENT's night, not the
//     sender's business day. Using the institute TZ would push US-coast
//     parents into 3 a.m. nudges.
//   - When the parent TZ is unknown we fall back to the institute TZ; if the
//     institute TZ is also unknown we fall back to Asia/Jerusalem (Cena MVP
//     home region). The fallback chain is explicit so ops can spot unknown
//     parents on the metric.
//
// WHY the window is a wall-clock time (21:00–07:00), not a duration:
//   - Telephone etiquette is a wall-clock norm. "Don't call after 21:00 local"
//     is the legal + cultural floor in Israel, matching the ICO/ePrivacy +
//     Israeli PPL guidance Rami + Iman cited in the persona panel.
//   - Wall-clock windows cross midnight naturally (21:00 end-of-day → 07:00
//     next-day). We test that explicitly.
//
// WHY DST-aware TimeZoneInfo.ConvertTimeFromUtc:
//   - Israel DST moves in March and October. A naive UTC+3 assumption would
//     shift quiet hours by an hour for several weeks per year. The BCL's
//     TimeZoneInfo covers DST transitions, spring-forward and fall-back,
//     correctly — we rely on its ConvertTimeFromUtc to resolve local time.
//
// WHY Defer and not Block:
//   - Parents opt-IN to these nudges. A drop on quiet-hours ground is a bug
//     for them — they chose to get the Friday digest; it should arrive Friday
//     morning after their quiet window, not silently vanish.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Globalization;
using Cena.Actors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Options bound from <c>Cena:Sms:QuietHours</c>. Defaults match the Israeli
/// 21:00–07:00 recipient-night norm.
/// </summary>
public sealed class SmsQuietHoursOptions
{
    public const string SectionName = "Cena:Sms:QuietHours";

    /// <summary>
    /// Default quiet window start (local hour, 24h). Default 21 (9 p.m.).
    /// </summary>
    public int DefaultStartHour { get; set; } = 21;

    /// <summary>
    /// Default quiet window end (local hour, 24h). Default 7 (7 a.m.).
    /// </summary>
    public int DefaultEndHour { get; set; } = 7;

    /// <summary>Fallback timezone when both parent and institute TZ are unknown.</summary>
    public string FallbackTimezone { get; set; } = "Asia/Jerusalem";

    /// <summary>
    /// Per-institute override of start/end hours. Useful for institutes serving
    /// international students who want a narrower or wider window.
    /// </summary>
    public Dictionary<string, SmsQuietHoursPerInstitute> InstituteOverrides { get; set; } = new();
}

public sealed class SmsQuietHoursPerInstitute
{
    public int? StartHour { get; set; }
    public int? EndHour { get; set; }
    public string? Timezone { get; set; }
}

/// <summary>
/// Quiet-hours policy. Returns Defer(earliestUtc) when the request's scheduled
/// time (converted to parent-local) is inside the quiet window.
/// </summary>
public sealed class SmsQuietHoursPolicy : IOutboundSmsPolicy
{
    private readonly SmsQuietHoursOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<SmsQuietHoursPolicy> _logger;
    private readonly Counter<long> _deferredCounter;

    public SmsQuietHoursPolicy(
        IConfiguration configuration,
        IClock clock,
        IMeterFactory meterFactory,
        ILogger<SmsQuietHoursPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _clock = clock;
        _logger = logger;

        _options = new SmsQuietHoursOptions();
        configuration.GetSection(SmsQuietHoursOptions.SectionName).Bind(_options);

        // Defensive clamp: out-of-range hours fall back to documented defaults.
        // An operator with a fat-finger "25" in config should see the 21:00
        // default, not a silent 25-hour-day math bug.
        if (_options.DefaultStartHour is < 0 or > 23) _options.DefaultStartHour = 21;
        if (_options.DefaultEndHour is < 0 or > 23) _options.DefaultEndHour = 7;

        var meter = meterFactory.Create("Cena.Actors.OutboundSms.QuietHours", "1.0.0");
        _deferredCounter = meter.CreateCounter<long>(
            "cena_sms_quiet_hours_deferred_total",
            description:
                "Outbound SMS deferred because scheduled time fell inside parent-local quiet hours (prr-018)");
    }

    public string Name => "quiet_hours";

    public Task<SmsPolicyOutcome> EvaluateAsync(
        OutboundSmsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instituteLabel = SmsSanitizerPolicy.NormalizeInstituteLabel(request.InstituteId);

        var (startHour, endHour, tz) = ResolveWindow(request);
        var localScheduled = ConvertToLocal(request.ScheduledForUtc, tz);

        if (!IsInQuietWindow(localScheduled, startHour, endHour))
        {
            return Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Allow(request));
        }

        var earliestLocal = NextSafeLocalTime(localScheduled, startHour, endHour);
        var earliestUtc = TimeZoneInfo.ConvertTimeToUtc(earliestLocal.DateTime, tz);

        _deferredCounter.Add(1,
            new KeyValuePair<string, object?>("institute_id", instituteLabel));
        _logger.LogInformation(
            "[prr-018] SMS quiet-hours defer: local={Local} tz={Tz} earliest_utc={EarliestUtc} correlation={Corr} institute={Institute}",
            localScheduled.ToString("O", CultureInfo.InvariantCulture),
            tz.Id,
            earliestUtc.ToString("O", CultureInfo.InvariantCulture),
            request.CorrelationId,
            instituteLabel);

        return Task.FromResult<SmsPolicyOutcome>(
            new SmsPolicyOutcome.Defer("quiet_hours", new DateTimeOffset(earliestUtc, TimeSpan.Zero)));
    }

    // -----------------------------------------------------------------------
    // Pure helpers — unit-tested directly.
    // -----------------------------------------------------------------------

    internal static bool IsInQuietWindow(DateTimeOffset local, int startHour, int endHour)
    {
        var h = local.Hour;
        if (startHour == endHour) return false;          // zero-length window
        if (startHour < endHour)
        {
            // Same-day window, e.g. 09:00–17:00.
            return h >= startHour && h < endHour;
        }
        // Wrap across midnight: 21:00–07:00 = [21..23] ∪ [0..6].
        return h >= startHour || h < endHour;
    }

    /// <summary>
    /// Compute the earliest acceptable send time (in local TZ) for a request
    /// that currently sits inside the quiet window. "Acceptable" means the
    /// exact moment the quiet window ends (endHour sharp).
    /// </summary>
    internal static DateTimeOffset NextSafeLocalTime(
        DateTimeOffset local, int startHour, int endHour)
    {
        var date = local.Date;
        var offset = local.Offset;

        if (startHour < endHour)
        {
            // Same-day window. We're inside [startHour, endHour) and want
            // endHour today.
            return new DateTimeOffset(date.AddHours(endHour), offset);
        }

        // Wrap-across-midnight window.
        if (local.Hour >= startHour)
        {
            // We're in the evening half (e.g. 22:00). The window ends tomorrow
            // morning at endHour.
            return new DateTimeOffset(date.AddDays(1).AddHours(endHour), offset);
        }

        // We're in the pre-dawn half (e.g. 03:00). The window ends today at
        // endHour.
        return new DateTimeOffset(date.AddHours(endHour), offset);
    }

    private (int StartHour, int EndHour, TimeZoneInfo Tz) ResolveWindow(OutboundSmsRequest request)
    {
        // Institute override wins the hour values. The TZ, however, always
        // prefers the parent's timezone — it's about recipient-night — and only
        // falls back to the institute TZ when the parent's TZ is blank.
        var startHour = _options.DefaultStartHour;
        var endHour = _options.DefaultEndHour;
        string? tzId = request.ParentTimezone;

        if (!string.IsNullOrWhiteSpace(request.InstituteId)
            && _options.InstituteOverrides.TryGetValue(request.InstituteId!, out var overrides))
        {
            if (overrides.StartHour is { } sh and >= 0 and <= 23) startHour = sh;
            if (overrides.EndHour is { } eh and >= 0 and <= 23) endHour = eh;
            if (string.IsNullOrWhiteSpace(tzId) && !string.IsNullOrWhiteSpace(overrides.Timezone))
            {
                tzId = overrides.Timezone;
            }
        }

        if (string.IsNullOrWhiteSpace(tzId)) tzId = _options.FallbackTimezone;

        // Try the requested TZ; on any lookup failure fall back silently. A
        // quiet-hours check is never fatal — a bogus parent TZ ("Mars/Olympus")
        // resolves to the institute/Asia-Jerusalem fallback.
        TimeZoneInfo tz;
        try
        {
            tz = FindTimezone(tzId!);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning(
                "[prr-018] Quiet-hours timezone '{Tz}' not found; falling back to {Fallback}",
                tzId, _options.FallbackTimezone);
            tz = FindTimezone(_options.FallbackTimezone);
        }
        catch (InvalidTimeZoneException)
        {
            _logger.LogWarning(
                "[prr-018] Quiet-hours timezone '{Tz}' invalid; falling back to {Fallback}",
                tzId, _options.FallbackTimezone);
            tz = FindTimezone(_options.FallbackTimezone);
        }

        return (startHour, endHour, tz);
    }

    private static DateTimeOffset ConvertToLocal(DateTimeOffset utc, TimeZoneInfo tz)
    {
        var localDt = TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, tz);
        return new DateTimeOffset(localDt, tz.GetUtcOffset(localDt));
    }

    private static TimeZoneInfo FindTimezone(string id)
    {
        // The BCL accepts IANA ids on Linux/macOS and Windows ids on Windows;
        // .NET 8+ has a combined tz database. We wrap in try/catch so any
        // unknown-tz defect fails with a specific exception the caller maps
        // into a fallback.
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }
}
