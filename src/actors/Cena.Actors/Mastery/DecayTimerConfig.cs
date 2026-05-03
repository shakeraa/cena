// =============================================================================
// Cena Platform -- Decay Timer Configuration
// MST-007: Configurable decay scan parameters
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Configuration for the decay timer that runs inside the StudentActor.
/// Loaded from IOptions&lt;DecayTimerConfig&gt; for DI.
/// </summary>
public sealed record DecayTimerConfig(
    float ScanIntervalHours = 6f,
    float DecayThreshold = 0.70f,
    float MinMasteryForScan = 0.70f);
