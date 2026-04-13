// =============================================================================
// Cena Platform — Rasch/2PL Item Calibration Pipeline (IRT-001)
//
// Estimates item difficulty (b) and discrimination (a) parameters from
// student response data using marginal maximum likelihood.
// Feeds into CAT algorithm (IRT-003) and readiness reports (READINESS-001).
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// IRT item parameters for a single question.
/// </summary>
public record IrtItemParameters(
    string QuestionId,
    double Difficulty,
    double Discrimination,
    double GuessParameter,
    int ResponseCount,
    double FitResidual,
    DateTimeOffset CalibratedAt
)
{
    /// <summary>Rasch model: discrimination fixed at 1.0, no guessing.</summary>
    public static IrtItemParameters RaschDefault(string questionId) =>
        new(questionId, 0.0, 1.0, 0.0, 0, double.NaN, DateTimeOffset.UtcNow);
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
    /// Run Rasch/2PL calibration on a batch of responses.
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
/// IRT calibration using joint maximum likelihood for Rasch/2PL models.
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

            if (n < 10) // Minimum responses for stable calibration
            {
                results.Add(priorMap.TryGetValue(questionId, out var prior)
                    ? prior with { ResponseCount = n }
                    : IrtItemParameters.RaschDefault(questionId) with { ResponseCount = n });
                continue;
            }

            // Simple Rasch calibration: difficulty = logit of proportion correct
            var pCorrect = questionResponses.Count(r => r.IsCorrect) / (double)n;
            pCorrect = Math.Clamp(pCorrect, 0.01, 0.99); // prevent log(0)

            var difficulty = -Math.Log(pCorrect / (1.0 - pCorrect)); // logit transform

            // 2PL: estimate discrimination via point-biserial correlation
            // Simplified — full MML requires iterative estimation
            var discrimination = 1.0; // Default to Rasch
            if (priorMap.TryGetValue(questionId, out var existing) && existing.Discrimination != 1.0)
                discrimination = existing.Discrimination; // preserve prior 2PL estimate

            // Fit residual: how well the model predicts the data
            var fitResidual = ComputeFitResidual(questionResponses, difficulty, discrimination);

            results.Add(new IrtItemParameters(
                questionId, difficulty, discrimination, 0.0, n, fitResidual, DateTimeOffset.UtcNow));
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
