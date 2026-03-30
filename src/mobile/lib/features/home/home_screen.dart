// =============================================================================
// Cena Adaptive Learning Platform — Home Screen
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/router.dart';
import '../gamification/gamification_screen.dart';

/// Navigation tab indices for the home screen bottom bar.
enum HomeTab {
  home('Home', Icons.home_rounded),
  sessions('Sessions', Icons.play_circle_outline_rounded),
  progress('Progress', Icons.bar_chart_rounded),
  settings('Settings', Icons.settings_outlined);

  const HomeTab(this.label, this.icon);
  final String label;
  final IconData icon;
}

/// Home screen with bottom navigation bar.
///
/// Displays a greeting card, subject quick-start buttons, and recent
/// session history. The bottom navigation provides access to Sessions,
/// Progress, and Settings tabs.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  HomeTab _currentTab = HomeTab.home;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Cena'),
        actions: [
          IconButton(
            icon: const Icon(Icons.notifications_outlined),
            onPressed: () {
              // Notification center — wired in MOB-008 (push notifications)
            },
          ),
        ],
      ),
      body: _buildTabContent(),
      bottomNavigationBar: BottomNavigationBar(
        currentIndex: _currentTab.index,
        onTap: (index) {
          setState(() {
            _currentTab = HomeTab.values[index];
          });
        },
        items: HomeTab.values.map((tab) {
          return BottomNavigationBarItem(
            icon: Icon(tab.icon),
            label: tab.label,
          );
        }).toList(),
      ),
    );
  }

  Widget _buildTabContent() {
    switch (_currentTab) {
      case HomeTab.home:
        return const _HomeTabContent();
      case HomeTab.sessions:
        return const _SessionsTabContent();
      case HomeTab.progress:
        return const _ProgressTabContent();
      case HomeTab.settings:
        return const _SettingsTabContent();
    }
  }
}

/// Home tab: greeting card, subject buttons, and recent activity.
class _HomeTabContent extends ConsumerWidget {
  const _HomeTabContent();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return ListView(
      padding: const EdgeInsets.all(SpacingTokens.md),
      children: [
        // Greeting Card
        Card(
          child: Padding(
            padding: const EdgeInsets.all(SpacingTokens.lg),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  _greetingForTimeOfDay(),
                  style: theme.textTheme.headlineMedium,
                ),
                const SizedBox(height: SpacingTokens.xs),
                Text(
                  'Ready to learn?',
                  style: theme.textTheme.bodyLarge?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: SpacingTokens.md),
                // Streak indicator
                Row(
                  children: [
                    Icon(
                      Icons.local_fire_department_rounded,
                      color: colorScheme.secondary,
                      size: 20,
                    ),
                    const SizedBox(width: SpacingTokens.xs),
                    Text(
                      '0 day streak',
                      style: theme.textTheme.labelLarge?.copyWith(
                        color: colorScheme.secondary,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),

        const SizedBox(height: SpacingTokens.lg),

        // Subject Quick Start
        Text(
          'Start a Session',
          style: theme.textTheme.titleLarge,
        ),
        const SizedBox(height: SpacingTokens.sm),
        _SubjectGrid(),

        const SizedBox(height: SpacingTokens.lg),

        // Quick Action Button
        FilledButton.icon(
          onPressed: () => context.go(CenaRoutes.session),
          icon: const Icon(Icons.play_arrow_rounded),
          label: const Text('Start Learning'),
        ),
      ],
    );
  }

  /// Returns a time-appropriate greeting.
  static String _greetingForTimeOfDay() {
    final hour = DateTime.now().hour;
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }
}

/// Grid of subject cards for quick session start.
class _SubjectGrid extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final subjects = [
      _SubjectItem(
        name: 'Math',
        icon: Icons.functions_rounded,
        primary: SubjectColorTokens.mathPrimary,
        background: SubjectColorTokens.mathBackground,
      ),
      _SubjectItem(
        name: 'Physics',
        icon: Icons.speed_rounded,
        primary: SubjectColorTokens.physicsPrimary,
        background: SubjectColorTokens.physicsBackground,
      ),
      _SubjectItem(
        name: 'Chemistry',
        icon: Icons.science_rounded,
        primary: SubjectColorTokens.chemistryPrimary,
        background: SubjectColorTokens.chemistryBackground,
      ),
      _SubjectItem(
        name: 'Biology',
        icon: Icons.biotech_rounded,
        primary: SubjectColorTokens.biologyPrimary,
        background: SubjectColorTokens.biologyBackground,
      ),
      _SubjectItem(
        name: 'CS',
        icon: Icons.computer_rounded,
        primary: SubjectColorTokens.csPrimary,
        background: SubjectColorTokens.csBackground,
      ),
    ];

    return GridView.builder(
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 3,
        mainAxisSpacing: SpacingTokens.sm,
        crossAxisSpacing: SpacingTokens.sm,
        childAspectRatio: 1.0,
      ),
      itemCount: subjects.length,
      itemBuilder: (context, index) {
        final subject = subjects[index];
        return Card(
          color: subject.background,
          child: InkWell(
            onTap: () {
              // Navigate to session with subject pre-selected
              context.go(CenaRoutes.session);
            },
            borderRadius: BorderRadius.circular(RadiusTokens.lg),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(subject.icon, color: subject.primary, size: 32),
                const SizedBox(height: SpacingTokens.xs),
                Text(
                  subject.name,
                  style: Theme.of(context).textTheme.labelLarge?.copyWith(
                    color: subject.primary,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }
}

/// Data class for subject grid items.
class _SubjectItem {
  const _SubjectItem({
    required this.name,
    required this.icon,
    required this.primary,
    required this.background,
  });

  final String name;
  final IconData icon;
  final Color primary;
  final Color background;
}

/// Sessions tab: shows history and active sessions.
class _SessionsTabContent extends StatelessWidget {
  const _SessionsTabContent();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.history_rounded,
              size: 64,
              color: theme.colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
            ),
            const SizedBox(height: SpacingTokens.md),
            Text(
              'No sessions yet',
              style: theme.textTheme.titleMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              'Start your first learning session to see your history here.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.lg),
            FilledButton.icon(
              onPressed: () => context.go(CenaRoutes.session),
              icon: const Icon(Icons.play_arrow_rounded),
              label: const Text('Start Session'),
            ),
          ],
        ),
      ),
    );
  }
}

/// Progress tab: gamification screen (XP, streak, badges, recent activity).
class _ProgressTabContent extends StatelessWidget {
  const _ProgressTabContent();

  @override
  Widget build(BuildContext context) {
    return const GamificationScreen();
  }
}

/// Settings tab: app preferences and account management.
class _SettingsTabContent extends StatelessWidget {
  const _SettingsTabContent();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return ListView(
      padding: const EdgeInsets.all(SpacingTokens.md),
      children: [
        // Language Selection
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
                  'Language',
                  style: theme.textTheme.titleMedium,
                ),
              ),
              ListTile(
                leading: const Icon(Icons.language_rounded),
                title: const Text('Hebrew'),
                trailing: const Icon(Icons.check_rounded),
                onTap: () {
                  // Language switching — wired via currentLocaleProvider
                },
              ),
              ListTile(
                leading: const Icon(Icons.language_rounded),
                title: const Text('Arabic'),
                onTap: () {
                  // Language switching
                },
              ),
              ListTile(
                leading: const Icon(Icons.language_rounded),
                title: const Text('English'),
                onTap: () {
                  // Language switching
                },
              ),
            ],
          ),
        ),

        const SizedBox(height: SpacingTokens.md),

        // Account Section
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
                  'Account',
                  style: theme.textTheme.titleMedium,
                ),
              ),
              ListTile(
                leading: const Icon(Icons.person_outline_rounded),
                title: const Text('Profile'),
                trailing: const Icon(Icons.chevron_right),
                onTap: () {
                  // Profile screen — future task
                },
              ),
              ListTile(
                leading: Icon(
                  Icons.logout_rounded,
                  color: theme.colorScheme.error,
                ),
                title: Text(
                  'Sign Out',
                  style: TextStyle(color: theme.colorScheme.error),
                ),
                onTap: () {
                  // Sign out — wired in MOB-002 (auth)
                },
              ),
            ],
          ),
        ),
      ],
    );
  }
}
