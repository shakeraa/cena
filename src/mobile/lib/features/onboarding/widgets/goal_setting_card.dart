// =============================================================================
// Cena Adaptive Learning Platform — Goal Setting Cards (MOB-033)
// =============================================================================
//
// Visual goal cards and time commitment cards for onboarding V2.
// GoalType drives personalized content; dailyMinutes controls session length.
// =============================================================================

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';

// ---------------------------------------------------------------------------
// GoalType enum
// ---------------------------------------------------------------------------

/// Learning goal types shown during onboarding.
enum GoalType {
  bagrutPrep(
    icon: Icons.emoji_events_rounded,
    color: Color(0xFFFF8F00),
    labelEn: 'Bagrut Prep',
    labelHe: 'הכנה לבגרות',
    descEn: 'Get ready for the Bagrut exams',
    descHe: 'התכוננו למבחני הבגרות',
  ),
  homeworkHelp(
    icon: Icons.menu_book_rounded,
    color: Color(0xFF0097A7),
    labelEn: 'Homework Help',
    labelHe: 'עזרה בשיעורי בית',
    descEn: 'Solve homework problems with guidance',
    descHe: 'פתרו תרגילים מהבית עם הנחיה',
  ),
  getAhead(
    icon: Icons.rocket_launch_rounded,
    color: Color(0xFF7B1FA2),
    labelEn: 'Get Ahead',
    labelHe: 'התקדמות',
    descEn: 'Learn topics before they\'re taught in class',
    descHe: 'למדו נושאים לפני שמלמדים בכיתה',
  ),
  review(
    icon: Icons.refresh_rounded,
    color: Color(0xFF388E3C),
    labelEn: 'Review',
    labelHe: 'חזרה',
    descEn: 'Strengthen what you already learned',
    descHe: 'חזקו מה שכבר למדתם',
  );

  const GoalType({
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

  /// Returns the label in the locale identified by [langCode].
  String label(String langCode) =>
      langCode == 'he' || langCode == 'ar' ? labelHe : labelEn;

  /// Returns the description in the locale identified by [langCode].
  String description(String langCode) =>
      langCode == 'he' || langCode == 'ar' ? descHe : descEn;
}

// ---------------------------------------------------------------------------
// GoalSettingCard
// ---------------------------------------------------------------------------

/// A large tappable card that represents a learning goal.
class GoalSettingCard extends StatelessWidget {
  const GoalSettingCard({
    super.key,
    required this.goalType,
    required this.isSelected,
    required this.onTap,
    this.langCode = 'he',
  });

  final GoalType goalType;
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
          color: isSelected ? goalType.color : cs.outlineVariant,
          width: isSelected ? 2.5 : 1,
        ),
        color: isSelected
            ? goalType.color.withValues(alpha: 0.12)
            : cs.surface,
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
        child: InkWell(
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(SpacingTokens.md),
            child: Row(
              children: [
                Container(
                  width: 48,
                  height: 48,
                  decoration: BoxDecoration(
                    color: goalType.color.withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(RadiusTokens.lg),
                  ),
                  child: Icon(
                    goalType.icon,
                    size: 28,
                    color: goalType.color,
                  ),
                ),
                const SizedBox(width: SpacingTokens.md),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        goalType.label(langCode),
                        style: theme.textTheme.titleMedium?.copyWith(
                          fontFamily: TypographyTokens.hebrewFontFamily,
                          fontWeight: FontWeight.w700,
                          color: isSelected
                              ? goalType.color
                              : cs.onSurface,
                        ),
                      ),
                      const SizedBox(height: SpacingTokens.xxs),
                      Text(
                        goalType.description(langCode),
                        style: theme.textTheme.bodySmall?.copyWith(
                          fontFamily: TypographyTokens.hebrewFontFamily,
                          color: cs.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
                if (isSelected)
                  Icon(
                    Icons.check_circle_rounded,
                    color: goalType.color,
                    size: 24,
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// TimeCommitmentCard
// ---------------------------------------------------------------------------

/// Large tappable card for selecting daily study time (5/10/15/20 min).
class TimeCommitmentCard extends StatelessWidget {
  const TimeCommitmentCard({
    super.key,
    required this.minutes,
    required this.isSelected,
    required this.onTap,
  });

  final int minutes;
  final bool isSelected;
  final VoidCallback onTap;

  static const Map<int, (IconData, String, String)> _meta = {
    5: (Icons.flash_on_rounded, 'Quick burst', 'פלאש מהיר'),
    10: (Icons.timer_rounded, 'Focused sprint', 'ריצה ממוקדת'),
    15: (Icons.schedule_rounded, 'Solid session', 'מפגש מוצק'),
    20: (Icons.hourglass_bottom_rounded, 'Deep practice', 'תרגול מעמיק'),
  };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final meta = _meta[minutes] ?? _meta[10]!;
    final (icon, _, labelHe) = meta;

    return AnimatedContainer(
      duration: AnimationTokens.fast,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
        border: Border.all(
          color: isSelected ? cs.primary : cs.outlineVariant,
          width: isSelected ? 2.5 : 1,
        ),
        color: isSelected ? cs.primaryContainer : cs.surface,
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
        child: InkWell(
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(
              vertical: SpacingTokens.md,
              horizontal: SpacingTokens.sm,
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  icon,
                  size: 32,
                  color: isSelected ? cs.onPrimaryContainer : cs.primary,
                ),
                const SizedBox(height: SpacingTokens.xs),
                Text(
                  '$minutes',
                  style: theme.textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.w800,
                    color: isSelected ? cs.onPrimaryContainer : cs.onSurface,
                  ),
                ),
                Text(
                  'דק\'',
                  style: theme.textTheme.labelMedium?.copyWith(
                    fontFamily: TypographyTokens.hebrewFontFamily,
                    color: isSelected
                        ? cs.onPrimaryContainer
                        : cs.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: SpacingTokens.xxs),
                Text(
                  labelHe,
                  style: theme.textTheme.labelSmall?.copyWith(
                    fontFamily: TypographyTokens.hebrewFontFamily,
                    color: isSelected
                        ? cs.onPrimaryContainer
                        : cs.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
