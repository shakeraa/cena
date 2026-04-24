// =============================================================================
// Cena Platform -- HLR Weight Provider Interface
// MST-006: DI interface for loading HLR weights (per concept category)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Provides HLR weights. At launch returns hand-tuned defaults;
/// at scale, returns weights trained offline by MST-016.
/// </summary>
public interface IHlrWeightProvider
{
    HlrWeights GetWeights(string? conceptCategory = null);
}

/// <summary>
/// Default provider returning HlrWeights.Default for all categories.
/// </summary>
public sealed class DefaultHlrWeightProvider : IHlrWeightProvider
{
    public HlrWeights GetWeights(string? conceptCategory = null) => HlrWeights.Default;
}
