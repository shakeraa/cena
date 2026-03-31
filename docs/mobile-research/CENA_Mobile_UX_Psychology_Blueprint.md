# CENA Mobile UX Psychology Master Blueprint

> **Date:** 2026-03-31
> **Status:** Synthesized from 10 deep-research agents (~10,000 lines of research)
> **Scope:** Flutter mobile app for Israeli Bagrut exam preparation, ages 14-18
> **Architecture:** .NET actor-based (Akka.NET), NATS messaging, CQRS/ES

---

## Table of Contents

1. [Research Sources Overview](#1-research-sources-overview)
2. [Core Design Principles](#2-core-design-principles)
3. [Master Actor Architecture](#3-master-actor-architecture)
4. [Onboarding & First-Time UX](#4-onboarding--first-time-ux)
5. [Session Architecture & Flow State](#5-session-architecture--flow-state)
6. [Adaptive Difficulty & Learning Science](#6-adaptive-difficulty--learning-science)
7. [Habit Formation & Retention](#7-habit-formation--retention)
8. [Gamification System](#8-gamification-system)
9. [Microinteractions & Emotional Design](#9-microinteractions--emotional-design)
10. [Social & Community Features](#10-social--community-features)
11. [Cognitive Load & Progressive Disclosure](#11-cognitive-load--progressive-disclosure)
12. [Mobile UX Patterns & Navigation](#12-mobile-ux-patterns--navigation)
13. [Ethical Guardrails & Digital Wellbeing](#13-ethical-guardrails--digital-wellbeing)
14. [Cross-Document Conflict Resolutions](#14-cross-document-conflict-resolutions)
15. [Implementation Roadmap](#15-implementation-roadmap)
16. [KPI Dashboard](#16-kpi-dashboard)
17. [Anti-Pattern Registry](#17-anti-pattern-registry)

---

## 1. Research Sources Overview

| # | Domain | Document | Lines | Key Theorists |
|---|--------|----------|-------|---------------|
| 1 | Habit Loops & Hook Model | `docs/habit-loops-hook-model-research.md` | ~700 | Nir Eyal, BJ Fogg |
| 2 | Gamification & Motivation | `docs/gamification-motivation-research.md` | ~850 | Yu-kai Chou, Deci & Ryan |
| 3 | Cognitive Load & Progressive Disclosure | `docs/cognitive-load-progressive-disclosure-research.md` | ~900 | Sweller, Cowan, Paivio |
| 4 | Flow State & Engagement | `docs/flow-state-design-research.md` | ~850 | Csikszentmihalyi, Cal Newport |
| 5 | Social Learning & Community | `docs/social-learning-research.md` | ~1000 | Bandura, Festinger |
| 6 | Microinteractions & Emotional Design | `docs/design/microinteractions-emotional-design.md` | ~1100 | Dan Saffer, Don Norman |
| 7 | Onboarding & First-Time UX | `docs/onboarding-first-time-ux-research.md` | ~1050 | Carroll & Carrithers |
| 8 | Learning Science & SRS | `docs/learning-science-srs-research.md` | ~1200 | Ebbinghaus, Bjork, Mayer |
| 9 | Mobile UX Patterns | `docs/mobile-research/mobile-ux-patterns-research.md` | ~850 | Ergonomics, Flutter |
| 10 | Ethical Persuasion & Wellbeing | `docs/ethical-persuasion-digital-wellbeing-research.md` | ~1648 | Cialdini, COPPA/FERPA |

**Total research corpus: ~10,148 lines across 10 documents.**

---

## 2. Core Design Principles

These 10 non-negotiable principles emerge from the intersection of all research:

### Principle 1: Time-to-Value < 30 Seconds
From app open to first question answered. No wall of forms, no tutorials, no account creation gates. Insert a "try question" step before signup. (Doc 7)

### Principle 2: ZPD Dynamic Difficulty — Always in the Flow Channel
Target P(correct) = 0.55-0.75, dynamically adjusted by focus level. Never too easy (boredom), never too hard (anxiety). (Docs 4, 8)

### Principle 3: Cognitive Load Budget — 6-8 Elements Max Per Screen
Every screen must pass the Cowan 4±1 audit. Layer information via progressive disclosure: core action → context → deep dive → meta. (Doc 3)

### Principle 4: Celebrate Proportionally — Never Over-Celebrate
5-tier celebration system scaled to achievement significance. Correct answer = subtle chime. Course mastered = full fireworks. (Doc 6)

### Principle 5: Ethical by Default — No Dark Patterns, Hard Wellbeing Limits
9 PM-7 AM notification quiet hours. 90/120/180-min study limits. No confirmshaming. No fake social proof. No loot boxes. (Doc 10)

### Principle 6: Social Proof Without Shame
Aggregate class stats only. No named leaderboards. Lateral peer models (similar mastery). All social features opt-in. (Docs 5, 10)

### Principle 7: Spaced Practice Over Cramming
FSRS-based review scheduling. Interleaved topics (never blocked practice). Behavioral grading (not self-assessment). (Doc 8)

### Principle 8: Habits Through Quality, Not Zombie Sessions
Quality-gated streaks (3+ questions, >5s avg response time). Momentum Meter alternative for anxiety-prone students. (Docs 1, 10)

### Principle 9: Flow State is Sacred
During FocusLevel.Flow: hide all gamification chrome, suppress notifications, skip microbreaks, defer celebrations to post-session. (Doc 4)

### Principle 10: Progressive Feature Discovery
Training wheels mode for first 3 sessions. Features unlock gradually (hints day 3, methodology day 7, knowledge graph day 15). (Docs 3, 7)

---

## 3. Master Actor Architecture

### Proposed Actor Hierarchy

```
StudentActor (root, long-lived per student)
├── RoutineProfileActor (Doc 1)
│   └── Learns daily patterns: wake, commute, study, sleep times
│   └── Feeds: OutreachSchedulerActor notification timing
│
├── GamificationIntensityActor (Doc 2)
│   └── Age/tenure-based gamification surface allocation
│   └── Emits: GamificationIntensityShifted events
│   └── Controls: minimal → standard → full intensity
│
├── ScaffoldingActor (Doc 3)
│   └── Tracks sessionsCompleted, currentScaffoldLevel
│   └── Controls: feature visibility (hints, methodology, graph)
│   └── Levels: L0=training wheels, L1=intermediate, L2=full
│
├── DigitalWellbeingEnforcer (Doc 10)
│   └── Daily minutesStudied tracking
│   └── Enforces: soft (90min), firm (120min), hard (180min) limits
│   └── Monitors: anxiety signals, zombie sessions
│
├── StreakAnxietyDetectorActor (Docs 1, 10)
│   └── Flags: <30s sessions, 2AM sessions, declining accuracy
│   └── Recommends: Momentum Meter switch, intensity reduction
│
├── OnboardingProgressTracker (Doc 7)
│   └── Milestones: try_question, diagnostic, graph_revealed
│   └── Emits: OnboardingMilestoneReached for funnel analytics
│
├── SRSActor [Phase 3] (Doc 8)
│   └── FSRS state per concept (Stability, Difficulty, Retrievability)
│   └── Messages: ReviewCompleted → SpacedReviewScheduled
│
├── MetacognitionActor (Doc 8)
│   └── Confidence calibration (predicted vs actual correctness)
│   └── Calibration graph data for student self-awareness
│
├── PeerProgressNarrativeActor (Doc 5)
│   └── Weekly anonymized success stories filtered by similarity
│
└── LearningSessionActor (per-session, child of StudentActor)
    ├── FlowMonitorActor (Doc 4)
    │   └── Per-question FocusLevel analysis
    │   └── Emits: StudentEnteredFlow / StudentDriftingFromFlow
    │   └── Feeds: dynamic ZPD P(correct) targeting
    │
    ├── SessionArcOrchestratorActor (Doc 4)
    │   └── Phase management: warm-up → core → cool-down
    │   └── Timing: K-2=5min, teens=15min, adults=25min
    │
    ├── InterleaveSchedulerActor (Doc 8)
    │   └── Topic diversity: switch every 3-4 questions
    │   └── Prevents blocked practice
    │
    └── CelebrationOrchestratorActor (Doc 6)
        └── Determines celebration tier (1-5)
        └── Sequences: animation + haptic + audio
        └── Respects: flow state (defers in Flow)

--- System-Level Actors ---

OutreachSchedulerActor
├── Respects: routineProfile.quietHours (9PM-7AM)
├── Respects: wellbeingLimits
├── Respects: ethicalGuards
└── Sends: class activity, review reminders, welcome-back

EthicalGuardActor (Doc 10)
├── Validates all outgoing messages against dark-pattern checklist
├── Blocks: shame language, fake urgency, late-night streaks
└── Audit trail for compliance

PeerSolutionRecommenderActor (Doc 5)
├── Post-answer: queries similar-mastery peer solutions
├── Ranks by helpfulness rating
└── Lateral modeling only (±0.1 P(known))

TutoringMatchmakerActor (Doc 5)
├── Hourly batch: expert (P≥0.90) ↔ struggling (5+ consecutive wrong)
├── Async voice/text explanations
└── Both parties earn XP

StudyGroupCoordinatorActor (Doc 5)
├── Group challenges (3-5 members)
├── Collective progress tracking
└── Teacher-created for <13, student-created for 13+
```

---

## 4. Onboarding & First-Time UX

### Critical Path: Time-to-Value < 30 Seconds

```
Page 1: Welcome (3s)
  → "Learn smarter for Bagrut" + Start button
  
Page 2: Try a Question (15-20s) ★ KEY INNOVATION
  → One real math MCQ
  → Student answers → instant XP animation + celebration
  → Proves: app works, it's interactive, not boring
  → First response feeds diagnostic difficulty calibration

Page 3: Subject Selection
  → Grid of Bagrut subjects with icons
  → Multi-select with visual feedback

Page 4: Grade & Units
  → Grade selector → relevant unit chips
  
Page 5: Goal Setting (Commitment Device)
  → "What's your goal?" — Pass Bagrut / Improve grades / Get ahead / University prep
  → Daily time commitment: 10 / 15 / 20 / 25 min
  → Self-set goals → 2x stronger commitment (Locke & Latham)

Page 6: Signup (Delayed Until After Value)
  → Email/Google/Apple — minimal fields
  → Runs in parallel with background account creation

Page 7: Diagnostic "Discovery Tour" (3-5 min)
  → 10-15 adaptive questions framed as exploration, not test
  → "Finding your starting point" — never "testing your level"
  → Each correct: green node on mini knowledge graph
  → Progress bar: "Mapping your knowledge..."

Page 8: Knowledge Graph Reveal ★ AHA MOMENT
  → Full-screen animated graph: nodes fade in, mastered = green, weak = yellow
  → 5-second reveal animation with ambient audio
  → "Your learning map is ready!"
  → Correlates with 3-5x higher Day-7 retention
```

### Progressive Feature Discovery (Not Tutorial)

| Session | Feature Unlocked | How |
|---------|-----------------|-----|
| 1-2 | Questions, XP, basic feedback | Available from start |
| 3 | Hint button | Pulses after 10s inactivity |
| 5 | Streak mechanic | Introduced with celebration |
| 7 | Methodology switch | Bottom sheet explanation |
| 10 | Study groups (if social) | Notification + badge |
| 15 | Knowledge graph full access | "New" badge on Map tab |

### Re-Onboarding (Returning After Absence)

- **< 7 days:** "Welcome back! Here's where you left off" + one-tap resume
- **7-30 days:** Quick recap card + skill decay visualization + "refresher session" (5 easy questions to rebuild confidence)
- **> 30 days:** Abbreviated diagnostic (5 questions) to recalibrate + "Your knowledge map may have shifted" + re-personalization option

---

## 5. Session Architecture & Flow State

### Three-Phase Session Arc

```
┌─────────────────────────────────────────────────────────────┐
│                    LEARNING SESSION (15-25 min)              │
│                                                              │
│  WARM-UP (3-5 min)    CORE CHALLENGE (12-20 min)   COOL-DOWN│
│  ┌──────────┐         ┌────────────────────┐       ┌───────┐│
│  │ Review    │         │ ZPD Targeting      │       │ End   ││
│  │ mastered  │────────▶│ Interleaved topics │──────▶│ on a  ││
│  │ concepts  │         │ Flow monitoring    │       │ WIN   ││
│  │ P=0.80-90 │         │ P=focus-adjusted   │       │P=0.85 ││
│  └──────────┘         └────────────────────┘       └───────┘│
│                                                              │
│  Confidence           Challenge + Learning           Satisfy │
│  builder              Deep engagement                Memory  │
└─────────────────────────────────────────────────────────────┘
```

### Dynamic ZPD Targeting by Focus Level

| FocusLevel | Target P(correct) | Rationale |
|------------|-------------------|-----------|
| Flow (score ≥0.8) | 0.55 - 0.65 | Student thriving — push harder |
| Engaged | 0.65 - 0.75 | Standard ZPD — productive struggle |
| Drifting | 0.75 - 0.85 | Ease pressure — rebuild attention |
| Fatigued | 0.85 - 0.90 | Confidence boost — prevent dropout |

### Immersive UI Rules During Core Challenge

**SHOW:**
- Question stem + answer options (60-70% viewport)
- Submit button (bottom thumb zone)
- 4px mastery progress bar (top, ambient)
- Pause icon (top-right, subtle)

**HIDE:**
- Bottom navigation bar
- XP counter, badges, streak display
- Leaderboard, social feed
- Timer (clock kills flow — use progress bar: X of N)
- All notifications

**On Correct:** Green pulse, bar advances, queue XP for post-session  
**On Wrong:** Soft orange glow, explanation as bottom sheet (not popup)

### Flow State Protection Rules

When `FocusLevel.Flow` (score ≥ 0.8):
1. DO NOT suggest microbreaks
2. DO NOT show notifications
3. DO NOT emit XP popups (queue for post-session summary)
4. DO NOT display gamification chrome
5. DO let them keep going past planned session length
6. AFTER session: show "You were in flow state for [N] minutes! That's when deep learning happens."

---

## 6. Adaptive Difficulty & Learning Science

### Spaced Repetition: HLR → FSRS Migration Path

| Phase | Timeline | System | Action |
|-------|----------|--------|--------|
| Current | Now | HLR (Half-Life Regression) | Baseline, working |
| Phase 2 | Q2 2026 | Shadow FSRS | Run FSRS in parallel, log predictions |
| Phase 3 | Q3 2026 | A/B Test | 10% cohort FSRS vs HLR, 8 weeks |
| Phase 4 | Q4 2026 | Full FSRS | Replace HLR if FSRS wins (expect +10-15% retention) |

### Behavioral Grading (Not Self-Assessment)

Students are unreliable self-assessors (Dunning-Kruger). Map behavior to SRS grade:

| Behavior | Grade | SRS Action |
|----------|-------|------------|
| Correct + fast (< 10th percentile) | 4 (Easy) | Long interval |
| Correct + normal + no hints | 3 (Good) | Standard interval |
| Correct + hints or slow | 2 (Hard) | Short interval |
| Wrong + hints didn't help | 1 (Again) | Reset — review soon |

### Interleaving Schedule

Never blocked practice. Within each session:
```
Queue: [Quadratic, Quadratic, Linear, Polynomial, Quadratic, Trig, Linear, ...]
```
- Switch topic every 3-4 questions
- Forces "which strategy applies?" discrimination
- Expected: +15-20% improvement in transfer of learning (Rohrer & Taylor, 2007)

### Progressive Worked-Solution Fading

| P(Known) | Solution Display |
|----------|-----------------|
| < 0.30 | Full solution, all steps visible |
| 0.30-0.60 | Steps 3-5 hidden: "Can you complete?" |
| 0.60-0.85 | Only answer shown, student re-derives |
| > 0.85 | No solution — full independent practice (Feynman mode) |

### Scaffolding by Mastery Level

| P(Known) | Question Type | Hints | Difficulty Range |
|----------|--------------|-------|-----------------|
| < 0.30 | MCQ only | Auto-show after 30s | 1-4 |
| 0.30-0.60 | Cued recall (fill-blank) | On-request | 1-7 |
| 0.60-0.85 | Free recall + guided | Minimal | Full range |
| > 0.85 | Explanation required | None | Full range |

---

## 7. Habit Formation & Retention

### Hook Model Implementation

| Hook Stage | CENA Implementation |
|------------|-------------------|
| **Trigger** | External: personalized push at routine-detected study time. Internal: exam anxiety → "I should study" → open app |
| **Action** | Single CTA: "Continue Studying" → auto-loads optimal next concept. Zero decisions. |
| **Variable Reward** | Unpredictable: XP bonus rolls, mystery badge unlocks, "Students Like You" success stories |
| **Investment** | Streaks, knowledge graph growth, peer explanations, customization progress |

### Trigger Migration: External → Internal

- **Weeks 1-4:** Heavy external triggers (push notifications 2-3/day)
- **Weeks 4-8:** Reduce to 1-2/day, increasingly personalized
- **Weeks 8+:** Target 70% internal triggers (student opens app without prompting)
- **Metric:** Trigger Migration Ratio (internal/external opens)

### Quality-Gated Streaks

Streak counts ONLY if:
- ≥ 3 questions answered
- Average response time > 5 seconds
- Not a zombie session (< 30 seconds total)

### Streak Anxiety Management

**Detect:** < 30s sessions, unusual hours (2 AM), accuracy declining despite maintained streaks

**Intervene:**
1. Offer Momentum Meter alternative (7-day rolling %, never reaches zero)
2. Add streak freeze (1 free/week, earn more)
3. Switch to `GamificationIntensity.minimal` with one tap

### Habit Stacking

Detect student routines via session timestamps:
- **Morning:** 3-min review @ wake-time + 15 min
- **Commute:** 5-10 min session (geofence-detected)
- **Evening:** 10-20 min main session @ routine time
- **Before bed:** 2-min review (sleep consolidation, by 9 PM)

---

## 8. Gamification System

### Octalysis Framework — Ethical Allocation

| Core Drive | Weight | Implementation |
|------------|--------|---------------|
| 1. Epic Meaning & Calling | 10% | "Master Bagrut together" class missions |
| 2. Development & Accomplishment | 25% | XP, levels, skill trees, badges |
| 3. Empowerment of Creativity | 10% | Student explanations, peer tutoring |
| 4. Ownership & Possession | 10% | Knowledge graph, customizable themes |
| 5. Social Influence & Relatedness | 15% | Class activity, study groups |
| 6. Scarcity & Impatience | 5% | Limited-time challenges (ethical only) |
| 7. Unpredictability & Curiosity | 10% | Mystery badges, surprise quizzes |
| 8. Loss & Avoidance | 5% | Streak protection (with anxiety escape valve) |
| **White Hat (1-3)** | **45%** | **Positive, empowering** |
| **Neutral (4-5)** | **25%** | **Engagement** |
| **Black Hat (6-8)** | **20%** | **Urgency — ALWAYS with escape valve** |
| **Buffer** | **10%** | **Reserve for experimentation** |

### Age-Stratified Gamification Intensity

| Age Group | Extrinsic Level | Strategy |
|-----------|----------------|----------|
| 12-14 | High | XP 2x multiplier, frequent badges, visible streaks |
| 15-17 | Moderate | XP 1x, meaningful milestones, optional streak |
| 18+ | Low (opt-in) | XP de-emphasized, mastery-focused dashboard |

### Extrinsic → Intrinsic Transition Curve

- **Weeks 1-2:** Heavy XP popups (scaffolding motivation)
- **Weeks 2-8:** Reduce popup frequency, emphasize mastery %
- **Week 9+:** Knowledge graph growth and mastery % are primary indicators, XP secondary
- Implement via `GamificationRotationService` progressive decay per element type

### 5-Tier Celebration System

| Tier | Trigger | Animation | Duration | Audio | Haptic |
|------|---------|-----------|----------|-------|--------|
| 1 | Correct answer | XP float + bounce | 900ms | Soft chime | Light tap |
| 2 | 3-in-a-row | XP + sparkle particles | 1.2s | Ascending chime | Medium |
| 3 | Concept mastered / badge / session complete | Full icon + confetti (50 particles) | 2s | Achievement fanfare | Heavy |
| 4 | Level up / streak 7/14/30 / first session | Full confetti (100 particles) + pulsing glow | 3s | Extended fanfare | Heavy sequence |
| 5 | Course mastered / 100-day streak | Full animated scene + fireworks + personal message | 5s | Celebration soundtrack | Custom pattern |

**Critical Rule:** Celebration magnitude MUST scale with achievement significance. Over-celebrating trivial events trains students to ignore celebrations by question #20.

---

## 9. Microinteractions & Emotional Design

### Don Norman's Three Levels

| Level | CENA Application |
|-------|-----------------|
| **Visceral** (first 50-200ms) | Clean typography, `SubjectColorTokens`, dark theme for 14-18 sophistication (not cartoonish) |
| **Behavioral** (interaction) | Every element responds within 200ms. Smooth 60fps animations. Sense of control. |
| **Reflective** (memory) | "You've mastered 47 concepts." Identity: "You're the kind of person who studies consistently." |

### Animation Tokens

- **Minimum response time:** 150ms (`AnimationTokens.fast`)
- **Standard transition:** 300ms (`AnimationTokens.medium`)
- **Celebration overlay:** 900ms-5s (scaled by tier)
- **Page transition:** 250ms (hero animations between list↔detail)

### Haptic Mapping

| Interaction | Haptic |
|-------------|--------|
| MCQ option tap | `selectionClick()` |
| Submit button | `mediumImpact()` |
| Correct answer | `heavyImpact()` |
| Level-up | `heavyImpact()` × 2 (0ms, 200ms gap) |
| Wrong answer | `lightImpact()` (gentle, not punitive) |

### Sound Design

- Correct: ascending major chord (warm, not startling)
- Wrong: soft descending tone (neutral, not harsh)
- Achievement: fanfare (instrument-based, not chiptune — sophistication for teens)
- Ambient study option: lo-fi instrumental, no lyrics (lyrics compete with math cognition)
- Default: sounds OFF, toggle in settings
- Volume auto-reduces during questions, increases during breaks

### Error/Wrong Answer Emotional Design

**DO:** Soft orange glow → "Let's figure this out" → constructive explanation → try-again option
**DON'T:** Red X + "WRONG!" + buzzer sound → shame → anxiety → dropout

### Gesture Language

| Gesture | Action | Context |
|---------|--------|---------|
| Swipe right | Confident (skip) | Flashcard review |
| Swipe left | Needs review | Flashcard review |
| Swipe up | Skip | Question skip |
| Long press | Bookmark | Any content |
| Double tap | Favorite | Content items |
| Pinch | Zoom | Diagrams, graphs |
| Draw | Handwriting | Math input mode |

---

## 10. Social & Community Features

### Social Feature Architecture (Safety-First)

All social features MUST be:
- Opt-in (never forced)
- Aggregate-only (no named individual comparisons)
- Age-gated (stricter for < 13)
- Moderated (AI pre-filter + community reports + teacher review)

### Feature Set

| Feature | Description | Safety |
|---------|-------------|--------|
| **Class Activity Feed** | "Your class mastered 47 concepts this week" | Aggregate only, no names |
| **"Students Like You" Stories** | Anonymized peer trajectories matched by mastery | No identifying info |
| **Peer Solution Replays** | See how a similar-level peer solved it (after your attempt) | Anonymized, post-attempt only |
| **Peer Tutoring** | Expert (P≥0.90) records explanation for struggling peer (5+ consecutive wrong) | Async, moderated |
| **Study Groups** | 3-5 members, shared weekly challenges | Teacher-created for <13 |
| **Quiz Battles** | Real-time competitive quiz (Kahoot-style, mobile) | Opt-in, improvement-based scoring |

### Social Proof Mechanisms (Ethical Only)

**Allowed:**
- "23 students in your class studied Chapter 3 this week" (real, aggregate)
- "87% of students found this concept challenging at first" (normalizes struggle)
- "Students who mastered Quadratics usually started with Linear Equations" (recommendation)

**Forbidden:**
- Named rankings ("You're #17 in class")
- Fabricated numbers
- Shaming non-participants
- Downward comparison ("Only 3 people scored lower than you")

### Three-Tier Moderation

1. **AI Pre-Filter (Haiku):** Auto-scan all user-generated content for inappropriate language, bullying, PII
2. **Community Reports:** Students flag content → reviewed within 24h
3. **Teacher Review:** Teacher dashboard with moderation queue + override powers

---

## 11. Cognitive Load & Progressive Disclosure

### Four-Layer Progressive Disclosure Model

| Layer | Content | Viewport % | Trigger |
|-------|---------|------------|---------|
| **1: Core Action** | Question + options + submit | 60-70% | Always visible |
| **2: Context** | Hints, difficulty badge, methodology | 15-20% | Tap to reveal |
| **3: Deep Dive** | Worked solution, theory, error analysis | 50-60% (bottom sheet) | After answer |
| **4: Meta** | XP, badges, knowledge graph, analytics | Full screen | Separate tabs |

### Screen Element Budget

**Maximum 6-8 visible elements per screen** (Cowan's 4±1 chunks)

**Audit every screen:**
1. Count all labels, buttons, indicators, text blocks
2. If > 8: move lowest-priority items to Layer 2 (tap-to-reveal)
3. If > 12: split into multiple screens

### Contiguity Principle (NEVER Violate)

Question stem + ALL answer options MUST fit one viewport without scrolling.
- Use responsive text sizing (min 14pt options, 16pt question)
- If too long: abbreviate question, never split options across scroll boundary
- Violation → 20-30% accuracy loss (split-attention effect)

### Accessibility Accommodations

| Condition | Accommodation |
|-----------|--------------|
| **Dyslexia** | OpenDyslexic font option, increased line spacing (1.5x), cream background option |
| **ADHD** | Timer opt-in (not default), reduced animation, focus mode, chunked content |
| **Color-blind** | Blue/orange palette (not red/green), pattern + icon + color (never color alone) |
| **Motor** | Enlarged touch targets (48dp minimum), swipe alternatives for tap |

---

## 12. Mobile UX Patterns & Navigation

### Bottom Navigation (5 Tabs)

```
┌──────────────────────────────────────────┐
│                                          │
│            [Screen Content]              │
│                                          │
├──────┬──────┬──────┬──────┬──────────────┤
│ Home │ Learn│  Map │Progress│ Profile    │
│  🏠  │  📚  │  🗺️  │  📊   │   👤       │
└──────┴──────┴──────┴──────┴──────────────┘
```

- **Home:** Activity feed, continue button, daily summary
- **Learn:** Session start, quick review, flashcards
- **Map:** Knowledge graph (first-class, not buried) ★ NEW
- **Progress:** Stats, achievements, performance trends
- **Profile:** Settings, preferences, wellbeing dashboard

### Thumb Zone Hierarchy

| Zone | Position | Actions |
|------|----------|---------|
| **Easy reach** | Bottom 40% | Submit, Skip, Hint, Continue |
| **Normal reach** | Middle 40-70% | End session, methodology switch |
| **Stretch** | Top 30% | Question text, info labels, pause |

### Session Screen (Immersive Mode)

```
┌─────────────────────────────────┐
│ ▓▓▓▓▓▓▓▓▓▓░░░░░░░░  (mastery) ▐│ ← 4px progress bar
│                              ⏸ │ ← Pause icon (subtle)
│                                 │
│   What is the derivative of     │
│   f(x) = 3x² + 2x - 5?        │ ← Question stem
│                                 │
│   ┌─────────────────────────┐   │
│   │  A) 6x + 2              │   │ ← MCQ options
│   ├─────────────────────────┤   │
│   │  B) 3x + 2              │   │
│   ├─────────────────────────┤   │
│   │  C) 6x² + 2             │   │
│   ├─────────────────────────┤   │
│   │  D) 6x - 5              │   │
│   └─────────────────────────┘   │
│                                 │
│        [ Submit Answer ]        │ ← Primary CTA, bottom zone
│                                 │
│ ← No bottom nav during session  │
└─────────────────────────────────┘
```

### Offline-First Design

- Download lessons for offline use (progress bar, storage management)
- DurableCommandQueue stores all answers → syncs when online
- Sync indicator: subtle cloud icon (✓ synced / ↻ syncing / ⚠ offline)
- Background sync when connection returns
- Never lose student progress (local-first, server-authoritative merge)

### RTL Support (Hebrew)

- Flutter `Directionality.of(context)` handles most cases
- Custom layouts need audit: FAB mirrors to bottom-left, card swipe direction reverses
- Test every screen in both LTR and RTL modes
- Answer options maintain visual alignment in RTL

---

## 13. Ethical Guardrails & Digital Wellbeing

### Notification Quiet Hours

**9 PM - 7 AM: ZERO notifications.** No exceptions.

- No streak warnings at 10:30 PM
- No "You've been idle 3 days" at midnight
- No social notifications during sleep hours
- All deferred notifications queue for 7:15 AM delivery

### Study Time Limits

| Threshold | Action | Message |
|-----------|--------|---------|
| 90 min (soft) | Gentle reminder + break suggestion | "Research shows diminishing returns after 90 min. Take a break?" |
| 120 min (firm) | Stronger suggestion + cool-down session | "You've studied 2 hours. Excellent effort. A break will help consolidation." |
| 180 min (hard) | Session ends | "Time's up for today. Your streak is safe. Return tomorrow refreshed." |

### Dark Pattern Prevention Checklist

| Anti-Pattern | Status | Implementation |
|--------------|--------|---------------|
| Forced continuity | ✅ BLOCKED | Cancellation equally easy as signup |
| Hidden costs | ✅ BLOCKED | All features disclosed upfront |
| Confirmshaming | ✅ BLOCKED | "Not now" — never "No, I don't want to learn" |
| Fake urgency | ✅ BLOCKED | No countdown timers on pricing |
| Fake social proof | ✅ BLOCKED | All numbers from real database queries |
| Roach motel | ✅ BLOCKED | Account deletion: 2-tap process |
| Privacy zuckering | ✅ BLOCKED | Consent opt-in per category |
| Loot boxes | ✅ BLOCKED | No randomized paid rewards, ever |
| Infinite scroll | ✅ BLOCKED | Sessions have natural endpoints |
| Manipulative notifications | ✅ BLOCKED | No late-night, no guilt messaging |

### Regulatory Compliance Matrix

| Regulation | Scope | Key Requirements | CENA Status |
|------------|-------|-----------------|-------------|
| **COPPA** (US) | Under 13 | Parental consent, data minimization, no behavioral ads | Gate required |
| **FERPA** (US) | Student records | Privacy of educational records, parent access rights | Architecture-ready |
| **GDPR-K** (EU) | Under 16 | Consent, data portability, right to deletion | Architecture-ready |
| **AADC** (UK) | Under 18 | Age-appropriate design, privacy by default | Designed-in |
| **Israeli Amendment 13** | All ages | Privacy protection, data subject rights | Primary compliance target |

### Mental Health-Aware Design

- Wrong answer feedback: "Let's figure this out" (never "Wrong!")
- Streak breaks: "Breaks are normal. Your progress is saved." (never "You lost your streak!")
- Growth mindset messaging: "Mistakes help your brain grow" (Dweck)
- Celebrate effort: "You tackled 15 challenging problems today!" (not just results)
- Failure normalization: "87% of students found this concept challenging at first"
- Test anxiety reduction: No visible timers (by default), practice mode without scoring

---

## 14. Cross-Document Conflict Resolutions

| Tension | Documents | Resolution |
|---------|-----------|------------|
| **Daily streaks vs. spaced review intervals** | Habit Loops (1) vs. SRS (8) | Orthogonal: streaks enforce *consistency*, SRS optimizes *what* to review within daily sessions |
| **Engagement maximization vs. wellbeing limits** | Gamification (2) vs. Ethical (10) | Reject engagement-at-all-costs. Sustainable retention > short-term DAU. Hard 180-min limit. |
| **Social comparison vs. social proof** | Social (5) vs. Ethical (10) | Aggregate-only stats. No named rankings. Lateral peer models. All social opt-in. |
| **Flow immersion vs. social chrome** | Flow (4) vs. Social (5) | Social features ONLY on home/progress screens. Zero social chrome during immersive session. |
| **Heavy extrinsic motivation (early) vs. overjustification** | Gamification (2) vs. Learning Science (8) | Intentional: extrinsic scaffolding weeks 1-2, progressive fade by week 9+. |
| **Onboarding try-question vs. cognitive load** | Onboarding (7) vs. CLT (3) | Try question is simple MCQ on basic math — actually *reduces* total onboarding complexity |
| **Training wheels vs. feature discovery** | CLT (3) vs. Onboarding (7) | Training wheels are invisible simplicity, not visible restriction. Students don't know features are hidden. |
| **Flow ZPD (P=0.60) vs. SRS recall target (P=0.75)** | Flow (4) vs. SRS (8) | SRS sets the *review queue* (which items). Flow tunes *difficulty within* those items. |

---

## 15. Implementation Roadmap

### Wave 1: Foundation (Weeks 1-4) — Highest ROI

| Priority | Feature | Expected Impact | Effort |
|----------|---------|----------------|--------|
| **P0** | Try-question before signup (Doc 7) | +40% Day-1 retention | 1-2 weeks |
| **P0** | Quiet hours enforcement 9PM-7AM (Doc 10) | Eliminate sleep disruption complaints | 3-5 days |
| **P0** | Contiguity audit — no scroll-split (Doc 3) | -20% accuracy loss prevented | 1 week |
| **P1** | Knowledge graph as 5th tab (Doc 9) | +40% feature discovery | 1 week |
| **P1** | Quality-gated streaks (Doc 1) | Eliminate zombie sessions | 3-5 days |

### Wave 2: Core Psychology (Weeks 5-10)

| Priority | Feature | Expected Impact | Effort |
|----------|---------|----------------|--------|
| **P1** | FlowMonitorActor + ZPD dynamic targeting (Doc 4) | +15-20% retention, +10% accuracy | 2-3 weeks |
| **P1** | Interleaving scheduler (Doc 8) | +15% transfer of learning | 1-2 weeks |
| **P1** | 5-tier celebration system (Doc 6) | Improved delight perception | 2 weeks |
| **P1** | Progressive feature discovery (Docs 3, 7) | -50% feature overwhelm, faster learning | 1-2 weeks |
| **P2** | Momentum Meter for anxious students (Doc 2) | Reduce streak anxiety incidents | 1 week |

### Wave 3: Social & Personalization (Weeks 11-18)

| Priority | Feature | Expected Impact | Effort |
|----------|---------|----------------|--------|
| **P2** | Class activity feed — aggregate (Doc 5) | +15% sessions on high-activity days | 2 weeks |
| **P2** | "Students Like You" stories (Doc 5) | +10% retention | 1-2 weeks |
| **P2** | Peer solution replays (Doc 5) | 70%+ helpful rating | 2-3 weeks |
| **P2** | RoutineProfileActor — habit stacking (Doc 1) | +30% notification relevance | 2 weeks |
| **P2** | Haptic + sound polish (Doc 6) | Premium feel, +15% return rate | 1-2 weeks |

### Wave 4: Advanced & Compliance (Weeks 19-26)

| Priority | Feature | Expected Impact | Effort |
|----------|---------|----------------|--------|
| **P2** | FSRS shadow mode (Doc 8) | Prepare for +10-15% recall improvement | 3 weeks |
| **P2** | Peer tutoring matching (Doc 5) | +60% tutee concept improvement | 2-3 weeks |
| **P3** | Study groups (Doc 5) | Social retention lever | 3 weeks |
| **P3** | COPPA parental consent flow (Doc 10) | Legal compliance for <13 expansion | 2 weeks |
| **P3** | RTL layout comprehensive audit (Doc 9) | Hebrew/Arabic UI correctness | 1-2 weeks |

---

## 16. KPI Dashboard

### Engagement Metrics

| Metric | Target | Source |
|--------|--------|--------|
| Time-to-first-value | < 30 seconds | Doc 7 |
| Onboarding completion rate | > 80% | Doc 7 |
| Day-1 retention | > 50% (benchmark: 40%) | Doc 7 |
| Day-7 retention | > 35% | Docs 1, 7 |
| Day-30 retention | > 25% | Doc 2 |
| Habit formation velocity | ≤ 21 days to 15+ sessions | Doc 1 |
| Trigger migration ratio | 70% internal by week 8 | Doc 1 |
| Zombie session rate | < 5% | Doc 1 |

### Learning Quality Metrics

| Metric | Target | Source |
|--------|--------|--------|
| Flow state prevalence | 30-40% of core challenge | Doc 4 |
| Flow episode duration | 8-15 min per episode | Doc 4 |
| Challenge-skill gap distribution | 70% in Challenge+Appropriate | Doc 4 |
| Interleaving effect size | +15-20% transfer improvement | Doc 8 |
| SRS recall accuracy (L1 loss) | < 0.35 | Doc 8 |
| 7/14/30-day retention | 90%/75%/55% | Doc 8 |
| Session completion rate | > 85% | Docs 3, 4 |

### UX Quality Metrics

| Metric | Target | Source |
|--------|--------|--------|
| CLT density score | 5-7 elements/screen (max 8) | Doc 3 |
| Scroll events per question | 0 (no scroll-split) | Doc 3 |
| Celebration skip rate (tier 3-5) | < 20% | Doc 6 |
| Delight perception survey | > 4.5/5 | Doc 6 |
| Touch accuracy (bottom zone) | < 2% error rate | Doc 9 |

### Ethical/Wellbeing Metrics

| Metric | Target | Source |
|--------|--------|--------|
| Sleep disruption rate (notifications after 9 PM) | 0% | Doc 10 |
| Streak anxiety incidents | < 5% of cohort | Doc 10 |
| Unwanted study pressure survey | < 15% agree | Doc 10 |
| Dark pattern compliance | 10/10 checks pass | Doc 10 |
| Parental trust score | > 4.5/5 | Doc 10 |

### Social Metrics

| Metric | Target | Source |
|--------|--------|--------|
| Peer solution helpfulness | > 70% rated ≥ 4/5 | Doc 5 |
| Tutoring success rate | > 60% improvement post-tutoring | Doc 5 |
| Class feed weekly engagement | > 70% of students | Doc 5 |
| Peer narrative retention uplift | +10% 7-day retention | Doc 5 |

---

## 17. Anti-Pattern Registry

### CRITICAL (Implement Immediately)

| # | Anti-Pattern | Why It's Harmful | Source |
|---|-------------|-----------------|--------|
| 1 | Late-night notifications (after 9 PM) | Sleep deprivation → academic collapse + parental backlash | Doc 10 |
| 2 | Named leaderboards / absolute rankings | Bottom-quartile: -15% engagement, increased anxiety, higher churn | Docs 5, 10 |
| 3 | Streak anxiety without escape valve | Zombie sessions + burnout → 60% abandon-on-break | Docs 1, 10 |
| 4 | Question + options split across scroll | Contiguity violation → 20-30% accuracy loss | Doc 3 |
| 5 | Force-continuity subscriptions | One incident → 10x negative reviews, parent trust destroyed | Doc 10 |

### HIGH (Prevent in Design)

| # | Anti-Pattern | Why It's Harmful | Source |
|---|-------------|-----------------|--------|
| 6 | Loading spinner before first question | Kills trigger reception, time-to-value | Doc 1 |
| 7 | Show all features on day 1 | Feature shock → 50% slower learning | Doc 3 |
| 8 | Blocked practice (all same topic) | Prevents topic discrimination, reduces transfer | Doc 8 |
| 9 | Self-assessed SRS quality grades | Dunning-Kruger → miscalibrated review intervals | Doc 8 |
| 10 | Gamification chrome during flow state | Breaks immersion → flow termination → reduced learning | Doc 4 |

### MODERATE (Design Guidelines)

| # | Anti-Pattern | Why It's Harmful | Source |
|---|-------------|-----------------|--------|
| 11 | Over-celebrating trivial events | Celebration fatigue by question #20 | Doc 6 |
| 12 | Harsh wrong-answer feedback (red X + buzzer) | Creates test anxiety → avoidance behavior | Doc 6 |
| 13 | Hamburger menu for primary navigation | -50% feature discovery vs bottom nav | Doc 9 |
| 14 | 6+ bottom nav items | Cognitive overload exceeds Cowan's 4±1 | Doc 9 |
| 15 | Forced social features on introverted students | Alienation → churn for solo learners | Doc 5 |
| 16 | Shame language in copy ("You're falling behind") | Amplifies anxiety, doesn't motivate | Doc 2 |
| 17 | XP/badges displayed during problem-solving | Splits attention from learning to reward-seeking | Doc 2 |
| 18 | Reviews at < 50% recall probability | High fail rate, inefficient learning | Doc 8 |
| 19 | Timer visible during questions (by default) | Artificial time pressure displaces focus | Doc 4 |
| 20 | Confirmshaming ("No, I don't want to learn") | Manipulative, erodes trust | Doc 10 |

---

## Summary

This blueprint synthesizes ~10,000 lines of deep research across 10 psychology domains into an actionable implementation guide for CENA's Flutter mobile app. The research is grounded in:

- **Csikszentmihalyi** (flow), **Vygotsky** (ZPD), **Sweller** (CLT), **Ebbinghaus** (spacing)
- **Deci & Ryan** (SDT), **Yu-kai Chou** (Octalysis), **Nir Eyal** (Hook Model), **BJ Fogg** (B=MAP)
- **Bandura** (social learning), **Festinger** (social comparison), **Cialdini** (ethical persuasion)
- **Dan Saffer** (microinteractions), **Don Norman** (emotional design), **Carol Dweck** (growth mindset)
- **Bjork** (desirable difficulties), **Mayer** (multimedia learning), **Paivio** (dual coding)

Every recommendation maps to CENA's actor-based architecture, includes measurable KPIs, and respects ethical guardrails for K-12/higher-ed students.

**The 4-wave implementation roadmap starts with highest-ROI, lowest-effort features and builds toward a psychologically optimized, ethically sound learning experience.**
