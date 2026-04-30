// =============================================================================
// Cena Platform — Trial cohort metrics DTO + calculator (Phase 4 / d9c663e2 follow-up).
//
// Aggregates trial-funnel metrics for the admin trial-cohort dashboard.
// The calculator is a pure function over event lists + lifetime counts —
// keeps the reader thin and the math 100% testable without Marten.
// =============================================================================

using System.Text.Json.Serialization;
using Cena.Actors.Subscriptions.Events;

namespace Cena.Admin.Api.Features.TrialCohort;

/// <summary>Wire shape for the admin dashboard trial-cohort card.</summary>
public sealed record TrialCohortMetricsDto(
    [property: JsonPropertyName("windowStart")] DateTimeOffset WindowStart,
    [property: JsonPropertyName("windowEnd")] DateTimeOffset WindowEnd,
    [property: JsonPropertyName("activeTrialsCount")] int ActiveTrialsCount,
    [property: JsonPropertyName("trialsStartedInWindow")] int TrialsStartedInWindow,
    [property: JsonPropertyName("trialsConvertedInWindow")] int TrialsConvertedInWindow,
    [property: JsonPropertyName("trialsExpiredInWindow")] int TrialsExpiredInWindow,
    [property: JsonPropertyName("conversionRatePct")] decimal? ConversionRatePct,
    [property: JsonPropertyName("avgDaysToConvert")] decimal? AvgDaysToConvert,
    [property: JsonPropertyName("medianDaysToConvert")] decimal? MedianDaysToConvert,
    [property: JsonPropertyName("avgTutorTurnsAtConvert")] decimal? AvgTutorTurnsAtConvert,
    [property: JsonPropertyName("avgPhotoDiagnosticsAtConvert")] decimal? AvgPhotoDiagnosticsAtConvert);

/// <summary>
/// Pure-function aggregator. Takes the in-window events plus lifetime counts
/// and returns a wire-ready DTO. ActiveTrialsCount = lifetime started −
/// (lifetime converted + lifetime expired); each trial has exactly one
/// terminal event (TrialConverted or TrialExpired) per ADR-0061.
///
/// Conversion-rate is computed over trials TERMINATED in the window (i.e.
/// either converted or expired in [start, end)) — that's the funnel
/// curators reason about. Active trials are excluded because they could
/// still convert, and including them in the denominator would understate
/// the rate visible from the cohort that's actually been observed.
/// </summary>
internal static class TrialCohortMetricsCalculator
{
    public static TrialCohortMetricsDto Compute(
        IReadOnlyList<TrialStarted_V1> startedInWindow,
        IReadOnlyList<TrialConverted_V1> convertedInWindow,
        IReadOnlyList<TrialExpired_V1> expiredInWindow,
        int lifetimeStarted,
        int lifetimeConverted,
        int lifetimeExpired,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var active = Math.Max(0, lifetimeStarted - lifetimeConverted - lifetimeExpired);

        decimal? conversionRate = null;
        var terminationCount = convertedInWindow.Count + expiredInWindow.Count;
        if (terminationCount > 0)
        {
            conversionRate = Math.Round(
                100m * convertedInWindow.Count / terminationCount, 1);
        }

        decimal? avgDays = null, medianDays = null;
        if (convertedInWindow.Count > 0)
        {
            var daysList = convertedInWindow
                .Select(c => (decimal)c.DaysIntoTrial)
                .OrderBy(d => d)
                .ToList();
            avgDays = Math.Round(daysList.Average(), 1);
            // Lower-median: with even N take the lower middle. For trial
            // funnels the distribution is right-skewed (a long tail of
            // late-converters), so lower-median undershoots more
            // honestly than the upper-pick.
            medianDays = daysList[(daysList.Count - 1) / 2];
        }

        decimal? avgTutorTurns = null, avgPhoto = null;
        if (convertedInWindow.Count > 0)
        {
            avgTutorTurns = Math.Round(
                (decimal)convertedInWindow.Average(c => c.UtilizationAtConversion.TutorTurnsUsed),
                1);
            avgPhoto = Math.Round(
                (decimal)convertedInWindow.Average(c => c.UtilizationAtConversion.PhotoDiagnosticsUsed),
                1);
        }

        return new TrialCohortMetricsDto(
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            ActiveTrialsCount: active,
            TrialsStartedInWindow: startedInWindow.Count,
            TrialsConvertedInWindow: convertedInWindow.Count,
            TrialsExpiredInWindow: expiredInWindow.Count,
            ConversionRatePct: conversionRate,
            AvgDaysToConvert: avgDays,
            MedianDaysToConvert: medianDays,
            AvgTutorTurnsAtConvert: avgTutorTurns,
            AvgPhotoDiagnosticsAtConvert: avgPhoto);
    }
}
