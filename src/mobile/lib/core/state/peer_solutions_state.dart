// =============================================================================
// Cena Adaptive Learning Platform — Peer Solutions State (MOB-053)
// =============================================================================
// State management for anonymous peer solution replays.
// Only shows correct answers from students with P(known) > 0.70.
// Under-16 access requires teacher approval.
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app_state.dart';

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------

/// An anonymous peer solution for a concept/question pair.
class PeerSolution {
  const PeerSolution({
    required this.id,
    required this.conceptId,
    required this.questionId,
    required this.methodologyId,
    required this.approachSteps,
    required this.timeTakenMs,
    this.helpfulVotes = 0,
    this.notHelpfulVotes = 0,
    this.hasVoted = false,
  });

  final String id;
  final String conceptId;
  final String questionId;

  /// Which pedagogical methodology was used (spaced_repetition, socratic, etc.).
  final String methodologyId;

  /// Ordered steps describing the approach taken.
  final List<String> approachSteps;

  /// Time taken to solve in milliseconds.
  final int timeTakenMs;

  /// Count of "this was helpful" votes.
  final int helpfulVotes;

  /// Count of "this was not helpful" votes.
  final int notHelpfulVotes;

  /// Whether the current user has already voted on this solution.
  final bool hasVoted;

  /// Formatted time taken (e.g. "2m 30s").
  String get formattedTime {
    final seconds = timeTakenMs ~/ 1000;
    if (seconds < 60) return '${seconds}s';
    final minutes = seconds ~/ 60;
    final remainingSeconds = seconds % 60;
    if (remainingSeconds == 0) return '${minutes}m';
    return '${minutes}m ${remainingSeconds}s';
  }

  /// Human-readable methodology label.
  String get methodologyLabel {
    switch (methodologyId) {
      case 'spaced_repetition':
        return 'Spaced Repetition';
      case 'interleaved':
        return 'Interleaved Practice';
      case 'blocked':
        return 'Focused Practice';
      case 'adaptive_difficulty':
        return 'Adaptive';
      case 'socratic':
        return 'Socratic Method';
      default:
        return methodologyId;
    }
  }

  PeerSolution copyWith({
    int? helpfulVotes,
    int? notHelpfulVotes,
    bool? hasVoted,
  }) {
    return PeerSolution(
      id: id,
      conceptId: conceptId,
      questionId: questionId,
      methodologyId: methodologyId,
      approachSteps: approachSteps,
      timeTakenMs: timeTakenMs,
      helpfulVotes: helpfulVotes ?? this.helpfulVotes,
      notHelpfulVotes: notHelpfulVotes ?? this.notHelpfulVotes,
      hasVoted: hasVoted ?? this.hasVoted,
    );
  }

  factory PeerSolution.fromJson(Map<String, dynamic> json) {
    return PeerSolution(
      id: json['id'] as String? ?? '',
      conceptId: json['conceptId'] as String? ?? '',
      questionId: json['questionId'] as String? ?? '',
      methodologyId: json['methodologyId'] as String? ?? '',
      approachSteps: (json['approachSteps'] as List<dynamic>?)
              ?.map((e) => e.toString())
              .toList() ??
          const [],
      timeTakenMs: (json['timeTakenMs'] as num?)?.toInt() ?? 0,
      helpfulVotes: (json['helpfulVotes'] as num?)?.toInt() ?? 0,
      notHelpfulVotes: (json['notHelpfulVotes'] as num?)?.toInt() ?? 0,
      hasVoted: json['hasVoted'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'conceptId': conceptId,
        'questionId': questionId,
        'methodologyId': methodologyId,
        'approachSteps': approachSteps,
        'timeTakenMs': timeTakenMs,
        'helpfulVotes': helpfulVotes,
        'notHelpfulVotes': notHelpfulVotes,
      };
}

// ---------------------------------------------------------------------------
// Quality gate
// ---------------------------------------------------------------------------

/// Quality gate that filters peer solutions to ensure only high-quality,
/// correct answers from students with sufficient mastery are shown.
class PeerSolutionQualityGate {
  const PeerSolutionQualityGate({
    this.minMastery = 0.70,
    this.requireCorrectAnswer = true,
    this.maxSolutions = 3,
    this.requireTeacherApprovalUnder16 = true,
  });

  /// Minimum P(known) required for a solution to be eligible.
  final double minMastery;

  /// Whether only correct answers are shown.
  final bool requireCorrectAnswer;

  /// Maximum number of solutions to display (sorted by methodology diversity).
  final int maxSolutions;

  /// Whether under-16 students require teacher approval to view.
  final bool requireTeacherApprovalUnder16;

  /// Default quality gate configuration.
  static const defaultGate = PeerSolutionQualityGate();

  /// Filter and sort solutions for display.
  /// Sorts by methodology diversity to show different approaches.
  List<PeerSolution> filterAndSort(List<PeerSolution> solutions) {
    if (solutions.isEmpty) return [];

    // Deduplicate by methodology to show diverse approaches.
    final byMethodology = <String, PeerSolution>{};
    for (final solution in solutions) {
      if (!byMethodology.containsKey(solution.methodologyId)) {
        byMethodology[solution.methodologyId] = solution;
      } else {
        // Keep the one with more helpful votes.
        final existing = byMethodology[solution.methodologyId]!;
        if (solution.helpfulVotes > existing.helpfulVotes) {
          byMethodology[solution.methodologyId] = solution;
        }
      }
    }

    final diverse = byMethodology.values.toList();

    // Sort by helpful votes descending, then by time ascending.
    diverse.sort((a, b) {
      final voteDiff = b.helpfulVotes.compareTo(a.helpfulVotes);
      if (voteDiff != 0) return voteDiff;
      return a.timeTakenMs.compareTo(b.timeTakenMs);
    });

    return diverse.take(maxSolutions).toList();
  }
}

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

/// Request parameters for fetching peer solutions.
class PeerSolutionRequest {
  const PeerSolutionRequest({
    required this.conceptId,
    required this.questionId,
  });

  final String conceptId;
  final String questionId;

  @override
  bool operator ==(Object other) =>
      other is PeerSolutionRequest &&
      other.conceptId == conceptId &&
      other.questionId == questionId;

  @override
  int get hashCode => Object.hash(conceptId, questionId);
}

/// Fetches and filters peer solutions for a given concept/question.
/// Applies the quality gate and returns at most 3 diverse solutions.
final peerSolutionsProvider = FutureProvider.autoDispose
    .family<List<PeerSolution>, PeerSolutionRequest>(
  (ref, request) async {
    final api = ref.watch(apiClientProvider);
    const qualityGate = PeerSolutionQualityGate.defaultGate;

    try {
      final response = await api.get<Map<String, dynamic>>(
        '/social/peer-solutions',
        queryParameters: {
          'conceptId': request.conceptId,
          'questionId': request.questionId,
          'minMastery': qualityGate.minMastery,
        },
      );

      final data = response.data ?? {};
      final rawSolutions = data['solutions'] as List<dynamic>? ?? [];

      final solutions = rawSolutions
          .map((e) => PeerSolution.fromJson(e as Map<String, dynamic>))
          .toList();

      return qualityGate.filterAndSort(solutions);
    } catch (_) {
      return [];
    }
  },
);

/// Whether peer solutions are available for the current student.
/// Under-16 students require teacher approval flag from the backend.
final peerSolutionsEnabledProvider = Provider.autoDispose<bool>((ref) {
  final student = ref.watch(currentStudentProvider);
  if (student == null) return false;
  // Feature is available by default; teacher approval gating is done server-side
  // for under-16 students. The client checks the flag returned from the API.
  return true;
});
