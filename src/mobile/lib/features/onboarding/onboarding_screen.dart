// =============================================================================
// Cena Adaptive Learning Platform — Onboarding Flow V2 (MOB-013 + MOB-033)
// =============================================================================
//
// Role-based onboarding experience:
//   Student (8 pages):
//     Page 1 — Welcome (branding, language selector)
//     Page 2 — Role Selection (student / teacher / parent)
//     Page 3 — Subject Selection
//     Page 4 — Grade & Bagrut Track
//     Page 5 — Goal Setting (Bagrut prep, homework help, get ahead, review)
//     Page 6 — Time Commitment (5/10/15/20 min)
//     Page 7 — Discovery Tour (diagnostic, reframed as exploration)
//     Page 8 — Ready (summary + "Start Learning")
//   Teacher (4 pages): Welcome, Role, Subjects, Ready
//   Parent  (4 pages): Welcome, Role, Child Info, Ready
//
// Persists completion via SharedPreferences so the router can redirect
// first-time users automatically.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/models/domain_models.dart';
import '../../core/router.dart';
import '../../core/state/app_state.dart';
import '../../l10n/app_localizations.dart';
import 'onboarding_state.dart';
import 'widgets/goal_setting_card.dart';
import 'widgets/role_selector.dart';

// ---------------------------------------------------------------------------
// Diagnostic question data (5 fixed seed questions for onboarding only)
// ---------------------------------------------------------------------------

class _DiagnosticQuestion {
  const _DiagnosticQuestion({
    required this.text,
    required this.options,
    required this.correctIndex,
  });

  final String text;
  final List<String> options;
  final int correctIndex;
}

const List<_DiagnosticQuestion> _diagnosticQuestions = [
  _DiagnosticQuestion(
    text: 'מה הוא הפתרון של המשוואה 2x + 6 = 14?',
    options: ['x = 2', 'x = 4', 'x = 6', 'x = 8'],
    correctIndex: 1,
  ),
  _DiagnosticQuestion(
    text: 'כמה הם 15% מ-200?',
    options: ['20', '25', '30', '35'],
    correctIndex: 2,
  ),
  _DiagnosticQuestion(
    text: 'מה שטח המשולש שבסיסו 8 ס"מ וגובהו 5 ס"מ?',
    options: ['20 סמ"ר', '25 סמ"ר', '40 סמ"ר', '13 סמ"ר'],
    correctIndex: 0,
  ),
  _DiagnosticQuestion(
    text: 'מה הוא שורש ריבועי של 144?',
    options: ['10', '11', '12', '14'],
    correctIndex: 2,
  ),
  _DiagnosticQuestion(
    text: 'סדרה חשבונית: 3, 7, 11, 15, ... מה המספר הבא?',
    options: ['17', '18', '19', '20'],
    correctIndex: 2,
  ),
];

// ---------------------------------------------------------------------------
// Subject metadata for display
// ---------------------------------------------------------------------------

class _SubjectMeta {
  const _SubjectMeta({
    required this.subject,
    required this.nameHe,
    required this.icon,
    required this.primaryColor,
    required this.backgroundColor,
    required this.available,
  });

  final Subject subject;
  final String nameHe;
  final IconData icon;
  final Color primaryColor;
  final Color backgroundColor;
  final bool available;
}

const List<_SubjectMeta> _subjects = [
  _SubjectMeta(
    subject: Subject.math,
    nameHe: 'מתמטיקה',
    icon: Icons.functions_rounded,
    primaryColor: SubjectColorTokens.mathPrimary,
    backgroundColor: SubjectColorTokens.mathBackground,
    available: true,
  ),
  _SubjectMeta(
    subject: Subject.physics,
    nameHe: 'פיזיקה',
    icon: Icons.speed_rounded,
    primaryColor: SubjectColorTokens.physicsPrimary,
    backgroundColor: SubjectColorTokens.physicsBackground,
    available: false,
  ),
  _SubjectMeta(
    subject: Subject.chemistry,
    nameHe: 'כימיה',
    icon: Icons.science_rounded,
    primaryColor: SubjectColorTokens.chemistryPrimary,
    backgroundColor: SubjectColorTokens.chemistryBackground,
    available: false,
  ),
  _SubjectMeta(
    subject: Subject.biology,
    nameHe: 'ביולוגיה',
    icon: Icons.biotech_rounded,
    primaryColor: SubjectColorTokens.biologyPrimary,
    backgroundColor: SubjectColorTokens.biologyBackground,
    available: false,
  ),
];

// ---------------------------------------------------------------------------
// OnboardingScreen
// ---------------------------------------------------------------------------

class OnboardingScreen extends ConsumerStatefulWidget {
  const OnboardingScreen({super.key});

  @override
  ConsumerState<OnboardingScreen> createState() => _OnboardingScreenState();
}

class _OnboardingScreenState extends ConsumerState<OnboardingScreen> {
  final _pageController = PageController();
  int _currentPage = 0;

  /// Total pages varies by role. Default 8 for student until role is selected.
  int get _totalPages {
    final sel = ref.read(onboardingProvider);
    return sel.totalPages;
  }

  void _goToPage(int page) {
    _pageController.animateToPage(
      page,
      duration: AnimationTokens.normal,
      curve: Curves.easeInOut,
    );
  }

  void _nextPage() {
    if (_currentPage < _totalPages - 1) {
      _goToPage(_currentPage + 1);
    }
  }

  void _prevPage() {
    if (_currentPage > 0) {
      _goToPage(_currentPage - 1);
    }
  }

  Future<void> _finish() async {
    await ref.read(onboardingProvider.notifier).completeOnboarding();
    if (mounted) {
      context.go(CenaRoutes.home);
    }
  }

  @override
  void dispose() {
    _pageController.dispose();
    super.dispose();
  }

  /// Build the pages list based on the current role.
  List<Widget> _buildPages(OnboardingSelections selections) {
    final role = selections.role;

    if (role == OnboardingRole.teacher) {
      // Teacher: welcome, role, subjects, ready (4 pages)
      return [
        _WelcomePage(onNext: _nextPage),
        _RolePage(
          selections: selections,
          onNext: _nextPage,
          onBack: _prevPage,
        ),
        _SubjectsPage(
          selections: selections,
          onNext: _nextPage,
          onBack: _prevPage,
        ),
        _ReadyPage(
          selections: selections,
          onFinish: _finish,
          onBack: _prevPage,
        ),
      ];
    }

    if (role == OnboardingRole.parent) {
      // Parent: welcome, role, subjects (to know child's subjects), ready (4 pages)
      return [
        _WelcomePage(onNext: _nextPage),
        _RolePage(
          selections: selections,
          onNext: _nextPage,
          onBack: _prevPage,
        ),
        _SubjectsPage(
          selections: selections,
          onNext: _nextPage,
          onBack: _prevPage,
        ),
        _ReadyPage(
          selections: selections,
          onFinish: _finish,
          onBack: _prevPage,
        ),
      ];
    }

    // Student (default): 8 pages
    return [
      _WelcomePage(onNext: _nextPage),
      _RolePage(
        selections: selections,
        onNext: _nextPage,
        onBack: _prevPage,
      ),
      _SubjectsPage(
        selections: selections,
        onNext: _nextPage,
        onBack: _prevPage,
      ),
      _GradePage(
        selections: selections,
        onNext: _nextPage,
        onBack: _prevPage,
      ),
      _GoalPage(
        selections: selections,
        onNext: _nextPage,
        onBack: _prevPage,
      ),
      _TimePage(
        selections: selections,
        onNext: _nextPage,
        onBack: _prevPage,
      ),
      _DiagnosticPage(
        selections: selections,
        onNext: _nextPage,
        onBack: _prevPage,
      ),
      _ReadyPage(
        selections: selections,
        onFinish: _finish,
        onBack: _prevPage,
      ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final selections = ref.watch(onboardingProvider);
    final pages = _buildPages(selections);
    final pageCount = pages.length;

    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            // Progress dots
            Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.md,
                vertical: SpacingTokens.sm,
              ),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: List.generate(pageCount, (index) {
                  return AnimatedContainer(
                    duration: AnimationTokens.fast,
                    margin: const EdgeInsets.symmetric(
                      horizontal: SpacingTokens.xxs,
                    ),
                    width: index == _currentPage ? 24.0 : 8.0,
                    height: 8.0,
                    decoration: BoxDecoration(
                      borderRadius:
                          BorderRadius.circular(RadiusTokens.full),
                      color: index == _currentPage
                          ? Theme.of(context).colorScheme.primary
                          : Theme.of(context)
                              .colorScheme
                              .outlineVariant,
                    ),
                  );
                }),
              ),
            ),

            // Page content
            Expanded(
              child: PageView(
                controller: _pageController,
                physics: const NeverScrollableScrollPhysics(),
                onPageChanged: (page) {
                  setState(() => _currentPage = page);
                },
                children: pages,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 1 — Welcome
// ---------------------------------------------------------------------------

class _WelcomePage extends ConsumerStatefulWidget {
  const _WelcomePage({required this.onNext});

  final VoidCallback onNext;

  @override
  ConsumerState<_WelcomePage> createState() => _WelcomePageState();
}

class _WelcomePageState extends ConsumerState<_WelcomePage> {
  /// Selected language code (he / ar / en).
  String _locale = 'he';

  static const _languageLabels = <String, (String, TextDirection)>{
    'he': ('עברית', TextDirection.rtl),
    'ar': ('عربية', TextDirection.rtl),
    'en': ('English', TextDirection.ltr),
  };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final config = ref.watch(appConfigProvider);
    final visibleLocales = AppLocales.visibleLocales(
      hebrewVisible: config.featureFlags.hebrewLocaleVisible,
    );

    return Directionality(
      textDirection: AppLocales.isRtl(Locale(_locale))
          ? TextDirection.rtl
          : TextDirection.ltr,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Spacer(flex: 2),

            // Logo
            Center(
              child: Container(
                width: 96,
                height: 96,
                decoration: BoxDecoration(
                  color: cs.primaryContainer,
                  borderRadius: BorderRadius.circular(RadiusTokens.xl),
                ),
                child: Icon(
                  Icons.school_rounded,
                  size: 56,
                  color: cs.onPrimaryContainer,
                ),
              ),
            ),
            const SizedBox(height: SpacingTokens.lg),

            // Title
            Text(
              l.welcomeToCena,
              style: theme.textTheme.displaySmall?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w800,
                color: cs.onSurface,
              ),
              textAlign: TextAlign.center,
              textDirection: TextDirection.rtl,
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              l.yourPersonalLearningCoach,
              style: theme.textTheme.titleLarge?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
              textDirection: TextDirection.rtl,
            ),

            const Spacer(),

            // Language selector
            Text(
              l.selectLanguage,
              style: theme.textTheme.labelLarge?.copyWith(
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.sm),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: visibleLocales.map((locale) {
                final code = locale.languageCode;
                final entry = _languageLabels[code];
                if (entry == null) return const SizedBox.shrink();
                final (label, dir) = entry;
                final selected = _locale == code;
                return Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: SpacingTokens.xs,
                  ),
                  child: ChoiceChip(
                    label: Text(
                      label,
                      textDirection: dir,
                    ),
                    selected: selected,
                    onSelected: (_) {
                      setState(() => _locale = code);
                    },
                  ),
                );
              }).toList(),
            ),

            const Spacer(flex: 2),

            FilledButton(
              onPressed: widget.onNext,
              child: Text(l.getStarted),
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 2 — Role Selection
// ---------------------------------------------------------------------------

class _RolePage extends ConsumerWidget {
  const _RolePage({
    required this.selections,
    required this.onNext,
    required this.onBack,
  });

  final OnboardingSelections selections;
  final VoidCallback onNext;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final notifier = ref.read(onboardingProvider.notifier);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SizedBox(height: SpacingTokens.lg),
            Text(
              l.whoAreYou,
              style: theme.textTheme.headlineMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w700,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xs),
            Text(
              l.selectYourRole,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xl),

            Expanded(
              child: ListView(
                children: OnboardingRole.values.map((role) {
                  return Padding(
                    padding: const EdgeInsets.only(bottom: SpacingTokens.md),
                    child: RoleSelectorCard(
                      role: role,
                      isSelected: selections.role == role,
                      onTap: () => notifier.setRole(role),
                    ),
                  );
                }).toList(),
              ),
            ),

            const SizedBox(height: SpacingTokens.md),
            _NavButtons(
              onBack: onBack,
              onNext: selections.canProceedFromRole ? onNext : null,
              nextLabel: l.next,
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 3 — Subject Selection
// ---------------------------------------------------------------------------

class _SubjectsPage extends ConsumerWidget {
  const _SubjectsPage({
    required this.selections,
    required this.onNext,
    required this.onBack,
  });

  final OnboardingSelections selections;
  final VoidCallback onNext;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final notifier = ref.read(onboardingProvider.notifier);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SizedBox(height: SpacingTokens.lg),
            Text(
              l.selectStudySubjects,
              style: theme.textTheme.headlineMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w700,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xs),
            Text(
              l.upTo3Subjects,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.lg),

            Expanded(
              child: GridView.count(
                crossAxisCount: 2,
                mainAxisSpacing: SpacingTokens.sm,
                crossAxisSpacing: SpacingTokens.sm,
                childAspectRatio: 1.1,
                children: _subjects.map((meta) {
                  final isSelected =
                      selections.selectedSubjects.contains(meta.subject);
                  final isDisabled = !meta.available;

                  return _SubjectCard(
                    meta: meta,
                    isSelected: isSelected,
                    isDisabled: isDisabled,
                    onTap: isDisabled
                        ? null
                        : () => notifier.toggleSubject(meta.subject),
                  );
                }).toList(),
              ),
            ),

            const SizedBox(height: SpacingTokens.md),
            _NavButtons(
              onBack: onBack,
              onNext: selections.canProceedFromSubjects ? onNext : null,
              nextLabel: l.next,
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }
}

class _SubjectCard extends StatelessWidget {
  const _SubjectCard({
    required this.meta,
    required this.isSelected,
    required this.isDisabled,
    this.onTap,
  });

  final _SubjectMeta meta;
  final bool isSelected;
  final bool isDisabled;
  final VoidCallback? onTap;

  String _localizedSubjectName(BuildContext context, Subject subject) {
    final l = AppLocalizations.of(context);
    switch (subject) {
      case Subject.math:
        return l.math;
      case Subject.physics:
        return l.physics;
      case Subject.chemistry:
        return l.chemistry;
      case Subject.biology:
        return l.biology;
      case Subject.cs:
        return l.computerScience;
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return AnimatedContainer(
      duration: AnimationTokens.fast,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
        border: Border.all(
          color: isSelected
              ? meta.primaryColor
              : Colors.transparent,
          width: 2,
        ),
        color: isDisabled
            ? theme.colorScheme.surfaceContainerHighest
            : (isSelected ? meta.primaryColor.withValues(alpha: 0.12) : meta.backgroundColor),
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
        child: InkWell(
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
          onTap: onTap,
          child: Stack(
            children: [
              Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      meta.icon,
                      size: 36,
                      color: isDisabled
                          ? theme.colorScheme.outline
                          : meta.primaryColor,
                    ),
                    const SizedBox(height: SpacingTokens.xs),
                    Text(
                      _localizedSubjectName(context, meta.subject),
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontFamily: TypographyTokens.hebrewFontFamily,
                        fontWeight: FontWeight.w600,
                        color: isDisabled
                            ? theme.colorScheme.outline
                            : meta.primaryColor,
                      ),
                    ),
                  ],
                ),
              ),

              // "Coming Soon" badge
              if (isDisabled)
                Positioned(
                  top: SpacingTokens.xs,
                  left: SpacingTokens.xs,
                  child: Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: SpacingTokens.xs,
                      vertical: SpacingTokens.xxs,
                    ),
                    decoration: BoxDecoration(
                      color: theme.colorScheme.secondaryContainer,
                      borderRadius: BorderRadius.circular(RadiusTokens.sm),
                    ),
                    child: Text(
                      AppLocalizations.of(context).comingSoon,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: theme.colorScheme.onSecondaryContainer,
                        fontFamily: TypographyTokens.hebrewFontFamily,
                      ),
                    ),
                  ),
                ),

              // Selected checkmark
              if (isSelected)
                Positioned(
                  top: SpacingTokens.xs,
                  right: SpacingTokens.xs,
                  child: CircleAvatar(
                    radius: 12,
                    backgroundColor: meta.primaryColor,
                    child: const Icon(
                      Icons.check_rounded,
                      size: 16,
                      color: Colors.white,
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 3 — Grade & Bagrut Track
// ---------------------------------------------------------------------------

class _GradePage extends ConsumerWidget {
  const _GradePage({
    required this.selections,
    required this.onNext,
    required this.onBack,
  });

  final OnboardingSelections selections;
  final VoidCallback onNext;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final notifier = ref.read(onboardingProvider.notifier);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: SingleChildScrollView(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SizedBox(height: SpacingTokens.lg),
            Text(
              l.gradeAndTrack,
              style: theme.textTheme.headlineMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w700,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xs),
            Text(
              l.weAdaptToYourLevel,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xl),

            // Grade selector
            Text(
              l.grade,
              style: theme.textTheme.titleMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            Wrap(
              spacing: SpacingTokens.sm,
              children: GradeLevel.values.map((grade) {
                final selected = selections.gradeLevel == grade;
                return ChoiceChip(
                  label: Text(
                    grade.label,
                    style: const TextStyle(
                      fontFamily: TypographyTokens.hebrewFontFamily,
                    ),
                  ),
                  selected: selected,
                  onSelected: (_) => notifier.setGrade(grade),
                );
              }).toList(),
            ),

            const SizedBox(height: SpacingTokens.xl),

            // Bagrut units selector
            Text(
              l.examLevel,
              style: theme.textTheme.titleMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            ...BagrutUnits.values.map((units) {
              final selected = selections.bagrutUnits == units;
              return Padding(
                padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
                child: _BagrutCard(
                  units: units,
                  selected: selected,
                  onTap: () => notifier.setBagrutUnits(units),
                ),
              );
            }),

            const SizedBox(height: SpacingTokens.lg),
            _NavButtons(
              onBack: onBack,
              onNext: selections.canProceedFromGrade ? onNext : null,
              nextLabel: l.next,
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }
}

class _BagrutCard extends StatelessWidget {
  const _BagrutCard({
    required this.units,
    required this.selected,
    required this.onTap,
  });

  final BagrutUnits units;
  final bool selected;
  final VoidCallback onTap;

  static const Map<BagrutUnits, String> _descriptions = {
    BagrutUnits.threeUnit: 'לימוד חומר בגרות בסיסי, מתאים לחיזוק יסודות המקצוע',
    BagrutUnits.fourUnit: 'חומר מורחב הכולל נושאים נוספים על גבי רמה בסיסית',
    BagrutUnits.fiveUnit: 'הרמה הגבוהה ביותר, נדרשת לקבלה למכללות הנדסה ורפואה',
  };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return AnimatedContainer(
      duration: AnimationTokens.fast,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(
          color: selected ? cs.primary : cs.outlineVariant,
          width: selected ? 2 : 1,
        ),
        color: selected ? cs.primaryContainer : cs.surface,
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        child: InkWell(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(SpacingTokens.md),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        units.label,
                        style: theme.textTheme.titleSmall?.copyWith(
                          fontFamily: TypographyTokens.hebrewFontFamily,
                          fontWeight: FontWeight.w700,
                          color: selected ? cs.onPrimaryContainer : cs.onSurface,
                        ),
                      ),
                      const SizedBox(height: SpacingTokens.xxs),
                      Text(
                        _descriptions[units]!,
                        style: theme.textTheme.bodySmall?.copyWith(
                          fontFamily: TypographyTokens.hebrewFontFamily,
                          color: selected
                              ? cs.onPrimaryContainer
                              : cs.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
                if (selected)
                  Icon(Icons.check_circle_rounded, color: cs.primary),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 5 — Goal Setting (student only)
// ---------------------------------------------------------------------------

class _GoalPage extends ConsumerWidget {
  const _GoalPage({
    required this.selections,
    required this.onNext,
    required this.onBack,
  });

  final OnboardingSelections selections;
  final VoidCallback onNext;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final notifier = ref.read(onboardingProvider.notifier);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SizedBox(height: SpacingTokens.lg),
            Text(
              l.whatDoYouWantToAchieve,
              style: theme.textTheme.headlineMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w700,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xs),
            Text(
              l.chooseYourGoal,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.lg),

            Expanded(
              child: ListView(
                children: GoalType.values.map((goal) {
                  return Padding(
                    padding: const EdgeInsets.only(bottom: SpacingTokens.md),
                    child: GoalSettingCard(
                      goalType: goal,
                      isSelected: selections.goalType == goal,
                      onTap: () => notifier.setGoalType(goal),
                    ),
                  );
                }).toList(),
              ),
            ),

            const SizedBox(height: SpacingTokens.md),
            _NavButtons(
              onBack: onBack,
              onNext: selections.canProceedFromGoal ? onNext : null,
              nextLabel: l.next,
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 6 — Time Commitment (student only)
// ---------------------------------------------------------------------------

class _TimePage extends ConsumerWidget {
  const _TimePage({
    required this.selections,
    required this.onNext,
    required this.onBack,
  });

  final OnboardingSelections selections;
  final VoidCallback onNext;
  final VoidCallback onBack;

  static const List<int> _options = [5, 10, 15, 20];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final notifier = ref.read(onboardingProvider.notifier);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SizedBox(height: SpacingTokens.lg),
            Text(
              l.howMuchTimePerDay,
              style: theme.textTheme.headlineMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w700,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xs),
            Text(
              l.youCanChangeThisLater,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.xl),

            Expanded(
              child: Center(
                child: GridView.count(
                  crossAxisCount: 2,
                  mainAxisSpacing: SpacingTokens.md,
                  crossAxisSpacing: SpacingTokens.md,
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  children: _options.map((minutes) {
                    return TimeCommitmentCard(
                      minutes: minutes,
                      isSelected: selections.dailyMinutes == minutes,
                      onTap: () => notifier.setDailyMinutes(minutes),
                    );
                  }).toList(),
                ),
              ),
            ),

            const SizedBox(height: SpacingTokens.md),
            _NavButtons(
              onBack: onBack,
              onNext: selections.canProceedFromTime ? onNext : null,
              nextLabel: l.next,
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 7 — Discovery Tour (diagnostic, reframed as exploration)
// ---------------------------------------------------------------------------

class _DiagnosticPage extends ConsumerStatefulWidget {
  const _DiagnosticPage({
    required this.selections,
    required this.onNext,
    required this.onBack,
  });

  final OnboardingSelections selections;
  final VoidCallback onNext;
  final VoidCallback onBack;

  @override
  ConsumerState<_DiagnosticPage> createState() => _DiagnosticPageState();
}

class _DiagnosticPageState extends ConsumerState<_DiagnosticPage> {
  bool _quizStarted = false;
  int _questionIndex = 0;

  void _startQuiz() {
    setState(() => _quizStarted = true);
  }

  void _skipQuiz() {
    ref.read(onboardingProvider.notifier).setSkipDiagnostic(true);
    widget.onNext();
  }

  void _selectAnswer(int answerIndex) {
    ref
        .read(onboardingProvider.notifier)
        .recordDiagnosticAnswer(_questionIndex, answerIndex);

    if (_questionIndex < _diagnosticQuestions.length - 1) {
      setState(() => _questionIndex++);
    } else {
      // Quiz finished — advance to ready page.
      widget.onNext();
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    if (!_quizStarted) {
      return _buildIntroView(theme, cs);
    }
    return _buildQuizView(theme, cs);
  }

  Widget _buildIntroView(ThemeData theme, ColorScheme cs) {
    final l = AppLocalizations.of(context);
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Spacer(),
            Icon(
              Icons.explore_rounded,
              size: 72,
              color: cs.primary,
            ),
            const SizedBox(height: SpacingTokens.lg),
            Text(
              l.discoveryTour,
              style: theme.textTheme.headlineMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w700,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              l.discoveryTourDesc,
              style: theme.textTheme.bodyLarge?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                color: cs.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const Spacer(),
            FilledButton.icon(
              onPressed: _startQuiz,
              icon: const Icon(Icons.explore_rounded),
              label: Text(l.startDiscovery),
            ),
            const SizedBox(height: SpacingTokens.sm),
            OutlinedButton(
              onPressed: _skipQuiz,
              child: Text(l.skipForNow),
            ),
            const SizedBox(height: SpacingTokens.md),
            _NavButtons(
              onBack: widget.onBack,
              onNext: null,
              nextLabel: '',
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }

  Widget _buildQuizView(ThemeData theme, ColorScheme cs) {
    final l = AppLocalizations.of(context);
    final question = _diagnosticQuestions[_questionIndex];
    final answers = widget.selections.diagnosticAnswers;
    final answeredForCurrent = answers[_questionIndex];

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SizedBox(height: SpacingTokens.lg),

            // Progress indicator
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  l.questionNOfTotal(_questionIndex + 1, _diagnosticQuestions.length),
                  style: theme.textTheme.labelLarge?.copyWith(
                    fontFamily: TypographyTokens.hebrewFontFamily,
                    color: cs.onSurfaceVariant,
                  ),
                ),
                TextButton(
                  onPressed: _skipQuiz,
                  child: Text(l.skip),
                ),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),
            LinearProgressIndicator(
              value: (_questionIndex + 1) / _diagnosticQuestions.length,
              borderRadius: BorderRadius.circular(RadiusTokens.full),
            ),
            const SizedBox(height: SpacingTokens.xl),

            // Question text
            Text(
              question.text,
              style: theme.textTheme.titleLarge?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(height: SpacingTokens.lg),

            // Answer options
            ...question.options.asMap().entries.map((entry) {
              final idx = entry.key;
              final option = entry.value;
              final isSelected = answeredForCurrent == idx;

              return Padding(
                padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
                child: _AnswerButton(
                  label: option,
                  isSelected: isSelected,
                  onTap: () => _selectAnswer(idx),
                ),
              );
            }),
          ],
        ),
      ),
    );
  }
}

class _AnswerButton extends StatelessWidget {
  const _AnswerButton({
    required this.label,
    required this.isSelected,
    required this.onTap,
  });

  final String label;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return AnimatedContainer(
      duration: AnimationTokens.fast,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(
          color: isSelected ? cs.primary : cs.outlineVariant,
          width: isSelected ? 2 : 1,
        ),
        color: isSelected ? cs.primaryContainer : cs.surface,
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        child: InkWell(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(SpacingTokens.md),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    label,
                    style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                          fontFamily: TypographyTokens.hebrewFontFamily,
                          color:
                              isSelected ? cs.onPrimaryContainer : cs.onSurface,
                        ),
                  ),
                ),
                if (isSelected)
                  Icon(Icons.radio_button_checked_rounded, color: cs.primary),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Page 8 — Ready
// ---------------------------------------------------------------------------

class _ReadyPage extends ConsumerStatefulWidget {
  const _ReadyPage({
    required this.selections,
    required this.onFinish,
    required this.onBack,
  });

  final OnboardingSelections selections;
  final Future<void> Function() onFinish;
  final VoidCallback onBack;

  @override
  ConsumerState<_ReadyPage> createState() => _ReadyPageState();
}

class _ReadyPageState extends ConsumerState<_ReadyPage>
    with SingleTickerProviderStateMixin {
  late final AnimationController _celebrationController;
  bool _loading = false;

  @override
  void initState() {
    super.initState();
    _celebrationController = AnimationController(
      vsync: this,
      duration: AnimationTokens.celebration,
    )..forward();
  }

  @override
  void dispose() {
    _celebrationController.dispose();
    super.dispose();
  }

  Future<void> _handleStart() async {
    setState(() => _loading = true);
    await widget.onFinish();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final selections = widget.selections;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Spacer(),

            // Celebration icon
            ScaleTransition(
              scale: CurvedAnimation(
                parent: _celebrationController,
                curve: Curves.elasticOut,
              ),
              child: Center(
                child: Stack(
                  alignment: Alignment.center,
                  children: [
                    Container(
                      width: 120,
                      height: 120,
                      decoration: BoxDecoration(
                        color: cs.primaryContainer,
                        shape: BoxShape.circle,
                      ),
                    ),
                    Icon(
                      Icons.celebration_rounded,
                      size: 64,
                      color: cs.onPrimaryContainer,
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: SpacingTokens.lg),

            Text(
              l.allSet,
              style: theme.textTheme.displaySmall?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w800,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              l.hereSummary,
              style: theme.textTheme.bodyLarge?.copyWith(
                color: cs.onSurfaceVariant,
                fontFamily: TypographyTokens.hebrewFontFamily,
              ),
              textAlign: TextAlign.center,
            ),

            const SizedBox(height: SpacingTokens.xl),

            // Summary card
            Card(
              child: Padding(
                padding: const EdgeInsets.all(SpacingTokens.md),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (selections.role != null) ...[
                      _SummaryRow(
                        icon: selections.role!.icon,
                        label: l.roleLabel,
                        value: selections.role!.label('he'),
                      ),
                      const Divider(height: SpacingTokens.lg),
                    ],
                    _SummaryRow(
                      icon: Icons.subject_rounded,
                      label: l.subjectsLabel,
                      value: selections.selectedSubjects
                          .map((s) => _subjectLabel(s, l))
                          .join(', '),
                    ),
                    if (selections.gradeLevel != null) ...[
                      const Divider(height: SpacingTokens.lg),
                      _SummaryRow(
                        icon: Icons.school_rounded,
                        label: l.gradeLabel,
                        value: selections.gradeLevel!.label,
                      ),
                    ],
                    if (selections.bagrutUnits != null) ...[
                      const Divider(height: SpacingTokens.lg),
                      _SummaryRow(
                        icon: Icons.grade_rounded,
                        label: l.examLevelLabel,
                        value: selections.bagrutUnits!.label,
                      ),
                    ],
                    if (selections.goalType != null) ...[
                      const Divider(height: SpacingTokens.lg),
                      _SummaryRow(
                        icon: selections.goalType!.icon,
                        label: l.goalLabel,
                        value: selections.goalType!.label('he'),
                      ),
                    ],
                    if (selections.dailyMinutes != null) ...[
                      const Divider(height: SpacingTokens.lg),
                      _SummaryRow(
                        icon: Icons.timer_rounded,
                        label: l.dailyTimeLabel,
                        value: '${selections.dailyMinutes} ${l.minutesShort}',
                      ),
                    ],
                    if (selections.isStudent) ...[
                      const Divider(height: SpacingTokens.lg),
                      _SummaryRow(
                        icon: selections.skipDiagnostic
                            ? Icons.skip_next_rounded
                            : Icons.explore_rounded,
                        label: l.discoveryLabel,
                        value: selections.skipDiagnostic
                            ? l.skipped
                            : l.nAnswersRecorded(selections.diagnosticAnswers.length),
                      ),
                    ],
                  ],
                ),
              ),
            ),

            const Spacer(),

            _loading
                ? const Center(child: CircularProgressIndicator())
                : FilledButton.icon(
                    onPressed: _handleStart,
                    icon: const Icon(Icons.play_arrow_rounded),
                    label: Text(l.startLearning),
                  ),
            const SizedBox(height: SpacingTokens.sm),
            TextButton(
              onPressed: widget.onBack,
              child: Text(l.back),
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ),
      ),
    );
  }

  static String _subjectLabel(Subject s, AppLocalizations l) {
    switch (s) {
      case Subject.math:
        return l.math;
      case Subject.physics:
        return l.physics;
      case Subject.chemistry:
        return l.chemistry;
      case Subject.biology:
        return l.biology;
      case Subject.cs:
        return l.computerScience;
    }
  }
}

class _SummaryRow extends StatelessWidget {
  const _SummaryRow({
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
    final cs = theme.colorScheme;

    return Row(
      children: [
        Icon(icon, size: 20, color: cs.primary),
        const SizedBox(width: SpacingTokens.sm),
        Text(
          '$label: ',
          style: theme.textTheme.bodyMedium?.copyWith(
            fontFamily: TypographyTokens.hebrewFontFamily,
            fontWeight: FontWeight.w600,
          ),
        ),
        Expanded(
          child: Text(
            value,
            style: theme.textTheme.bodyMedium?.copyWith(
              fontFamily: TypographyTokens.hebrewFontFamily,
              color: cs.onSurfaceVariant,
            ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Shared navigation button row
// ---------------------------------------------------------------------------

class _NavButtons extends StatelessWidget {
  const _NavButtons({
    required this.onBack,
    required this.onNext,
    required this.nextLabel,
  });

  final VoidCallback onBack;
  final VoidCallback? onNext;
  final String nextLabel;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    return Row(
      children: [
        OutlinedButton(
          onPressed: onBack,
          child: Text(l.back),
        ),
        if (nextLabel.isNotEmpty) ...[
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: FilledButton(
              onPressed: onNext,
              child: Text(nextLabel),
            ),
          ),
        ],
      ],
    );
  }
}
