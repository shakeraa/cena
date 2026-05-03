// =============================================================================
// Cena Platform -- Daily Schedule Model (EMU-002.1)
// Models a 24-hour arrival rate curve for simulated students.
// Uses a sine-based smooth interpolation between configurable rate windows.
//
// Schedule behaviour:
//   Weekday: dual peak (after-school 14-16, evening 16-18), low overnight
//   Weekend: shifted later (peak 11-14), 60% of weekday volume
//   Friday (Israel): early shutdown at 14:00, no evening activity
// =============================================================================

namespace Cena.Emulator.Scheduler;

/// <summary>
/// A rate window specifying the minimum and maximum students-per-minute
/// arrival rate for a specific hour band.
/// </summary>
public sealed record RateWindow(int Hour, double RateMin, double RateMax);

/// <summary>
/// The day type used to select a schedule variant.
/// </summary>
public enum DayKind { Weekday, Weekend, Friday }

/// <summary>
/// Models a smooth (sine-interpolated) 24-hour arrival rate curve per day type.
/// Loaded from config/emulator/schedule.yaml at construction time; falls back
/// to hard-coded defaults if the file is absent.
/// </summary>
public sealed class DailyScheduleModel
{
    // ── Hard-coded default windows (used when schedule.yaml is not found) ─────

    private static readonly RateWindow[] DefaultWeekdayWindows =
    {
        new(0,  0,  1),   // 00-06 night owls
        new(6,  2,  5),   // 06-08 early birds
        new(8,  5, 10),   // 08-10 morning wave
        new(10, 3,  6),   // 10-14 school hours (low)
        new(14,10, 20),   // 14-16 after-school peak
        new(16,15, 25),   // 16-18 evening peak  ← max concurrency zone
        new(18, 8, 15),   // 18-20 dinner dip + evening
        new(20, 5, 10),   // 20-22 evening
        new(22, 2,  4),   // 22-00 taper
    };

    private static readonly RateWindow[] DefaultWeekendWindows =
    {
        new(0,  0,  1),
        new(8,  2,  4),
        new(11, 8, 15),   // weekend peak 11-14
        new(14, 5, 10),
        new(17, 4,  8),
        new(20, 2,  5),
        new(22, 0,  2),
    };

    private static readonly RateWindow[] DefaultFridayWindows =
    {
        new(0,  0, 1),
        new(7,  3, 7),
        new(9,  5,10),
        new(12, 3, 6),
        new(14, 0, 0),    // shutdown — Shabbat eve
    };

    private const int FridayShutdownHour    = 14;
    private const double WeekendVolumeFactor = 0.60;

    // ── Instance data ─────────────────────────────────────────────────────────

    private readonly RateWindow[] _weekdayWindows;
    private readonly RateWindow[] _weekendWindows;
    private readonly RateWindow[] _fridayWindows;

    /// <summary>
    /// Construct using defaults. The schedule.yaml is informational; the C#
    /// defaults are the authoritative values for deterministic simulation.
    /// </summary>
    public DailyScheduleModel()
    {
        _weekdayWindows = DefaultWeekdayWindows;
        _weekendWindows = DefaultWeekendWindows;
        _fridayWindows  = DefaultFridayWindows;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the expected students-per-minute arrival rate at the given
    /// time of day, smoothed using sine interpolation between adjacent windows.
    /// </summary>
    /// <param name="dayKind">Day type (weekday, weekend, or Friday).</param>
    /// <param name="hourOfDay">Fractional hour in [0, 24).</param>
    /// <param name="rng">Random source for sampling within the [min,max] band.</param>
    public double ArrivalRateAt(DayKind dayKind, double hourOfDay, Random rng)
    {
        if (dayKind == DayKind.Friday && hourOfDay >= FridayShutdownHour)
            return 0.0;

        var windows = dayKind switch
        {
            DayKind.Weekend => _weekendWindows,
            DayKind.Friday  => _fridayWindows,
            _               => _weekdayWindows
        };

        var (minRate, maxRate) = InterpolateRate(windows, hourOfDay);

        // Apply weekend volume factor
        if (dayKind == DayKind.Weekend)
        {
            minRate *= WeekendVolumeFactor;
            maxRate *= WeekendVolumeFactor;
        }

        // Sample within the [min, max] band using a sine weight so the rate
        // peaks in the middle of the window rather than jumping step-wise.
        var t       = rng.NextDouble();
        var sinWeight = 0.5 + 0.5 * Math.Sin(t * Math.PI - Math.PI / 2.0); // 0..1 shaped
        return minRate + sinWeight * (maxRate - minRate);
    }

    /// <summary>
    /// Returns the DayKind for a given calendar date (Israel: Friday = Friday, 
    /// Saturday/Sunday = Weekend, else Weekday).
    /// </summary>
    public static DayKind KindFor(DateOnly date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Friday   => DayKind.Friday,
            DayOfWeek.Saturday => DayKind.Weekend,
            DayOfWeek.Sunday   => DayKind.Weekend,
            _                  => DayKind.Weekday
        };
    }

    /// <summary>
    /// Generates the sequence of student arrival offsets (minutes into the
    /// simulation) for a single simulated day, given the total population size.
    /// Returns arrival offsets sorted ascending.
    /// </summary>
    /// <param name="simulationDayIndex">0-based day index (used to derive calendar date).</param>
    /// <param name="simulationStart">Calendar date of day 0.</param>
    /// <param name="totalStudents">Total population size (to compute max concurrency).</param>
    /// <param name="rng">Seeded random source.</param>
    /// <returns>List of arrival offsets in minutes from the start of this day.</returns>
    public List<double> GenerateDayArrivals(
        int      simulationDayIndex,
        DateOnly simulationStart,
        int      totalStudents,
        Random   rng)
    {
        var date    = simulationStart.AddDays(simulationDayIndex);
        var dayKind = KindFor(date);

        var arrivals = new List<double>();

        // Walk through each minute of the 24-hour day
        for (int minute = 0; minute < 1440; minute++)
        {
            var hourFrac = minute / 60.0;
            var rate     = ArrivalRateAt(dayKind, hourFrac, rng);

            if (rate <= 0) continue;

            // Poisson-approximate: expected arrivals this minute = rate
            // Sample number of arrivals using Poisson distribution
            var expected = rate;
            var count    = SamplePoisson(expected, rng);

            for (int k = 0; k < count; k++)
            {
                // Sub-minute jitter so arrivals are not all on the exact minute boundary
                arrivals.Add(minute + rng.NextDouble());
            }
        }

        arrivals.Sort();
        return arrivals;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Linearly interpolate (with sine smoothing) between the two nearest
    /// rate windows for a given fractional hour.
    /// </summary>
    private static (double Min, double Max) InterpolateRate(
        RateWindow[] windows, double hourOfDay)
    {
        if (windows.Length == 0)
            return (0, 0);

        // Find the surrounding pair
        int lo = 0;
        for (int i = 0; i < windows.Length - 1; i++)
        {
            if (windows[i + 1].Hour > hourOfDay) break;
            lo = i + 1;
        }

        var loW = windows[lo];

        if (lo == windows.Length - 1)
            return (loW.RateMin, loW.RateMax);

        var hiW = windows[lo + 1];
        var span = hiW.Hour - loW.Hour;

        if (span <= 0)
            return (loW.RateMin, loW.RateMax);

        // Sine-shaped blend: smoother than linear ramp
        var t      = (hourOfDay - loW.Hour) / span;
        var smooth = 0.5 - 0.5 * Math.Cos(t * Math.PI); // 0..1 smooth S-curve

        var minRate = loW.RateMin + smooth * (hiW.RateMin - loW.RateMin);
        var maxRate = loW.RateMax + smooth * (hiW.RateMax - loW.RateMax);

        return (minRate, maxRate);
    }

    /// <summary>
    /// Draw a sample from a Poisson distribution using Knuth's algorithm.
    /// Suitable for small lambda (< ~20).
    /// </summary>
    private static int SamplePoisson(double lambda, Random rng)
    {
        if (lambda <= 0) return 0;

        // For large lambda fall back to Gaussian approximation to avoid degenerate loops
        if (lambda > 30)
        {
            var gauss = lambda + Math.Sqrt(lambda) * (rng.NextDouble() + rng.NextDouble() - 1.0);
            return (int)Math.Max(0, Math.Round(gauss));
        }

        var L = Math.Exp(-lambda);
        var p = 1.0;
        var k = 0;

        do
        {
            k++;
            p *= rng.NextDouble();
        }
        while (p > L);

        return k - 1;
    }
}
