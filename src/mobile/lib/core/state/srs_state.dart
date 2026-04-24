// =============================================================================
// Cena Adaptive Learning Platform — SRS Review Queue State
// =============================================================================
//
// Riverpod providers for spaced repetition review scheduling.
// Bridges the FSRS scheduler with the app's mastery state to determine
// which concepts are due for review and manage dedicated review sessions.
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/domain_models.dart';
import '../services/fsrs_scheduler.dart';

// ---------------------------------------------------------------------------
// FSRS Scheduler Provider
// ---------------------------------------------------------------------------

/// Canonical FSRS scheduler instance with default population weights.
final fsrsSchedulerProvider = Provider<FsrsScheduler>((ref) {
  return FsrsScheduler();
});

// ---------------------------------------------------------------------------
// Per-Concept FSRS Card Store
// ---------------------------------------------------------------------------

/// Stores FSRS card state for every concept the student has encountered.
/// Maps conceptId -> FsrsCard.
final fsrsCardStoreProvider =
    StateNotifierProvider<FsrsCardStoreNotifier, Map<String, FsrsCard>>((ref) {
  return FsrsCardStoreNotifier();
});

/// Manages the mutable map of concept FSRS cards.
class FsrsCardStoreNotifier extends StateNotifier<Map<String, FsrsCard>> {
  FsrsCardStoreNotifier() : super({});

  /// Get or create a card for a concept.
  FsrsCard getCard(String conceptId) {
    return state[conceptId] ?? const FsrsCard();
  }

  /// Update a card after a review.
  void updateCard(String conceptId, FsrsCard card) {
    state = {...state, conceptId: card};
  }

  /// Record a review for a concept using the scheduler.
  void recordReview(
      String conceptId, FsrsRating rating, FsrsScheduler scheduler) {
    final currentCard = getCard(conceptId);
    final updatedCard = scheduler.schedule(currentCard, rating);
    updateCard(conceptId, updatedCard);
  }

  /// Bulk-initialize cards from mastery data (e.g. after sync from server).
  /// Creates FsrsCard entries for concepts that have been attempted but
  /// don't yet have FSRS state.
  void initializeFromMastery(Map<String, MasteryState> masteryMap) {
    final updated = Map<String, FsrsCard>.from(state);
    for (final entry in masteryMap.entries) {
      if (!updated.containsKey(entry.key) && entry.value.attemptCount > 0) {
        // Estimate initial FSRS state from mastery data.
        final mastery = entry.value;
        final estimatedStability = _estimateStability(mastery.pKnown);
        updated[entry.key] = FsrsCard(
          stability: estimatedStability,
          difficulty: (1.0 - mastery.pKnown).clamp(0.0, 1.0),
          reps: mastery.attemptCount,
          state: mastery.isMastered
              ? FsrsCardState.review
              : FsrsCardState.learning,
          lastReview: mastery.lastAttempted,
          scheduledDays: max(1, (estimatedStability * 0.9).round()),
        );
      }
    }
    state = updated;
  }

  /// Estimate stability from P(Known) using inverse forgetting curve.
  /// If pKnown is high, the concept is well-memorized -> higher stability.
  static double _estimateStability(double pKnown) {
    // Assume 1 day elapsed; solve R = (1 + t/(9*S))^-1 for S:
    // S = t / (9 * (R^-1 - 1))
    if (pKnown <= 0.0 || pKnown >= 1.0) return 1.0;
    final s = 1.0 / (9.0 * (1.0 / pKnown - 1.0));
    return s.clamp(0.1, 365.0);
  }
}

int max(int a, int b) => a > b ? a : b;

// ---------------------------------------------------------------------------
// Due Review Providers
// ---------------------------------------------------------------------------

/// Maximum number of review items presented in a single day.
const int _maxDailyReviewItems = 50;

/// A single due review item with its card state and overdue factor.
class DueReviewItem {
  const DueReviewItem({
    required this.conceptId,
    required this.card,
    required this.overdueFactor,
  });

  final String conceptId;
  final FsrsCard card;

  /// How overdue this item is (> 1.0 = past due, higher = more urgent).
  final double overdueFactor;
}

/// Count of concepts currently due for review.
final dueReviewCountProvider = Provider<int>((ref) {
  final cards = ref.watch(fsrsCardStoreProvider);
  int count = 0;
  for (final card in cards.values) {
    if (card.isDue && card.state != FsrsCardState.newCard) {
      count++;
    }
  }
  return count.clamp(0, _maxDailyReviewItems);
});

/// List of concepts due for review, sorted by overdue factor (most overdue
/// first), capped at [_maxDailyReviewItems] per day.
final dueReviewItemsProvider = Provider<List<DueReviewItem>>((ref) {
  final cards = ref.watch(fsrsCardStoreProvider);

  final dueItems = <DueReviewItem>[];
  for (final entry in cards.entries) {
    final card = entry.value;
    // Only include cards that are due and have been reviewed at least once.
    if (card.isDue && card.state != FsrsCardState.newCard) {
      dueItems.add(DueReviewItem(
        conceptId: entry.key,
        card: card,
        overdueFactor: card.overdueFactor,
      ));
    }
  }

  // Sort by overdue factor descending: most overdue items first.
  dueItems.sort((a, b) => b.overdueFactor.compareTo(a.overdueFactor));

  // Cap at daily maximum.
  if (dueItems.length > _maxDailyReviewItems) {
    return dueItems.sublist(0, _maxDailyReviewItems);
  }
  return dueItems;
});

// ---------------------------------------------------------------------------
// Review Session State
// ---------------------------------------------------------------------------

/// State for a dedicated review-only session.
class ReviewSessionState {
  const ReviewSessionState({
    this.isActive = false,
    this.items = const [],
    this.currentIndex = 0,
    this.results = const {},
    this.completedAt,
  });

  /// Whether the review session is currently running.
  final bool isActive;

  /// The items to review in this session.
  final List<DueReviewItem> items;

  /// Index of the current item being reviewed.
  final int currentIndex;

  /// Map of conceptId -> rating given by the student.
  final Map<String, FsrsRating> results;

  /// When the session was completed, or null if still active.
  final DateTime? completedAt;

  /// The current item being reviewed, or null if session is done.
  DueReviewItem? get currentItem {
    if (!isActive || currentIndex >= items.length) return null;
    return items[currentIndex];
  }

  /// Total number of items in this session.
  int get totalItems => items.length;

  /// Number of items already reviewed.
  int get reviewedCount => results.length;

  /// Whether all items have been reviewed.
  bool get isComplete => reviewedCount >= totalItems;

  /// Progress fraction [0.0, 1.0].
  double get progress =>
      totalItems > 0 ? reviewedCount / totalItems : 0.0;

  ReviewSessionState copyWith({
    bool? isActive,
    List<DueReviewItem>? items,
    int? currentIndex,
    Map<String, FsrsRating>? results,
    DateTime? completedAt,
  }) {
    return ReviewSessionState(
      isActive: isActive ?? this.isActive,
      items: items ?? this.items,
      currentIndex: currentIndex ?? this.currentIndex,
      results: results ?? this.results,
      completedAt: completedAt ?? this.completedAt,
    );
  }
}

/// Manages a dedicated review-only session.
class ReviewSessionNotifier extends StateNotifier<ReviewSessionState> {
  ReviewSessionNotifier({
    required this.fsrsScheduler,
    required this.cardStoreNotifier,
  }) : super(const ReviewSessionState());

  final FsrsScheduler fsrsScheduler;
  final FsrsCardStoreNotifier cardStoreNotifier;

  /// Start a new review session with the given due items.
  void startSession(List<DueReviewItem> items) {
    if (items.isEmpty) return;
    state = ReviewSessionState(
      isActive: true,
      items: items,
      currentIndex: 0,
      results: {},
    );
  }

  /// Record the student's rating for the current item and advance.
  void rateCurrentItem(FsrsRating rating) {
    final current = state.currentItem;
    if (current == null) return;

    // Update the FSRS card store with the new review.
    cardStoreNotifier.recordReview(
      current.conceptId,
      rating,
      fsrsScheduler,
    );

    // Record the result and advance.
    final updatedResults = {
      ...state.results,
      current.conceptId: rating,
    };

    final nextIndex = state.currentIndex + 1;
    final isComplete = nextIndex >= state.totalItems;

    state = state.copyWith(
      results: updatedResults,
      currentIndex: nextIndex,
      isActive: !isComplete,
      completedAt: isComplete ? DateTime.now() : null,
    );
  }

  /// End the session early.
  void endSession() {
    state = state.copyWith(
      isActive: false,
      completedAt: DateTime.now(),
    );
  }

  /// Reset session state.
  void reset() {
    state = const ReviewSessionState();
  }
}

/// Provider for the review session manager.
final reviewSessionProvider =
    StateNotifierProvider<ReviewSessionNotifier, ReviewSessionState>((ref) {
  final scheduler = ref.watch(fsrsSchedulerProvider);
  final cardStore = ref.watch(fsrsCardStoreProvider.notifier);
  return ReviewSessionNotifier(
    fsrsScheduler: scheduler,
    cardStoreNotifier: cardStore,
  );
});
