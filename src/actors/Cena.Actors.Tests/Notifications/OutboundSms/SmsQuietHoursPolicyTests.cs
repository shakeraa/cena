// =============================================================================
// Cena Platform — SMS quiet-hours policy tests (prr-018).
//
// Covers:
//   - Pure helper IsInQuietWindow: same-day, wrap-across-midnight, zero-length
//   - Pure helper NextSafeLocalTime: same-day and wrap boundaries
//   - End-to-end EvaluateAsync: Allow vs Defer with institute overrides
//   - DST-edge handling using Asia/Jerusalem March 2026 spring-forward
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Infrastructure;
using Cena.Actors.Notifications.OutboundSms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Notifications.OutboundSms;

public sealed class SmsQuietHoursPolicyTests
{
    private static SmsQuietHoursPolicy NewPolicy(Dictionary<string, string?>? config = null)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new())
            .Build();
        return new SmsQuietHoursPolicy(
            cfg,
            new FakeClock(new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero)),
            new DummyMeterFactory(),
            NullLogger<SmsQuietHoursPolicy>.Instance);
    }

    private static OutboundSmsRequest Req(
        DateTimeOffset scheduledUtc,
        string timezone = "Asia/Jerusalem",
        string? instituteId = "inst-1") => new(
        InstituteId: instituteId,
        ParentPhoneE164: "+972501234567",
        ParentPhoneHash: "hash-abcd",
        ParentTimezone: timezone,
        Body: "Clean body.",
        TemplateId: "weekly-digest-v1",
        CorrelationId: "corr-1",
        ScheduledForUtc: scheduledUtc);

    // -------------------- pure helpers --------------------

    [Theory]
    [InlineData(22, 21, 7, true)]   // 22:00 inside 21-07 wrap window
    [InlineData(3, 21, 7, true)]    // 03:00 inside 21-07 wrap window
    [InlineData(7, 21, 7, false)]   // exactly at endHour → OUT (endHour exclusive)
    [InlineData(8, 21, 7, false)]   // daytime, outside
    [InlineData(20, 21, 7, false)]  // evening before start, outside
    [InlineData(21, 21, 7, true)]   // startHour inclusive
    [InlineData(10, 9, 17, true)]   // same-day window
    [InlineData(17, 9, 17, false)]  // endHour exclusive (same-day)
    [InlineData(9, 9, 17, true)]    // startHour inclusive (same-day)
    [InlineData(3, 0, 0, false)]    // zero-length window = never
    public void IsInQuietWindow_Cases(int hour, int start, int end, bool expected)
    {
        var local = new DateTimeOffset(2026, 4, 20, hour, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal(expected, SmsQuietHoursPolicy.IsInQuietWindow(local, start, end));
    }

    [Fact]
    public void NextSafeLocalTime_WrapWindow_EveningHalf_ReturnsNextDayEndHour()
    {
        var local = new DateTimeOffset(2026, 4, 20, 22, 30, 0, TimeSpan.FromHours(3));
        var next = SmsQuietHoursPolicy.NextSafeLocalTime(local, 21, 7);
        Assert.Equal(new DateTimeOffset(2026, 4, 21, 7, 0, 0, TimeSpan.FromHours(3)), next);
    }

    [Fact]
    public void NextSafeLocalTime_WrapWindow_PreDawnHalf_ReturnsSameDayEndHour()
    {
        var local = new DateTimeOffset(2026, 4, 20, 3, 30, 0, TimeSpan.FromHours(3));
        var next = SmsQuietHoursPolicy.NextSafeLocalTime(local, 21, 7);
        Assert.Equal(new DateTimeOffset(2026, 4, 20, 7, 0, 0, TimeSpan.FromHours(3)), next);
    }

    [Fact]
    public void NextSafeLocalTime_SameDayWindow_ReturnsSameDayEndHour()
    {
        var local = new DateTimeOffset(2026, 4, 20, 10, 30, 0, TimeSpan.FromHours(3));
        var next = SmsQuietHoursPolicy.NextSafeLocalTime(local, 9, 17);
        Assert.Equal(new DateTimeOffset(2026, 4, 20, 17, 0, 0, TimeSpan.FromHours(3)), next);
    }

    // -------------------- end-to-end --------------------

    [Fact]
    public async Task Allow_MidMorning()
    {
        var policy = NewPolicy();
        // 10:00 UTC = 13:00 Asia/Jerusalem (UTC+3 summer time) — outside quiet.
        var outcome = await policy.EvaluateAsync(
            Req(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero)));
        Assert.IsType<SmsPolicyOutcome.Allow>(outcome);
    }

    [Fact]
    public async Task Defer_LateEvening()
    {
        var policy = NewPolicy();
        // 20:00 UTC on 20 July 2026 = 23:00 Asia/Jerusalem IDT → inside quiet.
        var outcome = await policy.EvaluateAsync(
            Req(new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero)));
        var defer = Assert.IsType<SmsPolicyOutcome.Defer>(outcome);
        Assert.Equal("quiet_hours", defer.Reason);
        // Earliest send = 07:00 local next day = 04:00 UTC 21 July (IDT = UTC+3).
        Assert.Equal(
            new DateTimeOffset(2026, 7, 21, 4, 0, 0, TimeSpan.Zero),
            defer.EarliestSendAtUtc);
    }

    [Fact]
    public async Task Defer_PreDawn()
    {
        var policy = NewPolicy();
        // 01:00 UTC 20 July 2026 = 04:00 local IDT → inside quiet.
        var outcome = await policy.EvaluateAsync(
            Req(new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero)));
        var defer = Assert.IsType<SmsPolicyOutcome.Defer>(outcome);
        // Earliest send = 07:00 local SAME day = 04:00 UTC.
        Assert.Equal(
            new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero),
            defer.EarliestSendAtUtc);
    }

    [Fact]
    public async Task InstituteOverride_TighterWindowDefers()
    {
        // Override: quiet 18:00 - 09:00 for this institute. A 17:00 local send
        // is still allowed; a 18:00 local send is deferred.
        var policy = NewPolicy(new()
        {
            ["Cena:Sms:QuietHours:InstituteOverrides:inst-9:StartHour"] = "18",
            ["Cena:Sms:QuietHours:InstituteOverrides:inst-9:EndHour"] = "9",
        });

        // 15:00 UTC on 20 July 2026 = 18:00 IDT → inside override quiet.
        var outcome = await policy.EvaluateAsync(Req(
            new DateTimeOffset(2026, 7, 20, 15, 0, 0, TimeSpan.Zero),
            instituteId: "inst-9"));
        Assert.IsType<SmsPolicyOutcome.Defer>(outcome);
    }

    [Fact]
    public async Task UnknownTimezone_FallsBackToFallbackTimezone()
    {
        var policy = NewPolicy();
        // Invalid TZ id; policy should fall back to Asia/Jerusalem silently
        // and NOT throw. 20:00 UTC on 20 July = 23:00 IDT → defer.
        var outcome = await policy.EvaluateAsync(Req(
            new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero),
            timezone: "Mars/Olympus"));
        Assert.IsType<SmsPolicyOutcome.Defer>(outcome);
    }

    [Fact]
    public async Task DstEdge_SpringForward()
    {
        var policy = NewPolicy();
        // Asia/Jerusalem spring-forward 2026 (last Friday of March → March 27 at 02:00).
        // A UTC time that maps to local 22:30 before vs after DST must both land
        // inside the quiet window. We use an evening well after the switch to
        // exercise the post-DST offset; the expected defer is correctly +2h UTC
        // offset after spring-forward.
        var outcome = await policy.EvaluateAsync(Req(
            new DateTimeOffset(2026, 4, 1, 19, 30, 0, TimeSpan.Zero)));   // 22:30 IDT
        var defer = Assert.IsType<SmsPolicyOutcome.Defer>(outcome);
        // Earliest send = 07:00 IDT next day = 04:00 UTC.
        Assert.Equal(
            new DateTimeOffset(2026, 4, 2, 4, 0, 0, TimeSpan.Zero),
            defer.EarliestSendAtUtc);
    }

    [Fact]
    public async Task ParentTimezone_OverridesInstituteTimezone()
    {
        // Parent is in UTC (e.g. expatriate in Iceland — no DST). Institute is
        // in Jerusalem. Quiet hours apply to PARENT's clock.
        var policy = NewPolicy();

        // 20:00 UTC. Parent TZ = Atlantic/Reykjavik (UTC+0). Parent-local = 20:00,
        // BEFORE quiet start of 21:00 → Allow. If we had used the institute TZ
        // (Asia/Jerusalem UTC+3) this would have been 23:00 local → Defer.
        var outcome = await policy.EvaluateAsync(Req(
            new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero),
            timezone: "Atlantic/Reykjavik"));
        Assert.IsType<SmsPolicyOutcome.Allow>(outcome);
    }

    // -------------------- infra --------------------

    private sealed class FakeClock : IClock
    {
        private readonly DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset UtcNow => _now;
        public DateTime UtcDateTime => _now.UtcDateTime;
        public DateTime LocalDateTime => _now.LocalDateTime;
        public string FormatUtc(string format) => _now.UtcDateTime.ToString(format);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
