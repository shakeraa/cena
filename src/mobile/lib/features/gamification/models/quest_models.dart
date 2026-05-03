// =============================================================================
// Cena Adaptive Learning Platform — Quest System Models
// =============================================================================
//
// Defines quest types, statuses, criteria, and the Quest model used by
// the quest generator and quest panel widget.
// =============================================================================

// ---------------------------------------------------------------------------
// Quest Type
// ---------------------------------------------------------------------------

/// The cadence of a quest.
enum QuestType {
  /// Refreshes daily at midnight; expires at end of day.
  daily,

  /// Refreshes weekly on Sunday; expires at end of week.
  weekly,

  /// Refreshes monthly on the 1st; expires at end of month.
  monthly,

  /// Optional side quest; no strict expiry, student can dismiss.
  side,
}

// ---------------------------------------------------------------------------
// Quest Status
// ---------------------------------------------------------------------------

/// Lifecycle status of a quest.
enum QuestStatus {
  /// Visible to the student but not yet accepted.
  available,

  /// Student accepted the quest (auto-accepted for daily/weekly).
  accepted,

  /// Student has started working toward the quest goal.
  inProgress,

  /// Quest goal met; XP rewarded.
  completed,

  /// Quest expired before completion.
  expired,
}

// ---------------------------------------------------------------------------
// Quest Criteria
// ---------------------------------------------------------------------------

/// Sealed hierarchy describing what the student must do to complete a quest.
///
/// Each subtype carries the specific parameters needed for progress tracking.
sealed class QuestCriteria {
  const QuestCriteria();
}

/// Master [count] new concepts (P(Known) >= 0.85).
class MasterConcepts extends QuestCriteria {
  const MasterConcepts({required this.count, this.subject});

  /// Number of concepts to master.
  final int count;

  /// Optional subject filter (null = any subject).
  final String? subject;
}

/// Review [count] items that are due for spaced repetition.
class ReviewItems extends QuestCriteria {
  const ReviewItems({required this.count});

  /// Number of review items to complete.
  final int count;
}

/// Complete [count] learning sessions that pass the quality gate.
class CompleteSessions extends QuestCriteria {
  const CompleteSessions({required this.count});

  /// Number of qualifying sessions.
  final int count;
}

/// Achieve [targetAccuracy]% accuracy (0.0-1.0) across sessions.
class AchieveAccuracy extends QuestCriteria {
  const AchieveAccuracy({
    required this.targetAccuracy,
    this.subject,
  });

  /// Target accuracy as a fraction [0.0, 1.0].
  final double targetAccuracy;

  /// Optional subject filter (null = overall accuracy).
  final String? subject;
}

/// Try a pedagogical methodology the student hasn't used recently.
class TryMethodology extends QuestCriteria {
  const TryMethodology({required this.methodology});

  /// The methodology to try (e.g., "socratic", "interleaved").
  final String methodology;
}

/// Revisit a concept that hasn't been practiced in [daysSinceLastAttempt]+ days.
class ExploreOldConcept extends QuestCriteria {
  const ExploreOldConcept({required this.daysSinceLastAttempt});

  /// Minimum days since the concept was last attempted.
  final int daysSinceLastAttempt;
}

// ---------------------------------------------------------------------------
// Quest
// ---------------------------------------------------------------------------

/// A single quest assigned to the student.
class Quest {
  const Quest({
    required this.id,
    required this.type,
    required this.title,
    this.titleHe,
    required this.description,
    required this.criteria,
    required this.target,
    this.progress = 0,
    required this.xpReward,
    this.status = QuestStatus.available,
    this.expiresAt,
  });

  /// Unique quest identifier.
  final String id;

  /// Quest cadence type.
  final QuestType type;

  /// English title.
  final String title;

  /// Hebrew title (optional).
  final String? titleHe;

  /// Full description of what the student needs to do.
  final String description;

  /// The criteria that defines completion.
  final QuestCriteria criteria;

  /// Numeric target (e.g. 5 sessions, 80% accuracy expressed as 80).
  final int target;

  /// Current progress toward [target].
  final int progress;

  /// XP awarded on completion.
  final int xpReward;

  /// Current lifecycle status.
  final QuestStatus status;

  /// When this quest expires, or null for side quests.
  final DateTime? expiresAt;

  /// Progress fraction [0.0, 1.0].
  double get progressFraction =>
      target > 0 ? (progress / target).clamp(0.0, 1.0) : 0.0;

  /// Whether the quest goal has been met.
  bool get isGoalMet => progress >= target;

  Quest copyWith({
    String? id,
    QuestType? type,
    String? title,
    String? titleHe,
    String? description,
    QuestCriteria? criteria,
    int? target,
    int? progress,
    int? xpReward,
    QuestStatus? status,
    DateTime? expiresAt,
  }) {
    return Quest(
      id: id ?? this.id,
      type: type ?? this.type,
      title: title ?? this.title,
      titleHe: titleHe ?? this.titleHe,
      description: description ?? this.description,
      criteria: criteria ?? this.criteria,
      target: target ?? this.target,
      progress: progress ?? this.progress,
      xpReward: xpReward ?? this.xpReward,
      status: status ?? this.status,
      expiresAt: expiresAt ?? this.expiresAt,
    );
  }
}
