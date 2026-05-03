// =============================================================================
// Cena Platform — TrialCohortMetricsCalculator tests (Phase 4).
//
// Pure-function aggregator. Covers:
//   - Empty inputs → all aggregates null + zero counts
//   - ActiveCount = lifetime started − (converted + expired); never negative
//   - Conversion rate = converted / (converted + expired) × 100, rounded 1dp
//   - Conversion rate denominator excludes still-active trials
//   - Avg + lower-median days-to-convert
//   - Avg utilization at convert (TutorTurnsUsed, PhotoDiagnosticsUsed)
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Admin.Api.Features.TrialCohort;
using Xunit;

namespace Cena.Admin.Api.Tests.TrialCohort;

public sealed class TrialCohortMetricsCalculatorTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd   = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static TrialStarted_V1 Start(string parent, DateTimeOffset at) =>
        new(
            ParentSubjectIdEncrypted: parent,
            PrimaryStudentSubjectIdEncrypted: $"{parent}::s",
            TrialKind: TrialKind.SelfPay,
            TrialStartedAt: at,
            TrialEndsAt: at.AddDays(14),
            FingerprintHash: "fp",
            ExperimentVariantId: "v1",
            CapsSnapshot: new TrialCapsSnapshot(14, 50, 10, 6));

    private static TrialConverted_V1 Convert(
        string parent, DateTimeOffset at, int daysIn, int tutorTurns = 0, int photoDx = 0) =>
        new(
            ParentSubjectIdEncrypted: parent,
            PrimaryStudentSubjectIdEncrypted: $"{parent}::s",
            ConvertedAt: at,
            DaysIntoTrial: daysIn,
            ConvertedToTier: SubscriptionTier.Plus,
            BillingCycle: BillingCycle.Monthly,
            PaymentTransactionIdEncrypted: "txn",
            UtilizationAtConversion: new TrialUtilization(
                TutorTurnsUsed: tutorTurns,
                PhotoDiagnosticsUsed: photoDx,
                SessionsStarted: 0,
                DaysActive: daysIn,
                HitCapBeforeExpiry: false));

    private static TrialExpired_V1 Expire(string parent, DateTimeOffset at) =>
        new(
            ParentSubjectIdEncrypted: parent,
            PrimaryStudentSubjectIdEncrypted: $"{parent}::s",
            TrialEndedAt: at,
            Outcome: TrialExpired_V1.OutcomeExpired,
            Utilization: TrialUtilization.NoConsumption);

    [Fact]
    public void Empty_inputs_produce_zero_counts_and_null_aggregates()
    {
        var dto = TrialCohortMetricsCalculator.Compute(
            startedInWindow: Array.Empty<TrialStarted_V1>(),
            convertedInWindow: Array.Empty<TrialConverted_V1>(),
            expiredInWindow: Array.Empty<TrialExpired_V1>(),
            lifetimeStarted: 0, lifetimeConverted: 0, lifetimeExpired: 0,
            windowStart: WindowStart, windowEnd: WindowEnd);

        Assert.Equal(0, dto.ActiveTrialsCount);
        Assert.Equal(0, dto.TrialsStartedInWindow);
        Assert.Equal(0, dto.TrialsConvertedInWindow);
        Assert.Equal(0, dto.TrialsExpiredInWindow);
        Assert.Null(dto.ConversionRatePct);
        Assert.Null(dto.AvgDaysToConvert);
        Assert.Null(dto.MedianDaysToConvert);
        Assert.Null(dto.AvgTutorTurnsAtConvert);
        Assert.Null(dto.AvgPhotoDiagnosticsAtConvert);
    }

    [Fact]
    public void Active_count_is_lifetime_started_minus_terminated_clamped_to_zero()
    {
        var dto = TrialCohortMetricsCalculator.Compute(
            Array.Empty<TrialStarted_V1>(), Array.Empty<TrialConverted_V1>(),
            Array.Empty<TrialExpired_V1>(),
            lifetimeStarted: 100, lifetimeConverted: 30, lifetimeExpired: 50,
            WindowStart, WindowEnd);

        Assert.Equal(20, dto.ActiveTrialsCount);

        // Negative would happen only if event ordering broke (terminated > started);
        // clamp to 0 rather than expose nonsense to the dashboard.
        var glitchy = TrialCohortMetricsCalculator.Compute(
            Array.Empty<TrialStarted_V1>(), Array.Empty<TrialConverted_V1>(),
            Array.Empty<TrialExpired_V1>(),
            lifetimeStarted: 5, lifetimeConverted: 4, lifetimeExpired: 4,
            WindowStart, WindowEnd);
        Assert.Equal(0, glitchy.ActiveTrialsCount);
    }

    [Fact]
    public void Conversion_rate_excludes_active_trials_from_denominator()
    {
        // 10 started in window; 4 converted, 1 expired, 5 still active.
        // Rate = 4 / (4 + 1) = 80.0% — the active 5 are NOT counted as
        // failures yet because they could still convert.
        var started = Enumerable.Range(0, 10).Select(i =>
            Start($"p{i}", WindowStart.AddDays(i))).ToArray();
        var converted = Enumerable.Range(0, 4).Select(i =>
            Convert($"p{i}", WindowStart.AddDays(i + 5), daysIn: 5)).ToArray();
        var expired = new[] { Expire("p9", WindowStart.AddDays(20)) };

        var dto = TrialCohortMetricsCalculator.Compute(
            started, converted, expired,
            lifetimeStarted: 10, lifetimeConverted: 4, lifetimeExpired: 1,
            WindowStart, WindowEnd);

        Assert.Equal(80.0m, dto.ConversionRatePct);
        Assert.Equal(5, dto.ActiveTrialsCount);
    }

    [Fact]
    public void Conversion_rate_is_null_when_no_terminations_in_window()
    {
        // 5 started, none terminated yet (still in trial).
        var started = Enumerable.Range(0, 5).Select(i =>
            Start($"p{i}", WindowStart.AddDays(i))).ToArray();

        var dto = TrialCohortMetricsCalculator.Compute(
            started, Array.Empty<TrialConverted_V1>(), Array.Empty<TrialExpired_V1>(),
            lifetimeStarted: 5, lifetimeConverted: 0, lifetimeExpired: 0,
            WindowStart, WindowEnd);

        Assert.Null(dto.ConversionRatePct);
        Assert.Equal(5, dto.ActiveTrialsCount);
        Assert.Equal(5, dto.TrialsStartedInWindow);
    }

    [Fact]
    public void Avg_and_median_days_to_convert_use_lower_median()
    {
        // DaysIntoTrial values: 3, 7, 11, 14
        // Average: 8.75 → 8.8 (1dp banker's-aware rounding)
        // Lower-median (N=4): index = (4-1)/2 = 1 → daysList[1] = 7
        var converted = new[]
        {
            Convert("p1", WindowStart.AddDays(5),  daysIn: 3),
            Convert("p2", WindowStart.AddDays(10), daysIn: 7),
            Convert("p3", WindowStart.AddDays(14), daysIn: 11),
            Convert("p4", WindowStart.AddDays(17), daysIn: 14),
        };

        var dto = TrialCohortMetricsCalculator.Compute(
            Array.Empty<TrialStarted_V1>(), converted, Array.Empty<TrialExpired_V1>(),
            lifetimeStarted: 4, lifetimeConverted: 4, lifetimeExpired: 0,
            WindowStart, WindowEnd);

        Assert.Equal(8.8m, dto.AvgDaysToConvert);
        Assert.Equal(7m, dto.MedianDaysToConvert);
    }

    [Fact]
    public void Average_utilization_at_convert_is_per_field()
    {
        // tutor turns: 10 + 20 + 30 = 60 / 3 = 20.0
        // photo dx:   2 +  4 +  6 = 12 / 3 =  4.0
        var converted = new[]
        {
            Convert("p1", WindowStart.AddDays(5), daysIn: 5, tutorTurns: 10, photoDx: 2),
            Convert("p2", WindowStart.AddDays(8), daysIn: 7, tutorTurns: 20, photoDx: 4),
            Convert("p3", WindowStart.AddDays(11), daysIn: 9, tutorTurns: 30, photoDx: 6),
        };

        var dto = TrialCohortMetricsCalculator.Compute(
            Array.Empty<TrialStarted_V1>(), converted, Array.Empty<TrialExpired_V1>(),
            3, 3, 0, WindowStart, WindowEnd);

        Assert.Equal(20.0m, dto.AvgTutorTurnsAtConvert);
        Assert.Equal(4.0m, dto.AvgPhotoDiagnosticsAtConvert);
    }
}
