// =============================================================================
// Cena Adaptive Learning Platform — Learning Time Card (PAR-002)
// Displays time studied: today, this week, and a 7-day bar chart.
// =============================================================================
//
// TODO(l10n): Replace hardcoded strings with ARB keys once added:
//   timeStudied, today, thisWeek

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

/// Session history entries with duration and timestamp.
/// In production this would be populated from the session database.
class SessionHistoryEntry {
  const SessionHistoryEntry({
    required this.date,
    required this.duration,
  });

  final DateTime date;
  final Duration duration;
}

/// Session history provider — stores completed session durations.
final sessionHistoryProvider =
    StateProvider<List<SessionHistoryEntry>>((ref) => []);

/// Today's total study time derived from session history.
final todayStudyTimeProvider = Provider<Duration>((ref) {
  final history = ref.watch(sessionHistoryProvider);
  final now = DateTime.now();
  final todaySessions = history.where((e) =>
      e.date.year == now.year &&
      e.date.month == now.month &&
      e.date.day == now.day);
  return todaySessions.fold<Duration>(
    Duration.zero,
    (sum, e) => sum + e.duration,
  );
});

/// This week's total study time (Monday to now).
final weekStudyTimeProvider = Provider<Duration>((ref) {
  final history = ref.watch(sessionHistoryProvider);
  final now = DateTime.now();
  final monday = now.subtract(Duration(days: now.weekday - 1));
  final weekStart = DateTime(monday.year, monday.month, monday.day);
  final weekSessions = history.where((e) => e.date.isAfter(weekStart));
  return weekSessions.fold<Duration>(
    Duration.zero,
    (sum, e) => sum + e.duration,
  );
});

/// Per-day study minutes for the last 7 days (index 0 = 6 days ago, 6 = today).
final last7DaysMinutesProvider = Provider<List<int>>((ref) {
  final history = ref.watch(sessionHistoryProvider);
  final now = DateTime.now();
  return List.generate(7, (i) {
    final day = now.subtract(Duration(days: 6 - i));
    final daySessions = history.where((e) =>
        e.date.year == day.year &&
        e.date.month == day.month &&
        e.date.day == day.day);
    return daySessions.fold<int>(
        0, (sum, e) => sum + e.duration.inMinutes);
  });
});

// ---------------------------------------------------------------------------
// Widget
// ---------------------------------------------------------------------------

/// Compact card showing study time with a mini 7-day bar chart.
class LearningTimeCard extends ConsumerWidget {
  const LearningTimeCard({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final todayTime = ref.watch(todayStudyTimeProvider);
    final weekTime = ref.watch(weekStudyTimeProvider);
    final weekDays = ref.watch(last7DaysMinutesProvider);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header.
            Row(
              children: [
                Icon(Icons.timer_rounded,
                    size: 20, color: colorScheme.primary),
                const SizedBox(width: SpacingTokens.xs),
                Text(
                  'Time Studied',
                  style: theme.textTheme.titleSmall
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
              ],
            ),

            const SizedBox(height: SpacingTokens.sm),

            // Today + This Week stats.
            Row(
              children: [
                _TimeStat(
                  label: 'Today',
                  minutes: todayTime.inMinutes,
                  colorScheme: colorScheme,
                  theme: theme,
                ),
                const SizedBox(width: SpacingTokens.lg),
                _TimeStat(
                  label: 'This week',
                  minutes: weekTime.inMinutes,
                  colorScheme: colorScheme,
                  theme: theme,
                ),
              ],
            ),

            const SizedBox(height: SpacingTokens.md),

            // 7-day mini bar chart.
            SizedBox(
              height: 48,
              child: _MiniBarChart(
                values: weekDays,
                colorScheme: colorScheme,
              ),
            ),

            // Day labels.
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: _dayLabels().map((label) {
                return SizedBox(
                  width: 28,
                  child: Text(
                    label,
                    textAlign: TextAlign.center,
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                      fontSize: 10,
                    ),
                  ),
                );
              }).toList(),
            ),
          ],
        ),
      ),
    );
  }

  List<String> _dayLabels() {
    const names = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    final now = DateTime.now();
    return List.generate(7, (i) {
      final day = now.subtract(Duration(days: 6 - i));
      return names[(day.weekday - 1) % 7];
    });
  }
}

// ---------------------------------------------------------------------------
// Time Stat
// ---------------------------------------------------------------------------

class _TimeStat extends StatelessWidget {
  const _TimeStat({
    required this.label,
    required this.minutes,
    required this.colorScheme,
    required this.theme,
  });

  final String label;
  final int minutes;
  final ColorScheme colorScheme;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: theme.textTheme.labelSmall?.copyWith(
            color: colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 2),
        Text(
          _formatMinutes(minutes),
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    );
  }

  String _formatMinutes(int totalMinutes) {
    if (totalMinutes < 60) return '${totalMinutes}m';
    final hours = totalMinutes ~/ 60;
    final mins = totalMinutes % 60;
    if (mins == 0) return '${hours}h';
    return '${hours}h ${mins}m';
  }
}

// ---------------------------------------------------------------------------
// Mini Bar Chart
// ---------------------------------------------------------------------------

class _MiniBarChart extends StatelessWidget {
  const _MiniBarChart({
    required this.values,
    required this.colorScheme,
  });

  final List<int> values;
  final ColorScheme colorScheme;

  @override
  Widget build(BuildContext context) {
    final maxVal = values.fold<int>(1, (m, v) => v > m ? v : m);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: values.map((v) {
        final fraction = v / maxVal;
        final isToday = v == values.last;
        return Expanded(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 2),
            child: FractionallySizedBox(
              heightFactor: fraction.clamp(0.05, 1.0),
              child: Container(
                decoration: BoxDecoration(
                  color: isToday
                      ? colorScheme.primary
                      : colorScheme.primary.withValues(alpha: 0.4),
                  borderRadius: BorderRadius.circular(RadiusTokens.sm),
                ),
              ),
            ),
          ),
        );
      }).toList(),
    );
  }
}
