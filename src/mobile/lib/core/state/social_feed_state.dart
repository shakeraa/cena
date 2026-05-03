// =============================================================================
// Cena Adaptive Learning Platform — Social Feed State (MOB-044)
// =============================================================================
// Blueprint §10: Social Proof Without Shame — aggregate stats only,
// positive events only, privacy-respecting social feed.
// =============================================================================

import 'dart:async';
import 'dart:collection';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/websocket_service.dart';
import 'app_state.dart';
import 'derived_providers.dart' show webSocketServiceProvider;

// ---------------------------------------------------------------------------
// Event types
// ---------------------------------------------------------------------------

/// Types of positive social events displayed in the class feed.
enum SocialFeedEventType {
  conceptMastered,
  badgeEarned,
  streakMilestone,
  teacherEndorsement,
  questCompleted,
}

// ---------------------------------------------------------------------------
// Feed item model
// ---------------------------------------------------------------------------

/// A single item in the class social feed.
/// All events are positive — errors and failures are never shown.
class SocialFeedItem {
  const SocialFeedItem({
    required this.id,
    required this.eventType,
    required this.studentDisplayName,
    required this.detail,
    required this.timestamp,
    required this.classId,
    this.isOptedOut = false,
    this.reactions = const {},
  });

  final String id;
  final SocialFeedEventType eventType;

  /// Display name (may be anonymised if student opted out).
  final String studentDisplayName;

  /// Human-readable detail, e.g. "Quadratic Equations".
  final String detail;

  final DateTime timestamp;
  final String classId;

  /// Whether the originating student has opted out of social visibility.
  final bool isOptedOut;

  /// Reaction counts keyed by reaction type (thumbsUp, star, clap).
  final Map<String, int> reactions;

  factory SocialFeedItem.fromJson(Map<String, dynamic> json) {
    return SocialFeedItem(
      id: json['id'] as String? ?? '',
      eventType: _parseEventType(json['eventType'] as String?),
      studentDisplayName: json['studentDisplayName'] as String? ?? '',
      detail: json['detail'] as String? ?? '',
      timestamp: json['timestamp'] != null
          ? DateTime.parse(json['timestamp'] as String)
          : DateTime.now(),
      classId: json['classId'] as String? ?? '',
      isOptedOut: json['isOptedOut'] as bool? ?? false,
      reactions: _parseReactions(json['reactions']),
    );
  }

  SocialFeedItem copyWith({
    Map<String, int>? reactions,
  }) {
    return SocialFeedItem(
      id: id,
      eventType: eventType,
      studentDisplayName: studentDisplayName,
      detail: detail,
      timestamp: timestamp,
      classId: classId,
      isOptedOut: isOptedOut,
      reactions: reactions ?? this.reactions,
    );
  }

  static SocialFeedEventType _parseEventType(String? value) {
    switch (value) {
      case 'conceptMastered':
        return SocialFeedEventType.conceptMastered;
      case 'badgeEarned':
        return SocialFeedEventType.badgeEarned;
      case 'streakMilestone':
        return SocialFeedEventType.streakMilestone;
      case 'teacherEndorsement':
        return SocialFeedEventType.teacherEndorsement;
      case 'questCompleted':
        return SocialFeedEventType.questCompleted;
      default:
        return SocialFeedEventType.conceptMastered;
    }
  }

  static Map<String, int> _parseReactions(dynamic raw) {
    if (raw is Map) {
      return raw.map(
        (k, v) => MapEntry(k.toString(), (v as num?)?.toInt() ?? 0),
      );
    }
    return const {};
  }
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/// k-anonymity threshold: counters are only shown when at least this many
/// students have participated, preventing identification by aggregation.
const int kAnonymityThreshold = 10;

/// Maximum number of feed items kept in the circular buffer.
const int _maxBufferSize = 200;

/// Maps WebSocket event types to SocialFeedEventType.
/// Only positive events are mapped; others are ignored.
const Map<String, SocialFeedEventType> _positiveEventMapping = {
  'SocialConceptMastered': SocialFeedEventType.conceptMastered,
  'SocialBadgeEarned': SocialFeedEventType.badgeEarned,
  'SocialStreakMilestone': SocialFeedEventType.streakMilestone,
  'SocialTeacherEndorsement': SocialFeedEventType.teacherEndorsement,
  'SocialQuestCompleted': SocialFeedEventType.questCompleted,
};

// ---------------------------------------------------------------------------
// Feed state
// ---------------------------------------------------------------------------

/// Immutable snapshot of the social feed.
class SocialFeedState {
  const SocialFeedState({
    this.items = const [],
    this.isLoading = false,
    this.activeStudentCount = 0,
    this.error,
  });

  final List<SocialFeedItem> items;
  final bool isLoading;

  /// Number of students active today (for aggregate counter display).
  final int activeStudentCount;
  final String? error;

  /// Whether the aggregate counter can be displayed (k-anonymity check).
  bool get canShowAggregateCount => activeStudentCount >= kAnonymityThreshold;

  SocialFeedState copyWith({
    List<SocialFeedItem>? items,
    bool? isLoading,
    int? activeStudentCount,
    String? error,
    bool clearError = false,
  }) {
    return SocialFeedState(
      items: items ?? this.items,
      isLoading: isLoading ?? this.isLoading,
      activeStudentCount: activeStudentCount ?? this.activeStudentCount,
      error: clearError ? null : (error ?? this.error),
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier
// ---------------------------------------------------------------------------

/// Manages the class social feed state.
///
/// Listens to WebSocket events, filters to positive-only events,
/// respects student opt-out preferences, and maintains a circular buffer
/// of the most recent [_maxBufferSize] items.
class SocialFeedNotifier extends StateNotifier<SocialFeedState> {
  SocialFeedNotifier({
    required this.webSocketService,
    required this.apiClient,
    required this.classId,
  }) : super(const SocialFeedState(isLoading: true)) {
    _subscribeToEvents();
    _loadInitialFeed();
  }

  final WebSocketService webSocketService;
  final dynamic apiClient;
  final String classId;
  final List<StreamSubscription<dynamic>> _subscriptions = [];

  /// Circular buffer backing the feed. Newest items are at the front.
  final Queue<SocialFeedItem> _buffer = Queue<SocialFeedItem>();

  /// Set of student IDs that have opted out of social visibility.
  final Set<String> _optedOutStudentIds = {};

  // ---- Public API ----

  /// Refresh the feed from the server (pull-to-refresh).
  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, clearError: true);
    await _loadInitialFeed();
  }

  /// Add a reaction to a feed item.
  void addReaction(String itemId, String reactionType) {
    final updatedItems = state.items.map((item) {
      if (item.id == itemId) {
        final newReactions = Map<String, int>.from(item.reactions);
        newReactions[reactionType] = (newReactions[reactionType] ?? 0) + 1;
        return item.copyWith(reactions: newReactions);
      }
      return item;
    }).toList();
    state = state.copyWith(items: updatedItems);
  }

  // ---- Internal ----

  Future<void> _loadInitialFeed() async {
    try {
      final response = await apiClient.get<Map<String, dynamic>>(
        '/social/feed',
        queryParameters: {'classId': classId, 'limit': _maxBufferSize},
      );
      final data = response.data as Map<String, dynamic>? ?? {};
      final rawItems = data['items'] as List<dynamic>? ?? [];
      final activeCount = (data['activeStudentCount'] as num?)?.toInt() ?? 0;
      final optedOut = (data['optedOutStudentIds'] as List<dynamic>?)
              ?.map((e) => e.toString())
              .toSet() ??
          {};

      _optedOutStudentIds
        ..clear()
        ..addAll(optedOut);
      _buffer.clear();

      for (final raw in rawItems) {
        final item = SocialFeedItem.fromJson(raw as Map<String, dynamic>);
        if (!_shouldFilter(item)) {
          _buffer.addFirst(item);
        }
      }

      state = SocialFeedState(
        items: _buffer.toList(),
        activeStudentCount: activeCount,
      );
    } catch (e) {
      state = state.copyWith(
        isLoading: false,
        error: 'Could not load feed',
      );
    }
  }

  void _subscribeToEvents() {
    _subscriptions.add(
      webSocketService.messageStream.listen(_handleMessage),
    );
  }

  void _handleMessage(MessageEnvelope envelope) {
    final eventType = _positiveEventMapping[envelope.type];
    if (eventType == null) {
      // Not a social feed event or not a positive event — ignore.
      if (envelope.type == 'SocialActiveCount') {
        _updateActiveCount(envelope.payload);
      }
      return;
    }

    final item = SocialFeedItem(
      id: envelope.payload['id'] as String? ?? DateTime.now().toIso8601String(),
      eventType: eventType,
      studentDisplayName:
          envelope.payload['studentDisplayName'] as String? ?? '',
      detail: envelope.payload['detail'] as String? ?? '',
      timestamp: envelope.receivedAt,
      classId: envelope.payload['classId'] as String? ?? classId,
      isOptedOut: envelope.payload['isOptedOut'] as bool? ?? false,
    );

    if (_shouldFilter(item)) return;

    _buffer.addFirst(item);

    // Maintain circular buffer size.
    while (_buffer.length > _maxBufferSize) {
      _buffer.removeLast();
    }

    state = state.copyWith(items: _buffer.toList());
  }

  void _updateActiveCount(Map<String, dynamic> payload) {
    final count = (payload['count'] as num?)?.toInt() ?? 0;
    state = state.copyWith(activeStudentCount: count);
  }

  /// Filter out events from opted-out students.
  bool _shouldFilter(SocialFeedItem item) {
    return item.isOptedOut || _optedOutStudentIds.contains(item.id);
  }

  @override
  void dispose() {
    for (final sub in _subscriptions) {
      sub.cancel();
    }
    super.dispose();
  }
}

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

/// Whether the current student has opted in to social feed visibility.
/// Persisted via shared preferences; defaults to false (opt-in required).
final socialFeedEnabledProvider = StateProvider<bool>((ref) => false);

/// The social feed state provider, scoped to a class.
/// Requires classId to be passed via family or provider override.
final socialFeedProvider = StateNotifierProvider.autoDispose
    .family<SocialFeedNotifier, SocialFeedState, String>(
  (ref, classId) {
    return SocialFeedNotifier(
      webSocketService: ref.watch(webSocketServiceProvider),
      apiClient: ref.watch(apiClientProvider),
      classId: classId,
    );
  },
);
