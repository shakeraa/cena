// =============================================================================
// Cena Platform — BagrutPaperRunPool (PRR-291 cohort fairness)
//
// Real-Bagrut fairness invariant: when 100 students take 806/035582 between
// 14:00 and 17:00, they MUST all draw from the same shuffled question pool —
// not 100 independent shuffles. Otherwise some cohorts get the easy item-mix
// and others get the hard item-mix and the result-page bands are not
// comparable.
//
// Mechanism:
//   - Time bucket: 3-hour windows aligned to UTC midnight (00, 03, 06, 09,
//     12, 15, 18, 21). Real Ministry exams run ~3.5h; one window covers a
//     single Ministry sitting. The shorter side errs on the side of MORE
//     fairness: a 4-hour stretch will land in two windows occasionally,
//     and 8 buckets/day is fine for SIEM queries.
//   - Document key: `{paperCode}|{windowStartUtc:yyyy-MM-ddTHH}` — stable
//     across the whole window so concurrent starts converge on the same row.
//   - First start in (paperCode, window) seeds the pool: deterministic
//     RNG seed = stable hash of `paperCode|windowStart`, so even the FIRST
//     starter doesn't see "their" shuffle — all cohort members converge on
//     the same draw because the seed is independent of the individual
//     student.
//   - Subsequent starts in the window load the row and use its frozen
//     PartA/PartB lists verbatim — no re-draw.
//
// Coordinate with PRR-260 (variant cohort single-flight, kimi-coder) — same
// design problem at the variant-generation layer.
//
// NOT applied when request.PaperCode is null/empty: a self-pay practice
// run with no specific Ministry paper opts out of cohort fairness because
// the student isn't claiming a Ministry-fairness invariant. Per-student
// shuffle stays the default for those flows.
// =============================================================================

namespace Cena.Actors.Assessment;

/// <summary>
/// Frozen pool for one (paperCode, exam-window) cohort. Persisted as a
/// Marten document so concurrent starts within the same window converge.
/// </summary>
public sealed class BagrutPaperRunPool
{
    /// <summary>Composite identity: <c>{paperCode}|{windowStartUtc:yyyy-MM-ddTHH}</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Ministry שאלון code (e.g. "035582"). NEVER empty here —
    /// cohort fairness only applies when the caller supplied a paperCode.</summary>
    public string PaperCode { get; set; } = "";

    /// <summary>Start of the 3-hour exam-window bucket (UTC, top of hour).</summary>
    public DateTimeOffset WindowStart { get; set; }

    /// <summary>Shuffled Part-A question ids for the cohort. Subsequent
    /// starts use this verbatim — no re-shuffle.</summary>
    public List<string> PartAQuestionIds { get; set; } = new();

    /// <summary>Shuffled Part-B question ids for the cohort.</summary>
    public List<string> PartBQuestionIds { get; set; } = new();

    /// <summary>Variant seed the first starter computed from the cohort's
    /// deterministic RNG. Stored so subsequent starts get the same value.</summary>
    public int VariantSeed { get; set; }

    /// <summary>Server-time when this row was created (the first start in
    /// the window). Useful for forensics ("which student seeded the
    /// cohort?" — the answer is "the first one whose request landed at
    /// SeededAt").</summary>
    public DateTimeOffset SeededAt { get; set; }

    /// <summary>Composite-key formatter. Public for tests + dashboards.</summary>
    public static string ComposeId(string paperCode, DateTimeOffset windowStart) =>
        $"{paperCode}|{windowStart:yyyy-MM-ddTHH}";

    /// <summary>
    /// Bucket <paramref name="now"/> into a 3-hour window aligned to UTC
    /// midnight. Ministry exams run ~3.5h; this is the smallest stable
    /// bucket size that captures a single sitting.
    /// </summary>
    public const int WindowHours = 3;

    /// <summary>
    /// Compute the window-start (UTC midnight + N×3h) that contains
    /// <paramref name="now"/>. Pure function.
    /// </summary>
    public static DateTimeOffset ComputeWindowStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var hourBucket = (utc.Hour / WindowHours) * WindowHours;
        return new DateTimeOffset(
            utc.Year, utc.Month, utc.Day, hourBucket, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Stable RNG seed for the cohort. The seed is independent of the
    /// student so all cohort members converge on identical shuffles.
    /// We use FNV-1a 32-bit (deterministic, no implicit-encoding gotchas
    /// the way string.GetHashCode() does in .NET — randomized per process
    /// since .NET 5).
    /// </summary>
    public static int ComputeCohortSeed(string paperCode, DateTimeOffset windowStart)
    {
        var input = ComposeId(paperCode, windowStart);
        unchecked
        {
            const uint fnvOffsetBasis = 2166136261u;
            const uint fnvPrime = 16777619u;
            uint hash = fnvOffsetBasis;
            foreach (var c in input)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return (int)hash;
        }
    }
}
