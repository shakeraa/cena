// =============================================================================
// Cena Platform -- Mastery Engine Enums and Constants
// MST-001: Core enums used across all mastery computations
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Quality quadrant classifying mastery by speed × accuracy.
/// Fast+Correct = Mastered, Slow+Correct = Effortful,
/// Fast+Wrong = Careless, Slow+Wrong = Struggling.
/// </summary>
public enum MasteryQuality
{
    Mastered,
    Effortful,
    Careless,
    Struggling
}

/// <summary>
/// Classification of errors for pedagogical response routing.
/// </summary>
public enum ErrorType
{
    None,
    Procedural,
    Conceptual,
    Motivational,
    Careless,
    Systematic,
    Transfer
}

/// <summary>
/// Mastery level thresholds for visualization and action routing.
/// </summary>
public enum MasteryLevel
{
    NotStarted,
    Introduced,
    Developing,
    Proficient,
    Mastered
}

/// <summary>
/// Threshold crossing events emitted when effective mastery passes a boundary.
/// </summary>
public enum MasteryThresholdEvent
{
    ConceptMastered,
    MasteryDecayed,
    PrerequisiteBlocked
}

/// <summary>
/// Constants for mastery threshold boundaries.
/// </summary>
public static class MasteryThreshold
{
    public const float NotStarted = 0.10f;
    public const float Introduced = 0.40f;
    public const float Developing = 0.70f;
    public const float Proficient = 0.90f;
    public const float DecayWarning = 0.70f;
    public const float PrerequisiteGate = 0.60f;
}
