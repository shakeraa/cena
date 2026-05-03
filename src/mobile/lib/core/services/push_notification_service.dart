// =============================================================================
// Cena Adaptive Learning Platform — Push Notification Service (FCM)
// =============================================================================

import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:go_router/go_router.dart';
import 'package:logger/logger.dart';

final _logger = Logger(
  printer: PrettyPrinter(methodCount: 0, printTime: true),
);

/// Top-level background message handler.
///
/// Must be a top-level function (not a class method) so that the isolate
/// spawned by FCM can locate it.
@pragma('vm:entry-point')
Future<void> firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  _logger.d('Background message received: ${message.messageId}');
}

/// Service that manages Firebase Cloud Messaging lifecycle:
///
/// 1. Requests notification permission from the OS.
/// 2. Retrieves and stores the FCM device token.
/// 3. Listens for foreground messages, background-open taps, and
///    cold-start taps, routing each to the appropriate screen.
class PushNotificationService {
  PushNotificationService({required this.router});

  final GoRouter router;

  final FirebaseMessaging _messaging = FirebaseMessaging.instance;

  /// The most recent FCM token for this device.
  /// Send this to the backend for targeted push delivery.
  String? fcmToken;

  // ---------------------------------------------------------------------------
  // Public API
  // ---------------------------------------------------------------------------

  /// Call once after [Firebase.initializeApp] completes.
  Future<void> initialize() async {
    // 1. Request permission (iOS will show the native prompt; Android 13+
    //    requires runtime permission as well).
    final settings = await _messaging.requestPermission(
      alert: true,
      badge: true,
      sound: true,
    );

    _logger.i(
      'Notification permission status: ${settings.authorizationStatus}',
    );

    if (settings.authorizationStatus == AuthorizationStatus.denied) {
      _logger.w('User denied notification permission');
      return;
    }

    // 2. Retrieve the FCM token.
    fcmToken = await _messaging.getToken();
    _logger.i('FCM token: $fcmToken');

    // Listen for token refresh events so the backend always has the latest.
    _messaging.onTokenRefresh.listen((newToken) {
      fcmToken = newToken;
      _logger.i('FCM token refreshed: $newToken');
      // TODO: send refreshed token to Cena backend
    });

    // 3. Foreground messages — the app is open and visible.
    FirebaseMessaging.onMessage.listen(_handleForegroundMessage);

    // 4. Notification tap when the app was in background (not terminated).
    FirebaseMessaging.onMessageOpenedApp.listen(_handleNotificationTap);

    // 5. Cold-start tap — the app was terminated, user tapped notification.
    final initialMessage = await _messaging.getInitialMessage();
    if (initialMessage != null) {
      _handleNotificationTap(initialMessage);
    }
  }

  // ---------------------------------------------------------------------------
  // Internal handlers
  // ---------------------------------------------------------------------------

  /// Foreground messages.
  ///
  /// FCM does **not** show a system notification when the app is in the
  /// foreground. If you want a visual indicator, show a snackbar or in-app
  /// banner from here. For now we just log.
  void _handleForegroundMessage(RemoteMessage message) {
    _logger.d(
      'Foreground message: ${message.notification?.title ?? message.messageId}',
    );
  }

  /// Routes the user to the correct screen based on the data payload.
  ///
  /// Expected payload keys:
  ///   - `type`  : one of `session_start`, `badge_earned`, `parent_alert`,
  ///               `graph_update`
  ///   - `id`    : optional entity id (e.g. session id)
  ///   - `route` : optional explicit deep-link path
  void _handleNotificationTap(RemoteMessage message) {
    final data = message.data;
    _logger.i('Notification tap data: $data');

    // Prefer an explicit route if the backend provides one.
    final explicitRoute = data['route'] as String?;
    if (explicitRoute != null && explicitRoute.isNotEmpty) {
      router.go(explicitRoute);
      return;
    }

    // Otherwise, derive the route from the notification type.
    final type = data['type'] as String?;
    final id = data['id'] as String?;

    switch (type) {
      case 'session_start':
        if (id != null) {
          router.go('/session/$id');
        } else {
          router.go('/session');
        }
        break;
      case 'badge_earned':
      case 'graph_update':
        router.go('/graph');
        break;
      case 'parent_alert':
        router.go('/home');
        break;
      default:
        _logger.w('Unknown notification type: $type — navigating home');
        router.go('/home');
    }
  }
}
