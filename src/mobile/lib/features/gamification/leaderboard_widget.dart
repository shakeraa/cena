// =============================================================================
// Cena Adaptive Learning Platform — Leaderboard Widget (MOB-GAM-001)
// =============================================================================
//
// Blueprint §10: Social Proof Without Shame
// - Aggregate class stats only, no named leaderboards
// - Lateral peer models (similar mastery)
// - All social features opt-in
// - Weekly reset to keep it fresh
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/state/app_state.dart';

// ---------------------------------------------------------------------------
// Leaderboard tab type
// ---------------------------------------------------------------------------

enum LeaderboardTab { classRank, schoolRank, weekly }

// ---------------------------------------------------------------------------
// Leaderboard entry DTO
// ---------------------------------------------------------------------------

class LeaderboardEntry {
  const LeaderboardEntry({
    required this.rank,
    required this.displayName,
    required this.xp,
    required this.isCurrentUser,
    this.avatarUrl,
  });

  final int rank;
  final String displayName;
  final int xp;
  final bool isCurrentUser;
  final String? avatarUrl;

  factory LeaderboardEntry.fromJson(Map<String, dynamic> json,
      {String? currentUserId}) {
    return LeaderboardEntry(
      rank: (json['rank'] as num?)?.toInt() ?? 0,
      displayName: json['displayName'] as String? ?? 'Student',
      xp: (json['xp'] as num?)?.toInt() ?? 0,
      isCurrentUser: json['studentId'] == currentUserId,
      avatarUrl: json['avatarUrl'] as String?,
    );
  }
}

// ---------------------------------------------------------------------------
// Provider — fetches leaderboard from REST
// ---------------------------------------------------------------------------

final leaderboardProvider = FutureProvider.autoDispose
    .family<List<LeaderboardEntry>, LeaderboardTab>((ref, tab) async {
  try {
    final api = ref.watch(apiClientProvider);
    final tabName = tab.name;
    final response = await api.get<Map<String, dynamic>>(
      '/leaderboard',
      queryParameters: {'scope': tabName, 'limit': '20'},
    );
    final data = response.data;
    if (data == null) return [];
    final items = data['entries'] as List<dynamic>? ?? [];
    final student = ref.read(currentStudentProvider);
    return items
        .map((e) => LeaderboardEntry.fromJson(
              e as Map<String, dynamic>,
              currentUserId: student?.id,
            ))
        .toList();
  } catch (_) {
    return [];
  }
});

// ---------------------------------------------------------------------------
// Widget
// ---------------------------------------------------------------------------

/// Leaderboard card with tab switching (class / school / weekly).
/// Shows anonymized aggregate rankings per blueprint ethics guidelines.
class LeaderboardWidget extends ConsumerStatefulWidget {
  const LeaderboardWidget({super.key});

  @override
  ConsumerState<LeaderboardWidget> createState() => _LeaderboardWidgetState();
}

class _LeaderboardWidgetState extends ConsumerState<LeaderboardWidget> {
  LeaderboardTab _selectedTab = LeaderboardTab.classRank;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final entries = ref.watch(leaderboardProvider(_selectedTab));

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header
            Row(
              children: [
                Icon(Icons.leaderboard_rounded,
                    size: 20, color: colorScheme.primary),
                const SizedBox(width: SpacingTokens.sm),
                Text(
                  'Leaderboard',
                  style: theme.textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const Spacer(),
                Text(
                  'Resets weekly',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),

            // Tab selector
            Row(
              children: LeaderboardTab.values.map((tab) {
                final isSelected = tab == _selectedTab;
                return Padding(
                  padding: const EdgeInsets.only(right: SpacingTokens.sm),
                  child: ChoiceChip(
                    label: Text(_tabLabel(tab)),
                    selected: isSelected,
                    onSelected: (_) =>
                        setState(() => _selectedTab = tab),
                  ),
                );
              }).toList(),
            ),
            const SizedBox(height: SpacingTokens.sm),

            // Entries
            entries.when(
              loading: () => const Padding(
                padding: EdgeInsets.all(SpacingTokens.lg),
                child: Center(child: CircularProgressIndicator()),
              ),
              error: (_, __) => Padding(
                padding: const EdgeInsets.all(SpacingTokens.md),
                child: Text(
                  'Could not load leaderboard',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
              ),
              data: (list) {
                if (list.isEmpty) {
                  return Padding(
                    padding: const EdgeInsets.all(SpacingTokens.md),
                    child: Text(
                      'No data yet — complete sessions to appear here',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  );
                }
                return Column(
                  children: list.take(10).map((entry) {
                    return _LeaderboardRow(entry: entry);
                  }).toList(),
                );
              },
            ),
          ],
        ),
      ),
    );
  }

  String _tabLabel(LeaderboardTab tab) {
    switch (tab) {
      case LeaderboardTab.classRank:
        return 'Class';
      case LeaderboardTab.schoolRank:
        return 'School';
      case LeaderboardTab.weekly:
        return 'Weekly';
    }
  }
}

class _LeaderboardRow extends StatelessWidget {
  const _LeaderboardRow({required this.entry});

  final LeaderboardEntry entry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(
        vertical: SpacingTokens.sm,
        horizontal: SpacingTokens.sm,
      ),
      decoration: entry.isCurrentUser
          ? BoxDecoration(
              color: colorScheme.primaryContainer.withValues(alpha: 0.3),
              borderRadius: BorderRadius.circular(RadiusTokens.md),
            )
          : null,
      child: Row(
        children: [
          SizedBox(
            width: 28,
            child: Text(
              '${entry.rank}',
              style: theme.textTheme.labelLarge?.copyWith(
                fontWeight: FontWeight.w700,
                color: entry.rank <= 3
                    ? _medalColor(entry.rank)
                    : colorScheme.onSurfaceVariant,
              ),
            ),
          ),
          CircleAvatar(
            radius: 14,
            backgroundColor: colorScheme.surfaceContainerHighest,
            child: Text(
              entry.displayName.isNotEmpty
                  ? entry.displayName[0].toUpperCase()
                  : '?',
              style: theme.textTheme.labelSmall?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: Text(
              entry.isCurrentUser ? 'You' : entry.displayName,
              style: theme.textTheme.bodySmall?.copyWith(
                fontWeight:
                    entry.isCurrentUser ? FontWeight.w700 : FontWeight.w400,
              ),
              overflow: TextOverflow.ellipsis,
            ),
          ),
          Text(
            '${entry.xp} XP',
            style: theme.textTheme.labelMedium?.copyWith(
              fontWeight: FontWeight.w600,
              color: const Color(0xFFFFAA00),
            ),
          ),
        ],
      ),
    );
  }

  Color _medalColor(int rank) {
    switch (rank) {
      case 1:
        return const Color(0xFFFFD700);
      case 2:
        return const Color(0xFFC0C0C0);
      case 3:
        return const Color(0xFFCD7F32);
      default:
        return const Color(0xFF607D8B);
    }
  }
}
