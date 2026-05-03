// =============================================================================
// Cena Platform — Hint-Adjusted BKT Service
// SAI-02: Wraps BktService to reduce mastery credit when hints were used.
// Credit curve: 0 hints = 1.0, 1 = 0.7, 2 = 0.4, 3 = 0.1
// =============================================================================

namespace Cena.Actors.Services;

public interface IHintAdjustedBktService
{
    BktUpdateResult UpdateWithHints(BktUpdateInput input, int hintCountUsed);
}

public sealed class HintAdjustedBktService : IHintAdjustedBktService
{
    private readonly IBktService _bktService;
    private static readonly double[] CreditMultipliers = [1.0, 0.7, 0.4, 0.1];

    public HintAdjustedBktService(IBktService bktService)
    {
        _bktService = bktService;
    }

    public BktUpdateResult UpdateWithHints(BktUpdateInput input, int hintCountUsed)
    {
        var result = _bktService.Update(input);

        if (hintCountUsed > 0 && input.IsCorrect)
        {
            var multiplier = CreditMultipliers[Math.Min(hintCountUsed, CreditMultipliers.Length - 1)];
            var adjustedPosterior = input.PriorMastery
                + (result.PosteriorMastery - input.PriorMastery) * multiplier;

            return result with { PosteriorMastery = adjustedPosterior };
        }

        return result;
    }
}
