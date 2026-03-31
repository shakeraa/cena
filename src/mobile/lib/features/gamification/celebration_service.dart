// =============================================================================
// Cena Adaptive Learning Platform — 5-Tier Celebration System
// =============================================================================
//
// Proportional celebrations that match the magnitude of the achievement.
// Prevents celebration fatigue by using restrained micro-feedback for routine
// correct answers, and reserving immersive celebrations for genuine milestones.
// =============================================================================

/// The five celebration tiers, from subtle micro-feedback to immersive.
enum CelebrationTier {
  /// Tier 1: Subtle checkmark + color flash (1–10 XP, routine correct answer).
  micro,

  /// Tier 2: "+N XP" float with bounce (11–25 XP, streak milestone).
  minor,

  /// Tier 3: Expanding ring + sparkle particles (26–50 XP, topic complete).
  medium,

  /// Tier 4: Full-screen confetti + level badge (51–100 XP, level up).
  major,

  /// Tier 5: Immersive celebration with glow pulse (100+ XP, mastery).
  epic,
}

/// Event types that trigger celebrations.
enum CelebrationEvent {
  correctAnswer,
  streakMilestone,
  topicComplete,
  levelUp,
  masteryAchieved,
  badgeEarned,
}

/// Classifies an achievement into the appropriate celebration tier.
class CelebrationService {
  const CelebrationService._();

  /// Determine the celebration tier for a given event and XP delta.
  static CelebrationTier classify({
    required CelebrationEvent event,
    int xpDelta = 0,
  }) {
    // Mastery / level-up always get higher tiers regardless of XP.
    switch (event) {
      case CelebrationEvent.masteryAchieved:
        return CelebrationTier.epic;
      case CelebrationEvent.levelUp:
        return CelebrationTier.major;
      case CelebrationEvent.topicComplete:
      case CelebrationEvent.badgeEarned:
        return CelebrationTier.medium;
      case CelebrationEvent.streakMilestone:
        return CelebrationTier.minor;
      case CelebrationEvent.correctAnswer:
        // XP-based classification for routine answers.
        if (xpDelta >= 100) return CelebrationTier.epic;
        if (xpDelta >= 51) return CelebrationTier.major;
        if (xpDelta >= 26) return CelebrationTier.medium;
        if (xpDelta >= 11) return CelebrationTier.minor;
        return CelebrationTier.micro;
    }
  }

  /// Duration of the celebration animation for each tier.
  static Duration duration(CelebrationTier tier) {
    switch (tier) {
      case CelebrationTier.micro:
        return const Duration(milliseconds: 400);
      case CelebrationTier.minor:
        return const Duration(milliseconds: 900);
      case CelebrationTier.medium:
        return const Duration(milliseconds: 1400);
      case CelebrationTier.major:
        return const Duration(milliseconds: 2000);
      case CelebrationTier.epic:
        return const Duration(milliseconds: 2800);
    }
  }
}
