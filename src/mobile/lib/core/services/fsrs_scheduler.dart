// =============================================================================
// Cena Adaptive Learning Platform — FSRS-4.5 Spaced Repetition Scheduler
// =============================================================================
//
// Full FSRS-4.5 implementation with 15 learnable parameters (w0-w14).
// Reference: https://github.com/open-spaced-repetition/fsrs4anki/wiki/Algorithm
//
// The scheduler computes the optimal review interval for each concept based on
// the student's recall performance. It replaces the simplified shadow-mode FSRS
// in adaptive_difficulty_service.dart with a production-grade implementation.
// =============================================================================

import 'dart:math';

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

/// Card learning state in the FSRS state machine.
enum FsrsCardState {
  /// Brand new card, never reviewed.
  newCard,

  /// In the initial learning phase (short intervals).
  learning,

  /// Graduated to the review phase (growing intervals).
  review,

  /// Lapsed from review back to short intervals.
  relearning,
}

/// Student recall rating after reviewing a card.
enum FsrsRating {
  /// Complete failure to recall — reset stability.
  again,

  /// Recalled with significant difficulty.
  hard,

  /// Recalled with moderate effort (ideal).
  good,

  /// Recalled effortlessly — boost interval aggressively.
  easy,
}

// ---------------------------------------------------------------------------
// FsrsCard — Immutable card state
// ---------------------------------------------------------------------------

/// Represents the scheduling state of a single concept/card.
///
/// All fields are immutable; [FsrsScheduler.schedule] returns a new instance.
class FsrsCard {
  const FsrsCard({
    this.stability = 0.0,
    this.difficulty = 0.0,
    this.elapsedDays = 0,
    this.scheduledDays = 0,
    this.reps = 0,
    this.lapses = 0,
    this.state = FsrsCardState.newCard,
    this.lastReview,
  });

  /// Memory stability in days — expected half-life of recall probability.
  final double stability;

  /// Intrinsic difficulty of the card [0.0, 1.0] (0 = easy, 1 = hard).
  final double difficulty;

  /// Days since the last review.
  final int elapsedDays;

  /// Days until the next scheduled review.
  final int scheduledDays;

  /// Total number of successful reviews (non-lapse).
  final int reps;

  /// Total number of lapses (rating = Again after reaching review state).
  final int lapses;

  /// Current position in the FSRS state machine.
  final FsrsCardState state;

  /// Timestamp of the last review, or null if never reviewed.
  final DateTime? lastReview;

  /// Whether this card is due for review right now.
  bool get isDue {
    if (state == FsrsCardState.newCard) return true;
    if (lastReview == null) return true;
    final daysSince = DateTime.now().difference(lastReview!).inDays;
    return daysSince >= scheduledDays;
  }

  /// Estimated retrievability [0.0, 1.0] — probability of successful recall.
  ///
  /// Uses the FSRS power-law forgetting curve:
  ///   R(t) = (1 + t / (9 * S))^(-1)
  /// where t = elapsed days, S = stability.
  double get retrievability {
    if (state == FsrsCardState.newCard || lastReview == null) return 0.0;
    if (stability <= 0.0) return 0.0;
    final t = DateTime.now().difference(lastReview!).inDays.toDouble();
    // FSRS forgetting curve: R = (1 + t/(9*S))^-1
    return pow(1.0 + t / (9.0 * stability), -1.0).toDouble();
  }

  /// The overdue factor: how far past due this card is.
  /// > 1.0 means overdue, < 1.0 means not yet due.
  double get overdueFactor {
    if (scheduledDays <= 0) return double.infinity;
    if (lastReview == null) return double.infinity;
    final daysSince = DateTime.now().difference(lastReview!).inDays.toDouble();
    return daysSince / scheduledDays;
  }

  FsrsCard copyWith({
    double? stability,
    double? difficulty,
    int? elapsedDays,
    int? scheduledDays,
    int? reps,
    int? lapses,
    FsrsCardState? state,
    DateTime? lastReview,
  }) {
    return FsrsCard(
      stability: stability ?? this.stability,
      difficulty: difficulty ?? this.difficulty,
      elapsedDays: elapsedDays ?? this.elapsedDays,
      scheduledDays: scheduledDays ?? this.scheduledDays,
      reps: reps ?? this.reps,
      lapses: lapses ?? this.lapses,
      state: state ?? this.state,
      lastReview: lastReview ?? this.lastReview,
    );
  }
}

// ---------------------------------------------------------------------------
// FsrsScheduler — Core FSRS-4.5 algorithm
// ---------------------------------------------------------------------------

/// Full FSRS-4.5 scheduler with 15 learnable parameters.
///
/// The 15 weights (w0-w14) are population-level defaults from the FSRS-4.5
/// paper. In production, these would be personalized per-student via the
/// server-side optimizer after ~100 reviews.
class FsrsScheduler {
  FsrsScheduler({List<double>? weights}) : w = weights ?? defaultWeights {
    assert(w.length == 15, 'FSRS-4.5 requires exactly 15 weights');
  }

  /// The 15 learnable parameters.
  final List<double> w;

  /// FSRS-4.5 published default population weights.
  ///
  /// w0-w3:  Initial stability for each rating (Again, Hard, Good, Easy).
  /// w4:     Difficulty mean reversion rate.
  /// w5:     Difficulty mean reversion target.
  /// w6:     Difficulty update sensitivity.
  /// w7:     Stability increase base factor.
  /// w8:     Stability increase — difficulty attenuation.
  /// w9:     Stability increase — stability attenuation.
  /// w10:    Stability increase — retrievability bonus.
  /// w11:    Stability after lapse — base factor.
  /// w12:    Stability after lapse — difficulty effect.
  /// w13:    Stability after lapse — stability effect.
  /// w14:    Stability after lapse — retrievability effect.
  static const List<double> defaultWeights = [
    0.4072, // w0:  initial stability for Again
    1.1829, // w1:  initial stability for Hard
    3.1262, // w2:  initial stability for Good
    15.4722, // w3:  initial stability for Easy
    7.2102, // w4:  difficulty mean reversion rate
    0.5316, // w5:  difficulty mean reversion target
    1.0651, // w6:  difficulty update sensitivity
    0.0046, // w7:  stability increase base
    1.5071, // w8:  stability increase — difficulty attenuation
    0.1367, // w9:  stability increase — stability attenuation
    1.0139, // w10: stability increase — retrievability bonus
    2.1059, // w11: lapse stability base
    0.0210, // w12: lapse stability — difficulty effect
    0.3440, // w13: lapse stability — stability effect
    1.3972, // w14: lapse stability — retrievability effect
  ];

  /// Target retrievability at next review (90% = FSRS default).
  static const double _requestedRetention = 0.9;

  // ---- Public API ----

  /// Schedule the next review for [card] after the student rates it [rating].
  ///
  /// Returns a new [FsrsCard] with updated stability, difficulty, interval,
  /// and state machine position.
  FsrsCard schedule(FsrsCard card, FsrsRating rating) {
    final now = DateTime.now();

    // Calculate elapsed days since last review.
    final elapsed = card.lastReview != null
        ? now.difference(card.lastReview!).inDays
        : 0;

    switch (card.state) {
      case FsrsCardState.newCard:
        return _scheduleNewCard(card, rating, now);
      case FsrsCardState.learning:
      case FsrsCardState.relearning:
        return _scheduleLearningCard(card, rating, now, elapsed);
      case FsrsCardState.review:
        return _scheduleReviewCard(card, rating, now, elapsed);
    }
  }

  /// Auto-grade a student response based on correctness and response time.
  ///
  /// - correct + fast (< 60% of expected time) -> Easy
  /// - correct + normal -> Good
  /// - wrong + close answer (partial credit signal) -> Hard
  /// - wrong + far from answer -> Again
  ///
  /// [correct]: whether the answer was correct.
  /// [responseTimeMs]: actual response time in milliseconds.
  /// [expectedTimeMs]: expected time for this difficulty level.
  static FsrsRating autoGrade({
    required bool correct,
    required int responseTimeMs,
    required int expectedTimeMs,
  }) {
    if (expectedTimeMs <= 0) {
      return correct ? FsrsRating.good : FsrsRating.again;
    }

    final timeRatio = responseTimeMs / expectedTimeMs;

    if (correct) {
      // Fast correct = effortless recall -> Easy
      if (timeRatio < 0.6) return FsrsRating.easy;
      // Normal speed correct -> Good
      if (timeRatio < 1.5) return FsrsRating.good;
      // Slow correct = struggled but got it -> Hard
      return FsrsRating.hard;
    } else {
      // Wrong but answered quickly (close/careless error) -> Hard
      if (timeRatio < 0.8) return FsrsRating.hard;
      // Wrong and slow (genuine knowledge gap) -> Again
      return FsrsRating.again;
    }
  }

  // ---- Private scheduling methods ----

  /// Schedule a brand-new card (first review ever).
  FsrsCard _scheduleNewCard(
      FsrsCard card, FsrsRating rating, DateTime now) {
    // Initial stability is determined by the rating (w0-w3).
    final initStability = _initialStability(rating);
    // Initial difficulty is derived from the rating.
    final initDifficulty = _initialDifficulty(rating);

    // For new cards, the interval depends on the rating:
    // Again -> stay in learning (1 minute conceptually, 0 days)
    // Hard  -> stay in learning (short interval)
    // Good  -> graduate to review
    // Easy  -> graduate to review with longer interval
    final FsrsCardState nextState;
    final int interval;

    switch (rating) {
      case FsrsRating.again:
        nextState = FsrsCardState.learning;
        interval = 0; // Re-present same day
      case FsrsRating.hard:
        nextState = FsrsCardState.learning;
        interval = 0; // Re-present same day
      case FsrsRating.good:
        nextState = FsrsCardState.review;
        interval = _nextInterval(initStability);
      case FsrsRating.easy:
        nextState = FsrsCardState.review;
        // Easy gets a bonus multiplier on initial interval.
        interval = max(
          _nextInterval(initStability),
          _nextInterval(initStability * 1.3),
        );
    }

    return card.copyWith(
      stability: initStability,
      difficulty: initDifficulty,
      scheduledDays: interval,
      elapsedDays: 0,
      reps: rating == FsrsRating.again ? 0 : card.reps + 1,
      lapses: rating == FsrsRating.again ? card.lapses + 1 : card.lapses,
      state: nextState,
      lastReview: now,
    );
  }

  /// Schedule a card currently in learning or relearning phase.
  FsrsCard _scheduleLearningCard(
      FsrsCard card, FsrsRating rating, DateTime now, int elapsed) {
    switch (rating) {
      case FsrsRating.again:
        // Stay in learning, reset stability.
        return card.copyWith(
          stability: _initialStability(FsrsRating.again),
          scheduledDays: 0,
          elapsedDays: elapsed,
          lapses: card.lapses + 1,
          lastReview: now,
        );
      case FsrsRating.hard:
        // Stay in learning, slight stability bump.
        final newStability = max(
          card.stability,
          _initialStability(FsrsRating.hard),
        );
        return card.copyWith(
          stability: newStability,
          scheduledDays: 1,
          elapsedDays: elapsed,
          lastReview: now,
        );
      case FsrsRating.good:
        // Graduate to review.
        final newStability = max(
          card.stability,
          _initialStability(FsrsRating.good),
        );
        return card.copyWith(
          stability: newStability,
          difficulty: _nextDifficulty(card.difficulty, rating),
          scheduledDays: _nextInterval(newStability),
          elapsedDays: elapsed,
          reps: card.reps + 1,
          state: FsrsCardState.review,
          lastReview: now,
        );
      case FsrsRating.easy:
        // Graduate to review with boosted interval.
        final newStability = max(
          card.stability,
          _initialStability(FsrsRating.easy),
        );
        final boostedStability = newStability * 1.3;
        return card.copyWith(
          stability: boostedStability,
          difficulty: _nextDifficulty(card.difficulty, rating),
          scheduledDays: _nextInterval(boostedStability),
          elapsedDays: elapsed,
          reps: card.reps + 1,
          state: FsrsCardState.review,
          lastReview: now,
        );
    }
  }

  /// Schedule a card in the review phase.
  FsrsCard _scheduleReviewCard(
      FsrsCard card, FsrsRating rating, DateTime now, int elapsed) {
    // Current retrievability at the time of review.
    final retrievability = card.stability > 0
        ? pow(1.0 + elapsed / (9.0 * card.stability), -1.0).toDouble()
        : 0.0;

    switch (rating) {
      case FsrsRating.again:
        // Lapse: drop back to relearning with reduced stability.
        final lapseStability =
            _stabilityAfterLapse(card.difficulty, card.stability, retrievability);
        return card.copyWith(
          stability: lapseStability,
          difficulty: _nextDifficulty(card.difficulty, rating),
          scheduledDays: 0, // Re-present same day in relearning
          elapsedDays: elapsed,
          lapses: card.lapses + 1,
          state: FsrsCardState.relearning,
          lastReview: now,
        );
      case FsrsRating.hard:
        final newStability = _nextStability(
          card.difficulty,
          card.stability,
          retrievability,
          rating,
        );
        // Hard penalty: interval is 1.2x current elapsed or calculated,
        // whichever is shorter.
        final calcInterval = _nextInterval(newStability);
        final hardInterval = max(elapsed + 1, (calcInterval * 0.8).round());
        return card.copyWith(
          stability: newStability,
          difficulty: _nextDifficulty(card.difficulty, rating),
          scheduledDays: hardInterval,
          elapsedDays: elapsed,
          reps: card.reps + 1,
          lastReview: now,
        );
      case FsrsRating.good:
        final newStability = _nextStability(
          card.difficulty,
          card.stability,
          retrievability,
          rating,
        );
        return card.copyWith(
          stability: newStability,
          difficulty: _nextDifficulty(card.difficulty, rating),
          scheduledDays: _nextInterval(newStability),
          elapsedDays: elapsed,
          reps: card.reps + 1,
          lastReview: now,
        );
      case FsrsRating.easy:
        final newStability = _nextStability(
          card.difficulty,
          card.stability,
          retrievability,
          rating,
        );
        // Easy bonus: multiply interval by 1.3.
        final easyInterval = max(
          _nextInterval(newStability),
          (_nextInterval(newStability) * 1.3).round(),
        );
        return card.copyWith(
          stability: newStability,
          difficulty: _nextDifficulty(card.difficulty, rating),
          scheduledDays: easyInterval,
          elapsedDays: elapsed,
          reps: card.reps + 1,
          lastReview: now,
        );
    }
  }

  // ---- FSRS-4.5 core equations ----

  /// Initial stability for a new card based on rating.
  /// S_0(G) = w[G-1] where G is the rating index (1-4 for Again-Easy).
  double _initialStability(FsrsRating rating) {
    switch (rating) {
      case FsrsRating.again:
        return w[0]; // w0
      case FsrsRating.hard:
        return w[1]; // w1
      case FsrsRating.good:
        return w[2]; // w2
      case FsrsRating.easy:
        return w[3]; // w3
    }
  }

  /// Initial difficulty for a new card.
  /// D_0(G) = w5 - exp(w6 * (G - 1)) + 1
  /// where G is rating index: Again=1, Hard=2, Good=3, Easy=4.
  double _initialDifficulty(FsrsRating rating) {
    final g = rating.index + 1; // Again=1, Hard=2, Good=3, Easy=4
    final d = w[5] - exp(w[6] * (g - 1)) + 1;
    return d.clamp(0.0, 1.0);
  }

  /// Next difficulty after a review.
  /// D' = w4 * D_0(3) + (1 - w4) * (D - w6 * (G - 3))
  /// Mean-reverts toward D_0(Good) with rate w4.
  double _nextDifficulty(double currentD, FsrsRating rating) {
    final g = rating.index + 1;
    final d0Good = _initialDifficulty(FsrsRating.good);
    // Mean reversion formula.
    final newD = w[4] * d0Good + (1 - w[4]) * (currentD - w[6] * (g - 3));
    return newD.clamp(0.0, 1.0);
  }

  /// Next stability for a successful review (Good, Hard, or Easy).
  ///
  /// S' = S * (e^(w7) * (11 - D) * S^(-w8) * (e^(w9 * (1 - R)) - 1) *
  ///       hardPenaltyOrEasyBonus + 1)
  ///
  /// where D = difficulty, S = current stability, R = retrievability.
  double _nextStability(
    double difficulty,
    double stability,
    double retrievability,
    FsrsRating rating,
  ) {
    // Hard penalty or Easy bonus multiplier.
    final double modifier;
    switch (rating) {
      case FsrsRating.hard:
        modifier = 0.85; // Hard penalty
      case FsrsRating.easy:
        modifier = 1.30; // Easy bonus
      case FsrsRating.good:
        modifier = 1.00; // Neutral
      case FsrsRating.again:
        modifier = 1.00; // Not used for Again (lapse path)
    }

    // FSRS-4.5 stability increase formula:
    // sinc = e^w7 * (11 - D) * S^(-w8) * (e^(w9 * (1-R)) - 1) * modifier
    final sinc = exp(w[7]) *
        (11.0 - difficulty * 10.0) *
        pow(stability, -w[8]) *
        (exp(w[9] * (1.0 - retrievability)) - 1.0) *
        modifier;

    // New stability = S * (sinc + 1), minimum 0.1 days.
    return max(0.1, stability * (sinc + 1.0));
  }

  /// Stability after a lapse (rating = Again on a review card).
  ///
  /// S_lapse = w11 * D^(-w12) * ((S+1)^w13 - 1) * e^(w14 * (1-R))
  double _stabilityAfterLapse(
    double difficulty,
    double stability,
    double retrievability,
  ) {
    final lapseS = w[11] *
        pow(difficulty * 10.0 + 0.1, -w[12]) *
        (pow(stability + 1, w[13]) - 1) *
        exp(w[14] * (1.0 - retrievability));
    // Minimum stability of 0.1 days; must not exceed pre-lapse stability.
    return lapseS.clamp(0.1, stability);
  }

  /// Convert stability to a review interval in days.
  ///
  /// Interval = S * 9 * (1/R - 1)
  /// where R = requested retention (default 0.9 = 90%).
  int _nextInterval(double stability) {
    if (stability <= 0) return 1;
    // Solve R = (1 + t/(9*S))^-1 for t:
    // t = 9 * S * (R^-1 - 1)
    // But we want the interval where R = _requestedRetention.
    // So: interval = 9 * S * (1/_requestedRetention - 1)
    final interval = 9.0 * stability * (1.0 / _requestedRetention - 1.0);
    return max(1, interval.round());
  }
}
