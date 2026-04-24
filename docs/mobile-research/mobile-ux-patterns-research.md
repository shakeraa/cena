# CENA Mobile UX Patterns Research
## Mobile-Specific UX Patterns, Gestures & Platform Design for Flutter Educational Apps

> **Status:** Research Complete
> **Date:** 2026-03-31
> **Scope:** Comprehensive mobile UX pattern library for CENA's Flutter student app
> **Audience:** Engineering, Design, Product
> **Prerequisites read:** `stakeholder-experiences.md`, `offline-sync-protocol.md`, `interactive-questions-design.md`, `micro-lessons-design.md`, `CENA_UI_UX_Design_Strategy_2026.md`

---

## Table of Contents

1. [Thumb Zone & Ergonomics](#1-thumb-zone--ergonomics)
2. [Navigation Architecture](#2-navigation-architecture)
3. [Card-Based UI for Learning](#3-card-based-ui-for-learning)
4. [List & Grid Patterns](#4-list--grid-patterns)
5. [Input Patterns for Learning](#5-input-patterns-for-learning)
6. [Offline-First Design](#6-offline-first-design)
7. [Performance Patterns](#7-performance-patterns)
8. [Notification Design](#8-notification-design)
9. [Tablet & Adaptive Layout](#9-tablet--adaptive-layout)
10. [Platform-Specific Design](#10-platform-specific-design)
11. [Data Visualization on Mobile](#11-data-visualization-on-mobile)
12. [Gesture Language for Education](#12-gesture-language-for-education)
13. [Camera & AR Features](#13-camera--ar-features)
14. [Flutter Package Recommendations](#14-flutter-package-recommendations)
15. [Actor Backend Integration](#15-actor-backend-integration)
16. [Screen Layout Templates](#16-screen-layout-templates)
17. [Performance Optimization Checklist](#17-performance-optimization-checklist)

---

## 1. Thumb Zone & Ergonomics

### 1.1 Thumb Reachability Maps

Modern smartphones range from 5.4" (iPhone 16) to 6.9" (iPhone 16 Pro Max). For CENA's target demographic (14-18 year olds, Israeli high school students), the primary usage mode is **one-handed, right-hand dominant** (though RTL interface design for Hebrew/Arabic shifts some patterns).

```
Phone Screen Reachability (Right-Hand, One-Thumb)
+-------------------------------------------+
|                                           |
|           HARD TO REACH                   |  <- App bar actions, secondary icons
|         (top corners)                     |
|                                           |
+-------------------------------------------+
|                                           |
|          OK / STRETCH ZONE                |  <- Content area, question text
|       (mid-screen, mid-sides)             |
|                                           |
+-------------------------------------------+
|                                           |
|        NATURAL / EASY ZONE                |  <- Submit button, option selection,
|      (bottom-center arc)                  |     navigation tabs, FAB
|                                           |
+-------------------------------------------+
```

**RTL Adjustment for Hebrew/Arabic**: The natural thumb arc for right-hand use remains the same regardless of text direction. However, users scanning RTL content start from the right edge, meaning the right side of the screen gets slightly more attention. The reachability map does not flip -- thumbs are physical, not linguistic.

### 1.2 CENA-Specific Ergonomic Rules

| Zone | What Goes Here | CENA Example |
|------|---------------|--------------|
| **Easy (bottom 1/3)** | Primary actions, navigation, most-tapped controls | Submit Answer button, Skip button, Bottom nav bar, Hint request |
| **OK (middle 1/3)** | Content consumption, secondary interactions | Question text, MCQ option tiles, Answer text field |
| **Hard (top 1/3)** | Read-only info, rare actions | Session timer, question number, methodology badge, end session |

### 1.3 Critical Actions in Easy-Reach Zones

The following actions are tapped most frequently during a learning session and MUST be in the bottom 40% of the screen:

1. **Submit Answer** -- currently `FilledButton.icon` in `answer_input.dart`. Correct placement: full-width at bottom.
2. **Select MCQ Option** -- the OptionTile list in `question_card.dart` should flow from bottom to top (options D, C, B, A from bottom up) so the most common tap targets (first two options) are in the easy zone. However, this conflicts with natural reading order. **Resolution**: keep top-down reading order but ensure the option list is positioned in the lower half of the scrollable area, with question text above.
3. **Skip / Next Question** -- secondary action, should sit next to Submit.
4. **Request Hint** -- `ActionButtons` widget, should be anchored near the bottom action bar.

### 1.4 Bottom-Heavy Navigation Design

CENA already uses `BottomNavigationBar` in `home_screen.dart` with 4 tabs (Home, Sessions, Progress, Settings). This is correct for the thumb zone.

**Recommendation: Evolve to 5 tabs** (the maximum for BottomNavigationBar):

| Tab | Icon | Label | Rationale |
|-----|------|-------|-----------|
| Home | `home_rounded` | Home | Dashboard, greeting, quick start |
| Learn | `play_circle_outline_rounded` | Learn | Active sessions, session history |
| Map | `account_tree_rounded` | Map | Knowledge graph visualization |
| Progress | `bar_chart_rounded` | Progress | Gamification, XP, badges, streak |
| Profile | `person_outline_rounded` | Profile | Settings, language, account |

This separates the current overloaded "Settings" tab from the profile concept and surfaces the knowledge graph (currently a placeholder route) as a first-class navigation destination.

### 1.5 FAB Placement for Learning Actions

A Floating Action Button is appropriate for the single most important action on a given screen.

| Screen | FAB Action | Position | Icon |
|--------|-----------|----------|------|
| Home | "Start Learning" | Bottom-center, above nav bar | `play_arrow_rounded` |
| Sessions (empty) | "Start Session" | Center | `add_rounded` |
| Knowledge Graph | "Practice Weak Concept" | Bottom-right (LTR) / Bottom-left (RTL) | `fitness_center_rounded` |
| Session Active | **No FAB** -- Submit button replaces it | N/A | N/A |

**RTL FAB Note**: In RTL layouts, the FAB should mirror to the bottom-left. Flutter handles this automatically when using `FloatingActionButton` with `Directionality`.

### 1.6 Device Size Considerations

| Device Category | Screen Width | Layout Adjustment |
|----------------|-------------|-------------------|
| Small phone (5.0-5.4") | < 360dp | Single column, compact spacing, smaller touch targets (min 44x44) |
| Standard phone (5.5-6.1") | 360-411dp | Default layout, standard spacing |
| Large phone / phablet (6.2-6.9") | 412-480dp | Can show slightly more content, keep bottom-heavy layout |
| Small tablet (7-8") | 600-700dp | Consider split layout, side-by-side question+input |
| Full tablet (9-12") | 700-1024dp | Mandatory split layout, see Section 9 |

Use `MediaQuery.of(context).size.width` breakpoints:
- `< 360`: compact mode
- `360-599`: phone mode (default)
- `600-839`: small tablet / landscape phone
- `>= 840`: large tablet

---

## 2. Navigation Architecture

### 2.1 Complete Navigation Graph

```
App Launch
  |
  +-> Onboarding (first-time only)
  |     |-> Welcome
  |     |-> Subject Selection
  |     |-> Goal Setting
  |     |-> Profile Setup
  |     +-> Home
  |
  +-> Auth Gate
  |     |-> Login Screen
  |     +-> Home (after auth)
  |
  +-> Home Shell (BottomNavBar)
        |
        |-- Tab: Home
        |     |-> Greeting Card
        |     |-> Subject Quick Start Grid
        |     |-> Recent Activity Feed
        |     |-> Daily Goal Progress
        |     +-> (tap subject) -> Session Config
        |
        |-- Tab: Learn
        |     |-> Active Session (if any)
        |     |-> Session History List
        |     +-> (tap session) -> Session Detail
        |
        |-- Tab: Map (Knowledge Graph)
        |     |-> Interactive Node Graph
        |     |-> Concept Detail (bottom sheet)
        |     +-> (tap weak concept) -> Session Config (pre-filled)
        |
        |-- Tab: Progress
        |     |-> XP & Level Card
        |     |-> Streak Calendar
        |     |-> Badge Grid -> Badge Detail (dialog)
        |     |-> Recent Achievements
        |     +-> Subject Mastery Breakdown
        |
        |-- Tab: Profile
              |-> Language Selection
              |-> Notification Preferences
              |-> Download Manager (offline content)
              |-> Account Management
              +-> Sign Out
```

### 2.2 Bottom Navigation Bar Specification

```dart
// Recommended BottomNavigationBar with 5 tabs
NavigationBar(  // Material 3 NavigationBar replaces BottomNavigationBar
  destinations: [
    NavigationDestination(icon: Icon(Icons.home_rounded), label: 'Home'),
    NavigationDestination(icon: Icon(Icons.play_circle_outline_rounded), label: 'Learn'),
    NavigationDestination(icon: Icon(Icons.account_tree_rounded), label: 'Map'),
    NavigationDestination(icon: Icon(Icons.bar_chart_rounded), label: 'Progress'),
    NavigationDestination(icon: Icon(Icons.person_outline_rounded), label: 'Profile'),
  ],
)
```

**Why NavigationBar over BottomNavigationBar**: Material 3's `NavigationBar` provides pill-shaped active indicators, better touch targets (64dp height vs 56dp), and built-in label animation. CENA already uses Material 3 (`useMaterial3: true` in `cena_theme.dart`).

### 2.3 Tab Bar + Nested Navigation

Within each bottom tab, use **nested GoRouter branches** (ShellRoute with StatefulShellRoute.indexedStack) to preserve scroll position and state when switching tabs.

```dart
StatefulShellRoute.indexedStack(
  builder: (context, state, navigationShell) {
    return ScaffoldWithNavBar(navigationShell: navigationShell);
  },
  branches: [
    StatefulShellBranch(routes: [/* Home routes */]),
    StatefulShellBranch(routes: [/* Learn routes */]),
    StatefulShellBranch(routes: [/* Map routes */]),
    StatefulShellBranch(routes: [/* Progress routes */]),
    StatefulShellBranch(routes: [/* Profile routes */]),
  ],
)
```

This preserves state across tab switches -- critical for educational apps where a student might check their progress mid-session and return to the Learn tab without losing context.

### 2.4 Drawer Navigation -- When to Use vs Avoid

**Do NOT use drawer navigation for CENA.** Reasons:

1. Drawers are hidden by default -- students will not discover features like downloads or knowledge graph
2. The hamburger menu pattern has measurably lower feature discovery rates than bottom navigation (research: NNG studies show ~50% less engagement with hamburger-hidden items)
3. CENA has exactly 5 primary destinations -- the perfect number for bottom navigation
4. RTL drawer behavior is inconsistent across devices and requires additional testing

**Exception**: Use a drawer ONLY for the teacher/parent companion app where the feature set exceeds 5 destinations and the user is more exploratory than task-focused.

### 2.5 Bottom Sheets for Supplementary Content

Bottom sheets are the correct pattern for content that augments the current context without full navigation. Use for:

| Use Case | Sheet Type | Height | Dismissal |
|----------|-----------|--------|-----------|
| Concept detail (from knowledge graph) | Modal bottom sheet | 60% screen | Swipe down |
| Hint content during session | Persistent bottom sheet | 30-40% screen | Collapse button |
| Unit selector for numeric answers | Modal bottom sheet | 40% screen | Tap outside |
| Quick session config from home | Modal bottom sheet | 70% screen | Swipe down |
| Explanation after answer feedback | Persistent bottom sheet | 40% screen | "Got it" button |
| Badge detail (already a dialog) | Modal bottom sheet or dialog | 50% screen | Close button |

```dart
// Show concept detail from knowledge graph
showModalBottomSheet(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  showDragHandle: true,
  builder: (context) => DraggableScrollableSheet(
    initialChildSize: 0.6,
    minChildSize: 0.3,
    maxChildSize: 0.9,
    expand: false,
    builder: (context, scrollController) => ConceptDetailSheet(
      concept: selectedConcept,
      scrollController: scrollController,
    ),
  ),
);
```

### 2.6 Full-Screen Modals for Immersive Learning

Use full-screen modals (page routes with fullscreenDialog: true) for:

1. **Active learning session** -- `SessionScreen` should be a full-screen experience with no bottom nav bar visible. The student's focus should be 100% on the question.
2. **Onboarding flow** -- full screen, no escape except completing or explicitly dismissing.
3. **Video micro-lessons** -- when implemented, video playback should be full-screen.
4. **Proof builder** -- the structured proof input needs maximum screen real estate.

Currently, `SessionScreen` is a regular GoRoute. It should be pushed as a full-screen dialog:

```dart
GoRoute(
  path: CenaRoutes.session,
  name: 'session',
  pageBuilder: (context, state) => MaterialPage(
    fullscreenDialog: true,  // Hides bottom nav, shows close/back
    child: const SessionScreen(),
  ),
),
```

### 2.7 Navigation State Preservation

**Back Button Behavior Rules:**

| Context | Android Back / iOS Swipe-Back | Expected Behavior |
|---------|------------------------------|-------------------|
| Home tab, at root | Exit app (Android) / no-op (iOS) | Standard platform behavior |
| Nested screen within tab | Pop to previous screen in tab | Tab stack preserved |
| Active session | Show "End session?" dialog | Prevent accidental exits |
| Session config screen | Return to home | Discard config state |
| Modal bottom sheet | Dismiss sheet | Standard |
| Full-screen dialog (session) | Show confirmation dialog | Never silently lose progress |

**WillPopScope / PopScope usage** for session protection:

```dart
PopScope(
  canPop: false,  // Prevent back gesture during active session
  onPopInvokedWithResult: (didPop, result) {
    if (!didPop) {
      _confirmEndSession();  // Show dialog
    }
  },
  child: SessionScreen(...),
)
```

### 2.8 Deep Linking for Shared Content

CENA already has `DeferredDeepLink` in `router.dart`. Extend to support these deep link patterns:

| Deep Link Pattern | Target | Use Case |
|-------------------|--------|----------|
| `cena://session/start?subject=math` | Session config, pre-filled | Push notification: "Time to study math" |
| `cena://session/{sessionId}` | Resume session | Re-open an interrupted session |
| `cena://graph/concept/{conceptId}` | Knowledge graph, focused on concept | Teacher shares "review this concept" link |
| `cena://progress/badges` | Progress tab, badge grid | Achievement notification |
| `cena://lesson/{lessonId}` | Micro-lesson player | Shared learning content |

**Implementation**: Use `go_router`'s path parameter matching (already in place for `sessionById`). Add Universal Links (iOS) and App Links (Android) configuration for `cena.education` domain.

---

## 3. Card-Based UI for Learning

### 3.1 Question Cards (Current + Enhanced)

The existing `QuestionCard` in `question_card.dart` is well-structured. Enhancements for mobile UX:

**Swipeable Question Cards (Flashcard Mode)**

For spaced repetition methodology, implement a Tinder-style swipeable card stack:

```dart
// Swipeable flashcard pattern
class FlashcardStack extends StatelessWidget {
  // Stack of 3 cards visible, top card is interactive
  // Swipe right = "I know this" (mark as confident)
  // Swipe left = "Need review" (mark for re-study)
  // Swipe up = "Skip" (neutral)
  // Tap = "Flip" (show answer)
}
```

**Card Design Specifications:**

| Property | Value | Rationale |
|----------|-------|-----------|
| Corner radius | 16dp (`RadiusTokens.xl`) | Matches existing theme |
| Elevation | 2dp (default), 4dp (pressed) | Subtle depth cue |
| Padding | 16dp internal | `SpacingTokens.md` |
| Min height | 200dp | Ensure enough space for math content |
| Max width | 600dp (phone), fill (tablet) | Prevent overly wide cards |
| Card spacing | 16dp vertical | Between cards in a list |

**Card Content Density Tiers:**

| Tier | Content | When |
|------|---------|------|
| Compact | Stem + options only | Session mode, rapid-fire drill |
| Standard | Difficulty badge + type pill + stem + options + hints area | Default session |
| Expanded | All standard + diagram + worked example + concept tag | Review mode, detailed feedback |

### 3.2 Course/Subject Cards with Progress

For the Home tab subject grid and the Progress tab:

```
+---------------------------------------------+
|  [Subject Icon]  Mathematics                 |
|                  5-point Bagrut              |
|                                              |
|  [============================------] 72%    |
|  142/198 concepts mastered                   |
|                                              |
|  Last studied: 2 hours ago                   |
|  Estimated completion: May 15                |
+---------------------------------------------+
```

**Specifications:**
- Progress bar uses the subject's primary color (e.g., `SubjectColorTokens.mathPrimary`)
- Card background uses the subject's background color
- Tap navigates to session config pre-filled with that subject
- Long press shows a bottom sheet with subject detail (mastery breakdown by topic cluster)

### 3.3 Achievement Cards

Used in the Progress tab's recent achievements list:

```
+---------------------------------------------+
|  [Badge Icon]  First Steps                   |
|                                              |
|  Complete your first learning session        |
|  Earned: March 28, 2026                      |
|                            [+25 XP] [Share]  |
+---------------------------------------------+
```

- Badge icon matches the badge's rarity tier color (Bronze/Silver/Gold/Diamond)
- Tap opens `BadgeDetailDialog` (already implemented)
- Share button generates a shareable image for WhatsApp/Instagram

### 3.4 Activity Feed Cards

For the Home tab's recent activity and the Learn tab's session history:

```
+---------------------------------------------+
|  March 31, 2026                              |
|                                              |
|  [Play icon]  Math session - 25 min          |
|               12 questions, 83% accuracy     |
|               +150 XP                        |
|                                              |
|  [Star icon]  Mastered: Quadratic Equations  |
|               Level 4 -> Level 5             |
|                                              |
|  [Fire icon]  7-day streak maintained!       |
+---------------------------------------------+
```

- Group by date
- Use leading icons for event types
- Subtle left border color per event type (green = mastery, blue = session, orange = streak)
- Tap session card -> session detail/summary

### 3.5 Card Sizing & Spacing

| Card Type | Min Height | Padding | Margin (between cards) | Corner Radius |
|-----------|-----------|---------|----------------------|---------------|
| Question | 200dp | 16dp | 16dp vertical | 16dp |
| Subject | 100dp | 16dp | 8dp (grid gap) | 12dp |
| Achievement | 80dp | 12dp | 8dp vertical | 12dp |
| Activity feed | 60dp | 12dp | 4dp vertical | 8dp |
| Stat card (XP, streak) | 120dp | 16dp | 8dp | 16dp |

---

## 4. List & Grid Patterns

### 4.1 Subject/Course Grid Layout

The current `_SubjectGrid` in `home_screen.dart` uses a 3-column grid with `SliverGridDelegateWithFixedCrossAxisCount`. Enhancement:

```dart
// Responsive grid: 3 columns on phone, 4 on tablet
GridView.builder(
  gridDelegate: SliverGridDelegateWithMaxCrossAxisExtent(
    maxCrossAxisExtent: 140,  // Ensures min 3 columns on 360dp
    mainAxisSpacing: SpacingTokens.sm,
    crossAxisSpacing: SpacingTokens.sm,
    childAspectRatio: 0.85,  // Slightly taller than wide for progress indicator
  ),
)
```

### 4.2 Lesson Lists with Progress

For the Learn tab's session history and micro-lesson lists:

```dart
// Lesson list item with progress indicator
ListTile(
  leading: CircularProgressIndicator(
    value: lesson.progress,        // 0.0 to 1.0
    strokeWidth: 3,
    backgroundColor: colorScheme.surfaceContainerHighest,
    valueColor: AlwaysStoppedAnimation(subjectColor),
  ),
  title: Text(lesson.title),
  subtitle: Text('${lesson.questionsCompleted}/${lesson.totalQuestions} questions'),
  trailing: lesson.isCompleted
    ? Icon(Icons.check_circle_rounded, color: Colors.green)
    : Text('${(lesson.progress * 100).toInt()}%'),
  onTap: () => navigateToLesson(lesson.id),
)
```

### 4.3 Question List with Difficulty Indicators

For review mode -- listing questions a student has attempted:

```dart
// Question list with difficulty color coding
ListView.builder(
  itemBuilder: (context, index) {
    final question = questions[index];
    return ListTile(
      leading: _DifficultyDot(difficulty: question.difficulty),
      title: Text(
        question.stem,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      subtitle: Text(question.conceptName),
      trailing: question.wasCorrect
        ? Icon(Icons.check_rounded, color: Colors.green, size: 20)
        : Icon(Icons.close_rounded, color: Colors.red, size: 20),
    );
  },
)
```

Difficulty dot color coding (matches existing `_DifficultyBadge`):
- Green (difficulty 1-3): Easy
- Orange (difficulty 4-6): Medium
- Red (difficulty 7-10): Hard

### 4.4 Infinite Scroll vs Pagination

| Content Type | Pattern | Rationale |
|-------------|---------|-----------|
| Session history | Infinite scroll | Students browse their history casually, no precise navigation needed |
| Question bank (review) | Paginated (20 per page) | Large corpus (500-2000 questions), need random access ("jump to page 5") |
| Activity feed (home) | Infinite scroll | Chronological feed, casual browsing |
| Badge grid | No pagination | Fixed set (< 50 badges), load all |
| Knowledge graph concepts | Virtualized list | Performance -- only render visible nodes |
| Search results | Infinite scroll with debounced search | Results appear as student types |

**Implementation for infinite scroll:**

```dart
// Using ScrollController for infinite scroll
class _InfiniteListState extends State<InfiniteList> {
  final _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
  }

  void _onScroll() {
    if (_scrollController.position.pixels >=
        _scrollController.position.maxScrollExtent - 200) {
      // Load next page
      ref.read(sessionHistoryProvider.notifier).loadMore();
    }
  }
}
```

### 4.5 Search and Filter Patterns

For question review and concept browsing:

```dart
// Search bar with filter chips
Column(
  children: [
    // Search field -- sticky at top
    SearchBar(
      hintText: 'Search concepts or questions...',
      leading: Icon(Icons.search_rounded),
      trailing: [
        if (hasActiveFilters)
          IconButton(
            icon: Icon(Icons.filter_list_off),
            onPressed: clearFilters,
          ),
      ],
    ),
    // Filter chips -- horizontal scroll
    SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: EdgeInsets.symmetric(horizontal: SpacingTokens.md),
      child: Row(
        children: [
          FilterChip(label: Text('Math'), selected: isFiltered('math'), ...),
          FilterChip(label: Text('Easy'), selected: isFiltered('easy'), ...),
          FilterChip(label: Text('Incorrect Only'), selected: isFiltered('incorrect'), ...),
          FilterChip(label: Text('Bookmarked'), selected: isFiltered('bookmarked'), ...),
        ],
      ),
    ),
    // Results list
    Expanded(child: filteredList),
  ],
)
```

---

## 5. Input Patterns for Learning

### 5.1 Multiple Choice (Current + Enhanced)

The existing `OptionTile` in `question_card.dart` is well-designed with Hebrew labels. Enhancements:

**Radio Selection (Single Answer)**
- Current implementation: correct (single tap to select, visual highlight)
- Enhancement: add haptic feedback on selection (`HapticFeedback.selectionClick()`)
- Enhancement: add subtle scale animation on selected option

**Checkbox Selection (Multiple Answer)**
For questions with multiple correct answers (future question type):
```dart
CheckboxListTile(
  value: isSelected,
  onChanged: (v) => toggleOption(index),
  title: MathText(content: optionText),
  secondary: CircleAvatar(child: Text(_labels[index])),
  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
)
```

**Image-Based Options**
For diagram questions where options are images:
```dart
GridView.count(
  crossAxisCount: 2,
  children: options.map((option) => GestureDetector(
    onTap: () => selectOption(option.index),
    child: Card(
      color: isSelected ? colorScheme.primaryContainer : colorScheme.surface,
      child: CachedNetworkImage(imageUrl: option.imageUrl),
    ),
  )).toList(),
)
```

### 5.2 Free Text Input (Current + Enhanced)

The existing `TextField` in `answer_input.dart` handles free text and proof types. Enhancements:

**Auto-Check Feedback**
For free-text answers, provide real-time feedback indicators:
```dart
TextField(
  decoration: InputDecoration(
    suffixIcon: _buildInputStatusIcon(), // Green check for valid format, red X for invalid
    counterText: '${_controller.text.length}/500',
  ),
)
```

**Smart Keyboard Actions**
```dart
// For single-line answers
textInputAction: TextInputAction.done,
onSubmitted: (_) => _submit(),

// For multi-line proofs
textInputAction: TextInputAction.newline,
// Submit via explicit button only
```

### 5.3 Math Input (Equation Editors on Mobile)

CENA already uses `flutter_math_fork` for rendering LaTeX. For math INPUT:

**Option A: LaTeX text input with live preview (Recommended for MVP)**
```dart
Column(
  children: [
    // Live preview of entered LaTeX
    Container(
      padding: EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: SubjectColorTokens.mathBackground,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
      ),
      child: Math.tex(
        _latexController.text.isNotEmpty ? _latexController.text : r'\text{Preview}',
        textStyle: TextStyle(fontSize: 20),
      ),
    ),
    SizedBox(height: SpacingTokens.sm),
    // Input field with math keyboard toolbar
    TextField(
      controller: _latexController,
      decoration: InputDecoration(hintText: r'Type LaTeX: x^2 + 2x + 1'),
    ),
    // Quick-insert toolbar for common math symbols
    _MathSymbolToolbar(
      symbols: [r'\frac{}{}', r'\sqrt{}', r'\pi', r'\sum', r'\int',
                r'^{}', r'_{}', r'\leq', r'\geq', r'\neq'],
      onInsert: (symbol) => _insertAtCursor(symbol),
    ),
  ],
)
```

**Option B: Visual equation editor (Post-MVP)**
Use `math_keyboard` package for a purpose-built math input keyboard that replaces the system keyboard with math-aware keys.

**Option C: Handwriting recognition (Future)**
Use `google_ml_kit` for on-device handwriting recognition of mathematical notation. See Section 13.

### 5.4 Drawing/Handwriting Input

For geometry proofs and diagram annotation:

```dart
// Drawing canvas widget
class DrawingCanvas extends StatefulWidget {
  // Uses CustomPainter for freeform drawing
  // Supports:
  // - Pen tool (freeform)
  // - Line tool (straight lines)
  // - Circle tool
  // - Eraser
  // - Color picker (limited palette)
  // - Undo/Redo stack
}
```

**Flutter package**: `perfect_freehand` for pressure-sensitive, natural-looking strokes. Combined with `flutter_drawing_board` for a complete drawing solution.

### 5.5 Voice Input for Language Learning

Not a primary need for CENA's math focus, but relevant for future expansion:

```dart
// Speech-to-text for verbal explanations
// Package: speech_to_text
final stt = SpeechToText();
await stt.listen(
  onResult: (result) => _controller.text = result.recognizedWords,
  localeId: 'he-IL',  // Hebrew
);
```

### 5.6 Drag-and-Drop (Matching, Ordering, Categorizing)

Critical for interactive question types defined in `interactive-questions-design.md`:

**Matching (Connect pairs)**
```dart
// Drag items from left column to right column targets
ReorderableListView(
  onReorder: (oldIndex, newIndex) => reorderAnswers(oldIndex, newIndex),
  children: answers.map((answer) => ListTile(
    key: ValueKey(answer.id),
    leading: Icon(Icons.drag_handle_rounded),
    title: Text(answer.text),
  )).toList(),
)
```

**Ordering (Sequence steps)**
```dart
// For proof steps: drag to reorder
ReorderableListView.builder(
  onReorder: _reorderSteps,
  itemCount: proofSteps.length,
  itemBuilder: (context, index) => Card(
    key: ValueKey(proofSteps[index].id),
    child: ListTile(
      leading: CircleAvatar(child: Text('${index + 1}')),
      title: Text(proofSteps[index].statement),
      trailing: Icon(Icons.drag_handle_rounded),
    ),
  ),
)
```

**Categorizing (Sort items into buckets)**
```dart
// Drag items into category containers
// Use LongPressDraggable + DragTarget pattern
LongPressDraggable<MathItem>(
  data: item,
  feedback: Material(
    elevation: 4,
    child: ItemCard(item: item),
  ),
  childWhenDragging: ItemCard(item: item, ghosted: true),
  child: ItemCard(item: item),
)

DragTarget<MathItem>(
  onAcceptWithDetails: (details) => addToCategory(details.data),
  builder: (context, candidateData, rejectedData) => CategoryBucket(
    category: category,
    isHighlighted: candidateData.isNotEmpty,
  ),
)
```

### 5.7 Slider Inputs for Confidence Ratings

After answering a question, ask the student how confident they feel:

```dart
// Confidence slider shown after answer submission, before feedback
Column(
  children: [
    Text('How confident are you?'),
    Slider(
      value: _confidence,
      min: 0,
      max: 1,
      divisions: 4,
      label: _confidenceLabel(_confidence),
      onChanged: (v) => setState(() => _confidence = v),
    ),
    Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text('Guessing', style: theme.textTheme.labelSmall),
        Text('Very sure', style: theme.textTheme.labelSmall),
      ],
    ),
  ],
)
```

Confidence data feeds into the Bayesian Knowledge Tracing model on the server -- a student who is correct but unconfident has different mastery implications than one who is correct and confident.

### 5.8 Image Annotation

For diagram questions where the student must annotate an image:

```dart
// Stack an interactive overlay on a diagram image
Stack(
  children: [
    // Base diagram image
    InteractiveViewer(
      child: CachedNetworkImage(imageUrl: diagramUrl),
    ),
    // Annotation layer
    CustomPaint(
      painter: AnnotationPainter(annotations: _annotations),
    ),
    // Tap-to-place markers
    GestureDetector(
      onTapUp: (details) => _placeAnnotation(details.localPosition),
    ),
  ],
)
```

---

## 6. Offline-First Design

CENA already has a sophisticated offline sync protocol (`offline-sync-protocol.md`) with three-tier event classification, clock skew detection, and conflict resolution. This section covers the **UI patterns** for offline-first.

### 6.1 Download Lessons for Offline Use

```
Profile Tab -> Downloads
+---------------------------------------------+
|  Downloads                                   |
|                                              |
|  Available Storage: 2.3 GB free              |
|  CENA using: 145 MB                          |
|                                              |
|  [Download All Math Content]        [420 MB] |
|                                              |
|  Mathematics (5-point)                       |
|  [========================] 100% downloaded  |
|  Last updated: 2 hours ago                   |
|                                              |
|  Physics (5-point)                           |
|  [=============-----------]  55% downloading |
|  Estimated: 3 min remaining                  |
|                                              |
|  Chemistry (5-point)              [Download] |
|  Not downloaded                              |
|                                              |
|  [Auto-download on WiFi: ON]                 |
+---------------------------------------------+
```

**Download unit**: Per-subject content package containing:
- Question bank (JSON, compressed): ~5-15 MB per subject
- Diagram images (optimized WebP): ~20-50 MB per subject
- Micro-lesson content (HTML + assets): ~50-100 MB per subject (future)
- Total per subject: ~75-165 MB

### 6.2 Sync Indicators and Conflict Resolution UI

**Sync status indicators** (already modeled in `SyncStatus` enum):

| Status | Icon | Color | Location |
|--------|------|-------|----------|
| Synced | `cloud_done_rounded` | Green | App bar (subtle) |
| Syncing | `sync_rounded` (animated rotation) | Blue | App bar |
| Pending (N events) | `cloud_upload_rounded` + badge(N) | Orange | App bar |
| Offline | `cloud_off_rounded` | Gray | App bar + banner |
| Error | `sync_problem_rounded` | Red | App bar + snackbar |

**Offline banner** -- shown when `ConnectivityMonitor.isOnline == false`:

```dart
// Persistent banner at top of screen
if (!isOnline) MaterialBanner(
  content: Text('You are offline. Your work is saved and will sync when connected.'),
  leading: Icon(Icons.cloud_off_rounded),
  backgroundColor: colorScheme.surfaceContainerHighest,
  actions: [
    TextButton(
      onPressed: () => syncManager.syncNow(),
      child: Text('Retry'),
    ),
  ],
)
```

**Conflict resolution UI** -- shown when server returns corrections:

```dart
// Conflict resolution bottom sheet
showModalBottomSheet(
  context: context,
  builder: (context) => Column(
    children: [
      Text('Server Updated', style: theme.textTheme.titleLarge),
      Text('Your mastery scores were recalculated based on server data.'),
      ...conflicts.map((c) => ListTile(
        title: Text(c.fieldName),
        subtitle: Text('Your value: ${c.clientValue} -> Server: ${c.serverValue}'),
        trailing: FilledButton(
          onPressed: () => acceptCorrection(c.idempotencyKey),
          child: Text('Accept'),
        ),
      )),
    ],
  ),
);
```

### 6.3 Offline Progress Tracking

The app must track progress locally and show it immediately, even before sync:

```dart
// Optimistic UI pattern for progress
class OfflineProgressTracker {
  // Local SQLite/SharedPreferences stores:
  // - Questions attempted (count)
  // - Correct/incorrect (for accuracy calculation)
  // - XP earned (calculated locally using same formula as server)
  // - Streak status (maintained locally, reconciled on sync)
  // - Time spent (accumulated from session timers)

  // All displayed values are local-first.
  // After sync, server corrections are applied silently
  // unless they change a user-visible value significantly.
}
```

### 6.4 Background Sync When Connection Returns

```dart
// In ConnectivityMonitor implementation
void _onConnectivityChanged(bool isOnline) {
  if (isOnline && _previousState == false) {
    // Connection restored
    _syncManager.syncNow();

    // Show transient snackbar
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('Back online! Syncing ${_syncManager.pendingEventCount} events...'),
        duration: Duration(seconds: 3),
      ),
    );
  }
  _previousState = isOnline;
}
```

### 6.5 Storage Management UI

```
+---------------------------------------------+
|  Storage Management                          |
|                                              |
|  Device storage: 2.3 GB free of 64 GB       |
|                                              |
|  CENA app data:                              |
|  [===========================] 145 MB        |
|                                              |
|  Question banks:     45 MB                   |
|  Diagrams & images:  62 MB                   |
|  Cached content:     28 MB                   |
|  Pending sync:       10 MB (23 events)       |
|                                              |
|  [Clear Cache]              [Clear All Data] |
|                                              |
|  Auto-download: WiFi only                    |
|  Keep offline: Last 7 days of content        |
+---------------------------------------------+
```

### 6.6 Offline-First Architecture with Actors

The mobile app's offline architecture maps to the server's actor model:

```
Mobile (Offline)              Server (Online)
+------------------+          +------------------+
| LocalSession     |   sync   | StudentActor     |
| (SharedPrefs)    | =======> | (Proto.Actor)    |
+------------------+          +------------------+
| OfflineEventQueue|          | Event Store      |
| (write-ahead)    | =======> | (Marten/PG)      |
+------------------+          +------------------+
| Local BKT        |   <===   | Server BKT       |
| (approximate)    |  correct | (authoritative)  |
+------------------+          +------------------+
```

The mobile app maintains a lightweight, approximate copy of the student's state using local BKT calculations. On sync, the server recalculates and sends corrections. The UI handles corrections silently unless the visual impact is significant (e.g., a concept mastery status flips from mastered to not-mastered).

---

## 7. Performance Patterns

### 7.1 Lazy Loading for Content

```dart
// Lazy load question content only when needed
class LazyQuestionProvider extends StateNotifier<AsyncValue<Exercise>> {
  LazyQuestionProvider() : super(const AsyncValue.loading());

  Future<void> loadQuestion(String questionId) async {
    state = const AsyncValue.loading();
    try {
      final exercise = await _apiClient.getExercise(questionId);
      state = AsyncValue.data(exercise);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }
}
```

Use `AsyncValue` from Riverpod to represent loading/data/error states cleanly.

### 7.2 Image Optimization and Caching

CENA already uses `cached_network_image`. Enhance with:

```dart
// Pre-cache next question's diagram while current question is displayed
void _prefetchNextDiagram(String? nextDiagramUrl) {
  if (nextDiagramUrl != null) {
    precacheImage(
      CachedNetworkImageProvider(nextDiagramUrl),
      context,
    );
  }
}
```

**Image format strategy:**
- Diagrams: WebP format, max 800px wide, quality 85
- Icons/badges: SVG (already using `flutter_svg`)
- User avatars: WebP, 200x200px thumbnails
- Math content: Rendered as Flutter widgets (not images)

### 7.3 Skeleton Screens

Replace loading spinners with shimmer skeletons (CENA already has `shimmer` package):

```dart
// Skeleton for question card loading
class QuestionCardSkeleton extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Shimmer.fromColors(
      baseColor: Colors.grey[300]!,
      highlightColor: Colors.grey[100]!,
      child: Card(
        child: Padding(
          padding: EdgeInsets.all(SpacingTokens.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // Difficulty badge placeholder
              Container(width: 80, height: 20, color: Colors.white),
              SizedBox(height: SpacingTokens.md),
              // Question text placeholder (3 lines)
              Container(width: double.infinity, height: 16, color: Colors.white),
              SizedBox(height: 8),
              Container(width: double.infinity, height: 16, color: Colors.white),
              SizedBox(height: 8),
              Container(width: 200, height: 16, color: Colors.white),
              SizedBox(height: SpacingTokens.lg),
              // Option placeholders (4 options)
              ...List.generate(4, (_) => Padding(
                padding: EdgeInsets.only(bottom: SpacingTokens.sm),
                child: Container(
                  width: double.infinity,
                  height: 48,
                  decoration: BoxDecoration(
                    color: Colors.white,
                    borderRadius: BorderRadius.circular(RadiusTokens.lg),
                  ),
                ),
              )),
            ],
          ),
        ),
      ),
    );
  }
}
```

### 7.4 Optimistic UI Updates

When a student submits an answer:

1. Immediately show "Evaluating..." state (button spinner)
2. Immediately update local question counter
3. If offline, immediately show locally-evaluated result (approximate)
4. When server responds, reconcile (usually no visible change)

```dart
// Optimistic update pattern
void _submitAnswer(String answer, int timeSpentMs) {
  // 1. Optimistic local update
  ref.read(sessionProvider.notifier).optimisticSubmit(answer, timeSpentMs);

  // 2. Send to server (or queue for offline)
  ref.read(sessionProvider.notifier).submitAnswer(answer, timeSpentMs);

  // 3. Server response reconciles (no-op if local was correct)
}
```

### 7.5 60fps Animation Targets

Critical animations and their frame budget:

| Animation | Duration | Technique | Frame Budget |
|-----------|----------|-----------|-------------|
| Option selection | 150ms | `AnimatedContainer` | 9 frames |
| Card flip (flashcard) | 300ms | `AnimationController` + Transform3D | 18 frames |
| Feedback overlay entrance | 300ms | `SlideTransition` | 18 frames |
| XP pop-up | 1000ms | `AnimationController` + scale/fade | 60 frames |
| Progress bar fill | 600ms | `TweenAnimationBuilder` | 36 frames |
| Page transitions | 300ms | `CupertinoPageTransitionsBuilder` | 18 frames |
| Streak fire animation | Looping | Lottie animation file | GPU-rendered |

**Rules for maintaining 60fps:**
1. Never rebuild widgets during animation -- use `AnimatedBuilder` or `RepaintBoundary`
2. Use `const` constructors for all widgets that do not change
3. Profile with Flutter DevTools -- ensure no frame over 16.67ms
4. Use `RepaintBoundary` around the knowledge graph canvas
5. Use `shouldRepaint: false` in `CustomPainter` when data hasn't changed

### 7.6 Memory Management for Large Question Banks

With 500-2,000 questions per subject:

```dart
// Don't load all questions into memory
// Use pagination + LRU cache

class QuestionCache {
  static const int maxCachedQuestions = 50;  // Keep ~50 in memory
  final _cache = LinkedHashMap<String, Exercise>();

  Exercise? get(String id) => _cache[id];

  void put(String id, Exercise exercise) {
    if (_cache.length >= maxCachedQuestions) {
      _cache.remove(_cache.keys.first);  // Evict oldest
    }
    _cache[id] = exercise;
  }
}
```

### 7.7 Flutter-Specific Performance (Widget Rebuild Optimization)

```dart
// BAD: Rebuilds entire list when one item changes
Widget build(BuildContext context) {
  final state = ref.watch(sessionProvider);
  return Column(children: state.exercises.map((e) => QuestionCard(exercise: e)).toList());
}

// GOOD: Use select() to watch only what's needed
Widget build(BuildContext context) {
  final currentExercise = ref.watch(
    sessionProvider.select((s) => s.currentExercise),
  );
  return QuestionCard(exercise: currentExercise!);
}
```

**Riverpod select() usage** -- the existing `session_screen.dart` watches the entire `sessionProvider`. Use `.select()` to rebuild only when specific fields change:

```dart
// Watch only what's needed
final questionsAttempted = ref.watch(sessionProvider.select((s) => s.questionsAttempted));
final accuracy = ref.watch(sessionProvider.select((s) => s.accuracy));
final fatigueScore = ref.watch(sessionProvider.select((s) => s.fatigueScore));
```

---

## 8. Notification Design

### 8.1 Push Notification Categories for Education

Based on `stakeholder-experiences.md` notification design:

| Category | Student | Parent | Priority | Sound |
|----------|---------|--------|----------|-------|
| Streak expiry warning | "Your 7-day streak expires in 2 hours!" | -- | High | Default |
| Daily study reminder | "Time to study! 15 min of math?" | -- | Medium | Custom gentle |
| Achievement earned | "You earned the Quadratic Master badge!" | "Your child mastered Quadratic Equations" | Low | Celebration |
| Concept mastery | "You mastered Trigonometry!" | Subject milestone notification | Low | Celebration |
| Review due (spaced rep) | "3 concepts need review today" | -- | Medium | Default |
| Exam countdown | "Math Bagrut in 14 days" | Same | High | Default |
| Weekly digest | -- | Summary of child's week | Low | None |
| Risk alert | -- | "No math activity in 5 days" | High | Default |

### 8.2 In-App Notification Center

Access from the bell icon in the app bar (already in `home_screen.dart`):

```
+---------------------------------------------+
|  Notifications                     [Mark all]|
|                                              |
|  Today                                       |
|  [Trophy] You mastered Quadratic Equations!  |
|           2 hours ago                        |
|                                              |
|  [Clock] Review 3 concepts before they fade  |
|          5 hours ago                  [Start] |
|                                              |
|  Yesterday                                   |
|  [Fire] 7-day streak! Keep it going!         |
|         Yesterday at 20:15                   |
|                                              |
|  This Week                                   |
|  [Star] You reached Level 5!                 |
|         March 29 at 14:30                    |
+---------------------------------------------+
```

### 8.3 Badge Counts and Management

```dart
// Badge count on notification icon
IconButton(
  icon: Badge(
    isLabelVisible: unreadCount > 0,
    label: Text('$unreadCount'),
    child: Icon(Icons.notifications_outlined),
  ),
  onPressed: () => showNotificationCenter(context),
)
```

### 8.4 Notification Grouping

Group by category to prevent notification fatigue:

```dart
// Android notification channels
const AndroidNotificationChannel streakChannel = AndroidNotificationChannel(
  'streak_alerts',
  'Streak Alerts',
  description: 'Notifications about your learning streak',
  importance: Importance.high,
);

const AndroidNotificationChannel achievementChannel = AndroidNotificationChannel(
  'achievements',
  'Achievements',
  description: 'Badge and milestone notifications',
  importance: Importance.low,
);

const AndroidNotificationChannel studyReminders = AndroidNotificationChannel(
  'study_reminders',
  'Study Reminders',
  description: 'Daily study reminders and review alerts',
  importance: Importance.defaultImportance,
);
```

### 8.5 Quiet Hours for Students

```dart
// Quiet hours configuration (default: 22:00 - 07:00)
class QuietHoursConfig {
  final TimeOfDay start;    // Default: 22:00
  final TimeOfDay end;      // Default: 07:00
  final bool enabled;       // Default: true
  final Set<NotificationCategory> exceptions; // Exam countdown breaks quiet hours

  bool shouldSuppress(DateTime now) {
    if (!enabled) return false;
    final hour = now.hour;
    // Handle overnight range (22:00 -> 07:00)
    if (start.hour > end.hour) {
      return hour >= start.hour || hour < end.hour;
    }
    return hour >= start.hour && hour < end.hour;
  }
}
```

### 8.6 Teacher vs Student Notification Design

| Aspect | Student | Teacher |
|--------|---------|--------|
| Tone | Encouraging, gamified ("You earned a badge!") | Professional, informative ("3 students struggling with Trigonometry") |
| Frequency | Max 3/day (excluding responses to actions) | Batched daily digest |
| Action buttons | "Start session", "Review now" | "View dashboard", "Send encouragement" |
| Visuals | Badge icons, streak fire, XP numbers | Charts, student counts, alert indicators |

---

## 9. Tablet & Adaptive Layout

### 9.1 Responsive Layouts (Phone vs Tablet)

Flutter adaptive layout using breakpoints:

```dart
class ResponsiveLayout extends StatelessWidget {
  final Widget phone;
  final Widget? tablet;

  const ResponsiveLayout({required this.phone, this.tablet});

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.of(context).size.width;
    if (width >= 600 && tablet != null) {
      return tablet!;
    }
    return phone;
  }
}
```

### 9.2 Split-View on Tablets

For tablets (>= 600dp), use a master-detail layout:

```
+------------------+----------------------------+
|                  |                            |
|  Concept List    |   Question Card            |
|  (1/3 width)     |   (2/3 width)              |
|                  |                            |
|  - Algebra       |   [Question content]       |
|  - Geometry  *   |   [Options]                |
|  - Statistics    |   [Answer input]           |
|  - Calculus      |   [Submit / Skip]          |
|                  |                            |
+------------------+----------------------------+
```

Implementation:

```dart
// Tablet session layout
class TabletSessionLayout extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        // Left panel: concept navigation (1/3)
        SizedBox(
          width: MediaQuery.of(context).size.width / 3,
          child: ConceptNavigationPanel(),
        ),
        VerticalDivider(width: 1),
        // Right panel: question + answer (2/3)
        Expanded(
          child: SessionQuestionPanel(),
        ),
      ],
    );
  }
}
```

### 9.3 Landscape Mode for Specific Activities

| Activity | Landscape Support | Behavior |
|----------|-------------------|----------|
| Video micro-lessons | Required | Auto-rotate, full-screen player |
| Drawing/annotation | Recommended | More canvas space |
| Knowledge graph | Recommended | Better graph visualization |
| Question answering | Optional | Side-by-side question + input |
| Reading/text content | Discouraged | Lock to portrait |
| Session config | Discouraged | Lock to portrait |

```dart
// Lock session screen to portrait on phones
class SessionScreen extends ConsumerStatefulWidget {
  @override
  ConsumerState<SessionScreen> createState() => _SessionScreenState();

  @override
  void initState() {
    super.initState();
    final isTablet = MediaQuery.of(context).size.shortestSide >= 600;
    if (!isTablet) {
      SystemChrome.setPreferredOrientations([
        DeviceOrientation.portraitUp,
        DeviceOrientation.portraitDown,
      ]);
    }
  }

  @override
  void dispose() {
    // Restore all orientations
    SystemChrome.setPreferredOrientations(DeviceOrientation.values);
    super.dispose();
  }
}
```

### 9.4 Flutter Adaptive Layout Patterns

Use the `flutter_adaptive_scaffold` package (official Flutter package) for complex adaptive layouts:

```dart
AdaptiveScaffold(
  smallBody: (_) => PhoneLayout(),
  body: (_) => TabletLayout(),
  largeBody: (_) => DesktopLayout(),  // For web/Chromebook
  smallBreakpoint: const Breakpoint(endWidth: 599),
  mediumBreakpoint: const Breakpoint(beginWidth: 600, endWidth: 839),
  largeBreakpoint: const Breakpoint(beginWidth: 840),
  useDrawer: false,
  destinations: [
    NavigationDestination(icon: Icon(Icons.home), label: 'Home'),
    // ... etc
  ],
)
```

---

## 10. Platform-Specific Design

### 10.1 Material 3 (Android) vs Cupertino (iOS) Decisions

CENA uses Material 3 as the primary design system (`useMaterial3: true`). The decision matrix:

| Pattern | Use Material 3 (Both Platforms) | Use Platform-Adaptive | Use Custom |
|---------|--------------------------------|-----------------------|-----------|
| Bottom navigation | NavigationBar (M3) | -- | -- |
| Buttons | FilledButton, OutlinedButton | -- | -- |
| Cards | Card (M3) | -- | -- |
| Dialogs | AlertDialog (M3) | -- | -- |
| Page transitions | -- | CupertinoPageTransitionsBuilder (iOS), FadeUpwards (Android) | -- |
| Scroll physics | -- | BouncingScrollPhysics (iOS), ClampingScrollPhysics (Android) | -- |
| Date/time pickers | -- | showDatePicker (M3 on Android), CupertinoDatePicker (iOS) | -- |
| App bar back button | -- | Automatic (arrow on Android, chevron on iOS) | -- |
| Pull to refresh | -- | Platform-default scroll physics | -- |
| Question cards | -- | -- | Custom design (neither M3 card nor Cupertino card) |
| Option tiles | -- | -- | Custom interactive tiles |
| Knowledge graph | -- | -- | Custom canvas painting |

**Rationale for mostly-Material**: CENA serves an Israeli market where Android dominates (~65% market share). Material 3 provides a cohesive design language with excellent RTL support. iOS users get platform-appropriate transitions and scroll physics while maintaining visual consistency.

### 10.2 Platform-Adaptive Widgets in Flutter

```dart
// Platform-adaptive page transitions (already in cena_theme.dart)
pageTransitionsTheme: const PageTransitionsTheme(
  builders: {
    TargetPlatform.android: FadeUpwardsPageTransitionsBuilder(),
    TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
  },
),

// Platform-adaptive scroll physics
ScrollConfiguration(
  behavior: ScrollConfiguration.of(context).copyWith(
    physics: Theme.of(context).platform == TargetPlatform.iOS
      ? const BouncingScrollPhysics()
      : const ClampingScrollPhysics(),
  ),
  child: ListView(...),
)
```

### 10.3 When to Follow Platform Conventions vs Custom Design

**Follow platform conventions for:**
- Navigation gestures (iOS swipe-back, Android system back)
- Status bar and safe area handling
- Keyboard behavior and input handling
- Haptic feedback patterns
- Share sheets and system intents
- Permission request flows

**Use custom design for:**
- Question cards and learning UI -- this is CENA's brand identity
- Gamification elements (XP bar, streak fire, badge display)
- Knowledge graph visualization
- Session progress indicators
- Answer feedback animations

### 10.4 System Integration

**Home screen widgets** (future feature):
- Android widget: Today's streak count + "Start Session" button
- iOS widget: Streak count + next review due

**App shortcuts** (Android) / Quick Actions (iOS):
```dart
// Quick actions from long-press on app icon
const quickActions = [
  ShortcutItem(type: 'start_math', localizedTitle: 'Start Math Session', icon: 'math_icon'),
  ShortcutItem(type: 'start_last', localizedTitle: 'Continue Last Session', icon: 'play_icon'),
  ShortcutItem(type: 'review', localizedTitle: 'Review Due Concepts', icon: 'review_icon'),
];
```

**Share sheets:**
```dart
// Share achievement to WhatsApp / Instagram
Share.share(
  'I just mastered Quadratic Equations on CENA! 12-day streak!',
  subject: 'CENA Achievement',
);
// For image sharing (achievement cards):
Share.shareXFiles([XFile(achievementImagePath)]);
```

### 10.5 Accessibility Services Integration

```dart
// Semantic labels for screen readers
Semantics(
  label: 'Question 5 of 12. Difficulty: medium. Type: multiple choice.',
  child: QuestionCard(exercise: exercise),
)

// Ensure MCQ options are accessible
Semantics(
  label: 'Option A: ${optionText}. ${isSelected ? "Selected" : "Not selected"}',
  child: OptionTile(...),
)

// Large text support
MediaQuery(
  data: MediaQuery.of(context).copyWith(
    textScaler: TextScaler.linear(min(MediaQuery.of(context).textScaler.scale(1.0), 1.5)),
  ),
  child: child,
)
```

---

## 11. Data Visualization on Mobile

### 11.1 Progress Charts and Graphs

CENA already uses `fl_chart`. Chart patterns for mobile:

**Weekly Progress Bar Chart:**
```dart
BarChart(
  BarChartData(
    maxY: 200,  // Max XP per day
    barGroups: weeklyXp.map((day) => BarChartGroupData(
      x: day.weekday,
      barRods: [
        BarChartRodData(
          toY: day.xpEarned.toDouble(),
          color: day.isToday ? colorScheme.primary : colorScheme.primaryContainer,
          width: 20,
          borderRadius: BorderRadius.vertical(top: Radius.circular(4)),
        ),
      ],
    )).toList(),
    titlesData: FlTitlesData(
      bottomTitles: AxisTitles(
        sideTitles: SideTitles(
          showTitles: true,
          getTitlesWidget: (value, meta) => Text(
            ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][value.toInt()],
          ),
        ),
      ),
    ),
  ),
)
```

### 11.2 Skill Radar Chart

For showing mastery across topics within a subject:

```dart
RadarChart(
  RadarChartData(
    radarShape: RadarShape.polygon,
    dataSets: [
      RadarDataSet(
        fillColor: colorScheme.primary.withOpacity(0.2),
        borderColor: colorScheme.primary,
        dataEntries: topics.map((t) =>
          RadarEntry(value: t.masteryPercentage)
        ).toList(),
      ),
    ],
    getTitle: (index, angle) => RadarChartTitle(
      text: topics[index].name,
      angle: angle,
    ),
    titlePositionPercentageOffset: 0.2,
  ),
)
```

### 11.3 Time-Spent Visualizations

**Study time heatmap** (GitHub contribution graph style, mentioned in stakeholder-experiences.md for parent dashboard):

```dart
// 12-week calendar heatmap
class StudyHeatmap extends StatelessWidget {
  // Grid: 7 rows (days) x 12 columns (weeks)
  // Color intensity maps to study minutes:
  //   0 min = gray, 1-15 min = light green, 16-30 = medium green,
  //   31-60 = green, 61+ = dark green
}
```

### 11.4 Performance Trends

**Line chart showing accuracy over time:**

```dart
LineChart(
  LineChartData(
    lineBarsData: [
      LineChartBarData(
        spots: sessions.map((s) => FlSpot(
          s.date.millisecondsSinceEpoch.toDouble(),
          s.accuracy * 100,
        )).toList(),
        isCurved: true,
        color: colorScheme.primary,
        barWidth: 2,
        dotData: FlDotData(show: false),
        belowBarData: BarAreaData(
          show: true,
          color: colorScheme.primary.withOpacity(0.1),
        ),
      ),
    ],
  ),
)
```

### 11.5 Class Comparison Charts (Privacy-Safe)

Per `stakeholder-experiences.md`, cohort comparison requires opt-in and minimum 30 students:

```dart
// Percentile rank visualization
class CohortComparisonWidget extends StatelessWidget {
  final double percentile;  // 0.0 to 1.0

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Text('You are ahead of ${(percentile * 100).toInt()}% of students'),
        // Simple bar showing position in distribution
        Stack(
          children: [
            // Background bar
            Container(
              height: 8,
              decoration: BoxDecoration(
                color: colorScheme.surfaceContainerHighest,
                borderRadius: BorderRadius.circular(4),
              ),
            ),
            // Position marker
            FractionallySizedBox(
              widthFactor: percentile,
              child: Container(
                height: 8,
                decoration: BoxDecoration(
                  color: colorScheme.primary,
                  borderRadius: BorderRadius.circular(4),
                ),
              ),
            ),
          ],
        ),
      ],
    );
  }
}
```

### 11.6 Flutter Charting Package Recommendation

| Package | Use For | Why |
|---------|---------|-----|
| `fl_chart` (already in pubspec) | Bar charts, line charts, pie charts, radar charts | Well-maintained, highly customizable, good performance |
| `flutter_heatmap_calendar` | Study heatmap (GitHub-style) | Purpose-built for date heatmaps |
| `syncfusion_flutter_charts` (free community) | Complex charts if fl_chart insufficient | Richer chart types, but larger package |
| Custom `CustomPainter` | Knowledge graph, skill trees | No package handles CENA's specific graph visualization needs |

---

## 12. Gesture Language for Education

### 12.1 Gesture Reference Guide

| Gesture | Action | Context | Discoverability |
|---------|--------|---------|-----------------|
| **Tap** | Select option, navigate, submit | Everywhere | Intuitive, no hint needed |
| **Swipe right** | "I know this" (confident) | Flashcard/spaced rep mode | Onboarding tutorial + subtle arrow hint |
| **Swipe left** | "Need review" (not confident) | Flashcard/spaced rep mode | Onboarding tutorial + subtle arrow hint |
| **Swipe up** | Skip question | Flashcard mode | Small "skip" label at top edge |
| **Long press** | Bookmark question for review | Question card | Tooltip on first use |
| **Double tap** | Add to favorites | Achievement cards, concept nodes | Toast notification on first use |
| **Pinch to zoom** | Zoom on diagrams, images | Diagram questions, knowledge graph | Standard gesture, no hint needed |
| **Two-finger pan** | Pan zoomed content | Knowledge graph, diagrams | Follows pinch-zoom naturally |
| **Drag** | Reorder proof steps, categorize items | Interactive question types | Drag handle icon visible |
| **Pull down** | Refresh content | Lists, feeds | Platform standard |
| **Swipe down on modal** | Dismiss bottom sheet | All modal bottom sheets | Drag handle visible |

### 12.2 Swipe Gestures for Flashcards

```dart
class SwipeableFlashcard extends StatefulWidget {
  @override
  State<SwipeableFlashcard> createState() => _SwipeableFlashcardState();
}

class _SwipeableFlashcardState extends State<SwipeableFlashcard>
    with SingleTickerProviderStateMixin {
  late AnimationController _controller;
  Offset _dragOffset = Offset.zero;
  double _rotation = 0;

  void _onPanUpdate(DragUpdateDetails details) {
    setState(() {
      _dragOffset += details.delta;
      _rotation = _dragOffset.dx / 300 * 0.3;  // Max 0.3 radians (~17 degrees)
    });
  }

  void _onPanEnd(DragEndDetails details) {
    final dx = _dragOffset.dx;
    if (dx > 100) {
      // Swiped right: "I know this"
      _animateOut(direction: 1);
      widget.onConfident();
    } else if (dx < -100) {
      // Swiped left: "Need review"
      _animateOut(direction: -1);
      widget.onNeedReview();
    } else {
      // Return to center
      _animateBack();
    }
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onPanUpdate: _onPanUpdate,
      onPanEnd: _onPanEnd,
      child: Transform(
        transform: Matrix4.identity()
          ..translate(_dragOffset.dx, _dragOffset.dy)
          ..rotateZ(_rotation),
        alignment: Alignment.center,
        child: Stack(
          children: [
            widget.card,
            // Swipe direction indicator
            if (_dragOffset.dx > 30)
              Positioned(
                top: 20, left: 20,
                child: _SwipeLabel(text: 'Got it!', color: Colors.green),
              ),
            if (_dragOffset.dx < -30)
              Positioned(
                top: 20, right: 20,
                child: _SwipeLabel(text: 'Review', color: Colors.orange),
              ),
          ],
        ),
      ),
    );
  }
}
```

### 12.3 Long Press to Bookmark

```dart
GestureDetector(
  onLongPress: () {
    HapticFeedback.mediumImpact();
    ref.read(bookmarkProvider.notifier).toggleBookmark(exercise.id);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(isBookmarked ? 'Removed bookmark' : 'Bookmarked for review'),
        action: SnackBarAction(label: 'Undo', onPressed: () {
          ref.read(bookmarkProvider.notifier).toggleBookmark(exercise.id);
        }),
      ),
    );
  },
  child: QuestionCard(exercise: exercise),
)
```

### 12.4 Gesture Discoverability

First-time users need to learn custom gestures. Use a contextual onboarding approach:

```dart
// Show gesture hint overlay on first encounter
class GestureHint extends StatelessWidget {
  // Semi-transparent overlay with animated hand icon
  // showing the gesture motion
  // Displayed once per gesture type, tracked via SharedPreferences
  // Dismissed by performing the gesture or tapping "Got it"

  final String gestureKey;  // e.g., 'swipe_flashcard'
  final Widget animation;  // Lottie animation showing gesture
  final String instruction; // e.g., 'Swipe right if you know this'
}
```

---

## 13. Camera & AR Features

### 13.1 Photo-Based Problem Solving (Scan Homework)

**Use case**: Student photographs a Bagrut practice problem from a textbook. CENA OCRs the text, identifies the concept, and creates a learning session around it.

```dart
// Camera integration for homework scanning
Future<void> _scanHomework() async {
  final image = await ImagePicker().pickImage(source: ImageSource.camera);
  if (image == null) return;

  // On-device text recognition
  final inputImage = InputImage.fromFilePath(image.path);
  final textRecognizer = TextRecognizer(script: TextRecognitionScript.latin);
  // For Hebrew: TextRecognitionScript is not available for Hebrew in google_ml_kit
  // Alternative: send image to server for Cloud Vision API processing
  final recognized = await textRecognizer.processImage(inputImage);

  // Extract question text and match to concept
  final questionText = recognized.text;
  // Send to server for concept matching and session creation
}
```

**Hebrew/Arabic OCR challenge**: Google ML Kit's on-device text recognition does not support Hebrew or Arabic scripts directly. Solution:

1. **On-device**: Use `google_ml_kit` for Latin script (English math notation, numbers, formulas)
2. **Server-side**: Send image to Google Cloud Vision API or Tesseract OCR with Hebrew/Arabic language packs for full text recognition
3. **Hybrid**: Extract formulas on-device (LaTeX), send Hebrew text to server

### 13.2 AR for 3D Learning Content (Future)

Relevant for geometry (3D shapes), physics (force diagrams), chemistry (molecular structures):

```dart
// AR package: ar_flutter_plugin or arcore_flutter_plugin (Android) + arkit_plugin (iOS)
// Example: visualize a 3D geometric shape
ARView(
  onARViewCreated: (controller) {
    controller.addNode(
      ARNode(
        geometry: ARGeometry.sphere(radius: 0.1),
        position: Vector3(0, 0, -0.5),
        material: ARMaterial(color: SubjectColorTokens.mathPrimary),
      ),
    );
  },
)
```

**Recommendation**: AR is a Phase 3 feature. Do not invest in it before the core learning loop is complete and validated.

### 13.3 Document Scanning for Notes

```dart
// Use edge_detection package for document scanning
final edgeDetectionResult = await EdgeDetector().detectEdges(imagePath);
final croppedImage = await EdgeDetector().processImage(
  imagePath,
  edgeDetectionResult,
);
// Store as student annotation, linked to current concept
```

### 13.4 QR Code Scanning for Classroom Activities

**Use case**: Teacher projects a QR code. Students scan to join a group activity or start a specific session.

```dart
// QR scanning with mobile_scanner package
MobileScanner(
  onDetect: (capture) {
    final qrCode = capture.barcodes.first.rawValue;
    if (qrCode != null && qrCode.startsWith('cena://')) {
      // Handle deep link from QR code
      context.go(qrCode.replaceFirst('cena://', '/'));
    }
  },
)
```

QR code payload format:
- `cena://session/join/{classroomSessionId}` -- join a classroom activity
- `cena://graph/concept/{conceptId}` -- focus on a specific concept
- `cena://lesson/{lessonId}` -- start a specific micro-lesson

---

## 14. Flutter Package Recommendations

### 14.1 Complete Package Matrix

| Category | Package | Version | Already in pubspec? | Purpose |
|----------|---------|---------|--------------------|---------|
| **State Management** | `flutter_riverpod` | ^2.3.2 | Yes | App-wide state |
| **Navigation** | `go_router` | ^14.0.0 | Yes | Declarative routing |
| **Networking** | `dio` | ^5.1.1 | Yes | REST API client |
| **Networking** | `web_socket_channel` | ^3.0.1 | Yes | SignalR WebSocket |
| **Local Storage** | `flutter_secure_storage` | ^8.0.0 | Yes | Credentials |
| **Local Storage** | `shared_preferences` | ^2.3.0 | Yes | Settings, flags |
| **Local DB** | `drift` | ^2.15.0 | **Add** | Local SQLite for offline question cache |
| **Serialization** | `freezed` | ^2.4.7 | Yes | Immutable models |
| **Charts** | `fl_chart` | ^0.36.1 | Yes | Progress charts |
| **Images** | `cached_network_image` | ^3.4.1 | Yes | Image caching |
| **SVG** | `flutter_svg` | ^2.0.7 | Yes | SVG rendering |
| **Math** | `flutter_math_fork` | ^0.7.2 | Yes | LaTeX rendering |
| **Loading** | `shimmer` | ^2.0.0 | Yes | Skeleton screens |
| **Auth** | `firebase_auth` | ^5.5.3 | Yes | Authentication |
| **Analytics** | `firebase_analytics` | ^11.4.2 | Yes | Event tracking |
| **Push** | `firebase_messaging` | ^15.2.4 | Yes | Push notifications |
| **i18n** | `intl` | ^0.19.0 | Yes | Internationalization |
| **Connectivity** | `connectivity_plus` | ^6.1.0 | Yes | Network monitoring |
| **Animations** | `lottie` | ^3.1.0 | **Add** | Complex animations (streak fire, celebrations) |
| **Math Input** | `math_keyboard` | ^0.3.0 | **Add** | Math-aware keyboard for equation input |
| **Drawing** | `perfect_freehand` | ^2.0.0 | **Add** | Freehand drawing for annotations |
| **Camera** | `image_picker` | ^1.0.7 | **Add** | Photo capture for homework scanning |
| **QR** | `mobile_scanner` | ^4.0.0 | **Add** | QR code scanning for classroom |
| **Haptics** | (flutter core) | -- | Built-in | `HapticFeedback` class |
| **Adaptive** | `flutter_adaptive_scaffold` | ^0.1.10 | **Add** | Responsive tablet layouts |
| **Heatmap** | `flutter_heatmap_calendar` | ^1.0.5 | **Add** | Study activity heatmap |
| **Local notifications** | `flutter_local_notifications` | ^17.0.0 | **Add** | Scheduled local reminders |
| **Share** | `share_plus` | ^7.2.2 | **Add** | Share achievements to social |
| **App shortcuts** | `quick_actions` | ^1.0.0 | **Add** | Home screen quick actions |

### 14.2 Packages to Avoid

| Package | Reason | Alternative |
|---------|--------|-------------|
| `provider` | Riverpod is the evolution, already adopted | `flutter_riverpod` |
| `bloc` / `flutter_bloc` | Conflicting state management, Riverpod covers all cases | `flutter_riverpod` |
| `getx` | Template code uses GetX but CENA uses Riverpod | `flutter_riverpod` |
| `hive` / `isar` | Drift (SQLite) is better for relational offline data | `drift` |
| `graphql_flutter` | No GraphQL -- REST + SignalR decision (memory: `feedback_no_graphql.md`) | `dio` |

---

## 15. Actor Backend Integration

### 15.1 How Mobile Patterns Map to Actor Messages

Every user interaction on mobile generates events that travel to the actor cluster:

| Mobile Action | NATS Subject | Actor Target | Event Type |
|--------------|-------------|-------------|------------|
| Start session | `cena.session.start` | `StudentActor` | `SessionStarted` |
| Submit answer | `cena.mastery.attempt` | `StudentActor` | `ExerciseAttempted` |
| Skip question | `cena.mastery.skip` | `StudentActor` | `QuestionSkipped` |
| Request hint | `cena.session.hint` | `StudentActor` | `HintRequested` |
| End session | `cena.session.end` | `StudentActor` | `SessionEnded` |
| Bookmark question | `cena.student.annotate` | `StudentActor` | `AnnotationAdded` |
| Confidence rating | `cena.mastery.confidence` | `StudentActor` | `ConfidenceReported` |
| Swipe flashcard (know) | `cena.mastery.recall` | `StudentActor` | `RecallConfirmed` |
| Swipe flashcard (review) | `cena.mastery.recall` | `StudentActor` | `RecallFailed` |

### 15.2 Mobile -> SignalR -> Actor Flow

```
Mobile App                  API Gateway              Actor Host
+--------+     SignalR      +----------+    NATS     +----------+
| Flutter | =============> | .NET 9   | =========> | Proto.Actor|
|   App   |                | Hub      |            | Cluster   |
|         | <============= |          | <========= |           |
+--------+  Server Events  +----------+  Pub/Sub   +----------+
```

The mobile app communicates via SignalR (WebSocket). The API gateway translates SignalR messages to NATS publish/request-reply messages that reach the actor cluster. Responses flow back the same path.

### 15.3 Offline Queue -> Actor Reconciliation

When the mobile app reconnects after offline work:

1. `OfflineEventQueue` drains events in sequence order
2. Each event is sent via SignalR with its idempotency key and client timestamp
3. The API gateway publishes to NATS: `cena.sync.batch`
4. `StudentActor` processes the batch using the three-tier classification algorithm
5. Actor responds with corrections (if any) via `cena.sync.corrections`
6. Mobile `ConflictResolver` applies corrections to local state
7. UI updates are applied (optimistically or with correction banners)

### 15.4 Real-Time Features via SignalR

| Feature | SignalR Channel | Direction | Mobile UI Effect |
|---------|----------------|-----------|-----------------|
| Next question delivery | `ReceiveExercise` | Server -> Client | New question card appears |
| Answer evaluation | `ReceiveEvaluation` | Server -> Client | Feedback overlay |
| Methodology switch | `MethodologySwitched` | Server -> Client | Methodology badge updates |
| Break suggestion | `BreakSuggested` | Server -> Client | Cognitive load break overlay |
| Mastery update | `MasteryUpdated` | Server -> Client | Knowledge graph node color change |
| XP awarded | `XpAwarded` | Server -> Client | XP pop-up animation |
| Streak update | `StreakUpdated` | Server -> Client | Streak counter animation |
| Outreach message | `OutreachMessage` | Server -> Client | In-app notification |

---

## 16. Screen Layout Templates

### 16.1 Home Screen Layout

```
+---------------------------------------------+
| [App Logo]  Cena              [Bell] [Sync]  |  <- App bar (HARD zone - OK)
+---------------------------------------------+
|                                              |
|  Good morning, Ahmad!                        |  <- Greeting (OK zone)
|  Ready to learn?                             |
|  [Fire] 7-day streak                         |
|                                              |
+---------------------------------------------+
|                                              |
|  Start a Session                             |  <- Subject grid (OK zone)
|  +-------+  +-------+  +-------+            |
|  | Math  |  |Physics|  | Chem  |            |
|  |  72%  |  |  45%  |  |  30%  |            |
|  +-------+  +-------+  +-------+            |
|  +-------+  +-------+                       |
|  |Biology|  |  CS   |                        |
|  |  60%  |  |  15%  |                        |
|  +-------+  +-------+                       |
|                                              |
+---------------------------------------------+
|                                              |
|  Recent Activity                             |  <- Feed (OK zone)
|  [Today] Math session - 25 min, 83%          |
|  [Yesterday] Mastered: Trigonometry          |
|                                              |
+---------------------------------------------+
|                                              |
|  [===== START LEARNING =====]                |  <- CTA button (EASY zone)
|                                              |
+---------------------------------------------+
| [Home] [Learn] [Map] [Progress] [Profile]    |  <- Bottom nav (EASY zone)
+---------------------------------------------+
```

### 16.2 Session Screen Layout (Active Question)

```
+---------------------------------------------+
|  Q5  [====85%====]  12:34  [Fatigue: OK]     |  <- Progress (HARD zone)
|  [Spaced Repetition]                         |  <- Methodology badge
+---------------------------------------------+
|                                              |
|  Question 5                          [End]   |  <- Question header
|                                              |
|  +---------------------------------------+   |
|  | Difficulty: Medium 5/10  | MCQ        |   |  <- Question card (OK zone)
|  |                                       |   |
|  | Solve for x:                          |   |
|  | $x^2 + 2x - 3 = 0$                   |   |
|  |                                       |   |
|  | (A) x = 1, x = -3                    |   |
|  | (B) x = -1, x = 3     <-- selected   |   |
|  | (C) x = 1, x = 3                     |   |
|  | (D) x = -1, x = -3                   |   |
|  +---------------------------------------+   |
|                                              |
+---------------------------------------------+
|                                              |
|  [Hint]              [Skip] [=== Submit ===] |  <- Actions (EASY zone)
|                                              |
+---------------------------------------------+
```

### 16.3 Knowledge Graph Layout

```
+---------------------------------------------+
| Knowledge Map           [Filter] [Zoom fit]  |
+---------------------------------------------+
|                                              |
|           Algebra                            |
|          /       \                           |
|     Linear      Quadratic                    |
|    (green)      (yellow)                     |
|      |            |    \                     |
|  Systems      Factoring  Formula             |
|  (green)      (yellow)   (red)               |
|                  |                            |
|              Completing                      |
|              the Square                      |
|              (gray)                          |
|                                              |
|  [Interactive canvas with pinch-zoom,        |
|   pan, tap-to-select node]                   |
|                                              |
+---------------------------------------------+
| Concept: Quadratic Equations                 |  <- Detail (bottom sheet)
| Mastery: 65%  |  Last practiced: 2h ago      |
| [Practice this concept]                      |
+---------------------------------------------+
| [Home] [Learn] [Map*] [Progress] [Profile]   |
+---------------------------------------------+
```

### 16.4 Flashcard Mode Layout

```
+---------------------------------------------+
|                              [X Close]       |
+---------------------------------------------+
|                                              |
|              Card 3 of 20                    |
|                                              |
|  +---------------------------------------+   |
|  |                                       |   |
|  |   What is the derivative of           |   |
|  |   $\sin(x)$ ?                         |   |
|  |                                       |   |
|  |                                       |   |
|  |   [Tap to reveal answer]              |   |
|  |                                       |   |
|  +---------------------------------------+   |
|                                              |
|  <-- Need Review          Got it! -->        |  <- Swipe indicators
|                                              |
+---------------------------------------------+
|  [Skip up]                                   |
|                                              |
|  Progress: [===========--------] 11/20       |
+---------------------------------------------+
```

### 16.5 Session Summary Layout

```
+---------------------------------------------+
|                                              |
|              [Trophy Icon]                   |
|                                              |
|           Session Complete!                  |
|                                              |
|  +---------------------------------------+   |
|  |  Questions:  12                       |   |
|  |  Accuracy:   83%                      |   |
|  |  Time:       22:34                    |   |
|  |  XP Earned:  +180                     |   |
|  +---------------------------------------+   |
|                                              |
|  Concepts Practiced:                         |
|  [Quadratic Eq.]  [Factoring]  [Trig]       |
|                                              |
|  New Mastery:                                |
|  [Star] Mastered: Trigonometric Identities   |
|                                              |
+---------------------------------------------+
|                                              |
|  [=== RETURN HOME ===]                       |
|  [Start Another Session]                     |
|                                              |
+---------------------------------------------+
```

---

## 17. Performance Optimization Checklist

### Build-Time Optimizations

- [ ] Use `const` constructors for all stateless widgets that accept no dynamic data
- [ ] Use `const` for all EdgeInsets, BorderRadius, TextStyle literals
- [ ] Run `flutter analyze` with zero warnings before each release
- [ ] Use tree-shaking-friendly imports (import specific files, not barrel exports for large packages)
- [ ] Enable Dart AOT compilation for release builds

### Runtime Optimizations

- [ ] Use `Riverpod .select()` to minimize widget rebuilds (see Section 7.7)
- [ ] Wrap heavy paint operations in `RepaintBoundary` (knowledge graph, charts)
- [ ] Use `ListView.builder` / `GridView.builder` for all lists (never `ListView(children:)` for dynamic data)
- [ ] Implement `AutomaticKeepAliveClientMixin` for tab content to preserve state
- [ ] Use `precacheImage` to pre-load next question's diagram
- [ ] Limit `Timer.periodic` usage -- cancel timers in `dispose()`
- [ ] Use `ValueNotifier` + `ValueListenableBuilder` for micro-state (e.g., selected option index) instead of `setState` on the whole widget

### Image & Asset Optimizations

- [ ] Serve diagrams as WebP format (30-50% smaller than PNG)
- [ ] Cap diagram resolution at 800px width
- [ ] Use `CachedNetworkImage` for all network images (already done)
- [ ] Pre-render LaTeX on server and serve as SVG for complex formulas
- [ ] Bundle Lottie animations at < 50KB each
- [ ] Use SVG for all icons that scale (already using `flutter_svg`)

### Memory Optimizations

- [ ] Implement LRU cache for question objects (max 50 in memory)
- [ ] Dispose animation controllers in `dispose()` method
- [ ] Use `AutoDispose` Riverpod providers where possible
- [ ] Limit knowledge graph to visible nodes + 1 level of neighbors
- [ ] Profile with `flutter run --profile` and check for memory leaks

### Network Optimizations

- [ ] Batch API calls where possible (don't make N requests for N items)
- [ ] Use HTTP/2 multiplexing (Dio supports this)
- [ ] Implement request deduplication for identical concurrent requests
- [ ] Set appropriate cache-control headers for static content
- [ ] Use SignalR's built-in reconnection with exponential backoff
- [ ] Compress outgoing event payloads (gzip for sync batches)

### Accessibility Optimizations

- [ ] All interactive elements have `Semantics` labels
- [ ] Touch targets are minimum 48x48dp (Material guidelines)
- [ ] Color contrast ratios meet WCAG AA (4.5:1 for text, 3:1 for large text)
- [ ] Support dynamic type / text scaling up to 1.5x
- [ ] Test with TalkBack (Android) and VoiceOver (iOS)
- [ ] Ensure focus order is logical for screen reader navigation
- [ ] Provide text alternatives for all charts and graphs

### RTL-Specific Optimizations

- [ ] Use `Directionality` widget awareness -- never hardcode left/right
- [ ] Use `EdgeInsetsDirectional` instead of `EdgeInsets` for start/end padding
- [ ] Use `TextAlign.start` / `TextAlign.end` instead of left/right
- [ ] Mirror icons that have directional meaning (back arrow, progress direction)
- [ ] Test complete flows in Hebrew, Arabic, and English
- [ ] Ensure gesture directions do not conflict with RTL swipe-back navigation

### Testing Optimizations

- [ ] Widget tests for all custom input components (OptionTile, AnswerInput, MathText)
- [ ] Golden tests for question card layouts (Hebrew + Arabic + English)
- [ ] Integration tests for session lifecycle (start -> answer -> feedback -> end)
- [ ] Performance tests: measure frame rates during animations
- [ ] Offline scenario tests: queue events, reconnect, verify sync

---

## Appendix A: RTL Layout Considerations for CENA

Since CENA's primary locale is Hebrew (RTL), and Arabic is also supported:

| Element | LTR (English) | RTL (Hebrew/Arabic) | Flutter Handling |
|---------|---------------|---------------------|-----------------|
| Text alignment | Left | Right | Automatic with `Directionality` |
| Navigation drawer | Opens from left | Opens from right | Automatic |
| Back button | Left side of app bar | Right side of app bar | Automatic |
| Progress bar fill | Left to right | Right to left | Use `Directionality` aware widget |
| Swipe-back gesture | Left edge swipe | Right edge swipe | Automatic on iOS |
| FAB position | Bottom-right | Bottom-left | Automatic with `Directionality` |
| MCQ option labels | A, B, C, D | Alef, Bet, Gimel, Dalet | Already implemented in OptionTile |
| List leading icons | Left | Right | Automatic with `ListTile` |
| Slider direction | Left=min, Right=max | Right=min, Left=max | Automatic |

## Appendix B: Design Token Quick Reference

Already defined in `app_config.dart`:

| Token | Value | Usage |
|-------|-------|-------|
| `SpacingTokens.xs` | 4dp | Tiny gaps |
| `SpacingTokens.sm` | 8dp | Small gaps, chip padding |
| `SpacingTokens.md` | 16dp | Standard padding, card margins |
| `SpacingTokens.lg` | 24dp | Section spacing |
| `SpacingTokens.xl` | 32dp | Major section breaks |
| `SpacingTokens.xxl` | 48dp | Page-level spacing |
| `RadiusTokens.sm` | 4dp | Subtle rounding |
| `RadiusTokens.md` | 8dp | Input fields |
| `RadiusTokens.lg` | 12dp | Cards, buttons |
| `RadiusTokens.xl` | 16dp | Modal sheets, large cards |
| `RadiusTokens.full` | 999dp | Pill shapes, circles |
| `AnimationTokens.fast` | 150ms | Micro-interactions |
| `AnimationTokens.normal` | 300ms | Transitions |
| `AnimationTokens.slow` | 600ms | Emphasis animations |
| `AnimationTokens.celebration` | 1000ms | XP/achievement pop-ups |

---

*Research compiled 2026-03-31. Sources: existing CENA codebase analysis, Material Design 3 guidelines, Apple Human Interface Guidelines, Flutter documentation, NNG mobile UX research, educational technology interaction design literature.*
