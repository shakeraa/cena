// =============================================================================
// Cena Platform — Theta-to-Mastery Mapper (RDY-023)
//
// Maps IRT theta (ability estimate) to BKT P_Initial (initial mastery).
// Uses the logistic (sigmoid) function so the full IRT scale [-∞, +∞]
// maps smoothly to (0, 1) mastery probability space.
//
// Typical theta range: [-3, +3] for K-12 adaptive learning.
//   theta = -3 → P_Initial ≈ 0.05 (novice)
//   theta =  0 → P_Initial = 0.50 (average)
//   theta = +3 → P_Initial ≈ 0.95 (near-mastery)
// =============================================================================

using System.Runtime.CompilerServices;

namespace Cena.Actors.Services;

public static class ThetaMasteryMapper
{
    // Clamp bounds match BktService (MinP/MaxP)
    private const double MinMastery = 0.01;
    private const double MaxMastery = 0.99;

    /// <summary>
    /// Default P_Initial when the student skips the diagnostic quiz.
    /// Matches <see cref="BktParameters.Default"/>.PInitial.
    /// </summary>
    public const double SkipDefault = 0.10;

    /// <summary>
    /// Map IRT theta (ability) to BKT P_Initial (initial mastery).
    /// Uses the standard logistic function: σ(θ) = 1 / (1 + e^(−θ)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ThetaToPInitial(double theta)
    {
        var raw = 1.0 / (1.0 + Math.Exp(-theta));
        return Math.Clamp(raw, MinMastery, MaxMastery);
    }

    /// <summary>
    /// Map a batch of per-subject theta estimates to per-subject P_Initial values.
    /// </summary>
    public static Dictionary<string, double> MapAll(
        IEnumerable<(string Subject, double Theta)> estimates)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (subject, theta) in estimates)
            result[subject] = ThetaToPInitial(theta);
        return result;
    }
}
