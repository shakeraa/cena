// =============================================================================
// Cena Platform — Rasch Item Calibration Pipeline (IRT-001)
//
// Estimates item difficulty (b) using logit of proportion correct (Rasch model).
// Discrimination (a) is fixed at 1.0 unless an external calibration tool
// (e.g. Python girth, R mirt) supplies a 2PL estimate stored on the item.
//
// True 2PL calibration requires an EM/MML algorithm (Bock & Aitkin 1981)
// and is a future enhancement. Do not label this pipeline as 2PL.
//
// PP-011: Tiered calibration confidence based on response count N.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// PP-011: Calibration confidence tier based on response count N.
/// Baker &amp; Kim (2004): stable Rasch requires N >= 200, 2PL requires N >= 500.
/// </summary>
public enum CalibrationConfidence
{
    /// <summary>N &lt; 30: use Rasch defaults, flag as uncalibrated.</summary>
    Default,

    /// <summary>30 &lt;= N &lt; 100: logit-based Rasch estimate, wide standard error.</summary>
    LowConfidence,

    /// <summary>100 &lt;= N &lt; 200: stable Rasch estimate.</summary>
    Moderate,

    /// <summary>N >= 200: stable Rasch, eligible for 2PL if external tool provides.</summary>
    High,

    /// <summary>N >= 500: full confidence, external 2PL estimates trusted.</summary>
    Production
}

/// <summary>
/// IRT item parameters for a single question.
/// </summary>
public record IrtItemParameters(
    string QuestionId,
    double Difficulty,
    /// <summary>
    /// Default 1.0 (Rasch). External calibration tools (e.g. Python girth, R mirt)
    /// can supply 2PL estimates that are stored here. This pipeline does NOT
    /// estimate discrimination — it only preserves externally-supplied values.
    /// </summary>
    double Discrimination,
    double GuessParameter,
    int ResponseCount,
    double FitResidual,
    CalibrationConfidence Confidence,
    DateTimeOffset CalibratedAt
)
{
    /// <summary>Rasch model: discrimination fixed at 1.0, no guessing, uncalibrated.</summary>
    public static IrtItemParameters RaschDefault(string questionId) =>
        new(questionId, 0.0, 1.0, 0.0, 0, double.NaN, CalibrationConfidence.Default, DateTimeOffset.UtcNow);

    /// <summary>PP-011: Compute confidence tier from response count.</summary>
    public static CalibrationConfidence ConfidenceFromN(int n) => n switch
    {
        >= 500 => CalibrationConfidence.Production,
        >= 200 => CalibrationConfidence.High,
        >= 100 => CalibrationConfidence.Moderate,
        >= 30 => CalibrationConfidence.LowConfidence,
        _ => CalibrationConfidence.Default
    };
}

/// <summary>
/// Student ability estimate from IRT.
/// </summary>
public record IrtAbilityEstimate(
    string StudentId,
    string TrackId,
    double Theta,
    double StandardError,
    int ItemsAnswered,
    DateTimeOffset EstimatedAt
)
{
    /// <summary>95% confidence interval.</summary>
    public (double Lower, double Upper) ConfidenceInterval95 =>
        (Theta - 1.96 * StandardError, Theta + 1.96 * StandardError);

    /// <summary>Pass probability given exam difficulty.</summary>
    public double PassProbability(double examDifficulty) =>
        1.0 / (1.0 + Math.Exp(-(Theta - examDifficulty)));
}

/// <summary>
/// A single student response for IRT calibration.
/// </summary>
public record IrtResponse(
    string StudentId,
    string QuestionId,
    bool IsCorrect,
    double? ResponseTimeSeconds
);

public interface IIrtCalibrationPipeline
{
    /// <summary>
    /// Run Rasch calibration on a batch of responses.
    /// Assigns tiered confidence based on response count N.
    /// Returns updated item parameters.
    /// </summary>
    IReadOnlyList<IrtItemParameters> Calibrate(
        IReadOnlyList<IrtResponse> responses,
        IReadOnlyList<IrtItemParameters>? priors = null);

    /// <summary>
    /// Estimate a student's ability (theta) given their response pattern and item parameters.
    /// Uses maximum likelihood estimation.
    /// </summary>
    IrtAbilityEstimate EstimateAbility(
        string studentId,
        string trackId,
        IReadOnlyList<IrtResponse> responses,
        IReadOnlyList<IrtItemParameters> itemParams);
}

/// <summary>
/// IRT calibration using logit-based Rasch estimation with tiered confidence (PP-011).
/// Discrimination is fixed at 1.0 unless externally supplied.
/// </summary>
public sealed class IrtCalibrationPipeline : IIrtCalibrationPipeline
{
    private readonly ILogger<IrtCalibrationPipeline> _logger;
    private const int MaxIterations = 100;
    private const double ConvergenceThreshold = 0.001;

    private static readonly Meter Meter = new("Cena.Irt", "1.0");
    private static readonly Counter<long> CalibrationsRun = Meter.CreateCounter<long>(
        "cena.irt.calibrations.total");

    public IrtCalibrationPipeline(ILogger<IrtCalibrationPipeline> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IrtItemParameters> Calibrate(
        IReadOnlyList<IrtResponse> responses,
        IReadOnlyList<IrtItemParameters>? priors = null)
    {
        CalibrationsRun.Add(1);

        // Group responses by question
        var byQuestion = responses.GroupBy(r => r.QuestionId).ToList();
        var priorMap = priors?.ToDictionary(p => p.QuestionId) ?? new();

        var results = new List<IrtItemParameters>();

        foreach (var group in byQuestion)
        {
            var questionId = group.Key;
            var questionResponses = group.ToList();
            var n = questionResponses.Count;

            var confidence = IrtItemParameters.ConfidenceFromN(n);

            if (confidence == CalibrationConfidence.Default)
            {
                // PP-011: N < 30 — use Rasch defaults, flag as uncalibrated
                results.Add(priorMap.TryGetValue(questionId, out var prior)
                    ? prior with { ResponseCount = n, Confidence = CalibrationConfidence.Default }
                    : IrtItemParameters.RaschDefault(questionId) with { ResponseCount = n });
                continue;
            }

            // Rasch calibration: difficulty = logit of proportion correct
            var pCorrect = questionResponses.Count(r => r.IsCorrect) / (double)n;
            pCorrect = Math.Clamp(pCorrect, 0.01, 0.99); // prevent log(0)

            var difficulty = -Math.Log(pCorrect / (1.0 - pCorrect)); // logit transform

            // Discrimination fixed at 1.0 (Rasch). Externally-supplied 2PL
            // estimates are preserved if already stored on the item.
            var discrimination = 1.0;
            if (priorMap.TryGetValue(questionId, out var existing) && existing.Discrimination != 1.0)
                discrimination = existing.Discrimination;

            var fitResidual = ComputeFitResidual(questionResponses, difficulty, discrimination);

            results.Add(new IrtItemParameters(
                questionId, difficulty, discrimination, 0.0, n, fitResidual, confidence,
                DateTimeOffset.UtcNow));
        }

        _logger.LogInformation("IRT calibration complete: {Count} items, {Responses} responses",
            results.Count, responses.Count);

        return results;
    }

    public IrtAbilityEstimate EstimateAbility(
        string studentId,
        string trackId,
        IReadOnlyList<IrtResponse> responses,
        IReadOnlyList<IrtItemParameters> itemParams)
    {
        var paramMap = itemParams.ToDictionary(p => p.QuestionId);

        // Maximum likelihood estimation of theta via Newton-Raphson
        double theta = 0.0; // start at average ability

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            double firstDeriv = 0;
            double secondDeriv = 0;

            foreach (var response in responses)
            {
                if (!paramMap.TryGetValue(response.QuestionId, out var item)) continue;

                var p = Probability(theta, item);
                var u = response.IsCorrect ? 1.0 : 0.0;

                firstDeriv += item.Discrimination * (u - p);
                secondDeriv -= item.Discrimination * item.Discrimination * p * (1 - p);
            }

            if (Math.Abs(secondDeriv) < 1e-10) break;

            var delta = firstDeriv / secondDeriv;
            theta -= delta;

            if (Math.Abs(delta) < ConvergenceThreshold) break;
        }

        // Standard error from Fisher information
        double information = 0;
        foreach (var response in responses)
        {
            if (!paramMap.TryGetValue(response.QuestionId, out var item)) continue;
            var p = Probability(theta, item);
            information += item.Discrimination * item.Discrimination * p * (1 - p);
        }

        var se = information > 0 ? 1.0 / Math.Sqrt(information) : 10.0;

        return new IrtAbilityEstimate(studentId, trackId, theta, se, responses.Count, DateTimeOffset.UtcNow);
    }

    private static double Probability(double theta, IrtItemParameters item) =>
        item.GuessParameter + (1 - item.GuessParameter) /
        (1 + Math.Exp(-item.Discrimination * (theta - item.Difficulty)));

    private static double ComputeFitResidual(
        IReadOnlyList<IrtResponse> responses, double difficulty, double discrimination)
    {
        double residual = 0;
        foreach (var r in responses)
        {
            var expected = 1.0 / (1 + Math.Exp(-discrimination * (0 - difficulty))); // at average ability
            var observed = r.IsCorrect ? 1.0 : 0.0;
            residual += (observed - expected) * (observed - expected);
        }
        return residual / responses.Count;
    }
}
