// =============================================================================
// Cena Adaptive Learning Platform — Application Router
// =============================================================================

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../features/auth/auth_screen.dart';
import '../features/home/home_screen.dart';
import '../features/onboarding/onboarding_screen.dart';
import '../features/session/session_screen.dart';

/// Named route constants for type-safe navigation.
abstract class CenaRoutes {
  static const String login = '/login';
  static const String onboarding = '/onboarding';
  static const String home = '/home';
  static const String session = '/session';
}

/// SharedPreferences key that marks onboarding as complete.
const String _kOnboardingComplete = 'onboarding_complete';

/// Application router using go_router.
///
/// Redirects first-time users to [CenaRoutes.onboarding] before they can
/// reach any authenticated screen. The flag is persisted via
/// SharedPreferences and written by [OnboardingNotifier.completeOnboarding].
final GoRouter cenaRouter = GoRouter(
  initialLocation: CenaRoutes.home,
  debugLogDiagnostics: true,
  redirect: (BuildContext context, GoRouterState state) async {
    // Never redirect if the user is already on the onboarding flow.
    if (state.matchedLocation == CenaRoutes.onboarding) return null;

    final prefs = await SharedPreferences.getInstance();
    final onboardingDone = prefs.getBool(_kOnboardingComplete) ?? false;

    if (!onboardingDone) return CenaRoutes.onboarding;
    return null;
  },
  routes: [
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
