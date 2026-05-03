// =============================================================================
// Cena Adaptive Learning Platform — Notification Center Screen (MOB-CORE-007)
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/state/outreach_notifier.dart';

/// Full-screen notification center accessed from the app bar bell icon.
class NotificationCenterScreen extends ConsumerWidget {
  const NotificationCenterScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final outreach = ref.watch(outreachProvider);
    final notifications = outreach.pendingNotifications;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Notifications'),
        actions: [
          if (notifications.isNotEmpty)
            TextButton(
              onPressed: () =>
                  ref.read(outreachProvider.notifier).markAllRead(),
              child: const Text('Mark all read'),
            ),
        ],
      ),
      body: notifications.isEmpty
          ? _EmptyState()
          : ListView.separated(
              padding: const EdgeInsets.symmetric(vertical: SpacingTokens.sm),
              itemCount: notifications.length,
              separatorBuilder: (_, __) => const Divider(height: 1),
              itemBuilder: (context, index) {
                final notif = notifications[index];
                return _NotificationTile(
                  notification: notif,
                  onTap: () {
                    ref.read(outreachProvider.notifier).markRead(notif.id);
                    if (notif.actionRoute != null && context.mounted) {
                      context.push(notif.actionRoute!);
                    }
                  },
                  onDismiss: () {
                    ref.read(outreachProvider.notifier).dismiss(notif.id);
                  },
                );
              },
            ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.notifications_none_rounded,
              size: 64,
              color: theme.colorScheme.onSurfaceVariant.withValues(alpha: 0.4),
            ),
            const SizedBox(height: SpacingTokens.md),
            Text(
              'No notifications',
              style: theme.textTheme.titleMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              'You\'re all caught up!',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _NotificationTile extends StatelessWidget {
  const _NotificationTile({
    required this.notification,
    required this.onTap,
    required this.onDismiss,
  });

  final AppNotification notification;
  final VoidCallback onTap;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isUnread = !notification.isRead;

    return Dismissible(
      key: ValueKey(notification.id),
      direction: DismissDirection.endToStart,
      onDismissed: (_) => onDismiss(),
      background: Container(
        alignment: Alignment.centerRight,
        padding: const EdgeInsets.only(right: SpacingTokens.lg),
        color: colorScheme.errorContainer,
        child: Icon(Icons.delete_outline_rounded,
            color: colorScheme.onErrorContainer),
      ),
      child: ListTile(
        leading: Container(
          width: 40,
          height: 40,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: isUnread
                ? colorScheme.primaryContainer
                : colorScheme.surfaceContainerHighest,
          ),
          child: Icon(
            notification.actionRoute != null
                ? Icons.open_in_new_rounded
                : Icons.notifications_rounded,
            size: 20,
            color: isUnread
                ? colorScheme.onPrimaryContainer
                : colorScheme.onSurfaceVariant,
          ),
        ),
        title: Text(
          notification.title,
          style: theme.textTheme.bodyMedium?.copyWith(
            fontWeight: isUnread ? FontWeight.w600 : FontWeight.w400,
          ),
        ),
        subtitle: Text(
          notification.body,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: theme.textTheme.bodySmall?.copyWith(
            color: colorScheme.onSurfaceVariant,
          ),
        ),
        trailing: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Text(
              _formatTime(notification.createdAt),
              style: theme.textTheme.labelSmall?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
            if (isUnread)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Container(
                  width: 8,
                  height: 8,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: colorScheme.primary,
                  ),
                ),
              ),
          ],
        ),
        onTap: onTap,
      ),
    );
  }

  String _formatTime(DateTime dt) {
    final diff = DateTime.now().difference(dt);
    if (diff.inMinutes < 60) return '${diff.inMinutes}m';
    if (diff.inHours < 24) return '${diff.inHours}h';
    if (diff.inDays < 7) return '${diff.inDays}d';
    return '${dt.day}/${dt.month}';
  }
}
