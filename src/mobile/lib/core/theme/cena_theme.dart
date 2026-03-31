// =============================================================================
// Cena Adaptive Learning Platform — Theme Configuration
// =============================================================================

import 'package:flutter/material.dart';

import '../config/app_config.dart';

/// Cena Material 3 theme with locale-aware typography and RTL support.
///
/// Uses [TypographyTokens] to select the correct font family based on the
/// active locale: Heebo for Hebrew, Noto Sans Arabic for Arabic, Inter for
/// English/Latin. Both Hebrew and Arabic locales produce RTL text direction.
class CenaTheme {
  const CenaTheme._();

  // ---------------------------------------------------------------------------
  // Brand Colors
  // ---------------------------------------------------------------------------

  /// Primary brand color — teal (matches math subject color for consistency).
  static const Color _primarySeed = Color(0xFF0097A7);

  /// Secondary brand color — warm amber for CTAs and highlights.
  static const Color _secondarySeed = Color(0xFFFF8F00);

  /// Error color — standard Material red.
  static const Color _errorColor = Color(0xFFD32F2F);

  /// Surface tint for elevated components.
  static const Color _surfaceTint = Color(0xFF0097A7);

  // ---------------------------------------------------------------------------
  // Light Theme
  // ---------------------------------------------------------------------------

  /// Light theme for [locale]. Selects the correct font family and configures
  /// Material 3 color scheme, typography, and component themes.
  static ThemeData light(Locale locale) {
    final fontFamily = TypographyTokens.fontFamilyForLocale(
      locale.languageCode,
    );
    final colorScheme = ColorScheme.fromSeed(
      seedColor: _primarySeed,
      secondary: _secondarySeed,
      error: _errorColor,
      surfaceTint: _surfaceTint,
      brightness: Brightness.light,
    );

    return _buildTheme(
      colorScheme: colorScheme,
      fontFamily: fontFamily,
      locale: locale,
      brightness: Brightness.light,
    );
  }

  // ---------------------------------------------------------------------------
  // Dark Theme
  // ---------------------------------------------------------------------------

  /// Dark theme for [locale]. Same font/locale logic, dark color scheme.
  static ThemeData dark(Locale locale) {
    final fontFamily = TypographyTokens.fontFamilyForLocale(
      locale.languageCode,
    );
    final colorScheme = ColorScheme.fromSeed(
      seedColor: _primarySeed,
      secondary: _secondarySeed,
      error: _errorColor,
      surfaceTint: _surfaceTint,
      brightness: Brightness.dark,
    );

    return _buildTheme(
      colorScheme: colorScheme,
      fontFamily: fontFamily,
      locale: locale,
      brightness: Brightness.dark,
    );
  }

  // ---------------------------------------------------------------------------
  // Internal Builder
  // ---------------------------------------------------------------------------

  static ThemeData _buildTheme({
    required ColorScheme colorScheme,
    required String fontFamily,
    required Locale locale,
    required Brightness brightness,
  }) {
    final textTheme = _buildTextTheme(fontFamily);

    return ThemeData(
      useMaterial3: true,
      colorScheme: colorScheme,
      fontFamily: fontFamily,
      textTheme: textTheme,
      brightness: brightness,

      // AppBar
      appBarTheme: AppBarTheme(
        centerTitle: true,
        elevation: 0,
        scrolledUnderElevation: 1,
        backgroundColor: colorScheme.surface,
        foregroundColor: colorScheme.onSurface,
        titleTextStyle: textTheme.titleLarge?.copyWith(
          color: colorScheme.onSurface,
          fontWeight: FontWeight.w600,
        ),
      ),

      // Cards
      cardTheme: CardThemeData(
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          side: BorderSide(
            color: colorScheme.outlineVariant.withValues(alpha: 0.5),
          ),
        ),
        margin: EdgeInsets.zero,
      ),

      // Elevated Button
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          elevation: 0,
          padding: EdgeInsets.symmetric(
            horizontal: SpacingTokens.lg,
            vertical: SpacingTokens.md,
          ),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(RadiusTokens.md),
          ),
          textStyle: textTheme.labelLarge?.copyWith(
            fontWeight: FontWeight.w600,
          ),
        ),
      ),

      // Input Decoration
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.md),
          borderSide: BorderSide.none,
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.md),
          borderSide: BorderSide(color: colorScheme.primary, width: 2),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.md),
          borderSide: BorderSide(color: colorScheme.error),
        ),
        contentPadding: EdgeInsets.symmetric(
          horizontal: SpacingTokens.md,
          vertical: SpacingTokens.sm,
        ),
      ),

      // Bottom Navigation Bar
      bottomNavigationBarTheme: BottomNavigationBarThemeData(
        type: BottomNavigationBarType.fixed,
        selectedItemColor: colorScheme.primary,
        unselectedItemColor: colorScheme.onSurfaceVariant,
        showUnselectedLabels: true,
        elevation: 0,
        selectedLabelStyle: textTheme.labelSmall?.copyWith(
          fontWeight: FontWeight.w600,
        ),
        unselectedLabelStyle: textTheme.labelSmall,
      ),

      // Chip
      chipTheme: ChipThemeData(
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.full),
        ),
        padding: EdgeInsets.symmetric(
          horizontal: SpacingTokens.sm,
          vertical: SpacingTokens.xs,
        ),
      ),

      // Snackbar
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.md),
        ),
      ),

      // Visual density
      visualDensity: VisualDensity.adaptivePlatformDensity,

      // Page transitions — use platform-appropriate defaults
      pageTransitionsTheme: const PageTransitionsTheme(
        builders: {
          TargetPlatform.android: FadeUpwardsPageTransitionsBuilder(),
          TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
        },
      ),
    );
  }

  /// Build a locale-aware text theme using the specified [fontFamily].
  static TextTheme _buildTextTheme(String fontFamily) {
    return TextTheme(
      displayLarge: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.displayLarge,
        fontWeight: FontWeight.w800,
        letterSpacing: -0.5,
        height: 1.2,
      ),
      displayMedium: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.displayMedium,
        fontWeight: FontWeight.w700,
        letterSpacing: -0.25,
        height: 1.2,
      ),
      headlineLarge: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.headlineLarge,
        fontWeight: FontWeight.w700,
        height: 1.3,
      ),
      headlineMedium: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.headlineMedium,
        fontWeight: FontWeight.w600,
        height: 1.3,
      ),
      titleLarge: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.titleLarge,
        fontWeight: FontWeight.w600,
        height: 1.4,
      ),
      titleMedium: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.titleMedium,
        fontWeight: FontWeight.w500,
        height: 1.4,
      ),
      bodyLarge: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.bodyLarge,
        fontWeight: FontWeight.w400,
        height: 1.5,
      ),
      bodyMedium: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.bodyMedium,
        fontWeight: FontWeight.w400,
        height: 1.5,
      ),
      bodySmall: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.bodySmall,
        fontWeight: FontWeight.w400,
        height: 1.5,
      ),
      labelLarge: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.labelLarge,
        fontWeight: FontWeight.w500,
        height: 1.4,
      ),
      labelMedium: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.labelMedium,
        fontWeight: FontWeight.w500,
        height: 1.4,
      ),
      labelSmall: TextStyle(
        fontFamily: fontFamily,
        fontSize: TypographyTokens.labelSmall,
        fontWeight: FontWeight.w500,
        letterSpacing: 0.5,
        height: 1.4,
      ),
    );
  }
}
