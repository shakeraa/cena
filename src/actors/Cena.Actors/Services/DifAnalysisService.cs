// =============================================================================
// Cena Platform — DIF Analysis Service (RDY-007)
// Mantel-Haenszel Differential Item Functioning detection.
//
// Stratifies item responses by student locale (he vs. ar) and computes
// MH chi-square and D-DIF for each item. Categorizes items as:
//   A (negligible, |D-DIF| <= 1.0)
//   B (moderate,   1.0 < |D-DIF| <= 1.5)
//   C (large,      |D-DIF| > 1.5) — flagged for review, deprioritized in CAT
//
// Reference: Holland & Thayer (1988), Mantel & Haenszel (1959)
// =============================================================================

namespace Cena.Actors.Services;

// ═══════════════════════════════════════════════════════════════════════════
// RESULT TYPES
// ═══════════════════════════════════════════════════════════════════════════

public enum DifCategory
{
    /// <summary>|D-DIF| <= 1.0: negligible DIF, item is fair.</summary>
    A,

    /// <summary>1.0 < |D-DIF| <= 1.5: moderate DIF, monitor.</summary>
    B,

    /// <summary>|D-DIF| > 1.5: large DIF, item may be biased — flag for review.</summary>
    C,

    /// <summary>Insufficient data (< MinResponsesPerGroup per group).</summary>
    Pending
}

/// <summary>
/// DIF analysis result for a single item.
/// </summary>
public sealed record DifAnalysisResult(
    string QuestionId,
    DifCategory Category,
    double MhChiSquare,
    double DeltaDif,
    string ReferenceGroup,
    string FocalGroup,
    int ResponseCountReference,
    int ResponseCountFocal,
    DateTimeOffset AnalyzedAt)
{
    public bool IsFlagged => Category == DifCategory.C;
}

/// <summary>
/// A single response record for DIF analysis, grouped by ability stratum.
/// </summary>
public sealed record DifResponseRecord(
    string StudentId,
    string QuestionId,
    bool IsCorrect,
    string Locale,
    int AbilityStratum);

/// <summary>
/// Summary of DIF analysis across the item bank.
/// </summary>
public sealed record DifSummary(
    int TotalItemsAnalyzed,
    int CategoryA,
    int CategoryB,
    int CategoryC,
    int Pending,
    IReadOnlyList<DifAnalysisResult> FlaggedItems,
    DateTimeOffset AnalyzedAt);

// ═══════════════════════════════════════════════════════════════════════════
// SERVICE
// ═══════════════════════════════════════════════════════════════════════════

public interface IDifAnalysisService
{
    /// <summary>
    /// Compute MH DIF for a single item given stratified response data.
    /// </summary>
    DifAnalysisResult AnalyzeItem(
        string questionId,
        IReadOnlyList<DifResponseRecord> responses,
        string referenceGroup = "he",
        string focalGroup = "ar");

    /// <summary>
    /// Compute MH DIF for all items in the response dataset.
    /// </summary>
    DifSummary AnalyzeAll(
        IReadOnlyList<DifResponseRecord> responses,
        string referenceGroup = "he",
        string focalGroup = "ar");
}

public sealed class DifAnalysisService : IDifAnalysisService
{
    /// <summary>Minimum responses per group per item for DIF analysis.</summary>
    public const int MinResponsesPerGroup = 100;

    /// <summary>Number of ability strata for MH stratification.</summary>
    public const int DefaultStratumCount = 5;

    /// <summary>|D-DIF| threshold for Category B (moderate).</summary>
    public const double ModerateThreshold = 1.0;

    /// <summary>|D-DIF| threshold for Category C (large).</summary>
    public const double LargeThreshold = 1.5;

    public DifAnalysisResult AnalyzeItem(
        string questionId,
        IReadOnlyList<DifResponseRecord> responses,
        string referenceGroup = "he",
        string focalGroup = "ar")
    {
        var itemResponses = responses.Where(r => r.QuestionId == questionId).ToList();

        var refResponses = itemResponses.Where(r => r.Locale == referenceGroup).ToList();
        var focalResponses = itemResponses.Where(r => r.Locale == focalGroup).ToList();

        if (refResponses.Count < MinResponsesPerGroup || focalResponses.Count < MinResponsesPerGroup)
        {
            return new DifAnalysisResult(
                questionId, DifCategory.Pending, 0.0, 0.0,
                referenceGroup, focalGroup,
                refResponses.Count, focalResponses.Count,
                DateTimeOffset.UtcNow);
        }

        // Group by ability stratum and compute MH statistic
        var strata = itemResponses
            .GroupBy(r => r.AbilityStratum)
            .Where(g => g.Any(r => r.Locale == referenceGroup) && g.Any(r => r.Locale == focalGroup))
            .ToList();

        if (strata.Count == 0)
        {
            return new DifAnalysisResult(
                questionId, DifCategory.Pending, 0.0, 0.0,
                referenceGroup, focalGroup,
                refResponses.Count, focalResponses.Count,
                DateTimeOffset.UtcNow);
        }

        // Mantel-Haenszel computation
        double sumAlpha = 0.0;
        double sumA = 0.0;
        double sumExpectedA = 0.0;
        double sumVarianceA = 0.0;

        foreach (var stratum in strata)
        {
            var refs = stratum.Where(r => r.Locale == referenceGroup).ToList();
            var focals = stratum.Where(r => r.Locale == focalGroup).ToList();

            int nRef = refs.Count;
            int nFocal = focals.Count;
            int nTotal = nRef + nFocal;
            if (nTotal == 0) continue;

            int correctRef = refs.Count(r => r.IsCorrect);
            int correctFocal = focals.Count(r => r.IsCorrect);
            int totalCorrect = correctRef + correctFocal;
            int totalIncorrect = nTotal - totalCorrect;

            // 2x2 table per stratum:
            //              Correct    Incorrect    Total
            // Reference    A(=cRef)   B            nRef
            // Focal        C(=cFoc)   D            nFocal
            // Total        totalCorr  totalIncorr  nTotal

            double a = correctRef;
            double expectedA = (double)(nRef * totalCorrect) / nTotal;
            double varianceA = (double)(nRef * nFocal * totalCorrect * totalIncorrect)
                               / (nTotal * nTotal * (nTotal - 1.0));

            // Alpha for this stratum: (A * D) / (B * C) approximation
            double d = nRef - correctRef;  // B = incorrect in reference
            double c = correctFocal;
            double b = nFocal - correctFocal;  // D = incorrect in focal

            if (nTotal > 0)
            {
                sumAlpha += (a * b) / nTotal;  // numerator
                double denom = (c * d) / nTotal;
                if (denom > 0) sumAlpha += 0; // track separately
            }

            sumA += a;
            sumExpectedA += expectedA;
            sumVarianceA += varianceA;
        }

        // MH chi-square statistic (continuity-corrected)
        double mhChiSquare = 0.0;
        if (sumVarianceA > 0)
        {
            double absDeviation = Math.Abs(sumA - sumExpectedA) - 0.5; // continuity correction
            if (absDeviation < 0) absDeviation = 0;
            mhChiSquare = (absDeviation * absDeviation) / sumVarianceA;
        }

        // MH common odds ratio (alpha_MH)
        double numerator = 0.0;
        double denominator = 0.0;
        foreach (var stratum in strata)
        {
            var refs = stratum.Where(r => r.Locale == referenceGroup).ToList();
            var focals = stratum.Where(r => r.Locale == focalGroup).ToList();

            int nTotal = refs.Count + focals.Count;
            if (nTotal == 0) continue;

            int correctRef = refs.Count(r => r.IsCorrect);
            int incorrectFocal = focals.Count(r => !r.IsCorrect);
            int correctFocal = focals.Count(r => r.IsCorrect);
            int incorrectRef = refs.Count(r => !r.IsCorrect);

            numerator += (double)(correctRef * incorrectFocal) / nTotal;
            denominator += (double)(correctFocal * incorrectRef) / nTotal;
        }

        double alphaMh = denominator > 0 ? numerator / denominator : 1.0;

        // Delta DIF: D-DIF = -2.35 * ln(alpha_MH)
        // Negative means item favors focal group; positive means item favors reference group
        double deltaDif = alphaMh > 0 ? -2.35 * Math.Log(alphaMh) : 0.0;

        // Categorize
        var absDif = Math.Abs(deltaDif);
        var category = absDif switch
        {
            > LargeThreshold => DifCategory.C,
            > ModerateThreshold => DifCategory.B,
            _ => DifCategory.A
        };

        return new DifAnalysisResult(
            questionId, category, mhChiSquare, deltaDif,
            referenceGroup, focalGroup,
            refResponses.Count, focalResponses.Count,
            DateTimeOffset.UtcNow);
    }

    public DifSummary AnalyzeAll(
        IReadOnlyList<DifResponseRecord> responses,
        string referenceGroup = "he",
        string focalGroup = "ar")
    {
        var questionIds = responses.Select(r => r.QuestionId).Distinct().ToList();
        var results = new List<DifAnalysisResult>();

        foreach (var qid in questionIds)
        {
            results.Add(AnalyzeItem(qid, responses, referenceGroup, focalGroup));
        }

        return new DifSummary(
            TotalItemsAnalyzed: results.Count,
            CategoryA: results.Count(r => r.Category == DifCategory.A),
            CategoryB: results.Count(r => r.Category == DifCategory.B),
            CategoryC: results.Count(r => r.Category == DifCategory.C),
            Pending: results.Count(r => r.Category == DifCategory.Pending),
            FlaggedItems: results.Where(r => r.IsFlagged).ToList(),
            AnalyzedAt: DateTimeOffset.UtcNow);
    }
}
