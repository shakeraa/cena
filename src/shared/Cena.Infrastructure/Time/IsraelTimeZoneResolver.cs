// =============================================================================
// Cena Platform — Israel time-zone resolver (prr-157 / actor-system-review L1)
//
// Single source of truth for "what time is it in Israel?" across the actor
// system. Previously PushNotificationTriggerService.cs:60 called
// `TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Israel Standard Time")`
// — a Windows-only ID that throws `TimeZoneNotFoundException` on every Linux
// CI run and every production container. OutreachSchedulerActor.cs had the
// correct try/catch but kept the pattern private; this class lifts it to a
// reusable utility and the companion architecture test
// (NoDirectSystemTimeZoneCallsTest) forbids new direct call sites.
//
// Resolution strategy:
//   1. Try IANA ID "Asia/Jerusalem" (Linux, macOS, Windows 10+ with ICU).
//   2. Fall back to Windows legacy ID "Israel" (pre-ICU Windows hosts).
//   3. If neither resolves, throw InvalidOperationException loudly — a host
//      without the Israel TZDB entry cannot correctly run quiet-hours or
//      weekly-summary logic, and masking that failure would silently ship
//      wrong-timezone notifications to students.
// =============================================================================

namespace Cena.Infrastructure.Time;

/// <summary>
/// Cross-platform resolver for the Israel time zone. Use this instead of
/// calling <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/> or
/// <see cref="TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset, string)"/>
/// directly — those calls pick a platform-specific ID and will crash on the
/// wrong OS. The companion architecture test
/// <c>NoDirectSystemTimeZoneCallsTest</c> enforces this.
/// </summary>
public static class IsraelTimeZoneResolver
{
    /// <summary>
    /// The resolved Israel <see cref="TimeZoneInfo"/>. Initialized once on
    /// first access; safe to cache in a static field on callers.
    /// </summary>
    public static TimeZoneInfo Instance { get; } = Resolve();

    /// <summary>
    /// Converts a UTC <see cref="DateTimeOffset"/> to the Israel time zone.
    /// Preserves offset semantics (result's <c>Offset</c> reflects Israel DST).
    /// </summary>
    public static DateTimeOffset ConvertFromUtc(DateTimeOffset utc)
        => TimeZoneInfo.ConvertTime(utc, Instance);

    private static TimeZoneInfo Resolve()
    {
        // Linux/macOS (and modern Windows with ICU) use IANA IDs.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem"); }
        catch (TimeZoneNotFoundException) { /* fall through */ }

        // Legacy Windows uses the display-name-ish ID "Israel".
        try { return TimeZoneInfo.FindSystemTimeZoneById("Israel"); }
        catch (TimeZoneNotFoundException) { /* fall through */ }

        throw new InvalidOperationException(
            "Israel time zone could not be resolved: neither the IANA ID " +
            "'Asia/Jerusalem' nor the Windows legacy ID 'Israel' is available " +
            "on this host. Install the system tzdata package (Linux), enable " +
            "ICU (Windows with GlobalizationInvariant=false), or verify the " +
            "container image ships a complete zoneinfo database. See prr-157.");
    }
}
