// =============================================================================
// Cena Platform — Exam Schedule Service (SEC-ASSESS-004)
// Manages official exam periods and enforces restrictions during exam hours.
// =============================================================================

namespace Cena.Actors.Assessment;

/// <summary>
/// Tracks official Bagrut exam schedule dates/times per institute.
/// During active exam periods: rate-limits uploads, flags suspicious activity,
/// disables "show answer" paths.
/// </summary>
public sealed class ExamScheduleService
{
    private readonly List<ExamPeriod> _periods = new();

    /// <summary>
    /// Configures an exam period. Called by admin when MoE publishes schedule.
    /// </summary>
    public void ConfigurePeriod(ExamPeriod period)
    {
        _periods.RemoveAll(p => p.Id == period.Id);
        _periods.Add(period);
    }

    /// <summary>
    /// Checks if any exam is currently active for the given institute.
    /// </summary>
    public ExamPeriod? GetActiveExamPeriod(string instituteId, DateTimeOffset now)
    {
        return _periods.FirstOrDefault(p =>
            (p.InstituteId == instituteId || p.InstituteId == "*") &&
            now >= p.StartTime && now <= p.EndTime);
    }

    /// <summary>
    /// Checks if an upload should be flagged during exam hours.
    /// </summary>
    public bool ShouldFlagUpload(string instituteId, DateTimeOffset now)
    {
        return GetActiveExamPeriod(instituteId, now) != null;
    }

    public IReadOnlyList<ExamPeriod> GetUpcomingPeriods(string instituteId, DateTimeOffset now)
    {
        return _periods
            .Where(p => (p.InstituteId == instituteId || p.InstituteId == "*") && p.EndTime > now)
            .OrderBy(p => p.StartTime)
            .ToList();
    }
}

/// <summary>
/// An official exam period with time boundaries and restriction settings.
/// </summary>
public sealed record ExamPeriod
{
    public string Id { get; init; } = "";
    public string InstituteId { get; init; } = "";
    public string ExamCode { get; init; } = "";
    public string ExamName { get; init; } = "";
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }

    /// <summary>Drop upload rate limit to this value during exam (default: 0 = blocked).</summary>
    public int UploadRateLimitPerHour { get; init; } = 0;

    /// <summary>Disable "show me the answer" paths during this period.</summary>
    public bool DisableShowAnswer { get; init; } = true;

    /// <summary>Force step-solver-only mode (no full-answer reveal).</summary>
    public bool StepSolverOnlyMode { get; init; } = true;

    public bool IsActive(DateTimeOffset now) => now >= StartTime && now <= EndTime;
}

/// <summary>
/// Checks uploaded photos against known exam papers using perceptual hashing.
/// Flags matches for human review — does NOT block (could be legitimate practice).
/// </summary>
public sealed class HomeworkSimilarityChecker
{
    /// <summary>
    /// Compares an upload's hash against known exam paper hashes.
    /// Returns similarity score 0.0-1.0.
    /// </summary>
    public SimilarityResult CheckSimilarity(byte[] uploadHash, string examCode)
    {
        // Placeholder — actual implementation uses perceptual hash (pHash)
        // comparison against a stored registry of known exam paper hashes.
        return new SimilarityResult(0.0, false, null);
    }
}

public sealed record SimilarityResult(
    double Score,
    bool IsFlagged,
    string? MatchedExamPaperId);
