// =============================================================================
// Cena Adaptive Learning Platform — Reengagement Service (MOB-057)
// Positive, time-gated reengagement messages for inactive students.
// No guilt, no loss aversion, no manipulation. After 60 days of
// silence, we respect the departure and stop reaching out.
// =============================================================================

// ---------------------------------------------------------------------------
// Reengagement Campaign
// ---------------------------------------------------------------------------

/// A reengagement message with action buttons for the notification payload.
class ReengagementCampaign {
  const ReengagementCampaign({
    required this.message,
    required this.primaryAction,
    required this.primaryActionLabel,
    this.secondaryActionLabel = 'Snooze 2h',
  });

  /// The notification body text — always positive, never guilt-tripping.
  final String message;

  /// Deep-link action for the primary button.
  final String primaryAction;

  /// Label for the primary action button.
  final String primaryActionLabel;

  /// Label for the secondary (snooze) button.
  final String secondaryActionLabel;

  @override
  String toString() => 'ReengagementCampaign(action: $primaryActionLabel)';
}

// ---------------------------------------------------------------------------
// Reengagement Service
// ---------------------------------------------------------------------------

/// Generates time-gated reengagement messages for students who have
/// stopped using the app.
///
/// Design principles:
///   - Positive framing: celebrate what the student has accomplished.
///   - No guilt: never reference "you missed" or "you lost".
///   - No loss aversion: never say "your streak will reset" or similar.
///   - Escalating silence: messages get less frequent, then stop entirely.
///   - Respect: after 60 days, we assume the student has moved on and
///     we stop all outreach permanently (until they return on their own).
class ReengagementService {
  const ReengagementService._();

  // ---- Inactivity thresholds ----

  /// Day 3: First gentle nudge.
  static const int _day3 = 3;

  /// Day 7: Second nudge with knowledge graph framing.
  static const int _day7 = 7;

  /// Day 14: Third nudge highlighting new content.
  static const int _day14 = 14;

  /// Day 30: Final nudge with social framing.
  static const int _day30 = 30;

  /// Day 60+: Silence — respect the departure.
  static const int _silenceThreshold = 60;

  // ---- Message generation ----

  /// Returns a reengagement message for the given number of [inactiveDays],
  /// or null if no message should be sent.
  ///
  /// Returns null for:
  ///   - 0-2 days inactive (too early, normal variation)
  ///   - Days between campaign milestones (avoid over-messaging)
  ///   - 60+ days inactive (respect the departure)
  static ReengagementCampaign? getCampaign(int inactiveDays) {
    if (inactiveDays < _day3) return null;
    if (inactiveDays >= _silenceThreshold) return null;

    if (inactiveDays >= _day3 && inactiveDays < _day7) {
      return _day3Campaign;
    }
    if (inactiveDays >= _day7 && inactiveDays < _day14) {
      return _day7Campaign;
    }
    if (inactiveDays >= _day14 && inactiveDays < _day30) {
      return _day14Campaign;
    }
    if (inactiveDays >= _day30 && inactiveDays < _silenceThreshold) {
      return _day30Campaign;
    }

    return null;
  }

  /// Convenience method: returns just the message string, or null.
  static String? getReengagementMessage(int inactiveDays) {
    return getCampaign(inactiveDays)?.message;
  }

  // ---- Predefined campaigns ----

  /// Day 3: "Your progress is saved."
  static const _day3Campaign = ReengagementCampaign(
    message: "We've saved your progress! "
        'Pick up right where you left off whenever you are ready.',
    primaryAction: 'cena://session/review',
    primaryActionLabel: 'Start Review',
  );

  /// Day 7: "Your knowledge graph misses you."
  static const _day7Campaign = ReengagementCampaign(
    message: 'Your knowledge graph is waiting for you. '
        'A quick review session keeps those connections strong.',
    primaryAction: 'cena://session/review',
    primaryActionLabel: 'Start Review',
  );

  /// Day 14: "New concepts added."
  static const _day14Campaign = ReengagementCampaign(
    message: 'New concepts have been added to your course! '
        'Come explore what is new when you have a few minutes.',
    primaryAction: 'cena://home',
    primaryActionLabel: 'See What\'s New',
  );

  /// Day 30: "Your classmates have been learning."
  static const _day30Campaign = ReengagementCampaign(
    message: 'Your classmates have been learning and growing. '
        'Join them whenever you are ready — your place is still here.',
    primaryAction: 'cena://session/start',
    primaryActionLabel: 'Start Session',
  );

  // ---- Validation ----

  /// Returns true if this number of inactive days falls within the
  /// reengagement window (3-59 days).
  static bool isInReengagementWindow(int inactiveDays) {
    return inactiveDays >= _day3 && inactiveDays < _silenceThreshold;
  }

  /// Returns true if we should permanently stop contacting this student
  /// (60+ days inactive).
  static bool shouldSilence(int inactiveDays) {
    return inactiveDays >= _silenceThreshold;
  }
}
