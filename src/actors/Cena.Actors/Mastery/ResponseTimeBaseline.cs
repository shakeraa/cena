// =============================================================================
// Cena Platform -- Response Time Baseline
// MST-012: Tracks student's personal response time median for quality classification
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Immutable response time baseline using a circular buffer of last 20 response times.
/// The median is the student's personal "fast vs slow" threshold.
/// </summary>
public sealed record ResponseTimeBaseline(
    float MedianResponseTimeMs,
    int SampleCount,
    int[] ResponseTimes)
{
    private const int MaxSamples = 20;
    private const float DefaultMedianMs = 15_000f;
    private const int MinSamplesForMedian = 3;

    public static readonly ResponseTimeBaseline Initial = new(DefaultMedianMs, 0, Array.Empty<int>());

    /// <summary>
    /// Add a new response time and recompute the median.
    /// Returns a new immutable baseline.
    /// </summary>
    public ResponseTimeBaseline Update(int responseTimeMs)
    {
        // Circular buffer: append or replace oldest
        int[] newTimes;
        if (ResponseTimes.Length < MaxSamples)
        {
            newTimes = new int[ResponseTimes.Length + 1];
            ResponseTimes.CopyTo(newTimes, 0);
            newTimes[^1] = responseTimeMs;
        }
        else
        {
            newTimes = new int[MaxSamples];
            Array.Copy(ResponseTimes, 1, newTimes, 0, MaxSamples - 1);
            newTimes[MaxSamples - 1] = responseTimeMs;
        }

        int newCount = SampleCount + 1;

        // Only compute real median with enough samples
        if (newTimes.Length < MinSamplesForMedian)
            return new ResponseTimeBaseline(DefaultMedianMs, newCount, newTimes);

        // Compute median from sorted copy
        var sorted = new int[newTimes.Length];
        Array.Copy(newTimes, sorted, newTimes.Length);
        Array.Sort(sorted);

        float median;
        int len = sorted.Length;
        if (len % 2 == 0)
            median = (sorted[len / 2 - 1] + sorted[len / 2]) / 2.0f;
        else
            median = sorted[len / 2];

        return new ResponseTimeBaseline(median, newCount, newTimes);
    }
}
