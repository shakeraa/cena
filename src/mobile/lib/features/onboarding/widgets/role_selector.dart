// =============================================================================
// Cena Adaptive Learning Platform — Role Selector (MOB-033)
// =============================================================================
//
// Three large cards: Student / Teacher / Parent.
// Drives which onboarding pages are shown via OnboardingRole.
// =============================================================================

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';

// ---------------------------------------------------------------------------
// OnboardingRole enum
// ---------------------------------------------------------------------------

/// User role selected during onboarding. Drives page routing:
///   - student: 7 pages (welcome, role, subjects, grade, goal, time, discovery tour, ready)
///   - teacher: 4 pages (welcome, role, subjects, ready)
///   - parent:  4 pages (welcome, role, child-info, ready)
enum OnboardingRole {
  student(
    icon: Icons.school_rounded,
    color: Color(0xFF0097A7),
    labelEn: 'Student',
    labelHe: 'תלמיד/ה',
    descEn: 'I want to learn and practice',
    descHe: 'אני רוצה ללמוד ולתרגל',
  ),
  teacher(
    icon: Icons.person_rounded,
    color: Color(0xFFFF8F00),
    labelEn: 'Teacher',
    labelHe: 'מורה',
    descEn: 'I want to track my students',
    descHe: 'אני רוצה לעקוב אחרי התלמידים שלי',
  ),
  parent(
    icon: Icons.family_restroom_rounded,
    color: Color(0xFF7B1FA2),
    labelEn: 'Parent',
    labelHe: 'הורה',
    descEn: 'I want to support my child\'s learning',
    descHe: 'אני רוצה לתמוך בלמידה של הילד שלי',
  );

  const OnboardingRole({
    required this.icon,
    required this.color,
    required this.labelEn,
    required this.labelHe,
    required this.descEn,
    required this.descHe,
  });

  final IconData icon;
  final Color color;
  final String labelEn;
  final String labelHe;
  final String descEn;
  final String descHe;

  String label(String langCode) =>
      langCode == 'he' || langCode == 'ar' ? labelHe : labelEn;

  String description(String langCode) =>
      langCode == 'he' || langCode == 'ar' ? descHe : descEn;

  /// Number of onboarding pages for this role.
  int get pageCount {
    switch (this) {
      case OnboardingRole.student:
        return 8; // welcome, role, subjects, grade, goal, time, discovery, ready
      case OnboardingRole.teacher:
        return 4; // welcome, role, subjects, ready
      case OnboardingRole.parent:
        return 4; // welcome, role, child-info, ready
    }
  }
}

// ---------------------------------------------------------------------------
// RoleSelectorCard
// ---------------------------------------------------------------------------

/// A large tappable card for a single role choice.
class RoleSelectorCard extends StatelessWidget {
  const RoleSelectorCard({
    super.key,
    required this.role,
    required this.isSelected,
    required this.onTap,
    this.langCode = 'he',
  });

  final OnboardingRole role;
  final bool isSelected;
  final VoidCallback onTap;
  final String langCode;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return AnimatedContainer(
      duration: AnimationTokens.fast,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
        border: Border.all(
          color: isSelected ? role.color : cs.outlineVariant,
          width: isSelected ? 2.5 : 1,
        ),
        color: isSelected
            ? role.color.withValues(alpha: 0.12)
            : cs.surface,
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
        child: InkWell(
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(SpacingTokens.lg),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 64,
                  height: 64,
                  decoration: BoxDecoration(
                    color: role.color.withValues(alpha: 0.15),
                    shape: BoxShape.circle,
                  ),
                  child: Icon(
                    role.icon,
                    size: 36,
                    color: role.color,
                  ),
                ),
                const SizedBox(height: SpacingTokens.md),
                Text(
                  role.label(langCode),
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontFamily: TypographyTokens.hebrewFontFamily,
                    fontWeight: FontWeight.w700,
                    color: isSelected ? role.color : cs.onSurface,
                  ),
                ),
                const SizedBox(height: SpacingTokens.xxs),
                Text(
                  role.description(langCode),
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontFamily: TypographyTokens.hebrewFontFamily,
                    color: cs.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
                if (isSelected) ...[
                  const SizedBox(height: SpacingTokens.sm),
                  Icon(
                    Icons.check_circle_rounded,
                    color: role.color,
                    size: 28,
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
