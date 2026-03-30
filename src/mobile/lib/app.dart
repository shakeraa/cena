// =============================================================================
// Cena Adaptive Learning Platform — Root Application Widget
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/config/app_config.dart';
import 'core/router.dart';
import 'core/state/app_state.dart';
import 'core/theme/cena_theme.dart';

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

    return MaterialApp.router(
      title: 'Cena',
      debugShowCheckedModeBanner: config.isDev,

      // Theme — locale-aware font selection
      theme: CenaTheme.light(locale),
      darkTheme: CenaTheme.dark(locale),
      themeMode: ThemeMode.system,

      // Routing
      routerConfig: cenaRouter,

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
