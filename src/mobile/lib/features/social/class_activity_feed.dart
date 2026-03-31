// =============================================================================
// Cena — Class Activity Feed (MOB-SOC-001) + Study Groups (MOB-SOC-002)
// =============================================================================
// Blueprint §10: Social Proof Without Shame — aggregate stats only,
// no named leaderboards, opt-in social features.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/state/app_state.dart';

// ---------------------------------------------------------------------------
// Activity feed item
// ---------------------------------------------------------------------------

class ActivityItem {
  const ActivityItem({
    required this.message,
    required this.timestamp,
    this.icon = Icons.people_rounded,
  });

  final String message;
  final DateTime timestamp;
  final IconData icon;

  factory ActivityItem.fromJson(Map<String, dynamic> json) => ActivityItem(
        message: json['message'] as String? ?? '',
        timestamp: json['timestamp'] != null
            ? DateTime.parse(json['timestamp'] as String)
            : DateTime.now(),
      );
}

// ---------------------------------------------------------------------------
// Study group
// ---------------------------------------------------------------------------

class StudyGroup {
  const StudyGroup({
    required this.id,
    required this.name,
    required this.memberCount,
    this.isJoined = false,
  });

  final String id;
  final String name;
  final int memberCount;
  final bool isJoined;

  factory StudyGroup.fromJson(Map<String, dynamic> json) => StudyGroup(
        id: json['id'] as String? ?? '',
        name: json['name'] as String? ?? '',
        memberCount: (json['memberCount'] as num?)?.toInt() ?? 0,
        isJoined: json['isJoined'] as bool? ?? false,
      );
}

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

final classActivityProvider =
    FutureProvider.autoDispose<List<ActivityItem>>((ref) async {
  try {
    final api = ref.watch(apiClientProvider);
    final response = await api.get<Map<String, dynamic>>('/social/activity');
    final items = response.data?['items'] as List<dynamic>? ?? [];
    return items
        .map((e) => ActivityItem.fromJson(e as Map<String, dynamic>))
        .toList();
  } catch (_) {
    return [];
  }
});

final studyGroupsProvider =
    FutureProvider.autoDispose<List<StudyGroup>>((ref) async {
  try {
    final api = ref.watch(apiClientProvider);
    final response = await api.get<Map<String, dynamic>>('/social/groups');
    final items = response.data?['groups'] as List<dynamic>? ?? [];
    return items
        .map((e) => StudyGroup.fromJson(e as Map<String, dynamic>))
        .toList();
  } catch (_) {
    return [];
  }
});

// ---------------------------------------------------------------------------
// Class Activity Feed Widget
// ---------------------------------------------------------------------------

class ClassActivityFeed extends ConsumerWidget {
  const ClassActivityFeed({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final activity = ref.watch(classActivityProvider);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.groups_rounded, size: 20, color: colorScheme.primary),
                const SizedBox(width: SpacingTokens.sm),
                Text('Class Activity',
                    style: theme.textTheme.titleMedium
                        ?.copyWith(fontWeight: FontWeight.w700)),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),
            activity.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (_, __) => Text('Could not load activity',
                  style: theme.textTheme.bodySmall),
              data: (items) {
                if (items.isEmpty) {
                  return Text('No class activity yet',
                      style: theme.textTheme.bodySmall
                          ?.copyWith(color: colorScheme.onSurfaceVariant));
                }
                return Column(
                  children: items
                      .take(5)
                      .map((item) => Padding(
                            padding: const EdgeInsets.symmetric(
                                vertical: SpacingTokens.xs),
                            child: Row(
                              children: [
                                Icon(item.icon,
                                    size: 16,
                                    color: colorScheme.onSurfaceVariant),
                                const SizedBox(width: SpacingTokens.sm),
                                Expanded(
                                  child: Text(item.message,
                                      style: theme.textTheme.bodySmall),
                                ),
                              ],
                            ),
                          ))
                      .toList(),
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Study Groups Widget
// ---------------------------------------------------------------------------

class StudyGroupsList extends ConsumerWidget {
  const StudyGroupsList({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final groups = ref.watch(studyGroupsProvider);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.group_work_rounded,
                    size: 20, color: colorScheme.tertiary),
                const SizedBox(width: SpacingTokens.sm),
                Text('Study Groups',
                    style: theme.textTheme.titleMedium
                        ?.copyWith(fontWeight: FontWeight.w700)),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),
            groups.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (_, __) => Text('Could not load groups',
                  style: theme.textTheme.bodySmall),
              data: (list) {
                if (list.isEmpty) {
                  return Text('No study groups available yet',
                      style: theme.textTheme.bodySmall
                          ?.copyWith(color: colorScheme.onSurfaceVariant));
                }
                return Column(
                  children: list
                      .map((g) => ListTile(
                            dense: true,
                            leading: CircleAvatar(
                              radius: 16,
                              backgroundColor: colorScheme.tertiaryContainer,
                              child: Text(g.name.isNotEmpty ? g.name[0] : '?',
                                  style: theme.textTheme.labelSmall),
                            ),
                            title: Text(g.name),
                            subtitle: Text('${g.memberCount} members'),
                            trailing: g.isJoined
                                ? const Chip(label: Text('Joined'))
                                : OutlinedButton(
                                    onPressed: () {},
                                    child: const Text('Join'),
                                  ),
                          ))
                      .toList(),
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}
