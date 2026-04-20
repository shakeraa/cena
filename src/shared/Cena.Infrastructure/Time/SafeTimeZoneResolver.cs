// =============================================================================
// Cena Platform — Generic cross-platform time-zone resolver (prr-018 sibling
// to prr-157).
//
// prr-018 added per-parent configurable quiet-hours timezones for outbound
// SMS. The canonical Israel-only resolver (IsraelTimeZoneResolver) cannot
// cover arbitrary IANA IDs, but adding a raw TimeZoneInfo.FindSystemTimeZoneById
// call to the SMS policy file triggers NoDirectSystemTimeZoneCallsTest (prr-157).
// This file is the sanctioned second seam: a generic resolver that applies the
// same IANA-first, Windows-legacy-fallback pattern for any zone ID.
//
// NoDirectSystemTimeZoneCallsTest allowlists this file by name so no new
// raw call sites proliferate.
// =============================================================================

namespace Cena.Infrastructure.Time;

/// <summary>
/// Cross-platform safe wrapper around <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>.
/// Use this instead of calling the BCL method directly — the architecture test
/// <c>NoDirectSystemTimeZoneCallsTest</c> (prr-157) forbids direct call sites.
/// </summary>
public static class SafeTimeZoneResolver
{
    /// <summary>
    /// Resolves a time zone by ID. Accepts either IANA (<c>Asia/Jerusalem</c>,
    /// <c>Europe/London</c>, etc.) or Windows legacy IDs (<c>Israel</c>,
    /// <c>GMT Standard Time</c>, etc.). Throws
    /// <see cref="TimeZoneNotFoundException"/> when neither lookup succeeds, so
    /// callers can map a missing-zone defect to a deterministic fallback.
    /// </summary>
    public static TimeZoneInfo Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException)
        {
            // Some hosts only know the Windows alias for a given zone
            // (e.g. "Israel Standard Time" vs "Asia/Jerusalem"). Retry via the
            // curated Israel resolver if the caller happened to pass an Israel
            // alias — preserves behaviour when the caller did the right thing
            // but the host has a partial tzdata.
            if (IdentifiesIsrael(id)) return IsraelTimeZoneResolver.Instance;
            throw;
        }
    }

    /// <summary>
    /// Non-throwing variant of <see cref="Find"/>. Returns <c>false</c> and
    /// yields the BCL UTC zone when the ID is unknown — callers may prefer an
    /// explicit fallback over exception flow.
    /// </summary>
    public static bool TryFind(string id, out TimeZoneInfo tz)
    {
        try { tz = Find(id); return true; }
        catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; return false; }
        catch (InvalidTimeZoneException) { tz = TimeZoneInfo.Utc; return false; }
        catch (ArgumentException) { tz = TimeZoneInfo.Utc; return false; }
    }

    private static bool IdentifiesIsrael(string id) =>
        string.Equals(id, "Asia/Jerusalem", StringComparison.Ordinal)
        || string.Equals(id, "Israel", StringComparison.Ordinal)
        || string.Equals(id, "Israel Standard Time", StringComparison.Ordinal);
}
