// =============================================================================
// Cena Adaptive Learning Platform — 5-Tier Celebration System (MOB-050)
// =============================================================================
//
// Proportional celebrations that match the magnitude of the achievement.
// Prevents celebration fatigue by using restrained micro-feedback for routine
// correct answers, and reserving immersive celebrations for genuine milestones.
//
// Tier 1 (Micro)  — correct answer:       green glow + haptic selectionClick
// Tier 2 (Small)  — streak/quest:         color burst + 20 particles (600ms)
// Tier 3 (Medium) — badge/level up:       100 particles + glow (1000ms)
// Tier 4 (Large)  — concept mastered:     full-screen overlay (3s)
// Tier 5 (Epic)   — course complete:      Rive animation + certificate (5s)
//
// Performance: max 200 particles, CustomPainter for particles, max 8
// AnimationControllers. Tiers 1-2 use TweenAnimationBuilder only.
// =============================================================================

import '../../core/services/haptic_service.dart';
import '../../core/services/sound_service.dart';

/// The five celebration tiers, from subtle micro-feedback to immersive.
enum CelebrationTier {
  /// Tier 1: Subtle green glow + haptic selectionClick, scale pulse 150ms.
  micro,

  /// Tier 2: Color burst + 20 confetti particles (600ms), optional chime.
  minor,

  /// Tier 3: Full confetti (100 particles, 1000ms), glow, fanfare.
  medium,

  /// Tier 4: Full-screen overlay, particle effects, extended haptic (3s).
  major,

  /// Tier 5: Rive animation (3-5s), certificate, particle shower.
  epic,
}

/// Event types that trigger celebrations.
enum CelebrationEvent {
  correctAnswer,
  streakMilestone,
  questComplete,
  topicComplete,
  levelUp,
  masteryAchieved,
  badgeEarned,
  weeklyMission,
  courseComplete,
  semesterGoal,
}

/// Classifies an achievement into the appropriate celebration tier.
class CelebrationService {
  const CelebrationService._();

  /// Determine the celebration tier for a given event and XP delta.
  static CelebrationTier classify({
    required CelebrationEvent event,
    int xpDelta = 0,
  }) {
    switch (event) {
      // Epic (Tier 5)
      case CelebrationEvent.courseComplete:
      case CelebrationEvent.semesterGoal:
        return CelebrationTier.epic;

      // Large (Tier 4)
      case CelebrationEvent.masteryAchieved:
      case CelebrationEvent.weeklyMission:
        return CelebrationTier.major;

      // Medium (Tier 3)
      case CelebrationEvent.levelUp:
      case CelebrationEvent.badgeEarned:
      case CelebrationEvent.topicComplete:
        return CelebrationTier.medium;

      // Small (Tier 2)
      case CelebrationEvent.streakMilestone:
      case CelebrationEvent.questComplete:
        return CelebrationTier.minor;

      // Micro (Tier 1) or XP-based escalation
      case CelebrationEvent.correctAnswer:
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
        return const Duration(milliseconds: 150);
      case CelebrationTier.minor:
        return const Duration(milliseconds: 600);
      case CelebrationTier.medium:
        return const Duration(milliseconds: 1000);
      case CelebrationTier.major:
        return const Duration(milliseconds: 3000);
      case CelebrationTier.epic:
        return const Duration(milliseconds: 5000);
    }
  }

  /// Number of confetti particles for each tier.
  /// Max 200 across all tiers for Samsung A14 (Mali-G57) performance.
  static int particleCount(CelebrationTier tier) {
    switch (tier) {
      case CelebrationTier.micro:
        return 0;
      case CelebrationTier.minor:
        return 20;
      case CelebrationTier.medium:
        return 100;
      case CelebrationTier.major:
        return 150;
      case CelebrationTier.epic:
        return 200;
    }
  }

  /// Fire the appropriate haptic pattern for a celebration tier.
  static Future<void> triggerHaptic(
    CelebrationTier tier,
    CenaHaptics haptics,
  ) async {
    switch (tier) {
      case CelebrationTier.micro:
        await haptics.play(HapticPattern.buttonPress);
      case CelebrationTier.minor:
        await haptics.play(HapticPattern.swipeComplete);
      case CelebrationTier.medium:
        await haptics.play(HapticPattern.correctAnswer);
      case CelebrationTier.major:
        await haptics.play(HapticPattern.badgeUnlock);
      case CelebrationTier.epic:
        await haptics.play(HapticPattern.levelUp);
    }
  }

  /// Play the appropriate sound effect for a celebration tier.
  static Future<void> triggerSound(
    CelebrationTier tier,
    CenaSoundSystem sounds,
  ) async {
    switch (tier) {
      case CelebrationTier.micro:
        // No sound for micro — haptic only.
        break;
      case CelebrationTier.minor:
        await sounds.play(CenaSound.correctChime);
      case CelebrationTier.medium:
        await sounds.play(CenaSound.badgeUnlock);
      case CelebrationTier.major:
        await sounds.play(CenaSound.levelUp);
      case CelebrationTier.epic:
        await sounds.play(CenaSound.questComplete);
    }
  }
}
