// =============================================================================
// Cena Platform -- BKT Parameter Provider Interface
// MST-006: DI interface for loading per-KC BKT parameters
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Provides BKT parameters per knowledge component. At launch returns defaults;
/// at scale, returns parameters trained offline by MST-015.
/// </summary>
public interface IBktParameterProvider
{
    BktParameters GetParameters(string conceptId);
}

/// <summary>
/// Default provider returning BktParameters.Default for all concepts.
/// </summary>
public sealed class DefaultBktParameterProvider : IBktParameterProvider
{
    public BktParameters GetParameters(string conceptId) => BktParameters.Default;
}
