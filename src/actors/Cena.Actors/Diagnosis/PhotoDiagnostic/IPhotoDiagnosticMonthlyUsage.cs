// =============================================================================
// Cena Platform — IPhotoDiagnosticMonthlyUsage (EPIC-PRR-J PRR-400)
//
// Per-student monthly counter of photo-diagnostic invocations. Consumed by
// PhotoDiagnosticQuotaGate (which sits in front of the intake endpoint) to
// enforce PRR-I tier caps at the photo-diagnostic boundary:
//   Basic:     0/mo (hard-block)
//   Plus:      20/mo (soft=hard)
//   Premium:   100/mo soft, 300/mo hard
//   SchoolSku: 50/mo soft, 100/mo hard
//
// Canonical period = calendar month in UTC. Alignment to billing anchor
// is a subscription concern (see SubscriptionAggregate.BillingCycle) — at
// this layer we just need a stable (student, YYYY-MM) key.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Calendar-month counter for photo-diagnostic usage.</summary>
public interface IPhotoDiagnosticMonthlyUsage
{
    /// <summary>Current usage in the given UTC calendar month.</summary>
    Task<int> GetAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct);

    /// <summary>Increment usage by 1 for the given UTC calendar month. Returns the post-increment value.</summary>
    Task<int> IncrementAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct);
}

/// <summary>
/// Canonical (YYYY-MM) month key used for storage and queries.
/// Always returned as an upper-cased invariant string (e.g. "2026-04").
/// </summary>
public static class MonthlyUsageKey
{
    public static string For(DateTimeOffset asOfUtc) =>
        asOfUtc.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
