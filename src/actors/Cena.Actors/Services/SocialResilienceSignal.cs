// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Social Resilience Signal (FOC-012.3)
//
// Interface for future social resilience signals. Arab students build
// resilience through collective support (study groups, peer assistance).
// When social features launch, this signal replaces streakConsistency
// for collectivist students.
//
// NOT IMPLEMENTED YET — returns null until study group features exist.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Computes a social resilience score from peer interaction data.
/// Returns null until social features are implemented.
/// </summary>
public interface ISocialResilienceSignal
{
    double? ComputeSocialScore(SocialResilienceInput input);
}

public sealed class SocialResilienceSignal : ISocialResilienceSignal
{
    /// <summary>
    /// Returns null — social features are not yet implemented.
    /// When study group features launch, this will compute:
    ///   - Study group participation rate
    ///   - Peer help-giving/receiving frequency
    ///   - Shared session completions
    /// </summary>
    public double? ComputeSocialScore(SocialResilienceInput input)
    {
        // Social features not yet available — return null
        // to signal that resilience scoring should use existing signals.
        return null;
    }
}

public record SocialResilienceInput(
    Guid StudentId,
    int StudyGroupParticipationCount,    // Sessions where student was in a group
    int PeerHelpGivenCount,              // Times student helped a peer
    int PeerHelpReceivedCount,           // Times student received peer help
    int SharedSessionCompletions         // Sessions completed alongside a peer
);
