// =============================================================================
// Cena Platform — ParentVisibilityDefaults (prr-230)
//
// Central (and ONLY) seam that maps age band + reason tag to the default
// ParentVisibility for a newly-added ExamTarget. Used at target-creation
// time ONLY; subsequent changes go through explicit ParentVisibilityChanged
// events driven by the student or system.
//
// The policy:
//   SafetyFlag-tagged target    → Visible (regardless of age)
//   Under13                     → Visible (COPPA VPC — parent governs)
//   Teen13to15 / Teen16to17     → Hidden  (GDPR-K / PPL minor-dignity)
//   Adult                       → Hidden  (self-determination; no default share)
//
// Design:
//   - Pure function; no I/O, no state. Band + reason go in, default comes out.
//   - This is the only place that maps AgeBand → ParentVisibility default;
//     the architecture test <c>ParentVisibilityDefaultHiddenFor13PlusTest</c>
//     asserts no other file branches on AgeBand to choose a ParentVisibility.
//   - Safety-flag carve-out is evaluated FIRST — the age default is never
//     consulted for safety-flagged targets, matching ADR-0041 duty-of-care.
// =============================================================================

using Cena.Actors.Consent;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Policy helper for PRR-230 default parent-visibility resolution.
/// </summary>
public static class ParentVisibilityDefaults
{
    /// <summary>
    /// Resolve the default <see cref="ParentVisibility"/> for a newly-added
    /// target given the student's band + reason tag. See class remarks for
    /// the full policy table.
    /// </summary>
    /// <param name="band">Authoritative age band resolved via
    /// <see cref="IStudentAgeBandLookup"/>; never a request parameter.</param>
    /// <param name="reasonTag">The target's reason tag. Null is treated as
    /// non-safety (uses the age-default).</param>
    public static ParentVisibility Resolve(AgeBand band, ReasonTag? reasonTag)
    {
        // Safety-flag carve-out takes precedence at every band.
        if (reasonTag == ReasonTag.SafetyFlag)
        {
            return ParentVisibility.Visible;
        }

        return band switch
        {
            AgeBand.Under13    => ParentVisibility.Visible, // COPPA: parent governs
            AgeBand.Teen13to15 => ParentVisibility.Hidden,  // GDPR-K
            AgeBand.Teen16to17 => ParentVisibility.Hidden,  // PPL minor-dignity
            AgeBand.Adult      => ParentVisibility.Hidden,  // self-determination
            _ => ParentVisibility.Hidden, // defensive: unknown bands default to most-private
        };
    }

    /// <summary>
    /// Convenience: does this band default to Hidden for non-safety
    /// targets? Used by architecture + integration tests to probe the
    /// policy without replicating the switch.
    /// </summary>
    public static bool DefaultsToHiddenFor(AgeBand band)
        => Resolve(band, reasonTag: null) == ParentVisibility.Hidden;
}
