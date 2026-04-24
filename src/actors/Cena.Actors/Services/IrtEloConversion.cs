// =============================================================================
// Cena Platform — IRT ↔ Elo Conversion (RDY-028)
//
// Single source of truth for converting between IRT difficulty (logit scale)
// and Elo ratings. Used by item selection, diagnostic endpoints, and Bagrut
// anchor calibration.
//
// Convention: Elo 1500 = IRT b=0 (average difficulty).
//             200 Elo points = 1 logit.
// =============================================================================

using System.Runtime.CompilerServices;

namespace Cena.Actors.Services;

public static class IrtEloConversion
{
    public const double EloCenter = 1500.0;
    public const double EloPerLogit = 200.0;

    /// <summary>
    /// Convert Elo rating to IRT difficulty (logit scale).
    /// Elo 1500 → b=0, Elo 1700 → b=1.0, Elo 1300 → b=-1.0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double EloToIrt(double elo) => (elo - EloCenter) / EloPerLogit;

    /// <summary>
    /// Convert IRT difficulty (logit) to Elo rating.
    /// b=0 → 1500, b=1.0 → 1700, b=-1.0 → 1300.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double IrtToElo(double difficulty) => difficulty * EloPerLogit + EloCenter;

    /// <summary>
    /// Assign difficulty band from IRT difficulty (logit).
    /// Thresholds from Bagrut anchor distribution (config/bagrut-anchors.json).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string DifficultyBand(double irtDifficulty,
        double easyMax = -0.75, double hardMin = 0.50) =>
        irtDifficulty < easyMax ? "easy" : irtDifficulty > hardMin ? "hard" : "medium";
}
