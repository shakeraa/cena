// =============================================================================
// Cena Adaptive Learning Platform — Home Screen
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../app.dart' show currentLocaleProvider;
import '../../core/config/app_config.dart';
import '../../core/router.dart';
import '../../core/services/auth_service.dart';
import '../../core/state/app_state.dart' show apiClientProvider, appConfigProvider;
import '../../core/state/feature_discovery_state.dart';
import '../../core/state/gamification_state.dart';
import '../../core/state/interaction_feedback_state.dart';
import '../../core/state/momentum_state.dart';
import '../../core/state/outreach_notifier.dart';
import '../../core/theme/glass_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../gamification/gamification_screen.dart';
import '../knowledge_graph/knowledge_graph_screen.dart';

/// Navigation tab indices for the home screen bottom bar.
/// Order: Home | Learn | Map (center) | Progress | Profile
/// Map in center = easiest thumb reach (Cowan's 4±1, Doc 3).
enum HomeTab {
  home(Icons.home_rounded),
  learn(Icons.play_circle_outline_rounded),
  map(Icons.hub_rounded),
  progress(Icons.bar_chart_rounded),
  profile(Icons.person_outline_rounded);

  const HomeTab(this.icon);
  final IconData icon;
}

/// Returns the localized label for each [HomeTab].
String _tabLabel(HomeTab tab, AppLocalizations l) {
  switch (tab) {
    case HomeTab.home:
      return l.tabHome;
    case HomeTab.learn:
      return l.tabLearn;
    case HomeTab.map:
      return l.tabMap;
    case HomeTab.progress:
      return l.tabProgress;
    case HomeTab.profile:
      return l.tabProfile;
  }
}

/// Home screen with bottom navigation bar.
///
/// 5-tab layout: Home | Learn | Map | Progress | Profile
/// Map tab surfaces Knowledge Graph as first-class hero differentiator.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  HomeTab _currentTab = HomeTab.home;

  void _onDestinationSelected(int index, FeatureDiscoveryState discovery) {
    final selected = HomeTab.values[index];
    setState(() {
      _currentTab = selected;
    });

    // Mark the Knowledge Graph "NEW" badge as seen on first map visit.
    if (selected == HomeTab.map && discovery.showKnowledgeGraphNewBadge) {
      ref.read(featureDiscoveryProvider.notifier).markKnowledgeGraphNewSeen();
    }
  }

  @override
  Widget build(BuildContext context) {
    final discovery = ref.watch(featureDiscoveryProvider);
    final l = AppLocalizations.of(context);

    ref.listen<FeatureDiscoveryState>(featureDiscoveryProvider, (prev, next) {
      // Session 5: introduce streak mechanic with lightweight celebration.
      final unlockedStreak =
          (prev?.streakUnlocked ?? false) == false && next.streakUnlocked;
      if (unlockedStreak && !next.streakUnlockCelebrated && context.mounted) {
        final ll = AppLocalizations.of(context);
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(ll.newStreakUnlocked)),
        );
        ref
            .read(featureDiscoveryProvider.notifier)
            .markStreakUnlockCelebrated();
      }

      // Session 10: social feature unlock notification when enabled.
      final unlockedStudyGroups =
          (prev?.studyGroupsUnlocked ?? false) == false &&
              next.studyGroupsUnlocked;
      if (unlockedStudyGroups &&
          !next.studyGroupsUnlockNotified &&
          context.mounted) {
        final ll = AppLocalizations.of(context);
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(ll.newStudyGroups)),
        );
        ref
            .read(featureDiscoveryProvider.notifier)
            .markStudyGroupsUnlockNotified();
      }
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(l.appTitle),
        actions: [
          _NotificationBellButton(),
        ],
      ),
      body: _buildTabContent(discovery),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _currentTab.index,
        onDestinationSelected: (index) =>
            _onDestinationSelected(index, discovery),
        destinations: HomeTab.values.map((tab) {
          return NavigationDestination(
            icon: _DestinationIcon(tab: tab, discovery: discovery),
            label: _tabLabel(tab, l),
          );
        }).toList(),
      ),
    );
  }

  Widget _buildTabContent(FeatureDiscoveryState discovery) {
    switch (_currentTab) {
      case HomeTab.home:
        return _HomeTabContent(discovery: discovery);
      case HomeTab.learn:
        return const _SessionsTabContent();
      case HomeTab.map:
        return _MapTabContent(discovery: discovery);
      case HomeTab.progress:
        return const _ProgressTabContent();
      case HomeTab.profile:
        return const _SettingsTabContent();
    }
  }
}

class _DestinationIcon extends StatelessWidget {
  const _DestinationIcon({
    required this.tab,
    required this.discovery,
  });

  final HomeTab tab;
  final FeatureDiscoveryState discovery;

  @override
  Widget build(BuildContext context) {
    final base = Icon(tab.icon);
    if (tab != HomeTab.map || !discovery.showKnowledgeGraphNewBadge) {
      return base;
    }

    return Stack(
      clipBehavior: Clip.none,
      children: [
        base,
        Positioned(
          top: -6,
          right: -10,
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
            decoration: BoxDecoration(
              color: Theme.of(context).colorScheme.tertiary,
              borderRadius: BorderRadius.circular(RadiusTokens.full),
            ),
            child: Text(
              AppLocalizations.of(context).newLabel,
              style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    color: Theme.of(context).colorScheme.onTertiary,
                    fontWeight: FontWeight.w700,
                    fontSize: 9,
                  ),
            ),
          ),
        ),
      ],
    );
  }
}

/// Home tab: Bento grid dashboard with 6 modules (MOB-VIS-002).
///
/// Layout:
/// ┌───────────────────────────────────────┐
/// │  GREETING + STREAK (full width)       │
/// ├───────────────┬───────────────────────┤
/// │  PROGRESS     │  DAILY CHALLENGE      │
/// │  RING         │  / QUICK START        │
/// ├───────────────┴───────────────────────┤
/// │  CONTINUE LEARNING (Hero CTA)         │
/// ├───────────────┬───────────────────────┤
/// │  BADGES       │  SUBJECT GRID         │
/// │  PREVIEW      │                       │
/// └───────────────┴───────────────────────┘
class _HomeTabContent extends ConsumerWidget {
  const _HomeTabContent({required this.discovery});

  final FeatureDiscoveryState discovery;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final xpProgress = ref.watch(xpProgressProvider);
    final level = ref.watch(levelProvider);
    final streak = ref.watch(streakProvider);
    final badges = ref.watch(badgesProvider);

    return ListView(
      padding: const EdgeInsets.all(SpacingTokens.md),
      children: [
        // Row 1: Greeting + streak (full width glass card)
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _greetingForTimeOfDay(l),
                          style: theme.textTheme.headlineMedium,
                        ),
                        const SizedBox(height: SpacingTokens.xs),
                        Text(
                          l.readyToLearn,
                          style: theme.textTheme.bodyLarge?.copyWith(
                            color: colorScheme.onSurfaceVariant,
                          ),
                        ),
                      ],
                    ),
                  ),
                  // Streak badge (if unlocked)
                  if (discovery.streakUnlocked)
                    GlassChip(
                      icon: Icons.local_fire_department_rounded,
                      label: l.dayStreak(streak),
                      color: colorScheme.secondary,
                    ),
                ],
              ),
            ],
          ),
        ),

        const SizedBox(height: SpacingTokens.sm),

        // Row 2: Progress ring + daily challenge (2-column)
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Progress ring
            Expanded(
              child: GlassCard(
                child: Column(
                  children: [
                    GlassProgressRing(
                      progress: xpProgress,
                      label: 'Lv $level',
                      size: 72,
                    ),
                    const SizedBox(height: SpacingTokens.sm),
                    Text(
                      l.levelN(level),
                      style: theme.textTheme.labelLarge?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    Text(
                      l.percentToNext((xpProgress * 100).toInt()),
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(width: SpacingTokens.sm),
            // Daily challenge / quick start
            Expanded(
              child: GlassCard(
                child: Column(
                  children: [
                    Icon(Icons.bolt_rounded,
                        size: 32, color: const Color(0xFFFFD700)),
                    const SizedBox(height: SpacingTokens.sm),
                    Text(
                      l.quickPractice,
                      style: theme.textTheme.labelLarge?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: SpacingTokens.sm),
                    FilledButton(
                      onPressed: () => context.go(CenaRoutes.session),
                      style: FilledButton.styleFrom(
                        minimumSize: const Size(double.infinity, 36),
                        padding: EdgeInsets.zero,
                      ),
                      child: Text(l.start),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),

        const SizedBox(height: SpacingTokens.sm),

        // Row 3: Continue Learning hero CTA (full width)
        GlassCard(
          padding: const EdgeInsets.all(SpacingTokens.lg),
          child: InkWell(
            onTap: () => context.go(CenaRoutes.session),
            child: Row(
              children: [
                Container(
                  width: 48,
                  height: 48,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: colorScheme.primary.withValues(alpha: 0.15),
                  ),
                  child: Icon(Icons.play_arrow_rounded,
                      color: colorScheme.primary, size: 28),
                ),
                const SizedBox(width: SpacingTokens.md),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        l.continueLearning,
                        style: theme.textTheme.titleMedium?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      Text(
                        l.pickUpWhereYouLeftOff,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
                Icon(Icons.arrow_forward_rounded,
                    color: colorScheme.onSurfaceVariant),
              ],
            ),
          ),
        ),

        const SizedBox(height: SpacingTokens.sm),

        // Row 4: Badges preview + subject grid (2-column)
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Badge preview
            Expanded(
              child: GlassCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Icon(Icons.emoji_events_rounded,
                            size: 18, color: colorScheme.tertiary),
                        const SizedBox(width: SpacingTokens.xs),
                        Text(
                          l.badges,
                          style: theme.textTheme.labelLarge?.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: SpacingTokens.sm),
                    if (badges.isEmpty)
                      Text(
                        l.completeSessions,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                      )
                    else
                      Wrap(
                        spacing: 4,
                        children: badges.take(4).map((b) {
                          return Container(
                            width: 28,
                            height: 28,
                            decoration: BoxDecoration(
                              shape: BoxShape.circle,
                              color: colorScheme.tertiary
                                  .withValues(alpha: 0.15),
                            ),
                            child: Icon(Icons.star_rounded,
                                size: 16, color: colorScheme.tertiary),
                          );
                        }).toList(),
                      ),
                  ],
                ),
              ),
            ),
            const SizedBox(width: SpacingTokens.sm),
            // AI Tutor shortcut
            Expanded(
              child: GlassCard(
                child: Column(
                  children: [
                    Icon(Icons.psychology_rounded,
                        size: 32, color: colorScheme.primary),
                    const SizedBox(height: SpacingTokens.sm),
                    Text(
                      l.aiTutor,
                      style: theme.textTheme.labelLarge?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: SpacingTokens.sm),
                    OutlinedButton(
                      onPressed: () => context.push(CenaRoutes.tutor),
                      style: OutlinedButton.styleFrom(
                        minimumSize: const Size(double.infinity, 36),
                        padding: EdgeInsets.zero,
                      ),
                      child: Text(l.chat),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),

        const SizedBox(height: SpacingTokens.lg),

        // Subject Quick Start
        Text(
          l.subjects,
          style: theme.textTheme.titleLarge,
        ),
        const SizedBox(height: SpacingTokens.sm),
        _SubjectGrid(),

        const SizedBox(height: SpacingTokens.lg),
      ],
    );
  }

  static String _greetingForTimeOfDay(AppLocalizations l) {
    final hour = DateTime.now().hour;
    if (hour < 12) return l.goodMorning;
    if (hour < 17) return l.goodAfternoon;
    return l.goodEvening;
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
/// Fetches from REST /api/sessions; falls back to empty state.
class _SessionsTabContent extends ConsumerWidget {
  const _SessionsTabContent();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final sessionHistory = ref.watch(_sessionHistoryProvider);

    return sessionHistory.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (_, __) => _SessionsEmptyState(),
      data: (sessions) {
        if (sessions.isEmpty) return _SessionsEmptyState();
        return ListView.builder(
          padding: const EdgeInsets.all(SpacingTokens.md),
          itemCount: sessions.length + 1, // +1 for the start button header
          itemBuilder: (context, index) {
            if (index == 0) {
              return Padding(
                padding: const EdgeInsets.only(bottom: SpacingTokens.md),
                child: FilledButton.icon(
                  onPressed: () => context.go(CenaRoutes.session),
                  icon: const Icon(Icons.play_arrow_rounded),
                  label: Text(l.startNewSession),
                  style: FilledButton.styleFrom(
                    minimumSize: const Size(double.infinity, 48),
                  ),
                ),
              );
            }
            final session = sessions[index - 1];
            return _SessionHistoryCard(session: session);
          },
        );
      },
    );
  }
}

class _SessionsEmptyState extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
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
              l.noSessionsYet,
              style: theme.textTheme.titleMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              l.startFirstSession,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.lg),
            FilledButton.icon(
              onPressed: () => context.go(CenaRoutes.session),
              icon: const Icon(Icons.play_arrow_rounded),
              label: Text(l.startSession),
            ),
          ],
        ),
      ),
    );
  }
}

class _SessionHistoryCard extends StatelessWidget {
  const _SessionHistoryCard({required this.session});

  final _SessionSummaryDto session;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final accuracy = (session.accuracy * 100).toInt();

    return Card(
      margin: const EdgeInsets.only(bottom: SpacingTokens.sm),
      child: ListTile(
        leading: Container(
          width: 40,
          height: 40,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: _subjectColor(session.subject).withValues(alpha: 0.15),
          ),
          child: Icon(
            _subjectIcon(session.subject),
            size: 20,
            color: _subjectColor(session.subject),
          ),
        ),
        title: Text(
          session.subject.isNotEmpty ? session.subject : l.practiceSession,
          style: theme.textTheme.bodyMedium?.copyWith(
            fontWeight: FontWeight.w600,
          ),
        ),
        subtitle: Text(
          l.accuracyStats(accuracy, session.questionsAttempted,
              session.durationMinutes),
          style: theme.textTheme.bodySmall?.copyWith(
            color: colorScheme.onSurfaceVariant,
          ),
        ),
        trailing: Text(
          _formatDate(session.startedAt, l),
          style: theme.textTheme.labelSmall?.copyWith(
            color: colorScheme.onSurfaceVariant,
          ),
        ),
      ),
    );
  }

  Color _subjectColor(String subject) {
    switch (subject.toLowerCase()) {
      case 'math':
      case 'mathematics':
        return SubjectColorTokens.mathPrimary;
      case 'physics':
        return SubjectColorTokens.physicsPrimary;
      case 'chemistry':
        return SubjectColorTokens.chemistryPrimary;
      case 'biology':
        return SubjectColorTokens.biologyPrimary;
      case 'cs':
        return SubjectColorTokens.csPrimary;
      default:
        return const Color(0xFF607D8B);
    }
  }

  IconData _subjectIcon(String subject) {
    switch (subject.toLowerCase()) {
      case 'math':
      case 'mathematics':
        return Icons.functions_rounded;
      case 'physics':
        return Icons.speed_rounded;
      case 'chemistry':
        return Icons.science_rounded;
      case 'biology':
        return Icons.biotech_rounded;
      case 'cs':
        return Icons.computer_rounded;
      default:
        return Icons.school_rounded;
    }
  }

  String _formatDate(DateTime dt, AppLocalizations l) {
    final now = DateTime.now();
    final diff = now.difference(dt);
    if (diff.inMinutes < 60) return l.minutesAgo(diff.inMinutes);
    if (diff.inHours < 24) return l.hoursAgo(diff.inHours);
    if (diff.inDays == 1) return l.yesterday;
    if (diff.inDays < 7) return l.daysAgo(diff.inDays);
    return '${dt.day}/${dt.month}';
  }
}

/// Map tab: Knowledge Graph — hero differentiator.
/// Renders inline without its own AppBar (home screen provides it).
class _MapTabContent extends StatelessWidget {
  const _MapTabContent({required this.discovery});

  final FeatureDiscoveryState discovery;

  @override
  Widget build(BuildContext context) {
    if (discovery.knowledgeGraphFullAccess) {
      return const KnowledgeGraphScreen();
    }

    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Card(
          child: Padding(
            padding: const EdgeInsets.all(SpacingTokens.lg),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  Icons.hub_rounded,
                  size: 56,
                  color: theme.colorScheme.primary,
                ),
                const SizedBox(height: SpacingTokens.md),
                Text(
                  l.knowledgeMapBuilding,
                  style: theme.textTheme.titleMedium,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: SpacingTokens.sm),
                Text(
                  l.knowledgeMapBuildingDesc,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
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

/// Progress tab: gamification screen (XP, streak, badges, recent activity).
class _ProgressTabContent extends StatelessWidget {
  const _ProgressTabContent();

  @override
  Widget build(BuildContext context) {
    return const GamificationScreen();
  }
}

/// Settings tab: app preferences and account management.
class _SettingsTabContent extends ConsumerWidget {
  const _SettingsTabContent();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final useMomentum = ref.watch(useMomentumMeterProvider);
    final feedbackPrefs = ref.watch(interactionFeedbackProvider);

    return ListView(
      padding: const EdgeInsets.all(SpacingTokens.md),
      children: [
        // Language Selection
        _LanguageCard(),

        const SizedBox(height: SpacingTokens.md),

        // Learning feedback style (streak vs momentum)
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
                  l.learningStyle,
                  style: theme.textTheme.titleMedium,
                ),
              ),
              SwitchListTile(
                value: useMomentum,
                onChanged: (value) {
                  ref
                      .read(useMomentumMeterProvider.notifier)
                      .setUseMomentum(value);
                },
                title: Text(l.useMomentumMeter),
                subtitle: Text(
                  useMomentum ? l.momentumDesc : l.streakDesc,
                ),
                secondary: const Icon(Icons.insights_rounded),
              ),
            ],
          ),
        ),

        const SizedBox(height: SpacingTokens.md),

        // Haptic & sound feedback
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
                  l.feedback,
                  style: theme.textTheme.titleMedium,
                ),
              ),
              SwitchListTile(
                value: feedbackPrefs.hapticsEnabled,
                onChanged: (enabled) {
                  ref
                      .read(interactionFeedbackProvider.notifier)
                      .setHapticsEnabled(enabled);
                },
                title: Text(l.haptics),
                subtitle: Text(l.hapticsDesc),
                secondary: const Icon(Icons.vibration_rounded),
              ),
              SwitchListTile(
                value: feedbackPrefs.soundsEnabled,
                onChanged: (enabled) {
                  ref
                      .read(interactionFeedbackProvider.notifier)
                      .setSoundsEnabled(enabled);
                },
                title: Text(l.soundEffects),
                subtitle: Text(l.soundsOffByDefault),
                secondary: const Icon(Icons.volume_up_outlined),
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
                  l.account,
                  style: theme.textTheme.titleMedium,
                ),
              ),
              ListTile(
                leading: const Icon(Icons.person_outline_rounded),
                title: Text(l.profile),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.push(CenaRoutes.profile),
              ),
              ListTile(
                leading: Icon(
                  Icons.logout_rounded,
                  color: theme.colorScheme.error,
                ),
                title: Text(
                  l.signOut,
                  style: TextStyle(color: theme.colorScheme.error),
                ),
                onTap: () async {
                  final ll = AppLocalizations.of(context);
                  final confirmed = await showDialog<bool>(
                    context: context,
                    builder: (ctx) => AlertDialog(
                      title: Text(ll.signOut),
                      content: Text(ll.signOutConfirm),
                      actions: [
                        TextButton(
                          onPressed: () => Navigator.pop(ctx, false),
                          child: Text(ll.cancel),
                        ),
                        TextButton(
                          onPressed: () => Navigator.pop(ctx, true),
                          child: Text(ll.signOut),
                        ),
                      ],
                    ),
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
      ],
    );
  }
}

// =============================================================================
// Notification bell with unread badge (MOB-CORE-007)
// =============================================================================

class _NotificationBellButton extends ConsumerWidget {
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unreadCount = ref.watch(
      outreachProvider.select((s) => s.unreadCount),
    );

    return IconButton(
      icon: Badge(
        isLabelVisible: unreadCount > 0,
        label: Text('$unreadCount'),
        child: const Icon(Icons.notifications_outlined),
      ),
      onPressed: () => context.push(CenaRoutes.notifications),
    );
  }
}

// =============================================================================
// Language switcher card (MOB-CORE-001)
// =============================================================================

/// SharedPreferences key for persisted locale.
const String _kLocaleKey = 'app_locale';

class _LanguageCard extends ConsumerWidget {
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final currentLocale = ref.watch(currentLocaleProvider);
    final config = ref.watch(appConfigProvider);
    final visibleLocales = AppLocales.visibleLocales(
      hebrewVisible: config.featureFlags.hebrewLocaleVisible,
    );

    return Card(
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
              l.language,
              style: theme.textTheme.titleMedium,
            ),
          ),
          for (final locale in visibleLocales)
            ListTile(
              leading: const Icon(Icons.language_rounded),
              title: Text(_localeLabelFor(locale)),
              trailing: currentLocale.languageCode == locale.languageCode
                  ? Icon(Icons.check_rounded, color: theme.colorScheme.primary)
                  : null,
              onTap: () async {
                ref.read(currentLocaleProvider.notifier).state = locale;
                final prefs = await SharedPreferences.getInstance();
                await prefs.setString(_kLocaleKey, locale.languageCode);
              },
            ),
        ],
      ),
    );
  }

  /// Returns a human-readable label for the given locale.
  static String _localeLabelFor(Locale locale) {
    switch (locale.languageCode) {
      case 'he':
        return '\u05E2\u05D1\u05E8\u05D9\u05EA (Hebrew)';
      case 'ar':
        return '\u0627\u0644\u0639\u0631\u0628\u064A\u0629 (Arabic)';
      case 'en':
        return 'English';
      default:
        return locale.languageCode;
    }
  }
}

// =============================================================================
// Session history provider + DTO (MOB-CORE-002)
// =============================================================================

class _SessionSummaryDto {
  const _SessionSummaryDto({
    required this.id,
    required this.subject,
    required this.accuracy,
    required this.questionsAttempted,
    required this.durationMinutes,
    required this.startedAt,
  });

  final String id;
  final String subject;
  final double accuracy;
  final int questionsAttempted;
  final int durationMinutes;
  final DateTime startedAt;

  factory _SessionSummaryDto.fromJson(Map<String, dynamic> json) {
    return _SessionSummaryDto(
      id: json['id'] as String? ?? '',
      subject: json['subject'] as String? ?? '',
      accuracy: (json['accuracy'] as num?)?.toDouble() ?? 0.0,
      questionsAttempted: (json['questionsAttempted'] as num?)?.toInt() ?? 0,
      durationMinutes: (json['durationMinutes'] as num?)?.toInt() ?? 0,
      startedAt: json['startedAt'] != null
          ? DateTime.parse(json['startedAt'] as String)
          : DateTime.now(),
    );
  }
}

/// Fetches session history from REST API.
/// Auto-refreshes when the provider is re-watched (e.g., returning to Learn tab).
final _sessionHistoryProvider =
    FutureProvider.autoDispose<List<_SessionSummaryDto>>((ref) async {
  try {
    final apiClient = ref.watch(apiClientProvider);
    final response = await apiClient.get<Map<String, dynamic>>(
      '/sessions',
      queryParameters: {'limit': '20', 'sort': 'startedAt:desc'},
    );
    final data = response.data;
    if (data == null) return [];
    final items = data['items'] as List<dynamic>? ?? [];
    return items
        .map((e) => _SessionSummaryDto.fromJson(e as Map<String, dynamic>))
        .toList();
  } catch (_) {
    return [];
  }
});
