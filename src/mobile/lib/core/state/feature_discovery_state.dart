// =============================================================================
// Cena Adaptive Learning Platform — Progressive Feature Discovery State
// =============================================================================
//
// Training-wheels rollout that gradually reveals features as sessions complete:
//   Session 1-2  : core loop only
//   Session 3    : hints
//   Session 5    : streak mechanic
//   Session 7    : methodology switch
//   Session 10   : study groups (if social feature enabled)
//   Session 15   : full knowledge graph + NEW badge
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

const String _kSessionsCompleted = 'feature_discovery_sessions_completed';
const String _kMethodologyExplained = 'feature_discovery_methodology_explained';
const String _kStudyGroupsUnlockNotified =
    'feature_discovery_study_groups_unlock_notified';
const String _kKnowledgeGraphNewSeen =
    'feature_discovery_knowledge_graph_new_seen';
const String _kStreakUnlockCelebrated =
    'feature_discovery_streak_unlock_celebrated';

/// Overall scaffolding intensity.
enum ScaffoldingLevel {
  /// L0: Training wheels (minimal feature surface).
  l0,

  /// L1: Intermediate (hints + methodology tools).
  l1,

  /// L2: Full feature surface.
  l2,
}

/// Snapshot of feature-discovery progression.
class FeatureDiscoveryState {
  const FeatureDiscoveryState({
    this.sessionsCompleted = 0,
    this.methodologyExplained = false,
    this.studyGroupsUnlockNotified = false,
    this.knowledgeGraphNewSeen = false,
    this.streakUnlockCelebrated = false,
  });

  final int sessionsCompleted;
  final bool methodologyExplained;
  final bool studyGroupsUnlockNotified;
  final bool knowledgeGraphNewSeen;
  final bool streakUnlockCelebrated;

  ScaffoldingLevel get scaffoldingLevel {
    if (sessionsCompleted >= 15) return ScaffoldingLevel.l2;
    if (sessionsCompleted >= 7) return ScaffoldingLevel.l1;
    return ScaffoldingLevel.l0;
  }

  bool get hintsUnlocked => sessionsCompleted >= 3;
  bool get streakUnlocked => sessionsCompleted >= 5;
  bool get methodologyUnlocked => sessionsCompleted >= 7;

  // Social-groups surface is enabled from session 10 when that feature exists.
  bool get studyGroupsUnlocked => sessionsCompleted >= 10;

  bool get knowledgeGraphFullAccess => sessionsCompleted >= 15;

  bool get showKnowledgeGraphNewBadge =>
      knowledgeGraphFullAccess && !knowledgeGraphNewSeen;

  FeatureDiscoveryState copyWith({
    int? sessionsCompleted,
    bool? methodologyExplained,
    bool? studyGroupsUnlockNotified,
    bool? knowledgeGraphNewSeen,
    bool? streakUnlockCelebrated,
  }) {
    return FeatureDiscoveryState(
      sessionsCompleted: sessionsCompleted ?? this.sessionsCompleted,
      methodologyExplained: methodologyExplained ?? this.methodologyExplained,
      studyGroupsUnlockNotified:
          studyGroupsUnlockNotified ?? this.studyGroupsUnlockNotified,
      knowledgeGraphNewSeen:
          knowledgeGraphNewSeen ?? this.knowledgeGraphNewSeen,
      streakUnlockCelebrated:
          streakUnlockCelebrated ?? this.streakUnlockCelebrated,
    );
  }
}

class FeatureDiscoveryNotifier extends StateNotifier<FeatureDiscoveryState> {
  FeatureDiscoveryNotifier() : super(const FeatureDiscoveryState()) {
    _load();
  }

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    state = state.copyWith(
      sessionsCompleted: prefs.getInt(_kSessionsCompleted) ?? 0,
      methodologyExplained: prefs.getBool(_kMethodologyExplained) ?? false,
      studyGroupsUnlockNotified:
          prefs.getBool(_kStudyGroupsUnlockNotified) ?? false,
      knowledgeGraphNewSeen: prefs.getBool(_kKnowledgeGraphNewSeen) ?? false,
      streakUnlockCelebrated: prefs.getBool(_kStreakUnlockCelebrated) ?? false,
    );
  }

  /// Records one successfully completed session.
  Future<void> recordSessionCompleted() async {
    final prefs = await SharedPreferences.getInstance();
    final next = state.sessionsCompleted + 1;
    await prefs.setInt(_kSessionsCompleted, next);
    state = state.copyWith(sessionsCompleted: next);
  }

  Future<void> markMethodologyExplained() async {
    if (state.methodologyExplained) return;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kMethodologyExplained, true);
    state = state.copyWith(methodologyExplained: true);
  }

  Future<void> markStudyGroupsUnlockNotified() async {
    if (state.studyGroupsUnlockNotified) return;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kStudyGroupsUnlockNotified, true);
    state = state.copyWith(studyGroupsUnlockNotified: true);
  }

  Future<void> markKnowledgeGraphNewSeen() async {
    if (state.knowledgeGraphNewSeen) return;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kKnowledgeGraphNewSeen, true);
    state = state.copyWith(knowledgeGraphNewSeen: true);
  }

  Future<void> markStreakUnlockCelebrated() async {
    if (state.streakUnlockCelebrated) return;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kStreakUnlockCelebrated, true);
    state = state.copyWith(streakUnlockCelebrated: true);
  }
}

final featureDiscoveryProvider =
    StateNotifierProvider<FeatureDiscoveryNotifier, FeatureDiscoveryState>(
  (ref) => FeatureDiscoveryNotifier(),
);
