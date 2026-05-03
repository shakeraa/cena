// =============================================================================
// Cena Adaptive Learning Platform — Adaptive Interleaving (MOB-049)
// =============================================================================
//
// Controls the probability of interleaving (mixing) topics during a session
// based on the student's average mastery level.
//
// Research basis:
// - Novice students benefit from BLOCKED practice (same topic repeated)
//   because they need to build initial schema before mixing.
// - As mastery increases, INTERLEAVED practice (mixing topics) produces
//   stronger long-term retention via desirable difficulty.
// - The transition should be gradual, not a hard switch.
//
// Formula:
//   P(interleave) = clamp(0.0, 0.7, (avgMastery - 0.3) * 1.17)
//
// Key breakpoints:
//   P(known) < 0.30 (novice):     P(interleave) = 0.0 (pure blocking)
//   P(known) = 0.50 (developing): P(interleave) ~ 0.23
//   P(known) = 0.70 (proficient): P(interleave) ~ 0.47
//   P(known) >= 0.90 (mastery):   P(interleave) = 0.70 (max mixing)
//
// The 0.7 cap ensures even highly proficient students get some blocked
// practice to maintain fundamental skills.
// =============================================================================

import 'dart:math';

/// Computes interleaving probability and selects concepts for mixed practice.
class AdaptiveInterleaving {
  const AdaptiveInterleaving._();

  /// Random number generator for stochastic interleaving decisions.
  static final _rng = Random();

  /// Maximum interleaving probability cap (70%).
  /// Even at full mastery, 30% of questions remain on the current topic
  /// to maintain depth and avoid context-switching fatigue.
  static const double maxInterleavingP = 0.7;

  /// Mastery threshold below which no interleaving occurs.
  /// Students below P(known) = 0.30 are still building initial schema
  /// and need blocked practice exclusively.
  static const double noviceThreshold = 0.3;

  /// Scaling factor: maps (mastery - noviceThreshold) to [0, maxInterleavingP].
  /// Calculated as: maxInterleavingP / (1.0 - noviceThreshold) = 0.7 / 0.6 = 1.1667
  /// Rounded to 1.17 per the spec for readability.
  static const double _scalingFactor = 1.17;

  /// Compute the interleaving probability for a given mastery level.
  ///
  /// [mastery] is the student's average P(known) across active concepts,
  /// in the range [0.0, 1.0].
  ///
  /// Returns: probability of interleaving [0.0, 0.7].
  ///
  /// Examples:
  ///   probability(0.10) => 0.0   (pure blocking for novice)
  ///   probability(0.30) => 0.0   (threshold — still blocking)
  ///   probability(0.50) => 0.234 (starting to mix)
  ///   probability(0.70) => 0.468 (moderate mixing)
  ///   probability(0.90) => 0.70  (max mixing, capped)
  ///   probability(1.00) => 0.70  (max mixing, capped)
  static double probability(double mastery) {
    // Clamp mastery to valid range.
    final m = mastery.clamp(0.0, 1.0);

    // Below novice threshold: pure blocked practice.
    if (m <= noviceThreshold) return 0.0;

    // Linear ramp from 0.0 to maxInterleavingP.
    final p = (m - noviceThreshold) * _scalingFactor;

    // Clamp to [0.0, maxInterleavingP].
    return min(maxInterleavingP, max(0.0, p));
  }

  /// Decide whether to interleave the next question.
  ///
  /// Performs a random check against the interleaving probability
  /// for the given mastery level.
  ///
  /// [mastery] is the student's average P(known) across active concepts.
  /// Returns true if the next question should be from a different concept.
  static bool shouldInterleave(double mastery) {
    final p = probability(mastery);
    if (p <= 0.0) return false;
    if (p >= 1.0) return true; // Safety: never actually reaches 1.0
    return _rng.nextDouble() < p;
  }

  /// Select an interleaved concept to practice.
  ///
  /// When interleaving is triggered, this selects which concept to switch to.
  /// The selection is weighted to prefer concepts that:
  ///   1. Are different from the current concept
  ///   2. Have moderate mastery (in the "desirable difficulty" zone)
  ///   3. Have been practiced recently enough to benefit from interleaving
  ///
  /// [currentConceptId] is the concept the student is currently practicing.
  /// [availableConcepts] is the list of all concept IDs in the session's scope.
  /// [masteryMap] maps conceptId -> P(known) for each concept.
  ///
  /// Returns the selected concept ID, or [currentConceptId] if no suitable
  /// alternative exists.
  static String selectInterleavedConcept({
    required String currentConceptId,
    required List<String> availableConcepts,
    required Map<String, double> masteryMap,
  }) {
    // Filter out the current concept.
    final candidates = availableConcepts
        .where((id) => id != currentConceptId)
        .toList();

    if (candidates.isEmpty) return currentConceptId;

    // Weight each candidate by how "interleaving-beneficial" it is.
    // The ideal interleaving target is a concept the student knows reasonably
    // well (mastery 0.3-0.8) — too low and they need blocked practice on it,
    // too high and there's nothing to gain from mixing.
    final weights = <String, double>{};
    double totalWeight = 0.0;

    for (final id in candidates) {
      final mastery = (masteryMap[id] ?? 0.0).clamp(0.0, 1.0);

      // Bell-curve weighting centered at mastery 0.55.
      // Concepts near 0.55 mastery are ideal interleaving targets.
      // They're known well enough to benefit from mixing but not
      // so well that practice is wasted.
      final deviation = mastery - 0.55;
      final weight = exp(-2.0 * deviation * deviation);

      // Floor weight at 0.05 so every concept has some chance.
      final clampedWeight = max(0.05, weight);
      weights[id] = clampedWeight;
      totalWeight += clampedWeight;
    }

    if (totalWeight <= 0.0) {
      // Fallback: random selection.
      return candidates[_rng.nextInt(candidates.length)];
    }

    // Weighted random selection.
    double roll = _rng.nextDouble() * totalWeight;
    for (final entry in weights.entries) {
      roll -= entry.value;
      if (roll <= 0) return entry.key;
    }

    // Fallback (should not reach here due to floating point).
    return candidates.last;
  }
}
