// =============================================================================
// Cena Platform -- Canonical Mastery Threshold Constants
// Single source of truth for all mastery-related thresholds.
// All services and actors MUST reference these instead of hardcoding values.
// =============================================================================

namespace Cena.Actors;

/// <summary>
/// Canonical mastery threshold constants used across the actor system.
///
/// Three threshold tiers:
///   0.85 — Progression: student can proceed to dependent concepts
///   0.90 — Proficient:  rich mastery model "Mastered" display level
///   0.95 — Prerequisite Gate: critical prerequisites (edge weight >= 0.9)
///
/// The recall review threshold matches ProgressionThreshold by design:
/// when HLR-predicted recall drops below 0.85, a review is scheduled.
/// </summary>
public static class MasteryConstants
{
    /// <summary>
    /// BKT progression threshold: student has demonstrated sufficient mastery
    /// to proceed to dependent concepts. Used for frontier computation,
    /// review scheduling, ConceptMastered event emission, and item selection.
    /// </summary>
    public const double ProgressionThreshold = 0.85;

    /// <summary>
    /// Strict prerequisite gate: used for critical prerequisites (edge weight >= 0.9).
    /// Prevents premature progression on foundational dependency chains.
    /// </summary>
    public const double PrerequisiteGateThreshold = 0.95;

    /// <summary>
    /// HLR recall review threshold: when predicted recall drops below this,
    /// a review is scheduled. Same as ProgressionThreshold by design.
    /// </summary>
    public const double RecallReviewThreshold = ProgressionThreshold;

    // ── Float variants for Mastery/ namespace types ──

    /// <summary>Float version of ProgressionThreshold for Mastery namespace types.</summary>
    public const float ProgressionThresholdF = 0.85f;

    /// <summary>Float version of PrerequisiteGateThreshold for Mastery namespace types.</summary>
    public const float PrerequisiteGateThresholdF = 0.95f;

    /// <summary>Float version of RecallReviewThreshold for Mastery namespace types.</summary>
    public const float RecallReviewThresholdF = ProgressionThresholdF;
}
