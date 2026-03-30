// =============================================================================
// Cena Adaptive Learning Platform — Root Application Widget
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'core/config/app_config.dart';
import 'core/router.dart';
import 'core/services/analytics_service.dart';
import 'core/state/app_state.dart';
import 'core/theme/cena_theme.dart';

/// Riverpod provider that builds the router once, injecting the analytics
/// observer so all screen transitions are tracked automatically.
final cenaRouterProvider = Provider<GoRouter>((ref) {
  final analytics = ref.watch(analyticsServiceProvider);
  return buildCenaRouter(observers: [analytics.observer]);
});

/// Riverpod provider for the current locale.
/// Defaults to Hebrew (primary locale).
final currentLocaleProvider = StateProvider<Locale>((ref) {
  return AppLocales.primary;
});

/// Root application widget.
///
/// Wraps [MaterialApp.router] with locale-aware theming, RTL support,
/// and Riverpod state management. Receives [AppConfig] to configure
/// environment-specific behavior.
class CenaApp extends ConsumerWidget {
  const CenaApp({super.key, required this.config});

  final AppConfig config;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final locale = ref.watch(currentLocaleProvider);
    final isRtl = AppLocales.isRtl(locale);
    final router = ref.watch(cenaRouterProvider);

    return MaterialApp.router(
      title: 'Cena',
      debugShowCheckedModeBanner: config.isDev,

      // Theme — locale-aware font selection
      theme: CenaTheme.light(locale),
      darkTheme: CenaTheme.dark(locale),
      themeMode: ThemeMode.system,

      // Routing
      routerConfig: router,

      // Localization
      locale: locale,
      supportedLocales: AppLocales.supported,
      localizationsDelegates: const [
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],

      // Force text direction based on locale for the entire widget tree
      builder: (context, child) {
        return Directionality(
          textDirection: isRtl ? TextDirection.rtl : TextDirection.ltr,
          child: child ?? const SizedBox.shrink(),
        );
      },
    );
  }
}
