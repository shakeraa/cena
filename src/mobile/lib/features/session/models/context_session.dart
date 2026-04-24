// =============================================================================
// Cena Adaptive Learning Platform — Context-Aware Sessions (MOB-041)
// Maps time-of-day study windows to optimal session configurations.
// =============================================================================

import '../../../core/services/routine_profile_service.dart';

// ---------------------------------------------------------------------------
// Context Session Type
// ---------------------------------------------------------------------------

/// Session types tailored to different times of day and cognitive states.
///
/// Each type has a distinct pedagogical purpose and duration target,
/// derived from the student's [RoutineProfile] and current time.
enum ContextSessionType {
  /// Early morning: short SRS review, quick wins to start the day.
  morningReview,

  /// Commute / transit: medium session, large touch targets, audio-friendly.
  commuteSession,

  /// After school: deep session with full flow arc and new concepts.
  afterSchoolDeep,

  /// Evening wind-down: revisit today's concepts, consolidate learning.
  eveningReview,

  /// Before bed: gentle flashcard review, no challenging new material.
  beforeBed,
}

// ---------------------------------------------------------------------------
// Context Session Config
// ---------------------------------------------------------------------------

/// Configuration parameters for a context-aware session.
///
/// Each [ContextSessionType] maps to a specific config that controls
/// session duration, item count, difficulty ceiling, and UX hints.
class ContextSessionConfig {
  const ContextSessionConfig({
    required this.type,
    required this.minDurationMinutes,
    required this.maxDurationMinutes,
    required this.itemCount,
    required this.description,
    required this.allowNewConcepts,
    required this.maxDifficultyTarget,
    this.useLargeTouchTargets = false,
    this.audioFriendly = false,
  });

  /// The session type this config represents.
  final ContextSessionType type;

  /// Minimum session duration in minutes.
  final int minDurationMinutes;

  /// Maximum session duration in minutes.
  final int maxDurationMinutes;

  /// Number of items (questions / flashcards) to include.
  final int itemCount;

  /// Human-readable description shown in the session launcher.
  final String description;

  /// Whether the session may introduce new concepts (vs review-only).
  final bool allowNewConcepts;

  /// Maximum target P(correct) for difficulty selection [0.0, 1.0].
  /// Lower values mean harder questions; higher values mean easier.
  final double maxDifficultyTarget;

  /// Whether the UI should increase touch target sizes (e.g. for commute).
  final bool useLargeTouchTargets;

  /// Whether the session should support audio-only interaction.
  final bool audioFriendly;

  /// Estimated duration label, e.g. "3-5 min".
  String get durationLabel => '$minDurationMinutes-$maxDurationMinutes min';

  @override
  String toString() =>
      'ContextSessionConfig(type: $type, $durationLabel, '
      'items: $itemCount, newConcepts: $allowNewConcepts)';
}

// ---------------------------------------------------------------------------
// Predefined Configs
// ---------------------------------------------------------------------------

/// Predefined session configurations for each context type.
///
/// Rationale for each:
///   - morningReview: < 5 min, 5 SRS review items, quick wins.
///   - commuteSession: 10-15 min, larger touch targets, audio-friendly.
///   - afterSchoolDeep: 15-25 min, full session arc, new concepts allowed.
///   - eveningReview: 5-10 min, day's concepts revisited.
///   - beforeBed: 3-5 min, gentle flashcard review, no challenging material.
const Map<ContextSessionType, ContextSessionConfig> contextSessionConfigs = {
  ContextSessionType.morningReview: ContextSessionConfig(
    type: ContextSessionType.morningReview,
    minDurationMinutes: 3,
    maxDurationMinutes: 5,
    itemCount: 5,
    description: 'Quick morning review — 5 concepts to warm up your brain',
    allowNewConcepts: false,
    maxDifficultyTarget: 0.85,
  ),
  ContextSessionType.commuteSession: ContextSessionConfig(
    type: ContextSessionType.commuteSession,
    minDurationMinutes: 10,
    maxDurationMinutes: 15,
    itemCount: 10,
    description: 'On-the-go learning — larger buttons, audio support',
    allowNewConcepts: true,
    maxDifficultyTarget: 0.75,
    useLargeTouchTargets: true,
    audioFriendly: true,
  ),
  ContextSessionType.afterSchoolDeep: ContextSessionConfig(
    type: ContextSessionType.afterSchoolDeep,
    minDurationMinutes: 15,
    maxDurationMinutes: 25,
    itemCount: 18,
    description: 'Deep learning session — explore new concepts',
    allowNewConcepts: true,
    maxDifficultyTarget: 0.60,
  ),
  ContextSessionType.eveningReview: ContextSessionConfig(
    type: ContextSessionType.eveningReview,
    minDurationMinutes: 5,
    maxDurationMinutes: 10,
    itemCount: 8,
    description: 'Evening review — revisit today\'s learning',
    allowNewConcepts: false,
    maxDifficultyTarget: 0.80,
  ),
  ContextSessionType.beforeBed: ContextSessionConfig(
    type: ContextSessionType.beforeBed,
    minDurationMinutes: 3,
    maxDurationMinutes: 5,
    itemCount: 5,
    description: 'Bedtime flashcards — gentle review, no pressure',
    allowNewConcepts: false,
    maxDifficultyTarget: 0.90,
  ),
};

// ---------------------------------------------------------------------------
// Session Type Suggestion
// ---------------------------------------------------------------------------

/// Suggests the best [ContextSessionType] based on current time and the
/// student's [RoutineProfile].
///
/// When a profile is available, the suggestion aligns with the student's
/// observed study habits. Without a profile, it falls back to time-of-day
/// heuristics.
ContextSessionType suggestSessionType(
  DateTime now,
  RoutineProfile? profile,
) {
  final window = RoutineProfileService.currentWindow(profile, now);
  return _windowToSessionType(window);
}

/// Maps a [StudyWindow] to the corresponding [ContextSessionType].
ContextSessionType _windowToSessionType(StudyWindow window) {
  switch (window) {
    case StudyWindow.morning:
      return ContextSessionType.morningReview;
    case StudyWindow.commute:
      return ContextSessionType.commuteSession;
    case StudyWindow.afterSchool:
      return ContextSessionType.afterSchoolDeep;
    case StudyWindow.evening:
      return ContextSessionType.eveningReview;
    case StudyWindow.bedtime:
      return ContextSessionType.beforeBed;
  }
}

/// Returns the [ContextSessionConfig] for a given session type.
///
/// Guaranteed to return a valid config — all types have predefined entries.
ContextSessionConfig configForType(ContextSessionType type) {
  return contextSessionConfigs[type]!;
}
