// =============================================================================
// Cena Adaptive Learning Platform — Onboarding State & Providers
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/models/domain_models.dart';
import 'widgets/goal_setting_card.dart';
import 'widgets/role_selector.dart';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const String _kOnboardingComplete = 'onboarding_complete';
const String _kOnboardingSubjects = 'onboarding_subjects';
const String _kOnboardingGrade = 'onboarding_grade';
const String _kOnboardingBagrut = 'onboarding_bagrut_units';
const String _kOnboardingRole = 'onboarding_role';
const String _kOnboardingGoal = 'onboarding_goal';
const String _kOnboardingDailyMinutes = 'onboarding_daily_minutes';

// ---------------------------------------------------------------------------
// Onboarding Data
// ---------------------------------------------------------------------------

/// Bagrut (Israeli matriculation) study unit levels for a given subject.
enum BagrutUnits {
  threeUnit(3, '3 יחידות — בסיסי'),
  fourUnit(4, '4 יחידות — מורחב'),
  fiveUnit(5, '5 יחידות — גבוה');

  const BagrutUnits(this.units, this.label);
  final int units;
  final String label;
}

/// Grade levels (9th–12th, mapped to Israeli school years ט–יב).
enum GradeLevel {
  ninth(9, 'כיתה ט'),
  tenth(10, 'כיתה י'),
  eleventh(11, 'כיתה יא'),
  twelfth(12, 'כיתה יב');

  const GradeLevel(this.year, this.label);
  final int year;
  final String label;
}

/// Immutable snapshot of the user's onboarding selections.
class OnboardingSelections {
  const OnboardingSelections({
    this.role,
    this.selectedSubjects = const [],
    this.gradeLevel,
    this.bagrutUnits,
    this.goalType,
    this.dailyMinutes,
    this.skipDiagnostic = false,
    this.diagnosticAnswers = const {},
  });

  /// User role (student/teacher/parent) — drives page routing.
  final OnboardingRole? role;

  final List<Subject> selectedSubjects;
  final GradeLevel? gradeLevel;
  final BagrutUnits? bagrutUnits;

  /// Learning goal selected by the student.
  final GoalType? goalType;

  /// Daily study time commitment in minutes (5/10/15/20).
  final int? dailyMinutes;

  final bool skipDiagnostic;

  /// Maps question index → selected answer index (for diagnostic quiz).
  final Map<int, int> diagnosticAnswers;

  OnboardingSelections copyWith({
    OnboardingRole? role,
    List<Subject>? selectedSubjects,
    GradeLevel? gradeLevel,
    BagrutUnits? bagrutUnits,
    GoalType? goalType,
    int? dailyMinutes,
    bool? skipDiagnostic,
    Map<int, int>? diagnosticAnswers,
  }) {
    return OnboardingSelections(
      role: role ?? this.role,
      selectedSubjects: selectedSubjects ?? this.selectedSubjects,
      gradeLevel: gradeLevel ?? this.gradeLevel,
      bagrutUnits: bagrutUnits ?? this.bagrutUnits,
      goalType: goalType ?? this.goalType,
      dailyMinutes: dailyMinutes ?? this.dailyMinutes,
      skipDiagnostic: skipDiagnostic ?? this.skipDiagnostic,
      diagnosticAnswers: diagnosticAnswers ?? this.diagnosticAnswers,
    );
  }

  bool get canProceedFromRole => role != null;
  bool get canProceedFromSubjects => selectedSubjects.isNotEmpty;
  bool get canProceedFromGrade => gradeLevel != null && bagrutUnits != null;
  bool get canProceedFromGoal => goalType != null;
  bool get canProceedFromTime => dailyMinutes != null;

  /// Number of onboarding pages for the selected role.
  int get totalPages => role?.pageCount ?? 8;

  /// Whether the current role is student (default path).
  bool get isStudent => role == OnboardingRole.student || role == null;
}

// ---------------------------------------------------------------------------
// Onboarding Notifier
// ---------------------------------------------------------------------------

class OnboardingNotifier extends StateNotifier<OnboardingSelections> {
  OnboardingNotifier() : super(const OnboardingSelections());

  void setRole(OnboardingRole role) {
    state = state.copyWith(role: role);
  }

  void toggleSubject(Subject subject) {
    final current = List<Subject>.from(state.selectedSubjects);
    if (current.contains(subject)) {
      current.remove(subject);
    } else if (current.length < 3) {
      current.add(subject);
    }
    state = state.copyWith(selectedSubjects: current);
  }

  void setGrade(GradeLevel grade) {
    state = state.copyWith(gradeLevel: grade);
  }

  void setBagrutUnits(BagrutUnits units) {
    state = state.copyWith(bagrutUnits: units);
  }

  void setGoalType(GoalType goal) {
    state = state.copyWith(goalType: goal);
  }

  void setDailyMinutes(int minutes) {
    state = state.copyWith(dailyMinutes: minutes);
  }

  void setSkipDiagnostic(bool skip) {
    state = state.copyWith(skipDiagnostic: skip);
  }

  void recordDiagnosticAnswer(int questionIndex, int answerIndex) {
    final updated = Map<int, int>.from(state.diagnosticAnswers);
    updated[questionIndex] = answerIndex;
    state = state.copyWith(diagnosticAnswers: updated);
  }

  /// Persist completion flag and selections to SharedPreferences.
  Future<void> completeOnboarding() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kOnboardingComplete, true);
    await prefs.setStringList(
      _kOnboardingSubjects,
      state.selectedSubjects.map((s) => s.name).toList(),
    );
    if (state.role != null) {
      await prefs.setString(_kOnboardingRole, state.role!.name);
    }
    if (state.gradeLevel != null) {
      await prefs.setInt(_kOnboardingGrade, state.gradeLevel!.year);
    }
    if (state.bagrutUnits != null) {
      await prefs.setInt(_kOnboardingBagrut, state.bagrutUnits!.units);
    }
    if (state.goalType != null) {
      await prefs.setString(_kOnboardingGoal, state.goalType!.name);
    }
    if (state.dailyMinutes != null) {
      await prefs.setInt(_kOnboardingDailyMinutes, state.dailyMinutes!);
    }
  }
}

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

final onboardingProvider =
    StateNotifierProvider<OnboardingNotifier, OnboardingSelections>(
  (ref) => OnboardingNotifier(),
);

/// Whether the user has already completed onboarding.
/// Reads from SharedPreferences; defaults to false on first install.
final onboardingCompleteProvider = FutureProvider<bool>((ref) async {
  final prefs = await SharedPreferences.getInstance();
  return prefs.getBool(_kOnboardingComplete) ?? false;
});
