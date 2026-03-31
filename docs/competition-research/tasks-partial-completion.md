# Partial Feature Completion Tasks

> **Source:** feature-verification-report.md — 16 features with backend/data but missing UI or logic
> **Sprint Goal:** Turn existing backend investments into user-facing value
> **Total Estimate:** 30-42 weeks (parallelizable across 3-4 devs)
> **Expected Impact:** Converts "we have the data" into "users can see and use it"

---

## Quick Wins — Small (1-3 weeks each)

### PAR-001: Mastery % Per Topic List View
**Feature: M2 | Effort: S (1-2 weeks)**

**What exists:** `knowledge_graph_notifier.dart` tracks P(Known) per concept. Mastery overlays on graph.

**What's missing:** A simple list/grid view showing mastery % per topic outside the knowledge graph.

**Acceptance Criteria:**
- [ ] New "My Mastery" section on profile or dashboard screen
- [ ] List view: topic name, mastery %, color-coded progress bar (red <40%, yellow 40-70%, green >70%)
- [ ] Sortable by: mastery % (ascending for weakest first), alphabetical, last studied
- [ ] Tap topic to navigate to knowledge graph node
- [ ] Filterable by subject (Math, Physics, Chemistry, etc.)
- [ ] Pull-to-refresh updates from SRS state

**Subtasks:**
1. Create `mastery_list_widget.dart` — reads from `KnowledgeGraphNotifier`
2. Color-coded progress bar component
3. Sort/filter controls
4. Tap-to-navigate to knowledge graph node
5. Add section to profile screen

---

### PAR-002: Learning Time Display
**Feature: M6 | Effort: S (1-2 weeks)**

**What exists:** Session timestamps captured in session models.

**What's missing:** Prominent time-spent display on profile/dashboard.

**Acceptance Criteria:**
- [ ] "Time Studied" card on profile screen
- [ ] Shows: today, this week, this month, all time
- [ ] Daily breakdown (bar chart: Mon-Sun)
- [ ] Comparison to personal average ("20% more than your average!")
- [ ] Comparison to daily goal (if set during onboarding)

**Subtasks:**
1. Aggregate session durations by day/week/month
2. Time studied card widget
3. Weekly bar chart widget
4. Goal comparison logic
5. Add to profile screen

---

### PAR-003: Growth Mindset Messaging
**Feature: H9 | Effort: S (1-2 weeks)**

**What exists:** Some encouragement in UI. AI tutor functional.

**What's missing:** Systematic growth mindset language in AI tutor responses and session feedback.

**Acceptance Criteria:**
- [ ] AI tutor system prompt includes growth mindset instructions:
  - "Celebrate effort, not just correctness"
  - "Frame mistakes as learning opportunities"
  - "Use student's name"
  - "Say 'not yet' instead of 'wrong'"
- [ ] Post-wrong-answer messages rotate through growth mindset variants:
  - "Great attempt! Let's look at this from another angle"
  - "Mistakes help your brain grow stronger"
  - "You're on the right track — try one more time"
- [ ] Post-session summary highlights effort: "You tackled 3 hard problems today"
- [ ] Streak messaging: "Your dedication is building real knowledge"

**Subtasks:**
1. Update AI tutor system prompt with growth mindset guidelines
2. Create wrong-answer message pool (10+ variants)
3. Post-session effort summary logic
4. Streak celebration copy updates

---

### PAR-004: Micro-Lesson Mode Label
**Feature: A12 | Effort: S (1 week)**

**What exists:** Session architecture supports configurable lengths (5/10/15/20 min from onboarding).

**What's missing:** Explicit "Quick Review" or "Micro-Lesson" mode entry point.

**Acceptance Criteria:**
- [ ] "Quick Review" button on home screen (5-min session, SRS-driven)
- [ ] Uses existing session architecture with 5-min config
- [ ] Prioritizes overdue SRS reviews
- [ ] Distinct from "Start Session" (which uses onboarding-set duration)
- [ ] Badge: "Speed Learner" for completing 10 quick reviews

**Subtasks:**
1. Add "Quick Review" button to home screen
2. Configure 5-min SRS-priority session preset
3. "Speed Learner" badge definition

---

## Medium Tasks — (2-4 weeks each)

### PAR-005: Forgetting Curve Visualization
**Feature: B2 | Effort: S (2-3 weeks)**

**What exists:** `srs_state.dart` tracks all FSRS data (stability, difficulty, retrievability per card).

**What's missing:** A visual chart showing the forgetting curve and optimal review timing.

**Acceptance Criteria:**
- [ ] "Memory Strength" screen accessible from SRS review or profile
- [ ] Per-concept view: forgetting curve chart (time on X, recall probability on Y)
- [ ] Shows: current retrievability %, next optimal review date, stability value
- [ ] Color zones: green (>90% recall), yellow (70-90%), red (<70%)
- [ ] Aggregate view: "X concepts due for review, Y at risk of forgetting"
- [ ] Educational tooltip: "This shows how your memory fades over time — reviews at the right moment keep it strong"

**Subtasks:**
1. Forgetting curve chart widget (fl_chart or custom painter)
2. Per-concept memory strength detail view
3. Aggregate "memory health" summary card
4. Educational tooltips
5. Navigation from SRS review and profile screens

---

### PAR-006: Weak Areas Screen
**Feature: F5, M3 | Effort: S (2-3 weeks)**

**What exists:** `adaptive_difficulty_service.dart` tracks error types (conceptual, procedural, careless, notation, incomplete). Rolling accuracy per topic.

**What's missing:** Dedicated "Your Weak Areas" screen surfacing this data with actionable remediation.

**Acceptance Criteria:**
- [ ] "Weak Areas" screen accessible from dashboard/profile
- [ ] Lists topics where mastery < 50% OR rolling accuracy < 60%
- [ ] For each weak topic: mastery %, common error type, last practiced date
- [ ] Error type breakdown: "You make mostly conceptual errors in algebra"
- [ ] "Practice Now" button per topic → starts targeted remediation session
- [ ] Sorted by weakness severity (most struggling first)
- [ ] Empty state: "No weak areas detected! Keep it up" (celebration)

**Subtasks:**
1. Weak area detection algorithm (mastery threshold + accuracy threshold)
2. Weak areas list UI with error type breakdown
3. "Practice Now" deep link to targeted session
4. Error type icons/labels (conceptual, procedural, careless)
5. Empty state celebration
6. Navigation from profile/dashboard

---

### PAR-007: Frustration Detection & Intervention
**Feature: H5 | Effort: S (2-3 weeks)**

**What exists:** `flow_monitor_service.dart` tracks 5 signals including inverse fatigue and challenge/skill balance.

**What's missing:** Explicit frustration threshold that triggers intervention.

**Acceptance Criteria:**
- [ ] Frustration detection: 3+ wrong answers in a row on same concept, OR rapid answer attempts (<2s per question), OR session time > 2x average without progress
- [ ] Gentle intervention popup: "This is a tough one! Want to: (1) Get a hint, (2) Try a different topic, (3) Take a 2-min break?"
- [ ] If break chosen: breathing exercise or stretch prompt (30s)
- [ ] If topic switch: adaptive interleaving moves to different concept
- [ ] Frustration events logged for analytics
- [ ] Configurable sensitivity in settings (off / low / medium / high)

**Subtasks:**
1. Frustration detection rules engine (3 triggers)
2. Intervention popup UI (3 options)
3. Break activity screen (breathing/stretch)
4. Topic switch integration with adaptive interleaving
5. Sensitivity setting in preferences
6. Analytics: frustration_detected, intervention_shown, intervention_choice

---

### PAR-008: AI Tutor Personality Enhancement
**Feature: F6 | Effort: S (2-3 weeks)**

**What exists:** AI tutor functional with Socratic method. Tutor state manages conversation.

**What's missing:** Warm, named personality. Growth mindset prompts. Effort celebration.

**Acceptance Criteria:**
- [ ] AI tutor has a name (configurable or default, e.g., "Noor" for Arabic/Hebrew market)
- [ ] Tutor uses student's first name in responses
- [ ] Tutor personality consistent across sessions (remembers prior topics discussed)
- [ ] Tone settings: Encouraging (default), Neutral, Challenging
- [ ] Effort-based praise: "I can see you really thought about that one"
- [ ] Mistake reframing: "Not quite — but your approach was interesting because..."
- [ ] Session opener references prior progress: "Last time you mastered quadratic equations — ready for the next challenge?"
- [ ] Tutor avatar/icon consistent in chat UI

**Subtasks:**
1. System prompt engineering (personality, tone, name usage, growth mindset templates)
2. Tone selector in settings (encouraging/neutral/challenging)
3. Session context: reference prior topics from conversation history
4. Tutor avatar design + implementation in chat bubbles
5. A/B test warm vs neutral tone on engagement metrics

---

### PAR-009: Neurodiverse-First Design Branding
**Feature: N5 | Effort: S (2-3 weeks)**

**What exists:** `accessibility_service.dart` already has dyslexia font, reduced motion, enlarged touch targets, high contrast, color-blind mode.

**What's missing:** Not branded as neurodiverse-friendly. No ADHD-specific mode. No marketing of these features.

**Acceptance Criteria:**
- [ ] "Accessibility & Learning Needs" section in settings (renamed from generic "Accessibility")
- [ ] Grouped presets: "Dyslexia-Friendly", "ADHD Focus Mode", "Low Vision", "Custom"
- [ ] ADHD Focus Mode preset: reduced motion + simplified UI + shorter sessions (5 min max) + fewer choices per screen
- [ ] Dyslexia-Friendly preset: OpenDyslexic font + increased spacing + cream background
- [ ] Feature discovery: mention in onboarding ("Do you have any learning preferences?")
- [ ] App Store description mentions neurodiverse support

**Subtasks:**
1. Rename accessibility section, add learning needs presets
2. ADHD Focus Mode preset configuration
3. Dyslexia-Friendly preset configuration
4. Onboarding learning preferences step
5. App Store description update copy

---

## Larger Tasks — (3-5 weeks each)

### PAR-010: Personalized Learning Path UI
**Feature: F2 | Effort: M (3-4 weeks)**

**What exists:** MasteryState + Concept models with prerequisiteIds. Knowledge graph with dependencies.

**What's missing:** "Recommended next lesson" UI orchestration. Visual learning path.

**Acceptance Criteria:**
- [ ] "Your Learning Path" screen showing recommended sequence
- [ ] Path derived from knowledge graph: prerequisites → current level → next concepts
- [ ] Each node shows: topic name, mastery %, estimated time, locked/unlocked status
- [ ] "Start Next Lesson" button at current position
- [ ] Branching paths when multiple topics are available at same level
- [ ] "Why this order?" tooltip explaining prerequisite logic
- [ ] Re-renders when mastery changes (unlock new paths)

**Subtasks:**
1. Path generation algorithm (topological sort of knowledge graph filtered by mastery)
2. Learning path UI (vertical timeline or horizontal scroll)
3. Node states: locked, available, in-progress, mastered
4. "Start Next Lesson" deep link
5. Branching path visualization
6. "Why this order?" tooltip with prerequisite explanation
7. Dynamic re-rendering on mastery change

---

### PAR-011: Consolidated Progress Dashboard
**Feature: M1 | Effort: M (3-4 weeks)**

**What exists:** Profile screen + gamification screen exist separately. Data spread across multiple notifiers.

**What's missing:** A single consolidated dashboard view.

**Acceptance Criteria:**
- [ ] "Dashboard" tab (replace or augment current home screen)
- [ ] At-a-glance cards:
  - Streak (current count + freeze status)
  - Today's progress (sessions done / goal, XP earned)
  - Weekly XP (bar chart)
  - Mastery summary (topics mastered / total, top 3 weak areas)
  - SRS health (reviews due, overdue count)
  - League position (if enrolled)
- [ ] Each card tappable → deep link to detail screen
- [ ] Scroll for more: recent activity, badges earned, quest progress
- [ ] Pull-to-refresh

**Subtasks:**
1. Dashboard layout (card grid/list)
2. Streak card widget (streak count + freeze indicator)
3. Daily progress card (sessions + XP)
4. Weekly XP bar chart card
5. Mastery summary card (top 3 weak areas)
6. SRS health card (due + overdue)
7. League position card
8. Recent activity list
9. Deep link navigation from each card
10. Integration with all existing state notifiers

---

### PAR-012: Historical Progress Charts
**Feature: M7 | Effort: S (2-3 weeks)**

**What exists:** Session history with timestamps, scores, topics.

**What's missing:** Charts visualizing progress over time.

**Acceptance Criteria:**
- [ ] "Progress Over Time" section on dashboard or profile
- [ ] Charts:
  - Mastery trend (line chart: overall mastery % over weeks/months)
  - XP earned per week (bar chart)
  - Session count per week (bar chart)
  - Accuracy trend (line chart: % correct over time)
- [ ] Time range selector: 1 week, 1 month, 3 months, all time
- [ ] Annotations for milestones (badge earned, level up, boss battle won)

**Subtasks:**
1. Data aggregation service (mastery/XP/sessions/accuracy by time period)
2. Line chart widget (mastery trend, accuracy trend)
3. Bar chart widget (weekly XP, session count)
4. Time range selector
5. Milestone annotations on charts

---

### PAR-013: Placement Diagnostic Test
**Feature: D1 | Effort: M (4-5 weeks)**

**What exists:** Onboarding has goal setting + pace selection. Knowledge graph with concept prerequisites.

**What's missing:** Adaptive diagnostic assessment on first use to place students at correct level.

**Acceptance Criteria:**
- [ ] "Let's see what you know" screen after onboarding goals
- [ ] Adaptive test: 10-20 questions, starts at middle difficulty
- [ ] Computer Adaptive Testing (CAT): correct → harder, incorrect → easier
- [ ] Covers selected subject(s) from onboarding
- [ ] Results: places student on knowledge graph (unlocks mastered nodes, focuses on frontier)
- [ ] "Your starting point" summary: "You're solid in X, let's work on Y"
- [ ] Skip option: "I'd rather start from the beginning"
- [ ] Takes 5-10 minutes max

**Subtasks:**
1. CAT algorithm (item selection based on current ability estimate)
2. Question pool tagged by difficulty and topic
3. Diagnostic test session UI (progress bar, encouraging tone)
4. Results screen with knowledge graph placement visualization
5. Knowledge graph node unlocking based on results
6. Skip option with default placement
7. Integration with onboarding flow (after goal setting, before first session)

---

### PAR-014: AI Practice Problem Generation
**Feature: F9 | Effort: M (3-4 weeks)**

**What exists:** Session question flow with multiple question types. Adaptive difficulty service.

**What's missing:** On-demand "Generate practice problems" for a specific topic/difficulty.

**Acceptance Criteria:**
- [ ] "Practice More" button on topic detail or post-session screen
- [ ] User selects: topic, difficulty (easy/medium/hard), count (5/10/20)
- [ ] AI generates fresh problems (not from existing bank) via LLM
- [ ] Generated problems include: question, answer, step-by-step solution, hints
- [ ] Problems integrate with FSRS (added to review queue if answered wrong)
- [ ] Generated problems saved for re-use (build content bank over time)
- [ ] Quality gate: generated problems validated against answer before showing

**Subtasks:**
1. Problem generation prompt engineering (per subject, difficulty, question type)
2. "Practice More" entry points (topic detail, post-session)
3. Configuration UI (topic, difficulty, count)
4. Generation service (LLM call → structured problem)
5. Quality validation (verify answer is correct before presenting)
6. FSRS integration for wrong answers
7. Generated problem storage for re-use

---

## Summary Table

| Task ID | Feature | Effort | What's Being Built |
|---------|---------|--------|--------------------|
| PAR-001 | M2 Mastery list | S (1-2w) | Topic mastery % list view with sort/filter |
| PAR-002 | M6 Time display | S (1-2w) | "Time Studied" card with daily/weekly breakdown |
| PAR-003 | H9 Growth mindset | S (1-2w) | Systematic growth mindset language in AI + feedback |
| PAR-004 | A12 Micro-lesson | S (1w) | "Quick Review" button (5-min SRS session) |
| PAR-005 | B2 Forgetting curve | S (2-3w) | Memory strength chart per concept |
| PAR-006 | F5/M3 Weak areas | S (2-3w) | "Your Weak Areas" screen with remediation |
| PAR-007 | H5 Frustration | S (2-3w) | Frustration detection + gentle intervention |
| PAR-008 | F6 AI personality | S (2-3w) | Named, warm AI tutor with tone settings |
| PAR-009 | N5 Neurodiverse | S (2-3w) | Branded presets: Dyslexia, ADHD, Low Vision |
| PAR-010 | F2 Learning path | M (3-4w) | "Your Learning Path" visual sequence UI |
| PAR-011 | M1 Dashboard | M (3-4w) | Consolidated progress dashboard |
| PAR-012 | M7 History charts | S (2-3w) | Mastery/XP/accuracy trend charts |
| PAR-013 | D1 Placement test | M (4-5w) | Adaptive diagnostic on first use |
| PAR-014 | F9 Practice gen | M (3-4w) | AI-generated practice problems on demand |

**Total: 14 tasks | ~30-42 weeks | Parallelizable to ~10-14 weeks with 3 devs**
