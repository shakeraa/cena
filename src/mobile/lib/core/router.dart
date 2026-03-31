// =============================================================================
// Cena Adaptive Learning Platform — Application Router
// =============================================================================

import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../features/auth/auth_screen.dart';
import '../features/home/home_screen.dart';
import '../features/onboarding/onboarding_screen.dart';
import '../features/notifications/notification_center_screen.dart';
import '../features/profile/profile_screen.dart';
import '../features/auth/try_question_screen.dart';
import '../features/session/session_screen.dart';
import '../features/tutor/tutor_chat_screen.dart';
import 'services/analytics_service.dart';
import 'services/deep_link_service.dart';

/// Named route constants for type-safe navigation.
abstract class CenaRoutes {
  static const String login = '/login';
  static const String onboarding = '/onboarding';
  static const String home = '/home';
  static const String session = '/session';
  static const String sessionById = '/session/:id';
  static const String graph = '/graph';
  static const String profile = '/profile';
  static const String notifications = '/notifications';
  static const String tutor = '/tutor';
  static const String tryQuestion = '/try';

  /// Routes that require authentication.
  static const Set<String> authenticated = {
    home,
    session,
    graph,
    profile,
    notifications,
    tutor,
  };

  /// Returns `true` if the given [path] requires an authenticated user.
  static bool requiresAuth(String path) {
    return authenticated.any(
      (route) => path == route || path.startsWith('$route/'),
    );
  }
}

/// SharedPreferences key that marks onboarding as complete.
const String _kOnboardingComplete = 'onboarding_complete';

/// Creates the application router, optionally injecting [observers] for
/// analytics screen tracking.
///
/// Deep link redirect logic:
///   1. Onboarding gate — first-time users always see onboarding first.
///   2. Auth gate — protected routes redirect to login; the intended path is
///      stored in [DeferredDeepLink] and replayed after successful login.
///   3. Deferred replay — on returning to home after login, any stored deep
///      link is consumed and navigated to.
GoRouter buildCenaRouter({
  List<NavigatorObserver>? observers,
}) {
  return GoRouter(
    initialLocation: CenaRoutes.home,
    debugLogDiagnostics: true,
    observers: observers ?? const [],
    redirect: (BuildContext context, GoRouterState state) async {
      final location = state.matchedLocation;

      // Never redirect if the user is already on the onboarding, login, or try flow.
      if (location == CenaRoutes.onboarding ||
          location == CenaRoutes.login ||
          location == CenaRoutes.tryQuestion) {
        return null;
      }

      final prefs = await SharedPreferences.getInstance();
      final onboardingDone = prefs.getBool(_kOnboardingComplete) ?? false;

      // Gate 1: Try-question → Onboarding for first-time users.
      // Time-to-value < 30s: show one question before any forms.
      if (!onboardingDone) {
        final triedQuestion = prefs.getBool('try_question_done') ?? false;
        if (!triedQuestion) {
          if (location != CenaRoutes.home) {
            DeferredDeepLink.instance.store(location);
          }
          return CenaRoutes.tryQuestion;
        }
        if (location != CenaRoutes.home) {
          DeferredDeepLink.instance.store(location);
        }
        return CenaRoutes.onboarding;
      }

      // Gate 2: Authentication — protected routes require a logged-in user.
      final isLoggedIn = FirebaseAuth.instance.currentUser != null;

      if (!isLoggedIn && CenaRoutes.requiresAuth(location)) {
        DeferredDeepLink.instance.store(location);
        return CenaRoutes.login;
      }

      // Gate 3: Replay deferred deep link after successful login.
      if (isLoggedIn && location == CenaRoutes.home) {
        final deferred = DeferredDeepLink.instance.consume();
        if (deferred != null) return deferred;
      }

      return null;
    },
    routes: [
      GoRoute(
        path: CenaRoutes.tryQuestion,
        name: 'tryQuestion',
        builder: (BuildContext context, GoRouterState state) {
          return const TryQuestionScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.onboarding,
        name: 'onboarding',
        builder: (BuildContext context, GoRouterState state) {
          return const OnboardingScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.login,
        name: 'login',
        builder: (BuildContext context, GoRouterState state) {
          return const AuthScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.home,
        name: 'home',
        builder: (BuildContext context, GoRouterState state) {
          return const HomeScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.session,
        name: 'session',
        builder: (BuildContext context, GoRouterState state) {
          return const SessionScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.sessionById,
        name: 'sessionById',
        builder: (BuildContext context, GoRouterState state) {
          return const SessionScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.graph,
        name: 'graph',
        builder: (BuildContext context, GoRouterState state) {
          return const HomeScreen(); // Graph tab within home — placeholder
        },
      ),
      GoRoute(
        path: CenaRoutes.profile,
        name: 'profile',
        builder: (BuildContext context, GoRouterState state) {
          return const ProfileScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.notifications,
        name: 'notifications',
        builder: (BuildContext context, GoRouterState state) {
          return const NotificationCenterScreen();
        },
      ),
      GoRoute(
        path: CenaRoutes.tutor,
        name: 'tutor',
        builder: (BuildContext context, GoRouterState state) {
          return const TutorChatScreen();
        },
      ),
    ],
    errorBuilder: (BuildContext context, GoRouterState state) {
      return Scaffold(
        appBar: AppBar(title: const Text('Page Not Found')),
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.error_outline,
                size: 64,
                color: Theme.of(context).colorScheme.error,
              ),
              const SizedBox(height: 16),
              Text(
                'Route not found: ${state.uri}',
                style: Theme.of(context).textTheme.bodyLarge,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 24),
              FilledButton(
                onPressed: () => context.go(CenaRoutes.home),
                child: const Text('Go Home'),
              ),
            ],
          ),
        ),
      );
    },
  );
}

/// Default router instance (no analytics observer).
/// Kept for backward compatibility with code that references [cenaRouter].
final GoRouter cenaRouter = buildCenaRouter();
