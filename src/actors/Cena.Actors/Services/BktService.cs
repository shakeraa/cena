// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Bayesian Knowledge Tracing (BKT) Service
// Layer: Domain Services | Runtime: .NET 9
// Reference: Corbett & Anderson (1994) — Knowledge Tracing Model
// ═══════════════════════════════════════════════════════════════════════

using System.Runtime.CompilerServices;

namespace Cena.Actors.Services;

/// <summary>
/// BKT parameters for a single concept.
/// All probabilities must be in (0, 1).
/// </summary>
public readonly record struct BktParameters(
    double PLearning,
    double PSlip,
    double PGuess,
    double PForget,
    double PInitial,
    double ProgressionThreshold,
    double PrerequisiteGateThreshold
)
{
    /// <summary>
    /// Default BKT parameters calibrated for K-12 adaptive math.
    /// PForget=0.02 reflects slow forgetting during active learning;
    /// spaced repetition (HLR) handles longer-term decay separately.
    /// </summary>
    public static BktParameters Default => new(
        PLearning: 0.10,
        PSlip: 0.05,
        PGuess: 0.20,
        PForget: 0.02,
        PInitial: 0.10,
        ProgressionThreshold: MasteryConstants.ProgressionThreshold,
        PrerequisiteGateThreshold: MasteryConstants.PrerequisiteGateThreshold
    );
}

/// <summary>
/// Input for a single BKT update step.
/// </summary>
public readonly record struct BktUpdateInput(
    double PriorMastery,
    bool IsCorrect,
    BktParameters Parameters
);

/// <summary>
/// Result of a single BKT update step.
/// </summary>
public readonly record struct BktUpdateResult(
    double PosteriorMastery,
    bool CrossedProgressionThreshold,
    bool MeetsPrerequisiteGate
);

public interface IBktService
{
    /// <summary>
    /// Perform a single BKT update given an observation.
    /// Allocation-free on the hot path.
    /// </summary>
    BktUpdateResult Update(in BktUpdateInput input);

    /// <summary>
    /// Compute the probability of a correct response given current mastery.
    /// P(correct) = P(L) * (1 - P(S)) + (1 - P(L)) * P(G)
    /// </summary>
    double ProbCorrect(double pMastery, in BktParameters parameters);
}

/// <summary>
/// Production BKT implementation using the Corbett &amp; Anderson (1994) model.
///
/// Update equations:
///   If correct:  P(L_n | obs) = P(L_n-1) * (1 - P(S)) / P(correct)
///   If incorrect: P(L_n | obs) = P(L_n-1) * P(S) / P(incorrect)
///   Then: P(L_n) = P(L_n | obs) + (1 - P(L_n | obs)) * P(T)
///
/// With forgetting extension:
///   P(L_n) adjusted by: P(L_n) * (1 - P(F))
/// </summary>
public sealed class BktService : IBktService
{
    // Clamp bounds to prevent degenerate probabilities (division by zero, log(0), etc.)
    private const double MinP = 0.01;
    private const double MaxP = 0.99;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BktUpdateResult Update(in BktUpdateInput input)
    {
        double prior = Clamp(input.PriorMastery);
        double pSlip = input.Parameters.PSlip;
        double pGuess = input.Parameters.PGuess;
        double pLearn = input.Parameters.PLearning;
        double pForget = input.Parameters.PForget;

        // Step 1: Compute P(correct) and P(incorrect) from current mastery
        double pCorrect = prior * (1.0 - pSlip) + (1.0 - prior) * pGuess;
        double pIncorrect = 1.0 - pCorrect;

        // Guard against division by zero — if P(correct) or P(incorrect) is
        // near zero, the observation is maximally informative: clamp.
        pCorrect = ClampDenominator(pCorrect);
        pIncorrect = ClampDenominator(pIncorrect);

        // Step 2: Posterior given observation (Bayes update)
        double pLearned;
        if (input.IsCorrect)
        {
            // P(L | correct) = P(L) * P(correct | L) / P(correct)
            //                = P(L) * (1 - P(S)) / P(correct)
            pLearned = prior * (1.0 - pSlip) / pCorrect;
        }
        else
        {
            // P(L | incorrect) = P(L) * P(incorrect | L) / P(incorrect)
            //                  = P(L) * P(S) / P(incorrect)
            pLearned = prior * pSlip / pIncorrect;
        }

        // Step 3: Account for learning transition
        // P(L_n) = P(L_n | obs) + (1 - P(L_n | obs)) * P(T)
        double posterior = pLearned + (1.0 - pLearned) * pLearn;

        // Step 4: Apply forgetting factor (INTENTIONAL DEVIATION from standard Corbett & Anderson)
        //
        // The standard BKT model does NOT include forgetting within the trial update.
        // Long-term decay is HLR's responsibility. However, we apply a small within-session
        // micro-forgetting factor (pForget=0.02 default) to prevent mastery from ratcheting up
        // too aggressively on lucky-guess streaks.
        //
        // IMPACT: With pForget=0.02, students need ~6 more correct answers to reach 0.85
        // mastery compared to the standard model. See ACT-024 for domain review.
        posterior = posterior * (1.0 - pForget);

        // Step 5: Clamp to valid range
        posterior = Clamp(posterior);

        return new BktUpdateResult(
            PosteriorMastery: posterior,
            CrossedProgressionThreshold: posterior >= input.Parameters.ProgressionThreshold,
            MeetsPrerequisiteGate: posterior >= input.Parameters.PrerequisiteGateThreshold
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ProbCorrect(double pMastery, in BktParameters parameters)
    {
        double p = Clamp(pMastery);
        return p * (1.0 - parameters.PSlip) + (1.0 - p) * parameters.PGuess;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp(double value)
    {
        if (value < MinP) return MinP;
        if (value > MaxP) return MaxP;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ClampDenominator(double value)
    {
        // Prevent division by zero: denominator must be at least MinP
        if (value < MinP) return MinP;
        return value;
    }
}
