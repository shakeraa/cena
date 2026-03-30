// =============================================================================
// Cena Adaptive Learning Platform — Application Router
// =============================================================================

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_screen.dart';
import '../features/home/home_screen.dart';
import '../features/session/session_screen.dart';

/// Named route constants for type-safe navigation.
abstract class CenaRoutes {
  static const String login = '/login';
  static const String home = '/home';
  static const String session = '/session';
}

/// Application router using go_router.
///
/// Initial location is [CenaRoutes.home]. Auth guard will be added
/// once the auth state provider is wired up in MOB-002.
final GoRouter cenaRouter = GoRouter(
  initialLocation: CenaRoutes.home,
  debugLogDiagnostics: true,
  routes: [
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
