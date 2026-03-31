// =============================================================================
// Cena Adaptive Learning Platform — Profile Screen (MOB-CORE-008)
// =============================================================================

import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/router.dart';
import '../../core/services/auth_service.dart';
import '../../core/state/app_state.dart';
import '../../l10n/app_localizations.dart';

/// Profile screen showing user info, school/grade details, and account actions.
class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final firebaseUser = FirebaseAuth.instance.currentUser;
    final student = ref.watch(currentStudentProvider);

    final displayName =
        firebaseUser?.displayName ?? student?.name ?? 'Student';
    final email = firebaseUser?.email ?? '';
    final photoUrl = firebaseUser?.photoURL;

    final l = AppLocalizations.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(l.profile)),
      body: ListView(
        padding: const EdgeInsets.all(SpacingTokens.md),
        children: [
          // Avatar + name header
          Center(
            child: Column(
              children: [
                CircleAvatar(
                  radius: 48,
                  backgroundImage:
                      photoUrl != null ? NetworkImage(photoUrl) : null,
                  backgroundColor: colorScheme.primaryContainer,
                  child: photoUrl == null
                      ? Icon(
                          Icons.person_rounded,
                          size: 48,
                          color: colorScheme.onPrimaryContainer,
                        )
                      : null,
                ),
                const SizedBox(height: SpacingTokens.md),
                Text(
                  displayName,
                  style: theme.textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
                if (email.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: SpacingTokens.xs),
                    child: Text(
                      email,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
              ],
            ),
          ),

          const SizedBox(height: SpacingTokens.xl),

          // School & Grade info
          Card(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(
                    SpacingTokens.md,
                    SpacingTokens.md,
                    SpacingTokens.md,
                    SpacingTokens.sm,
                  ),
                  child: Text(
                    l.academicInfo,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                _InfoTile(
                  icon: Icons.local_fire_department_rounded,
                  label: l.streak,
                  value: l.nDays(student?.streak ?? 0),
                ),
                _InfoTile(
                  icon: Icons.star_rounded,
                  label: l.xp,
                  value: '${student?.xp ?? 0}',
                ),
                _InfoTile(
                  icon: Icons.emoji_events_rounded,
                  label: l.level,
                  value: '${student?.level ?? 1}',
                ),
                _InfoTile(
                  icon: Icons.fingerprint_rounded,
                  label: l.userId,
                  value: firebaseUser?.uid.substring(0, 8) ?? '—',
                ),
              ],
            ),
          ),

          const SizedBox(height: SpacingTokens.md),

          // Account actions
          Card(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(
                    SpacingTokens.md,
                    SpacingTokens.md,
                    SpacingTokens.md,
                    SpacingTokens.sm,
                  ),
                  child: Text(
                    l.account,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                ListTile(
                  leading: const Icon(Icons.edit_rounded),
                  title: Text(l.editDisplayName),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => _editDisplayName(context, firebaseUser),
                ),
                ListTile(
                  leading: Icon(
                    Icons.logout_rounded,
                    color: colorScheme.error,
                  ),
                  title: Text(
                    l.signOut,
                    style: TextStyle(color: colorScheme.error),
                  ),
                  onTap: () async {
                    final confirmed = await showDialog<bool>(
                      context: context,
                      builder: (ctx) {
                        final dl = AppLocalizations.of(ctx);
                        return AlertDialog(
                          title: Text(dl.signOut),
                          content: Text(dl.signOutConfirm),
                          actions: [
                            TextButton(
                              onPressed: () => Navigator.pop(ctx, false),
                              child: Text(dl.cancel),
                            ),
                            TextButton(
                              onPressed: () => Navigator.pop(ctx, true),
                              child: Text(dl.signOut),
                            ),
                          ],
                        );
                      },
                    );
                    if (confirmed == true && context.mounted) {
                      await ref.read(authNotifierProvider.notifier).signOut();
                      if (context.mounted) context.go(CenaRoutes.login);
                    }
                  },
                ),
              ],
            ),
          ),

          const SizedBox(height: SpacingTokens.md),

          // App version
          Center(
            child: Text(
              l.appVersion,
              style: theme.textTheme.bodySmall?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _editDisplayName(
      BuildContext context, User? firebaseUser) async {
    if (firebaseUser == null) return;
    final controller =
        TextEditingController(text: firebaseUser.displayName ?? '');
    final result = await showDialog<String>(
      context: context,
      builder: (ctx) {
        final dl = AppLocalizations.of(ctx);
        return AlertDialog(
          title: Text(dl.editDisplayName),
          content: TextField(
            controller: controller,
            autofocus: true,
            decoration: InputDecoration(
              labelText: dl.displayName,
              hintText: dl.enterYourName,
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: Text(dl.cancel),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(ctx, controller.text.trim()),
              child: Text(dl.save),
            ),
          ],
        );
      },
    );
    if (result != null && result.isNotEmpty) {
      await firebaseUser.updateDisplayName(result);
    }
  }
}

class _InfoTile extends StatelessWidget {
  const _InfoTile({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ListTile(
      leading: Icon(icon, size: 20),
      title: Text(label, style: theme.textTheme.bodySmall),
      trailing: Text(
        value,
        style: theme.textTheme.bodyMedium?.copyWith(
          fontWeight: FontWeight.w600,
        ),
      ),
      dense: true,
    );
  }
}
