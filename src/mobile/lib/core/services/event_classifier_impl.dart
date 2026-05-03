// =============================================================================
// Cena Adaptive Learning Platform — EventClassifierImpl
// Classifies event types into sync tiers per the contract map.
// =============================================================================

import '../models/domain_models.dart';
import 'offline_sync_service.dart';

/// Classifies events into three tiers that control how the server handles
/// them during a sync cycle.
///
/// Contract classification map (from MOB-004 spec):
/// - Unconditional : AddAnnotation, SkipQuestion, SwitchApproach
/// - Conditional   : AttemptConcept, RequestHint, StartSession, EndSession
/// - ServerAuthoritative: MasteryUpdate, XpCalculation, StreakCalculation
///
/// Any event type not in the map is treated as Conditional by default.
class EventClassifierImpl implements EventClassifier {
  static const Map<String, EventClassification> _map = {
    // Unconditional — always accepted by server without revalidation.
    'AddAnnotation': EventClassification.unconditional,
    'SkipQuestion': EventClassification.unconditional,
    'SwitchApproach': EventClassification.unconditional,

    // Conditional — server validates context before accepting.
    'AttemptConcept': EventClassification.conditional,
    'RequestHint': EventClassification.conditional,
    'StartSession': EventClassification.conditional,
    'EndSession': EventClassification.conditional,

    // Server-authoritative — server recalculates entirely.
    'MasteryUpdate': EventClassification.serverAuthoritative,
    'XpCalculation': EventClassification.serverAuthoritative,
    'StreakCalculation': EventClassification.serverAuthoritative,
  };

  @override
  EventClassification classify(String eventType) {
    return _map[eventType] ?? EventClassification.conditional;
  }

  @override
  double weightFor(EventClassification classification) {
    switch (classification) {
      case EventClassification.unconditional:
        return 1.0;
      case EventClassification.conditional:
        return 0.75;
      case EventClassification.serverAuthoritative:
        return 0.0;
    }
  }
}
