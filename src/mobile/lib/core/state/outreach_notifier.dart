// =============================================================================
// Cena Adaptive Learning Platform — Outreach Notifier
// Manages in-app notifications and streak expiry warnings.
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------

/// An in-app notification surfaced to the student.
class AppNotification {
  const AppNotification({
    required this.id,
    required this.title,
    required this.body,
    required this.createdAt,
    this.isRead = false,
    this.actionRoute,
  });

  final String id;
  final String title;
  final String body;
  final DateTime createdAt;
  final bool isRead;

  /// Optional deep-link route, e.g. "/session/start" or "/graph/concept/42".
  final String? actionRoute;

  AppNotification copyWithRead() {
    return AppNotification(
      id: id,
      title: title,
      body: body,
      createdAt: createdAt,
      isRead: true,
      actionRoute: actionRoute,
    );
  }
}

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

/// Immutable snapshot of the outreach / notification state.
class OutreachState {
  const OutreachState({
    this.pendingNotifications = const [],
    this.streakExpiresAt,
    this.isStreakWarningActive = false,
    this.lastNotificationDismissedAt,
  });

  final List<AppNotification> pendingNotifications;
  final DateTime? streakExpiresAt;
  final bool isStreakWarningActive;
  final DateTime? lastNotificationDismissedAt;

  int get unreadCount =>
      pendingNotifications.where((n) => !n.isRead).length;

  OutreachState copyWith({
    List<AppNotification>? pendingNotifications,
    DateTime? streakExpiresAt,
    bool? isStreakWarningActive,
    DateTime? lastNotificationDismissedAt,
    bool clearStreakExpiry = false,
  }) {
    return OutreachState(
      pendingNotifications:
          pendingNotifications ?? this.pendingNotifications,
      streakExpiresAt:
          clearStreakExpiry ? null : (streakExpiresAt ?? this.streakExpiresAt),
      isStreakWarningActive:
          isStreakWarningActive ?? this.isStreakWarningActive,
      lastNotificationDismissedAt:
          lastNotificationDismissedAt ?? this.lastNotificationDismissedAt,
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier
// ---------------------------------------------------------------------------

/// Manages in-app notifications and streak expiry warnings.
///
/// No WebSocket dependency — notifications arrive via FCM/local push and are
/// forwarded here by the push notification handler at startup.
class OutreachNotifier extends StateNotifier<OutreachState> {
  OutreachNotifier() : super(const OutreachState());

  // ---- Notifications ----

  /// Add a new notification to the pending list.
  void addNotification(AppNotification notification) {
    state = state.copyWith(
      pendingNotifications: [...state.pendingNotifications, notification],
    );
  }

  /// Mark a specific notification as read by id.
  void markRead(String notificationId) {
    state = state.copyWith(
      pendingNotifications: state.pendingNotifications.map((n) {
        return n.id == notificationId ? n.copyWithRead() : n;
      }).toList(),
    );
  }

  /// Mark all notifications as read.
  void markAllRead() {
    state = state.copyWith(
      pendingNotifications:
          state.pendingNotifications.map((n) => n.copyWithRead()).toList(),
    );
  }

  /// Dismiss (remove) a specific notification by id.
  void dismiss(String notificationId) {
    state = state.copyWith(
      pendingNotifications: state.pendingNotifications
          .where((n) => n.id != notificationId)
          .toList(),
      lastNotificationDismissedAt: DateTime.now(),
    );
  }

  /// Dismiss all notifications.
  void dismissAll() {
    state = state.copyWith(
      pendingNotifications: const [],
      lastNotificationDismissedAt: DateTime.now(),
    );
  }

  // ---- Streak warnings ----

  /// Activate streak expiry warning shown when streak will reset at midnight.
  void warnStreakExpiring(DateTime expiresAt) {
    state = state.copyWith(
      streakExpiresAt: expiresAt,
      isStreakWarningActive: true,
    );
  }

  /// Dismiss the streak warning after the student acknowledges it.
  void dismissStreakWarning() {
    state = state.copyWith(isStreakWarningActive: false);
  }

  /// Clear the streak expiry warning after the student completed a session.
  void clearStreakWarning() {
    state = state.copyWith(
      isStreakWarningActive: false,
      clearStreakExpiry: true,
    );
  }
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

/// Outreach / notification state provider — kept alive.
final outreachProvider =
    StateNotifierProvider<OutreachNotifier, OutreachState>(
  (ref) => OutreachNotifier(),
);
