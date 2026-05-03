// =============================================================================
// Cena Adaptive Learning Platform — Streak Widget
// =============================================================================
//
// Full streak display: animated flame icon, counter, calendar strip, and
// "Streak at risk!" warning. Used on the gamification screen and home card.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/config/app_config.dart';
import '../../core/state/gamification_state.dart';

/// Full streak display card.
class StreakWidget extends ConsumerStatefulWidget {
  const StreakWidget({super.key});

  @override
  ConsumerState<StreakWidget> createState() => _StreakWidgetState();
}

class _StreakWidgetState extends ConsumerState<StreakWidget>
    with SingleTickerProviderStateMixin {
  late final AnimationController _pulseController;
  late final Animation<double> _pulseAnimation;

  @override
  void initState() {
    super.initState();
    _pulseController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 800),
    )..repeat(reverse: true);

    _pulseAnimation = Tween<double>(begin: 0.85, end: 1.15).animate(
      CurvedAnimation(parent: _pulseController, curve: Curves.easeInOut),
    );
  }

  @override
  void dispose() {
    _pulseController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final streak = ref.watch(streakProvider);
    final longest = ref.watch(longestStreakProvider);
    final isSafe = ref.watch(streakSafeProvider);
    final freezes = ref.watch(streakFreezesProvider);
    final days = ref.watch(last7DaysActivityProvider);
    final vacationMode = ref.watch(vacationModeProvider);
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    final isAtRisk = streak > 0 && !isSafe && !vacationMode;
    final isActive = streak > 0;

    return Card(
      elevation: 2,
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header row: flame + counter + longest streak badge
            Row(
              children: [
                // Animated flame
                AnimatedBuilder(
                  animation: _pulseAnimation,
                  builder: (context, child) {
                    final scale = isActive ? _pulseAnimation.value : 1.0;
                    return Transform.scale(
                      scale: scale,
                      child: child,
                    );
                  },
                  child: Icon(
                    Icons.local_fire_department_rounded,
                    size: 40,
                    color: isActive
                        ? _flameColor(streak)
                        : colorScheme.onSurfaceVariant.withValues(alpha: 0.4),
                  ),
                ),
                const SizedBox(width: SpacingTokens.sm),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '$streak',
                      style: theme.textTheme.headlineLarge?.copyWith(
                        fontWeight: FontWeight.w800,
                        color: isActive
                            ? _flameColor(streak)
                            : colorScheme.onSurfaceVariant,
                      ),
                    ),
                    Text(
                      streak == 1 ? '1 day streak' : '$streak day streak',
                      style: theme.textTheme.labelLarge?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
                const Spacer(),
                // Longest streak + new record badge
                Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      'Best: $longest',
                      style: theme.textTheme.labelMedium?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                    if (streak > 0 && streak == longest && longest > 1) ...[
                      const SizedBox(height: SpacingTokens.xxs),
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: SpacingTokens.sm,
                          vertical: SpacingTokens.xxs,
                        ),
                        decoration: BoxDecoration(
                          color: colorScheme.primaryContainer,
                          borderRadius:
                              BorderRadius.circular(RadiusTokens.full),
                        ),
                        child: Text(
                          'New record!',
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: colorScheme.onPrimaryContainer,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
              ],
            ),

            const SizedBox(height: SpacingTokens.md),

            // Calendar strip — last 7 days
            _CalendarStrip(days: days),

            // Streak at risk warning
            if (isAtRisk) ...[
              const SizedBox(height: SpacingTokens.sm),
              _StreakAtRiskBanner(freezes: freezes),
            ],

            // Vacation mode indicator
            if (vacationMode) ...[
              const SizedBox(height: SpacingTokens.sm),
              _VacationModeBanner(
                endDate: ref.watch(vacationEndDateProvider),
              ),
            ],

            // Streak freezes count
            if (freezes > 0 && !isAtRisk) ...[
              const SizedBox(height: SpacingTokens.sm),
              Row(
                children: [
                  Icon(
                    Icons.ac_unit_rounded,
                    size: 16,
                    color: colorScheme.tertiary,
                  ),
                  const SizedBox(width: SpacingTokens.xs),
                  Text(
                    '$freezes streak freeze${freezes == 1 ? '' : 's'} stored',
                    style: theme.textTheme.labelMedium?.copyWith(
                      color: colorScheme.tertiary,
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }

  Color _flameColor(int streak) {
    if (streak >= 30) return const Color(0xFFFF3D00); // red-hot
    if (streak >= 7) return const Color(0xFFFF6D00); // deep orange
    return const Color(0xFFFF9100); // amber
  }
}

/// 7-day calendar strip showing active/missed practice days.
class _CalendarStrip extends StatelessWidget {
  const _CalendarStrip({required this.days});

  final List<DayActivity> days;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final dayFormat = DateFormat('E'); // "Mon", "Tue", ...
    final dateFormat = DateFormat('d'); // "3", "14", ...

    // Display oldest on left → reverse the list
    final ordered = days.reversed.toList();

    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: ordered.map((day) {
        final isActive = day.isActive;
        final isToday = day.isToday;

        return Column(
          children: [
            Text(
              dayFormat.format(day.date),
              style: theme.textTheme.labelSmall?.copyWith(
                color: colorScheme.onSurfaceVariant,
                fontWeight: isToday ? FontWeight.w700 : FontWeight.w400,
              ),
            ),
            const SizedBox(height: SpacingTokens.xxs),
            Container(
              width: 34,
              height: 34,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: isActive
                    ? colorScheme.primary
                    : colorScheme.surfaceContainerHighest,
                border: isToday
                    ? Border.all(color: colorScheme.primary, width: 2)
                    : null,
              ),
              child: Center(
                child: isActive
                    ? Icon(
                        Icons.check_rounded,
                        size: 18,
                        color: colorScheme.onPrimary,
                      )
                    : Text(
                        dateFormat.format(day.date),
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                          fontWeight:
                              isToday ? FontWeight.w700 : FontWeight.w400,
                        ),
                      ),
              ),
            ),
          ],
        );
      }).toList(),
    );
  }
}

class _StreakAtRiskBanner extends StatelessWidget {
  const _StreakAtRiskBanner({required this.freezes});

  final int freezes;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm,
        vertical: SpacingTokens.xs,
      ),
      decoration: BoxDecoration(
        color: colorScheme.errorContainer,
        borderRadius: BorderRadius.circular(RadiusTokens.md),
      ),
      child: Row(
        children: [
          Icon(
            Icons.warning_amber_rounded,
            size: 18,
            color: colorScheme.onErrorContainer,
          ),
          const SizedBox(width: SpacingTokens.xs),
          Expanded(
            child: Text(
              freezes > 0
                  ? 'Streak at risk! Practice now or a freeze will be used.'
                  : 'Streak at risk! Practice today to keep your streak.',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.onErrorContainer,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _VacationModeBanner extends StatelessWidget {
  const _VacationModeBanner({required this.endDate});

  final DateTime? endDate;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final dateStr = endDate != null
        ? ' until ${DateFormat.MMMd().format(endDate!)}'
        : '';

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm,
        vertical: SpacingTokens.xs,
      ),
      decoration: BoxDecoration(
        color: colorScheme.secondaryContainer,
        borderRadius: BorderRadius.circular(RadiusTokens.md),
      ),
      child: Row(
        children: [
          Icon(
            Icons.beach_access_rounded,
            size: 18,
            color: colorScheme.onSecondaryContainer,
          ),
          const SizedBox(width: SpacingTokens.xs),
          Expanded(
            child: Text(
              'Vacation mode active$dateStr — streak paused.',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.onSecondaryContainer,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
