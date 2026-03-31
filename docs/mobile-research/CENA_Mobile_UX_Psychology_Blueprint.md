# CENA Mobile UX Psychology Master Blueprint

> **Date:** 2026-03-31
> **Status:** Master synthesis of 9 deep-research documents + platform context
> **Scope:** Flutter mobile app for Israeli Bagrut exam preparation (primary: ages 14-18; expandable: K-5 through adult)
> **Architecture:** .NET 9 Proto.Actor, NATS JetStream, Marten event sourcing, Flutter 3.x
> **Source documents:** 9 research papers totaling ~200,000 words of applied psychology research
> **Audience:** Product, Engineering, Design, Executive leadership

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Psychology Integration Map](#2-psychology-integration-map)
3. [The Student Journey (End-to-End)](#3-the-student-journey-end-to-end)
4. [Screen-by-Screen Psychology Guide](#4-screen-by-screen-psychology-guide)
5. [Actor Architecture Summary](#5-actor-architecture-summary)
6. [Age-Segmented Design Matrix](#6-age-segmented-design-matrix)
7. [Cross-Cutting Concerns](#7-cross-cutting-concerns)
8. [Implementation Priority Matrix](#8-implementation-priority-matrix)
9. [KPI Dashboard](#9-kpi-dashboard)
10. [Competitive Advantage Summary](#10-competitive-advantage-summary)
11. [Risk Register](#11-risk-register)
12. [Research Bibliography](#12-research-bibliography)

---

## 1. Executive Summary

### What Makes CENA Psychologically Compelling

CENA is not a tutoring app with gamification bolted on. It is a psychologically engineered learning system where every screen, transition, notification, and data flow is grounded in peer-reviewed behavioral and cognitive science. Nine research domains have been deeply analyzed and mapped to concrete Flutter UI patterns, Proto.Actor message flows, and measurable KPIs.

The core thesis: **sustainable learning behavior emerges when cognitive load is managed, flow states are cultivated, habits are engineered, social belonging is fostered, and intrinsic motivation gradually replaces extrinsic scaffolding -- all adapted to the learner's age, mastery level, and circadian rhythm.**

### The 10 Psychology Domains and How They Interconnect

| # | Domain | Primary Source | Core Principle |
|---|--------|----------------|----------------|
| 1 | **Habit Loops & Hook Model** | Eyal (2014), Fogg (2019), Clear (2018) | Four-phase cycle (Trigger-Action-Variable Reward-Investment) creates automatic behavior in 4-8 weeks |
| 2 | **Gamification & Motivation** | Deci & Ryan (2000), Chou (2016) | SDT's autonomy/competence/relatedness + Octalysis 8 core drives; White Hat > Black Hat |
| 3 | **Cognitive Load & Progressive Disclosure** | Sweller (1988-2024), Cowan (2001) | Total load (intrinsic + extraneous + germane) must not exceed 4+-1 chunk capacity |
| 4 | **Flow State Design** | Csikszentmihalyi (1990), Vygotsky (1978) | Challenge-skill balance at P(correct) 0.60-0.75; 8-15 minute flow episodes |
| 5 | **Social Learning & Community** | Bandura (1977), Festinger (1954), Cialdini (2006) | Observational learning, social proof, lateral comparison; class as primary social unit |
| 6 | **Microinteractions & Emotional Design** | Saffer (2013), Norman (2004) | Five celebration tiers; visceral/behavioral/reflective design; every state change animated |
| 7 | **Onboarding & First-Time UX** | Industry benchmarks (Duolingo, Headspace) | Time-to-value < 30 seconds; explore-before-signup; diagnostic as discovery |
| 8 | **Learning Science & SRS** | Ebbinghaus (1885), FSRS (2022), Mayer (2009) | Spaced repetition, active recall, interleaving, desirable difficulties, dual coding |
| 9 | **Mobile UX Patterns** | Hoober (2017), Apple HIG, Material 3 | Thumb zone primacy; 5-tab bottom nav; offline-first; card-based UI |
| 10 | **Intelligence Layer** | Platform architecture doc | Six data flywheels (MCM, BKT, IRT, HLR, Stagnation, Engagement) compound into defensible moat |

These 10 domains form a reinforcement network, not a linear stack. Habit loops depend on flow states for session quality. Flow states depend on cognitive load management for sustained attention. Gamification depends on social learning for relatedness. SRS depends on habit loops for consistent review. The intelligence layer observes all domains and tunes parameters continuously.

### Key Competitive Advantages Over Competitors

1. **Adaptive psychology, not static gamification.** CENA's GamificationRotationService (FOC-011) detects when each gamification element loses novelty and rotates it -- a capability no competitor has.
2. **Actor-per-student architecture.** Every student has a dedicated Proto.Actor grain holding their complete psychological state: mastery, focus level, circadian rhythm, streak mode, methodology preferences. This enables truly personalized UX at scale.
3. **Flow-aware item selection.** CENA dynamically adjusts target P(correct) based on real-time focus level detection, not just mastery score. Flow state students get harder questions; fatigued students get easier ones.
4. **Six data flywheels.** Each student interaction feeds MCM methodology optimization, BKT parameter refinement, IRT question calibration, HLR half-life personalization, stagnation signal weighting, and engagement pattern modeling. At 100K+ students, this data is unreplicable.
5. **Age-segmented social safety.** Four-tier social feature matrix (ages 6-9, 10-12, 13-15, 16-18) with COPPA compliance, Israeli data protection (Amendment 13), and anti-bullying design patterns.

---

## 2. Psychology Integration Map

### System-Level Interaction Diagram

```
                        +-------------------+
                        |   INTELLIGENCE    |
                        |      LAYER        |
                        | (6 Data Flywheels)|
                        +--------+----------+
                                 |
              Observes all, tunes parameters
                                 |
     +----------+----------+----+----+----------+----------+
     |          |          |         |          |          |
     v          v          v         v          v          v
+--------+ +--------+ +--------+ +--------+ +--------+ +--------+
| HABIT  | | FLOW   | | COGN.  | | GAMIF. | | SOCIAL | | SRS &  |
| LOOPS  | | STATE  | | LOAD   | | & SDT  | | LEARN  | | LEARN  |
| (Hook) | | Design | | Mgmt   | | Drives | | Comm.  | | Science|
+---+----+ +---+----+ +---+----+ +---+----+ +---+----+ +---+----+
    |          |          |         |          |          |
    +-----+----+----+-----+----+----+----+-----+----+----+
          |         |          |         |          |
          v         v          v         v          v
    +----------+ +--------+ +--------+ +--------+ +--------+
    | ONBOARD  | | MICRO- | | MOBILE | | EMOT.  | | STAKE- |
    | & FTUX   | | INTER. | | UX     | | DESIGN | | HOLDER |
    |          | |        | | Patt.  | |        | | Views  |
    +----------+ +--------+ +--------+ +--------+ +--------+
```

### Reinforcement Loops Between Domains

```
LOOP 1: The Daily Engagement Cycle
  Trigger (Habit) -> Open App -> Quick Review (SRS) -> Flow State (Session)
  -> Variable Reward (Gamification) -> Investment (Streak, Graph Growth)
  -> Loads Next Trigger (Notification at optimal time)

LOOP 2: The Competence-Flow Cycle
  BKT Mastery Rise -> ZPD Item Selection (Flow) -> Concept Mastered (Reward)
  -> Knowledge Graph Grows (Investment) -> New Concepts Unlock (Curiosity)
  -> Intrinsic Motivation Rises -> Deeper Engagement

LOOP 3: The Social Reinforcement Cycle
  Class Activity Feed (Social Proof) -> "Others are studying" (Trigger)
  -> Join Session (Action) -> Teacher Sees Progress (Social Reward)
  -> Teacher Endorses Concept (Authority Trigger) -> Study Recommended Topic

LOOP 4: The Adaptive Intelligence Cycle
  Student Interactions -> Event Store -> Flywheel Processing (Monthly/Quarterly)
  -> Updated Models (MCM, BKT, IRT, HLR) -> Better Item Selection
  -> Better Learning Outcomes -> More Engagement -> More Data -> Better Models

LOOP 5: The Anti-Anxiety Safety Cycle
  Streak Anxiety Detected -> Momentum Meter Offered (Alternative)
  -> Quality-Gated Streak (Prevents Zombie Sessions)
  -> Vacation Mode / Freezes (Escape Valves) -> Trust Builds -> Genuine Engagement
```

### Psychology Principle-to-Feature Mapping

| Psychology Principle | Feature(s) It Drives | Source Doc |
|---------------------|---------------------|-----------|
| Hook Model triggers | Push notifications, home screen widget, streak-at-risk banner | habit-loops |
| B=MAP (Fogg) | One-tap "Continue Studying", < 3s to first question, zero decision friction | habit-loops |
| Variable reward schedules | Mystery boxes every 10th answer, XP variation, knowledge graph reveal | habit-loops, gamification |
| Habit stacking | Morning review, commute mode, bedtime review, teacher-aligned homework sessions | habit-loops |
| SDT Autonomy | Learning path selection (2-3 choices), pace controls, daily goal setting | gamification |
| SDT Competence | Skill tree visualization, mastery progress bars, difficulty scaling feedback | gamification |
| SDT Relatedness | Class cohort awareness, study groups, teacher connection, mentorship | gamification, social |
| Octalysis Core Drives 1-8 | Class missions, XP/levels, student-generated content, avatar, social proof, scarcity, mystery, streak protection | gamification |
| Cognitive Load Theory | Single-concept screens, proximity grouping, progressive disclosure 4 levels | cognitive-load |
| Working memory 4+-1 | Max 6-8 visible elements per screen, 4-tab nav, chunked information | cognitive-load |
| Worked example effect | Faded worked examples controlled by BKT mastery level | cognitive-load |
| Csikszentmihalyi's 9 conditions | Immersive mode, DND during session, no clock visible, ZPD targeting | flow-state |
| Flow channel math | DifficultyGap targeting -0.1 to +0.3; dynamic P(correct) by focus level | flow-state |
| Session flow arc | Warm-up (easy) -> Core challenge (ZPD) -> Cool-down (easy, end on success) | flow-state |
| Bandura observational learning | Peer solution replays (shown after student attempts), coping models | social |
| Cialdini social proof | Activity counters, "most popular" labels, teacher endorsements | social |
| Festinger social comparison | Smart comparison widget with "typical range" band, opt-in only | social |
| Saffer microinteraction framework | Trigger-Rules-Feedback-Loops for every interaction (MCQ tap, hint, submit) | microinteractions |
| Norman visceral/behavioral/reflective | Material 3 beauty, predictable controls, identity-building milestones | microinteractions |
| 5-tier celebration scale | Micro (correct answer) through Epic (course completion), proportional response | microinteractions |
| Ebbinghaus forgetting curve | Memory health widget, recall probability bars, "review due" badges | learning-science |
| FSRS algorithm | SRSActor with stability/difficulty/retrievability per concept | learning-science |
| Active recall + testing effect | All interactions are retrieval-based; MCQ distractors force discrimination | learning-science |
| Interleaving | Adaptive interleave probability (0.0 at novice, 0.7 at mastery) | learning-science |
| Metacognition | Confidence ratings, calibration tracking, teach-back feature | learning-science |
| Thumb zone ergonomics | Primary actions in bottom 40%, submit/hint/skip in easy-reach zone | mobile-patterns |
| Offline-first | 20-question session packs cached, three-tier sync protocol, offline banner | mobile-patterns |

---

## 3. The Student Journey (End-to-End)

### Stage 1: Discovery & Install

**Duration:** Days before first open
**Psychology principles active:** Social proof, authority, anticipation

| Touchpoint | Psychology Mechanism | Implementation |
|-----------|---------------------|----------------|
| App Store listing | Social proof ("X students preparing for Bagrut") | Screenshots showing XP, streaks, knowledge graph |
| Teacher recommendation | Authority (Cialdini) -- most trusted EdTech acquisition channel | Teacher shares class code via WhatsApp to class group |
| Parent referral | Relationship trigger (Eyal) + loss aversion ("94% savings vs tutoring") | Parent dashboard screenshots in app store |
| Friend share | Relationship trigger + social identity | "Your friend just mastered Calculus" shareable cards |

**Key metric:** Install-to-first-open gap (target: < 6 hours for education apps)

> Deep dive: `docs/onboarding-first-time-ux-research.md` Section 1.1

### Stage 2: Onboarding (First 5-7 Minutes)

**Duration:** 5-7 minutes
**Psychology principles active:** Commitment/consistency (Cialdini), progressive disclosure (Sweller), IKEA effect, explore-before-signup

**Screen sequence:**
1. **Welcome + Language + Role** (10s) -- Brand priming, RTL-aware, role cards (Student/Teacher/Parent)
2. **Try a Question** (25s) -- Real math question BEFORE account creation. XP animation on correct. Demonstrates core value.
3. **Subject Selection** (15s) -- 2x2 grid, Math enabled, others "coming soon"
4. **Grade + Bagrut Track** (15s) -- Horizontal chips for grade, vertical cards for units
5. **Goal Setting** (20s) -- Empowering card selection (pass Bagrut/improve/get ahead/university), daily time commitment slider with "recommended" anchor
6. **Diagnostic Quiz** (2-3 min) -- Framed as "Discovery Tour" not "test". Mini knowledge graph lights up in real-time. Adaptive (KST-based). Skip is always available.
7. **Knowledge Graph Reveal** (15s) -- Full-screen animated reveal. This is the aha moment: "This app knows where I am and shows me exactly what to learn next."

**Critical design rules:**
- NEVER call the diagnostic a "test" -- it is a "Discovery Tour"
- Insert a real question BEFORE sign-up (Duolingo's 4x Day 7 retention improvement)
- Every step is skip-able (only 12% of users skip placement tests)
- Frame low scores positively: "Your starting point is perfect! We have lots of room to learn together."

**Aha moment:** When the student sees their personalized knowledge graph with concepts lit up based on diagnostic results. Students who reach this view within the first session have 3-5x higher Day 7 retention.

> Deep dive: `docs/onboarding-first-time-ux-research.md` Sections 1-5

### Stage 3: First Session (Minutes 7-25)

**Duration:** 10-20 minutes
**Psychology principles active:** Flow state (Csikszentmihalyi), cognitive load management (Sweller), immediate feedback, competence satisfaction (SDT), microinteraction delight

**Session architecture (three-phase flow arc):**

| Phase | Duration | Difficulty Target | Purpose |
|-------|----------|------------------|---------|
| Warm-up | 3-5 min, 3-4 questions | P(correct) = 0.80-0.90 | Activate prior knowledge, build confidence, establish rhythm |
| Core Challenge | 12-20 min, 8-15 questions | P(correct) = 0.55-0.75 (dynamic by focus level) | Sustained learning in the flow channel; new mastery happens here |
| Cool-down | 3-5 min, 2-4 questions | P(correct) = 0.80-0.90 | Consolidate, provide closure, end on success (peak-end effect) |

**First session special rules:**
- Training wheels mode: MCQ only, auto-show first hint after 30s, methodology selection hidden, difficulty range 1-4
- First question is always a review from diagnostic (predicted recall > 0.9)
- "First Steps" badge auto-awarded on completion
- Session summary shows knowledge graph growth animation
- "Tomorrow's preview" loads the re-engagement trigger

**Immersive mode during core phase:**
- System status bar hidden
- Bottom navigation hidden
- Gamification chrome hidden
- Only visible: question + answer input + 4px mastery whisper bar + pause icon
- DND mode requested from OS

> Deep dive: `docs/flow-state-design-research.md` Sections 1-4; `docs/cognitive-load-progressive-disclosure-research.md` Section 3

### Stage 4: First Week (Days 1-7)

**Duration:** 7 days
**Psychology principles active:** Habit formation (Clear), streak mechanics, loss aversion (gentle), social onboarding, progressive feature reveal

**Day-by-day activation strategy:**

| Day | Lever | Psychology |
|-----|-------|-----------|
| Day 1 | Complete first session + see first badge + set reminder | Commitment, competence |
| Day 2 | Push notification at preferred study time: "Day 2! Keep your streak going" | External trigger, streak inception |
| Day 3 | Streak reaches 3 days. Introduce second concept cluster. Social proof if class code used. | Streak milestone, novelty |
| Day 4-5 | Gradual feature reveal: hint button (session 3), skip question (session 5) | Progressive disclosure |
| Day 6 | "Nearly a week!" notification. Fatigue indicator appears for first time. | Anticipation, new feature discovery |
| Day 7 | Weekly summary screen. Unlock knowledge graph tab. Parent digest goes out. | Reflection, social accountability |

**Streak system (enhanced beyond Duolingo):**
- Quality-gated: requires 3+ genuine-effort questions (response time > 5s average) OR one deep review (5+ questions on decaying concepts). Prevents zombie sessions.
- Streak freezes: earned via milestones (2 at start, +1 at 7-day, +1 at 30-day, +1 at 60-day)
- 24-hour repair window: complete 10 questions to repair a broken streak
- Momentum Meter alternative for anxiety-prone students: rolling 7-day score that never goes to zero
- Consistency Score alternative for data-oriented students: weighted 30-day rolling average
- Vacation mode: unlimited pause up to 14 days
- NEVER shame on streak loss. NEVER show a sad mascot. NEVER use "YOUR STREAK IS ABOUT TO END!!!" messaging.

> Deep dive: `docs/habit-loops-hook-model-research.md` Sections 3-5

### Stage 5: First Month (Days 7-30)

**Duration:** Weeks 2-4
**Psychology principles active:** Gamification deepens, SRS kicks in, social bonds form, extrinsic-to-intrinsic transition begins

**Key transitions:**
- **Gamification rotation begins:** GamificationRotationService (FOC-011) starts tracking per-element engagement decay. Elements that lose novelty are rotated.
- **SRS becomes visible:** Memory health widget appears on home screen. Daily review badge shows decaying concept count. HLR/FSRS scheduling produces personalized review intervals.
- **Social features introduced:** Study groups (class-level), teacher-assigned challenges ("Teacher Quests"), peer achievement feed (opt-in)
- **Extrinsic-to-intrinsic shift:** XP popups become smaller and less frequent. Progress dashboard becomes primary home element. Knowledge graph and skill tree replace XP counter as dominant feedback.
- **Monthly progress report:** "One month in! You've mastered 45 concepts and are 23% ready for the Bagrut."
- **30-Day Scholar badge** earned

> Deep dive: `docs/gamification-motivation-research.md` Section 3 (Extrinsic-to-Intrinsic Transition); `docs/learning-science-srs-research.md` Section 1

### Stage 6: Ongoing Mastery (Month 2+)

**Duration:** Months 2-12
**Psychology principles active:** Intrinsic motivation dominates, metacognition develops, transfer learning, deep work, mastery orientation

**Features that emerge:**
- **Teach-back mode:** After mastering a concept (P(known) > 0.80), students can write explanations for peers. LLM evaluates quality. Earns "Teacher" badge after 10 submissions.
- **Confidence calibration:** Metacognitive monitoring tracks overconfidence/underconfidence patterns. "You rated yourself 'Very Confident' on 8 questions. You got 5 right (63%)."
- **Deep Study mode:** 45-90 minute blocks for exam prep. 2-3 standard flow arcs with 5-minute recovery breaks. Full OS-level DND.
- **Boss battles:** Narrative-framed assessments ("The Fraction Dragon") covering entire concept clusters. 15-25 questions. Retreat and practice option.
- **Student-generated questions:** After mastering a concept, students write questions for the bank. Auto-quality gate. "Creator" badge. 100 XP per approved question.
- **Prestige system (optional):** At level 20, reset to level 1 with a visible prestige star. Infinite progression without level inflation.

> Deep dive: `docs/learning-science-srs-research.md` Sections 5, 7, 10; `docs/flow-state-design-research.md` Section 7

### Stage 7: Re-engagement (After Absence)

**Duration:** Variable
**Psychology principles active:** Loss aversion (gentle), social pull, fresh start effect, curiosity

**Graduated re-engagement by absence duration:**

| Absence | Pattern | Message Tone |
|---------|---------|-------------|
| < 5 min | Invisible resume, same question state | No message |
| 5-30 min | "Ready to continue?" over last question | Soft nudge |
| 30 min - 24 hours | Quick recap of last session + one-tap continue | Informational |
| 1-3 days | Checkpoint quiz (3 questions) + knowledge graph highlights | Welcoming |
| 3-7 days | "Welcome back! Streak frozen successfully." + SRS items prioritized | Reassuring |
| 7-14 days | Mini-diagnostic (5 questions) + knowledge graph shows fading areas | Supportive |
| 14-30 days | Email only: "Your progress is saved. Pick up whenever you're ready." | No pressure |
| 30-60 days | One final email. Then silence. | Respectful |
| 60+ days | Stop all outreach. Welcome warmly if they return organically. | Dignified |

**What to NEVER do in re-engagement:**
- Never increase notification frequency for inactive users
- Never send "we miss you" messages
- Never show "you're falling behind your classmates"
- Never email parents about student inactivity without explicit dual opt-in

> Deep dive: `docs/habit-loops-hook-model-research.md` Section 5.5; `docs/flow-state-design-research.md` Section 9

---

## 4. Screen-by-Screen Psychology Guide

### Home Screen

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Time-aware greeting ("Good evening, Shiham") | Personalization, belonging (SDT Relatedness) | habit-loops |
| Bagrut readiness scores with trend arrows | Competence feedback, goal proximity | gamification |
| "Continue Studying" as SOLE primary CTA | B=MAP friction elimination (Fogg) | habit-loops |
| Streak calendar strip with flame icon | Investment visualization, loss aversion (gentle) | habit-loops |
| Subject quick-start grid | Autonomy (SDT) | gamification |
| "Review Due: N concepts" badge | SRS integration, gentle loss aversion | learning-science |
| Live activity indicator ("12 students studying now") | Social proof, consensus (Cialdini) | social |
| Teacher endorsement card | Authority social proof | social |

### Learning Session (Question Flow)

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Full-screen immersive mode (no nav, no status bar) | Flow condition 2 (merging of action and awareness), 5 (concentration) | flow-state |
| 4px mastery whisper bar (no numbers, just color) | Ambient progress feedback without attention allocation | flow-state |
| Single-concept per screen | Reduce element interactivity (CLT) | cognitive-load |
| Question + options visible without scrolling | Contiguity principle (Mayer), split attention prevention | cognitive-load |
| Progressive hint reveal (clue -> strategy -> solution) | Scaffolding (Wood et al., 1976), productive struggle | cognitive-load |
| Dynamic difficulty targeting by focus level | Flow channel maintenance (Csikszentmihalyi) | flow-state |
| No clock visible during session | Flow condition 8 (transformation of time) | flow-state |
| No peer comparison during session | Flow condition 7 (loss of self-consciousness) | flow-state |
| Background color temperature shifts by difficulty | Ambient emotional calibration | flow-state |
| Microbreak scheduling (every 8 questions or 10 min) | Attention restoration, fatigue management | flow-state |
| Never interrupt FocusLevel.Flow with a microbreak | Flow protection | flow-state |

### Feedback / Results

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Green pulse + heavy haptic for correct | Immediate reinforcement, dopamine | microinteractions |
| Gentle orange glow for incorrect (never harsh red X) | Error as information not failure (Norman reflective level) | microinteractions |
| XP popup float + bounce (900ms) | Variable reward (Hunt), competence | microinteractions |
| Error type classification in Hebrew | Metacognition development | cognitive-load |
| Worked solution with step highlighting | Germane load promotion, schema construction | cognitive-load |
| "Try a similar problem" CTA after wrong answer | Growth mindset framing | learning-science |
| Encouraging phrases rotated randomly | Emotional resilience, preventing learned helplessness | microinteractions |

### Progress / Dashboard

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Total concepts mastered / total in curriculum | Competence (SDT), growth visualization | gamification |
| Weekly growth mini line chart | Trend > absolute (reflective design) | microinteractions |
| Subject breakdown bar chart | Autonomy (see where to focus) | gamification |
| XP + Level with gradient circle | Development & Accomplishment (Octalysis Drive 2) | gamification |
| Streak calendar with escalating flame colors | Ownership (Octalysis Drive 4), investment | gamification |
| Badge grid with rarity tiers | Collection mechanic (Ownership), discovery (Unpredictability) | gamification |
| Memory health widget (recall % per concept) | Metacognition, SRS awareness | learning-science |

### Knowledge Graph

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Nodes with 4 visual states (locked/available/in-progress/mastered) | Competence mapping, curiosity (locked nodes) | gamification |
| "?" markers on unrevealed concepts | Curiosity drive (Octalysis Drive 7) | gamification |
| Animated node reveal on mastery | Autotelic experience (flow condition 9) | flow-state |
| Prerequisite arrows between nodes | Schema construction visibility | cognitive-load |
| Activity counters on nodes ("127 students practiced today") | Social proof (Cialdini) | social |
| "Most popular" labels on top concepts | Consensus social proof | social |

### Social / Class Feed

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Live achievement feed (opt-in, positive events only) | Vicarious reinforcement (Bandura) | social |
| "Students Like You" progress stories | Self-efficacy through vicarious experience | social |
| Study group progress bars (collaborative, not competitive) | Cooperative > competitive (Johnson & Johnson) | social |
| Teacher feedback cards with XP reward | Authority social proof, SDT Relatedness | social |
| Peer solution replays (shown AFTER student attempts) | Observational learning, coping models (Bandura) | social |
| Anonymous class aggregate stats | Social proof without individual comparison | social |

### Gamification / Achievements

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Daily quests (1-2 per day, always achievable) | Short engagement loop, proximal goals | gamification |
| Weekly missions (2-3, include social element) | Medium engagement loop, commitment | gamification |
| Monthly campaigns with narrative wrapper | Epic Meaning (Octalysis Drive 1) | gamification |
| Boss battles (narrative-framed assessments) | Challenge-skill balance at high level | gamification |
| Hidden/secret badges ("Night Owl", "Perfectionist") | Unpredictability (Octalysis Drive 7) | gamification |
| Badge rarity tiers (Common through Legendary) | Collection depth, aspiration | gamification |
| 3 pinned profile badges | Ownership, self-expression | gamification |

### Settings / Profile

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| Streak mode selector (standard/momentum/consistency) | Autonomy, anxiety reduction | habit-loops |
| Session length preference (5/15/30/60 min) | Autonomy (SDT), ability factor (Fogg) | gamification |
| Notification preferences with quiet hours | Respect, trust building | habit-loops |
| Dyslexia mode toggle | Accessibility, inclusion | cognitive-load |
| Gamification visibility toggle (for 18+) | Autonomy for mature learners | gamification |
| Study routine preferences (morning/commute/evening/bedtime) | Habit stacking support | habit-loops |

### Review / Spaced Repetition

| Element | Psychology Principle | Source |
|---------|---------------------|--------|
| "Daily Review" badge with due count | SRS urgency (gentle) | learning-science |
| Recall probability per concept (color-coded bars) | Metacognitive awareness | learning-science |
| Memory curve visualization (personalized sparkline) | Forgetting curve made tangible | learning-science |
| Estimated review time ("~8 min to review all due") | Ability factor (Fogg) | habit-loops |
| Automatic grading (no self-rating) | Avoids Dunning-Kruger reliability issues | learning-science |
| Post-review celebration when all due items reviewed | Completion satisfaction, closure | microinteractions |

---

## 5. Actor Architecture Summary

### Consolidated Actor Hierarchy

All 9 research documents propose actor extensions. This section consolidates them into a single coherent hierarchy.

```
StudentActor (long-lived, per-student, event-sourced via Marten)
  |
  |-- LearningSessionActor (session-scoped)
  |     |-- TutorActor (conversation-scoped, for confusion/Socratic/Feynman)
  |     |-- FlowMonitorActor [NEW - from flow-state doc]
  |     |     Monitors focus level in real-time
  |     |     Adjusts P(correct) target dynamically
  |     |     Detects flow/frustration/boredom within 2-3 questions
  |     |     Suppresses microbreaks during flow
  |     |
  |     |-- (CognitiveLoadService, FocusDegradationService,
  |     |    MicrobreakScheduler, DisengagementClassifier -- services, not actors)
  |
  |-- StagnationDetectorActor (cross-session analysis)
  |
  |-- SRSActor [NEW - from learning-science doc]
  |     State: Map<ConceptId, FsrsState> + FsrsWeights[15]
  |     Messages: ReviewCompleted, GetReviewQueue, TrainWeights
  |     Schedules next review at R = 0.90 threshold
  |
  |-- HabitProfileActor [NEW - from habit-loops doc]
  |     State: routineProfile (wake/commute/study/bed times),
  |            sessionPreferences, notificationPermissions,
  |            streakMode (standard/momentum/consistency),
  |            studyContract, accountabilityPartner
  |     Feeds OutreachSchedulerActor with personalized timing
  |
  |-- SocialProfileActor [NEW - from social doc]
  |     State: studyGroups, accountabilityPartner, mentorStatus,
  |            feedVisibility, challengeHistory, communityReputation
  |     Messages: JoinGroup, LeaveGroup, SendEncouragement,
  |               CreateChallenge, AcceptChallenge
  |
  |-- GamificationActor [EXTENDED - from gamification doc]
  |     State: XP, level, badges, quests (daily/weekly/monthly),
  |            elementDecayScores (from GamificationRotationService),
  |            collectibleCards, avatarCustomization
  |     Messages: AwardXp, UnlockBadge, CompleteQuest,
  |               RotateElement, CheckPrestige
  |
  |-- MetacognitionActor [NEW - from learning-science doc]
  |     State: confidenceCalibration, selfExplanationHistory,
  |            overconfidencePattern, errorTypeFrequency
  |     Messages: RecordConfidence, GetCalibrationReport,
  |               GenerateTeachBackPrompt
  |
  |-- OutreachSchedulerActor (existing, enhanced)
        Enhanced with: circadian rhythm optimization,
        habit stack timing, notification budget enforcement (max 2/day),
        re-engagement campaign management,
        Shabbat/holiday suppression
```

### New Actors Proposed Across Documents

| Actor | Proposed In | Relationship to Existing | Priority |
|-------|-------------|-------------------------|----------|
| FlowMonitorActor | flow-state doc | Child of LearningSessionActor | Phase 1 |
| SRSActor | learning-science doc | Child of StudentActor | Phase 1 |
| HabitProfileActor | habit-loops doc | Child of StudentActor | Phase 2 |
| SocialProfileActor | social doc | Child of StudentActor | Phase 3 |
| MetacognitionActor | learning-science doc | Child of StudentActor | Phase 4 |
| GamificationActor (extended) | gamification doc | Extends existing gamification state | Phase 2 |

### Key Event Flows and NATS Topics

| Event | Publisher | NATS Topic | Consumers |
|-------|----------|-----------|-----------|
| `FlowStateDetected_V1` | FlowMonitorActor | `cena.session.{studentId}.flow` | LearningSessionActor (adjust difficulty), Analytics |
| `SpacedRepetitionScheduled_V1` | SRSActor | `cena.srs.{studentId}.scheduled` | OutreachSchedulerActor (notification), Flutter client (review badge) |
| `HabitStackTriggered_V1` | HabitProfileActor | `cena.habit.{studentId}.trigger` | OutreachSchedulerActor (personalized notification) |
| `StudyGroupChallengeCompleted_V1` | SocialProfileActor | `cena.social.class.{classId}.challenge` | Analytics, Teacher Dashboard |
| `PeerSolutionViewed_V1` | SocialProfileActor | `cena.social.{studentId}.peer-view` | Recommendation engine (collaborative filtering) |
| `GamificationElementRotated_V1` | GamificationActor | `cena.gamification.{studentId}.rotated` | Flutter client (UI update) |
| `ConfidenceCalibrationUpdated_V1` | MetacognitionActor | `cena.metacognition.{studentId}.calibration` | SRSActor (prioritize overconfident items) |
| `QuestCompleted_V1` | GamificationActor | `cena.gamification.{studentId}.quest` | Analytics, Engagement flywheel |
| `SessionExtended_V1` | LearningSessionActor | `cena.session.{studentId}.extended` | Flow metrics (voluntary continuation) |

---

## 6. Age-Segmented Design Matrix

Every feature adapts across four age tiers. This matrix consolidates recommendations from all 9 research documents.

### Gamification & Motivation

| Feature | K-5 (6-11) | Middle (12-14) | High (15-17) | Adult (18+) |
|---------|-----------|---------------|-------------|------------|
| Streaks | Visual only (sticker calendar), no anxiety | Prominent, celebrated, parent-mediated quiet hours | Prominent but quality-gated, vacation mode promoted | Optional, toggleable, consistency score preferred |
| XP & Levels | Always visible, frequent celebrations | Always visible, subject-specific tracks | Visible but de-emphasized vs mastery signals | Hidden by default, opt-in |
| Leaderboards | Not available | Class-level improvement-based, opt-in | Cohort-level, improvement-based, opt-in | Study group only |
| Badges | Frequent, sticker-book style | Meaningful milestones, rarity tiers | Competence-based, hidden badges | Competence-based only |
| Quests | Story-driven class quests (teacher-created) | Daily + weekly + class missions | Full quest hierarchy + boss battles | Optional, self-directed |
| Gamification rotation | Fast (every 2 weeks) | Standard (3-week cycle) | Standard (30-day per FOC-011) | Slow or disabled |
| Extrinsic emphasis | High -- visual, immediate | Moderate -- peer-visible | Low -- subtle, mastery-oriented | Minimal -- opt-in only |

### Social Features

| Feature | K-5 (6-11) | Middle (12-14) | High (15-17) | Adult (18+) |
|---------|-----------|---------------|-------------|------------|
| Activity feed | Aggregate only ("Class mastered 10 concepts!") | Aggregate + anonymized individual | Named achievements (opt-in) | Full feed with names (opt-in) |
| Study groups | Teacher-created only | Teacher-created only | Student-created (teacher-approved) | Student-created |
| Peer Q&A | Not available | Read only (teacher-posted) | Read + ask + answer (moderated) | Full (moderated) |
| Accountability partner | Not available | Teacher-paired only | Self-selected | Self-selected |
| Challenges | Class only (teacher-created) | Class only | Friend challenges | Friend challenges |
| Peer tutoring | Not available | Not available | Receive help (not give) | Give + receive help |
| Direct messages | Not available | Pre-set responses only | Pre-set + short text (moderated) | Free text (moderated) |
| Profile visibility | Teacher + parent only | First name to class | Name + avatar + badges | Full profile |
| Social comparison | Self-only ("you vs past") | Class aggregate only | Optional lateral + percentile | Full suite (opt-in) |

### Cognitive Load & UI

| Feature | K-5 (6-11) | Middle (12-14) | High (15-17) | Adult (18+) |
|---------|-----------|---------------|-------------|------------|
| Session length | 5-8 min | 15-20 min | 20-30 min (default 25) | 25-50 min |
| Question types | MCQ only, large targets | MCQ + simple numeric | All types | All types + proofs |
| Knowledge graph | Simplified cluster view | Full interactive, 2 levels ahead | Full interactive | Full + export |
| Navigation depth | Max 2 taps, large icons | Max 3 taps, icon + label | Max 3 taps | 3-4 taps acceptable |
| Push notifications | Parent-mediated (parent sets hours) | Student-controlled, max 2/day | Student-controlled, max 2/day | Minimal, fully controlled |
| Visual design | Colorful, character-driven | Modern, geometric | Confident, modern, not cartoonish | Clean, data-oriented |
| Scaffolding | Auto-hints, worked examples | Hints available, fading scaffolds | Hint button only, mastery-gated | No scaffolding unless requested |
| Text size | 18sp+ body | 16sp body | 14-16sp body | 14sp body |

### Learning Science & SRS

| Feature | K-5 (6-11) | Middle (12-14) | High (15-17) | Adult (18+) |
|---------|-----------|---------------|-------------|------------|
| SRS visibility | Hidden (auto-scheduled) | "Review due" badge on home | Full memory health widget | Full + statistics |
| Interleaving | None (blocked practice) | Light (0.3 probability) | Moderate-heavy (0.5-0.7) | Heavy (0.7), student-controlled |
| Metacognition | Not surfaced | Basic calibration feedback | Full confidence tracking | Full + calibration reports |
| Retrieval type | Recognition (MCQ) | Recognition + cued recall | All types incl. free recall | All + teach-back |
| Desirable difficulty framing | "Fun challenge!" | "Brain workout!" | "Challenge Zone" with research framing | Efficiency metrics |

---

## 7. Cross-Cutting Concerns

### 7.1 Accessibility

**Source:** `cognitive-load-progressive-disclosure-research.md` Section 10

| Accommodation | Implementation | Population Served |
|--------------|----------------|-------------------|
| **Dyslexia mode** | Toggle in Settings: 18sp min text, 1.8 line height, +0.05em letter spacing, cream background (#FFF8E7), max 60 chars/line | ~10% of students |
| **ADHD accommodations** | Shorter default session (12 min), more frequent breaks (fatigue 0.5 threshold), stronger visual structure, immediate XP feedback, micro-progress indicators | ~5-7% of students |
| **Color-blind safe palettes** | Never use color as sole differentiator. All color-coded elements pair with icons and text labels. Already largely compliant (checkmark/X icons, emoji fatigue indicator). | ~8% of males |
| **Motor accessibility** | Touch targets min 44x44pt (48dp for commute mode). Single-hand operation. All primary actions in thumb zone. | All users benefit |
| **Screen reader support** | Semantic Flutter widgets, proper `Semantics` annotations, meaningful `label` on all interactive elements | Visually impaired users |
| **Reduced motion** | Respect `MediaQuery.of(context).disableAnimations`. Provide static alternatives for all celebration animations. | Motion-sensitive users |

### 7.2 Offline-First

**Source:** `mobile-ux-patterns-research.md` Section 6

| Capability | Implementation |
|-----------|----------------|
| **Session packs** | Pre-load 20-question session packs on WiFi. All question content, diagrams, and hints cached locally. |
| **Three-tier event sync** | Critical events (answers, mastery) sync immediately on reconnect. Important events (XP, badges) sync within minutes. Background events (analytics) sync opportunistically. |
| **Offline banner** | Non-blocking banner: "Working offline. Your answers will sync when you reconnect." Tertiary container color, cloud_off icon. |
| **Sync indicator** | Small sync icon in app bar during sync. "Syncing N answers..." Progress. "All synced!" with checkmark, auto-dismiss. |
| **Offline limitations** | LLM hints unavailable (show message). Knowledge graph shows cached snapshot. Badge unlocks queued for sync. |
| **Download manager** | Per-subject content packages (75-165 MB each). Auto-download on WiFi toggle. Storage usage display. |
| **Clock skew detection** | Existing protocol detects device clock manipulation. Prevents streak fraud. |

### 7.3 Performance

**Source:** `mobile-ux-patterns-research.md` Section 7, 17

| Target | Metric | Implementation |
|--------|--------|----------------|
| Time to first question | < 3s (app open), < 8s (cold start) | Pre-fetch question set during startup, use cached questions |
| Answer evaluation response | < 800ms P50 | SLA already specified |
| Animation frame rate | 60fps on Samsung A14 (Mali-G57) | CustomPainter for particles, RepaintBoundary, max 200 particles |
| Touch response | < 16ms (next frame) | Native Flutter gesture handling |
| Content transition | 300ms (AnimationTokens.normal) | Consistent across all screens |
| Celebration animation | 1000ms (AnimationTokens.celebration) | Non-blocking, auto-dismissing |
| Image loading | Skeleton shimmer during load | `cached_network_image` + `shimmer` |

### 7.4 RTL / Multilingual

| Concern | Implementation |
|---------|----------------|
| **Hebrew (primary)** | Heebo font, RTL layout via `Directionality.of(context)`, right-aligned text |
| **Arabic** | Noto Sans Arabic, RTL layout, shared with Hebrew directional logic |
| **English** | Inter font, LTR layout |
| **RTL FAB placement** | Bottom-left in RTL (Flutter auto-handles with Directionality) |
| **RTL thumb zone** | Thumb arc is physical not linguistic -- reachability map does not flip |
| **Math notation** | LaTeX rendered via `flutter_math_fork` -- inherently direction-neutral |
| **Contiguity in RTL** | Labels to the right of associated elements in RTL |

### 7.5 Privacy & Safety

**Source:** `social-learning-research.md` Section 8; `stakeholder-experiences.md` Section 1.3

| Requirement | Implementation |
|-------------|----------------|
| **COPPA compliance** | Parent creates child account for under-13. No behavioral advertising. Data minimization. No free-text messaging for under-13. Pre-set response templates only. |
| **Israeli Data Protection (Amendment 13)** | Parental consent for under-18 biometric data. GDPR alignment. Right of access/deletion. Israel-region data storage. |
| **Parent-student privacy boundary** | Parents see outcomes (mastery %, study time, streak) but NOT individual questions, annotations, session details, or methodology switches. Enforced at GraphQL resolver + CQRS read model level. |
| **Teacher privacy boundary** | Teachers see class-scoped data from student join date only. No retroactive historical data. Cannot see annotations, individual answers, or exact session timestamps. |
| **Anti-bullying by design** | No open messaging for under-13. All UGC moderated before display. No visible rankings for under-13. No negative social signals. Anonymous peer content. Block/report with immediate hide. |
| **Moderation** | AI pre-filter + teacher review for all student-generated content. 3+ reports auto-escalate. Rate limiting (5 messages/day). |

---

## 8. Implementation Priority Matrix

This matrix synthesizes all implementation roadmaps from the 9 research documents into one unified sequence.

### Phase 1: Foundation (Months 1-3)

**Goal:** Core learning experience with flow state support and basic gamification

| Priority | Feature | Source Doc | Effort | Impact |
|----------|---------|-----------|--------|--------|
| P1.1 | Session flow arc (warm-up/core/cool-down) | flow-state | M | Critical |
| P1.2 | Flow-aware dynamic difficulty (FlowMonitorActor) | flow-state | L | Critical |
| P1.3 | Immersive mode during sessions (hide nav, DND) | flow-state | S | High |
| P1.4 | Onboarding v2 (add "Try a Question" before signup) | onboarding | M | Critical |
| P1.5 | Progressive disclosure 4 levels | cognitive-load | M | High |
| P1.6 | Training wheels mode (sessions 1-3 reduced complexity) | cognitive-load | S | High |
| P1.7 | SRSActor with FSRS scheduling | learning-science | L | Critical |
| P1.8 | Review due badge + daily review session | learning-science | M | High |
| P1.9 | Thumb zone audit + bottom-heavy layout | mobile-patterns | S | Medium |
| P1.10 | Skeleton screens replacing spinners | mobile-patterns, microinteractions | S | Medium |

### Phase 2: Engagement Layer (Months 3-5)

**Goal:** Habit formation and gamification depth

| Priority | Feature | Source Doc | Effort | Impact |
|----------|---------|-----------|--------|--------|
| P2.1 | Quality-gated streaks | habit-loops | M | Critical |
| P2.2 | Momentum Meter + Consistency Score alternatives | habit-loops | M | High |
| P2.3 | Habit stacking (morning/commute/evening/bedtime sessions) | habit-loops | L | High |
| P2.4 | Personalized notification timing (circadian rhythm) | habit-loops | M | High |
| P2.5 | Quest system (daily/weekly/monthly) | gamification | L | High |
| P2.6 | Badge expansion (subject mastery, behavior, hidden) | gamification | M | Medium |
| P2.7 | XP difficulty bonus + review bonus | gamification | S | Medium |
| P2.8 | Home screen widget (streak + quick review) | habit-loops, mobile-patterns | M | Medium |
| P2.9 | Smart notification suppression rules | habit-loops | M | High |
| P2.10 | Re-engagement campaigns (graduated by absence) | habit-loops | M | Medium |
| P2.11 | GamificationActor extension (quests, collectibles) | gamification | M | Medium |
| P2.12 | HabitProfileActor (routine tracking, streak modes) | habit-loops | M | Medium |

### Phase 3: Social Layer (Months 5-8)

**Goal:** Community, collaboration, and social motivation

| Priority | Feature | Source Doc | Effort | Impact |
|----------|---------|-----------|--------|--------|
| P3.1 | Class achievement feed (opt-in, positive only) | social | L | High |
| P3.2 | Teacher endorsement cards | social | M | High |
| P3.3 | Study groups with shared challenges | social | L | Medium |
| P3.4 | Activity counters on concepts ("X students practiced today") | social | S | Medium |
| P3.5 | Peer solution replays (shown after attempt) | social | L | High |
| P3.6 | Accountability partners | social | M | Medium |
| P3.7 | Friend challenges (mastery race) | social | M | Medium |
| P3.8 | Smart comparison widget (with "typical range" band) | social | M | Medium |
| P3.9 | Age-tiered social safety matrix | social | L | Critical |
| P3.10 | Moderation pipeline (AI pre-filter + teacher review) | social | L | Critical |
| P3.11 | SocialProfileActor | social | M | Medium |
| P3.12 | Virtual study rooms (ambient presence) | social | M | Low |

### Phase 4: Advanced Intelligence (Months 8-12)

**Goal:** Metacognition, transfer learning, deep adaptive algorithms

| Priority | Feature | Source Doc | Effort | Impact |
|----------|---------|-----------|--------|--------|
| P4.1 | Confidence calibration tracking | learning-science | M | High |
| P4.2 | Teach-back mode (student explanations evaluated by LLM) | learning-science | L | High |
| P4.3 | Adaptive interleaving probability by mastery | learning-science | M | High |
| P4.4 | Pre-testing (test before teaching for priming) | learning-science | M | Medium |
| P4.5 | Student-generated questions with quality gate | gamification | L | Medium |
| P4.6 | Deep Study mode (45-90 min blocks) | flow-state | M | Medium |
| P4.7 | MetacognitionActor | learning-science | M | Medium |
| P4.8 | Boss battles (narrative-framed assessments) | gamification | L | Medium |
| P4.9 | Mentorship program (older students help younger) | social | L | Medium |
| P4.10 | FSRS parameter optimization from individual history | learning-science | L | High |

### Phase 5: Polish & Delight (Months 12-15)

**Goal:** Microinteraction refinement, seasonal content, emotional depth

| Priority | Feature | Source Doc | Effort | Impact |
|----------|---------|-----------|--------|--------|
| P5.1 | Five-tier celebration system (Rive/Lottie animations) | microinteractions | L | Medium |
| P5.2 | Sound design system (chimes, fanfares, ambient) | microinteractions | M | Medium |
| P5.3 | Haptic feedback audit (selectionClick, mediumImpact, heavyImpact) | microinteractions | S | Medium |
| P5.4 | Particle effects for streaks (14+), glow for streaks (30+) | microinteractions | M | Low |
| P5.5 | Hero animations (badge grid to detail, subject to session) | microinteractions | M | Low |
| P5.6 | Monthly themed campaigns with narrative | gamification | M | Medium |
| P5.7 | Prestige system for level 20+ | gamification | S | Low |
| P5.8 | Avatar system with achievement-unlocked accessories | gamification | L | Low |
| P5.9 | Seasonal gamification rotation | gamification | M | Medium |
| P5.10 | Camera/AR features for geometry | mobile-patterns | XL | Low |

**Effort key:** S = Small (< 1 week), M = Medium (1-3 weeks), L = Large (3-6 weeks), XL = Extra Large (6+ weeks)

---

## 9. KPI Dashboard

### 9.1 Engagement KPIs

| KPI | Definition | Target | Source |
|-----|-----------|--------|--------|
| DAU/MAU ratio | Daily active / Monthly active users | > 40% (Duolingo: ~45%) | habit-loops |
| Session completion rate | Sessions reaching cool-down / total started | > 75% | flow-state |
| Voluntary continuation rate | Sessions extended beyond scheduled end / total | > 30% | flow-state |
| Time-to-first-question | Seconds from app open to first question | < 3s (warm), < 8s (cold) | habit-loops |
| Quality streak rate | Streak days with genuine effort / total streak days | > 85% | habit-loops |
| Notification click-through | Notification taps / notifications sent | > 12% (industry avg: 8%) | habit-loops |
| Flow time per session | Minutes in FocusLevel.Flow / total session minutes | > 40% | flow-state |
| Daily review completion | Students who clear all due SRS items / those with items due | > 60% | learning-science |

### 9.2 Learning KPIs

| KPI | Definition | Target | Source |
|-----|-----------|--------|--------|
| Concepts mastered per week | ConceptMastered events per student per week | > 3 | learning-science |
| Mastery velocity | Days from first attempt to P(known) > 0.85 per concept | < 14 days | flow-state |
| SRS review retention | Accuracy on spaced repetition review items | > 85% | learning-science |
| Interleaving benefit | Delayed-test accuracy on interleaved vs blocked concepts | > 20% improvement | learning-science |
| Methodology effectiveness | Mastery rate after MCM-recommended methodology switch | > baseline | intelligence-layer |
| BKT prediction accuracy | Predicted P(correct) vs actual accuracy | RMSE < 0.10 | intelligence-layer |
| Bagrut readiness growth | Monthly change in per-subject readiness score | > 5% / month | stakeholder-experiences |
| Teach-back completion | Students who submit teach-back explanations / eligible | > 15% | learning-science |

### 9.3 Retention KPIs

| KPI | Definition | Target | Source |
|-----|-----------|--------|--------|
| Day 1 retention | Users returning day after install | > 45% | onboarding |
| Day 7 retention | Users active 7 days after install | > 22% | onboarding |
| Day 30 retention | Users active 30 days after install | > 13% | onboarding |
| Day 90 retention | Users active 90 days after install | > 8% | onboarding |
| Activation rate | Users reaching M7 (first lesson complete) + M9 (Day 2 return) | > 35% | onboarding |
| Aha moment conversion | Users who see knowledge graph / users who start onboarding | > 70% | onboarding |
| Onboarding completion rate | Users who finish all 7 steps / users who start | > 65% | onboarding |
| Streak reactivation rate | Users who restart after streak break / total breaks | > 40% | habit-loops |

### 9.4 Social KPIs

| KPI | Definition | Target | Source |
|-----|-----------|--------|--------|
| Class adoption rate | Students joined via class code / total class roster | > 80% | social |
| Study group participation | Students in at least one group / total students | > 30% | social |
| Peer solution view rate | Peer solutions viewed / available | > 20% | social |
| Accountability partner pairs | Active partner pairs / total eligible students | > 15% | social |
| Challenge acceptance rate | Challenges accepted / challenges sent | > 60% | social |
| Moderation escalation rate | Content flagged for teacher review / total UGC | < 5% | social |
| Teacher endorsement engagement | Students who start endorsed concept / total who see endorsement | > 40% | social |

### 9.5 Wellbeing KPIs

| KPI | Definition | Target | Source |
|-----|-----------|--------|--------|
| Streak anxiety rate | Students switching to momentum meter / total | < 10% | habit-loops |
| Zombie session rate | Sessions with avg response time < 5s / total | < 5% | habit-loops |
| Session over-extension rate | Sessions exceeding MaxSessionMinutes / total | < 3% | flow-state |
| Notification opt-out rate | Students revoking notification permission / total | < 5% | habit-loops |
| Frustration detection rate | Sessions with ascending error pattern / total | < 15% | flow-state |
| Boredom detection rate | Sessions with flat-low error pattern / total | < 10% | flow-state |
| Break compliance | Microbreaks accepted / microbreaks offered | > 50% | flow-state |

### 9.6 Technical KPIs

| KPI | Definition | Target | Source |
|-----|-----------|--------|--------|
| Animation frame rate | P95 fps during celebrations | > 55 fps | microinteractions |
| Offline session success rate | Offline sessions that sync without conflict / total offline | > 98% | mobile-patterns |
| Sync latency (P50) | Time from reconnect to all events synced | < 5s | mobile-patterns |
| App crash rate | Crashes per 1000 sessions | < 2 | mobile-patterns |
| Cold start time | Time from tap to first interactive frame | < 3s | mobile-patterns |
| Memory footprint (P95) | Max resident memory during session | < 200 MB | mobile-patterns |
| Question pre-fetch hit rate | Questions served from cache / total questions | > 95% | mobile-patterns |

---

## 10. Competitive Advantage Summary

### Head-to-Head Comparison

| Capability | CENA | Duolingo | Khan Academy | Anki | Kahoot | Brilliant |
|-----------|------|---------|-------------|------|-------|-----------|
| **Adaptive difficulty** | Per-question, focus-aware, ZPD-targeted (BKT + IRT + FocusLevel) | Basic (XP-driven progression) | Manual course selection | None (user-selected decks) | Fixed per quiz | Moderate (mastery gates) |
| **Spaced repetition** | Full FSRS/HLR with personalized half-lives per concept | None (lessons repeat but no SRS scheduling) | None | Best-in-class SM-2/FSRS | None | None |
| **Flow state engineering** | 3-phase session arc, immersive mode, dynamic P(correct) by focus level, microbreak suppression during flow | Session design for engagement but not flow-specific | Video + practice (no flow engineering) | Flashcard rhythm (micro-flow only) | Competitive pressure (not true flow) | Guided problem-solving (closest competitor for flow) |
| **Gamification depth** | 8 Octalysis drives, novelty rotation (FOC-011), extrinsic-to-intrinsic transition, boss battles, quests | 3-4 drives (development, scarcity, loss), no rotation | Minimal (points, badges) | None | Points, competitive | Minimal (streaks, progress) |
| **Gamification safety** | White Hat 70%, Black Hat 10%, explicit escape valves, anxiety monitoring | Heavy Black Hat (streak anxiety acknowledged in TSLW metric) | Low risk (minimal gamification) | No gamification risk | High competitive pressure | Low risk |
| **Social learning** | Class-scoped, age-tiered, privacy-first, teacher presence, mentorship, collaborative challenges | Friend leaderboards, leagues | Classroom integration (basic) | None (solo app) | Real-time competitive quizzes | None |
| **Social safety** | 4-tier age matrix, COPPA, anti-bullying by design, moderation pipeline, parent controls | Basic (leagues, friend lists) | Basic classroom | N/A | Limited (quiz host controls) | N/A |
| **Cognitive load management** | Single-concept screens, progressive disclosure (4 levels), faded worked examples, attention management | Not explicitly designed for CLT | Good content but not CLT-optimized | High extraneous load (wall of due cards) | High load (time pressure + competition) | Good (guided, step-by-step) |
| **Onboarding** | 7-screen progressive flow, try-before-signup, diagnostic as discovery, knowledge graph aha moment | Excellent (placement test before signup, immediate lesson) | Good (immediate video access) | Poor (complex deck management) | Good (join game instantly) | Good (immediate problem) |
| **Offline support** | Full offline sessions, 3-tier sync, question packs, conflict resolution | Limited offline | Limited offline | Full offline | None (real-time only) | Limited offline |
| **Stakeholder views** | Student + Teacher + Parent with privacy boundaries | Student only | Student + Teacher (basic) | Student only | Teacher + Student (real-time) | Student only |
| **Data flywheel** | 6 flywheels (MCM, BKT, IRT, HLR, stagnation, engagement) compounding into moat | Engagement data (massive scale) | Content analytics | Community shared decks | Quiz analytics | Problem-solving data |
| **Age adaptation** | 4 tiers with feature adaptation per tier | Basic (difficulty levels) | Course-level adaptation | None | Quiz-level adaptation | None |

### Where CENA Uniquely Combines What No Competitor Does

1. **Actor-per-student personalization + SRS + flow state engineering.** No competitor has all three. Duolingo has engagement engineering but no SRS. Anki has SRS but no flow engineering. Brilliant has flow but no SRS or social.

2. **Gamification with built-in decay and rotation.** FOC-011's novelty rotation engine is architecturally unique. All competitors suffer from gamification fatigue after one semester (Zeng et al., 2024 meta-analysis). CENA actively counters this.

3. **Privacy-first social learning with age segmentation.** No competitor has a 4-tier social safety matrix with COPPA compliance, Israeli data protection, and anti-bullying design-out patterns.

4. **Teacher-student-parent triangulation.** The three-stakeholder view with enforced privacy boundaries (parents see outcomes not process, teachers see gaps not individual errors) is unique to CENA.

5. **Six data flywheels compounding into a moat.** At 100K+ students, the combination of methodology effectiveness data, calibrated question parameters, personalized forgetting curves, validated stagnation signals, and engagement patterns is unreplicable by any new entrant.

---

## 11. Risk Register

### Psychology Principles That Could Backfire

| Risk | Severity | Likelihood | Source Principle | Mitigation |
|------|----------|-----------|-----------------|------------|
| **Streak anxiety** | High | High | Loss aversion (Kahneman & Tversky) | Quality-gated streaks, momentum meter alternative, vacation mode, streak freeze escalation, no-shame messaging on break, weekday-only option. NEVER show crying mascot. |
| **Social comparison harm** | High | Medium | Festinger social comparison theory | All comparison opt-in, no visible bottom rankings, improvement-based leaderboards only, "typical range" band (not rank), disabled for under-13, teacher can disable for entire class |
| **Extrinsic motivation crowding out intrinsic** | Medium | High | Overjustification effect (Lepper et al., 1973) | Planned extrinsic-to-intrinsic transition over 8 weeks. GamificationRotationService reduces element weights over time. Frame rewards informationally ("You mastered X") not controlling ("3 more for badge!"). |
| **Screen addiction** | Medium | Medium | Variable reward schedules (Skinner) | Session end is clean (no infinite scroll). Max 90 min before wellbeing check. Study timer visibility. Weekly study report framed as accomplishment. No "one more" prompt after 45 minutes. |
| **Notification fatigue** | Medium | High | Push notification research (Mehrotra, 2016) | Hard budget: max 2/day. Smart suppression (quiet hours, Shabbat, after practice). 3 consecutive dismissals = 50% frequency reduction for 7 days. Graduated re-engagement (stop all at 60 days). |
| **Zombie sessions** | Medium | Medium | Streak mechanics | Quality gate: 3+ genuine-effort questions required. Response time > 5s average. System detects and flags zombie patterns. |
| **Leaderboard demoralization** | High | Medium | Hanus & Fox (2015) | No traditional "Top 10". Relative positioning only (show 2 above and 2 below). Improvement-based, not absolute. Opt-in. Hidden for first 2 weeks. Teacher can disable. |
| **Hint over-reliance** | Low | Medium | Scaffolding theory (Wood et al.) | XP cost per hint (100% -> 80% -> 50% -> 20%). Fading scaffolds as mastery rises. ScaffoldingService.MaxHints enforced. |
| **Expertise reversal effect** | Low | Low | Kalyuga et al. (2003) | When P(known) > 0.85, suppress hints, worked examples, and methodology badges. Mastered students get clean question+answer UI only. |
| **Flow disruption from celebrations** | Low | Medium | Csikszentmihalyi flow conditions | No gamification popups mid-question. XP/badge notifications silent during questions, queued for between-question transitions. Max 1 peer celebration per session. |
| **Parental helicopter monitoring** | Medium | Low | SDT autonomy | Privacy boundary enforced at architecture level. Parents see aggregate outcomes, never individual mistakes or session details. Student annotations always private. |
| **Cultural insensitivity** | Medium | Low | Israeli context | Shabbat/holiday notification suppression (configurable). Arabic full support alongside Hebrew. Curriculum aligned to Israeli Ministry of Education Bagrut syllabus. |

### Monitoring and Early Warning

Each risk has a corresponding KPI in Section 9 that serves as an early warning system:
- Streak anxiety -> streak anxiety rate KPI (Section 9.5)
- Zombie sessions -> zombie session rate KPI (Section 9.5)
- Extrinsic crowding -> track ratio of mastery-oriented actions to XP-oriented actions
- Notification fatigue -> notification opt-out rate KPI (Section 9.5)
- Social harm -> moderation escalation rate KPI (Section 9.4)

---

## 12. Research Bibliography

### Foundational Researchers and Theories

**Behavioral Psychology & Habit Formation**
- Eyal, N. (2014). *Hooked: How to Build Habit-Forming Products*. Portfolio/Penguin.
- Fogg, B.J. (2019). *Tiny Habits: The Small Changes That Change Everything*. Houghton Mifflin Harcourt.
- Clear, J. (2018). *Atomic Habits*. Avery.
- Skinner, B.F. (1957). *Verbal Behavior*. Appleton-Century-Crofts.
- Lally, P. et al. (2010). "How are habits formed." *European Journal of Social Psychology*, 40(6), 998-1009.
- Gollwitzer, P.M. (1999). "Implementation intentions." *American Psychologist*, 54(7), 493-503.

**Motivation & Self-Determination Theory**
- Deci, E.L. & Ryan, R.M. (1985, 2000). Self-Determination Theory. Multiple publications.
- Deci, E.L., Koestner, R. & Ryan, R.M. (1999). Meta-analysis of extrinsic rewards on intrinsic motivation. *Psychological Bulletin*, 125(6), 627-668.
- Lepper, M.R., Greene, D. & Nisbett, R.E. (1973). "Undermining children's intrinsic interest with extrinsic reward." *Journal of Personality and Social Psychology*, 28(1), 129-137.
- Vansteenkiste, M. et al. (2004). "Motivating learning, performance, and persistence." *Journal of Personality and Social Psychology*, 87(2), 246-260.
- Chou, Y. (2016). *Actionable Gamification: Beyond Points, Badges, and Leaderboards*. Octalysis Media.

**Cognitive Load Theory**
- Sweller, J. (1988-2024). Cognitive Load Theory. Multiple publications.
- Sweller, J., Ayres, P. & Kalyuga, S. (2011). *Cognitive Load Theory*. Springer.
- Cowan, N. (2001, 2010). Working memory capacity. *Behavioral and Brain Sciences*.
- Miller, G.A. (1956). "The magical number seven." *Psychological Review*, 63(2), 81-97.
- Kalyuga, S. et al. (2003). "The expertise reversal effect." *Educational Psychologist*, 38(1), 23-31.
- Chandler, P. & Sweller, J. (1991, 1992). Split attention effect studies.

**Flow State Psychology**
- Csikszentmihalyi, M. (1990). *Flow: The Psychology of Optimal Experience*. Harper & Row.
- Kotler, S. (2014). *The Rise of Superman*. New Harvest.
- Newport, C. (2016). *Deep Work*. Grand Central Publishing.
- Vygotsky, L.S. (1978). *Mind in Society*. Harvard University Press.

**Social Psychology & Learning**
- Bandura, A. (1977). *Social Learning Theory*. Prentice Hall.
- Bandura, A. (1986). *Social Cognitive Theory*. Prentice Hall.
- Bandura, A. (1997). *Self-Efficacy: The Exercise of Control*. W.H. Freeman.
- Festinger, L. (1954). "Social comparison processes." *Human Relations*, 7(2), 117-140.
- Cialdini, R.B. (2006). *Influence: The Psychology of Persuasion*. Harper Business.
- Johnson, D.W. & Johnson, R.T. (2009). "Cooperative learning." *Review of Educational Research*.
- Schunk, D.H. (1987). "Peer models and children's behavioral change." *Review of Educational Research*, 57(2), 149-174.

**Design & Microinteractions**
- Saffer, D. (2013). *Microinteractions: Designing with Details*. O'Reilly Media.
- Norman, D. (2004). *Emotional Design: Why We Love (or Hate) Everyday Things*. Basic Books.
- Kahneman, D. (1999). Peak-end rule. In *Well-Being: Foundations of Hedonic Psychology*.

**Learning Science & Memory**
- Ebbinghaus, H. (1885). *Memory: A Contribution to Experimental Psychology*. Dover.
- Wozniak, P.A. (1987-1990). SM-2 Algorithm. SuperMemo.
- Ye, J. (2022). FSRS Algorithm. arXiv:2402.01032.
- Karpicke, J.D. & Blunt, J.R. (2011). "Retrieval practice produces more learning." *Science*, 331(6018), 772-775.
- Roediger, H.L. & Karpicke, J.D. (2006). "Test-enhanced learning." *Psychological Science*, 17(3), 249-255.
- Rohrer, D. & Taylor, K. (2007). "Shuffling of mathematics problems." *Instructional Science*, 35, 481-498.
- Bjork, R.A. (1994). "Memory and metamemory." In *Metacognition: Knowing About Knowing*. MIT Press.
- Mayer, R.E. (2009). *Multimedia Learning* (2nd ed.). Cambridge University Press.
- Dunlosky, J. et al. (2013). "Effective learning techniques." *Psychological Science in the Public Interest*, 14(1), 4-58.
- Chi, M.T.H. et al. (1989, 2014). Self-explanation studies. *Cognitive Science*, *Journal of Educational Psychology*.

**Behavioral Economics**
- Kahneman, D. & Tversky, A. (1979). "Prospect theory." *Econometrica*, 47(2), 263-292.
- Thaler, R.H. & Sunstein, C.R. (2008). *Nudge*. Penguin.

**Mobile UX Research**
- Hoober, S. (2013, 2017). Thumb zone research.
- Apple Human Interface Guidelines. iOS design principles.
- Google Material Design 3. Android design system.

**EdTech Industry Data**
- Duolingo product blog (2023). Streak effectiveness, leaderboard impact, TSLW metric.
- Appsflyer (2025). Mobile app retention benchmarks.
- Adjust (2025). Mobile App Trends Report.
- Lenny's Newsletter (2023). Duolingo growth analysis.

**Additional Cited Research**
- Dweck, C.S. (2006). *Mindset*. Random House.
- Walker, M. (2017). *Why We Sleep*. Scribner.
- Hanus, M.D. & Fox, J. (2015). Gamification in education study.
- Landers, R.N. et al. (2017). Leaderboard effects study.
- Brunmair, M. & Richter, T. (2019). "Interleaved learning meta-analysis." *Psychological Bulletin*, 145(11), 1029-1052.
- Lord, F.M. (1980). *Applications of Item Response Theory*. LEA.
- Embretson, S.E. & Reise, S.P. (2000). *Item Response Theory for Psychologists*. LEA.
- Rello, L. & Baeza-Yates, R. (2013). Dyslexia-friendly typography research.
- Carroll, J.M. & Carrithers, C. (1984). Training wheels interface study.
- Garrison, D.R., Anderson, T. & Archer, W. (2000). Community of Inquiry framework.
- Kruger, J. & Dunning, D. (1999). "Unskilled and unaware of it." *JPSP*, 77(6), 1121-1134.
- Zeng et al. (2024). Meta-analysis: gamification effects decline after 1 semester.

---

## Appendix: Source Document Cross-Reference

| Section in this Blueprint | Primary Source Documents |
|--------------------------|------------------------|
| Executive Summary | All 9 documents + intelligence-layer.md + stakeholder-experiences.md |
| Psychology Integration Map | All 9 documents (cross-domain synthesis) |
| Student Journey | onboarding, habit-loops, flow-state, gamification, learning-science, social |
| Screen-by-Screen Guide | All 9 documents |
| Actor Architecture | flow-state (FlowMonitorActor), learning-science (SRSActor, MetacognitionActor), habit-loops (HabitProfileActor), social (SocialProfileActor), gamification (extended GamificationActor) |
| Age-Segmented Matrix | gamification (Sec 9), social (Sec 8.4), cognitive-load (Sec 15), flow-state (Sec 3.1), habit-loops (Sec 2.1) |
| Cross-Cutting Concerns | cognitive-load (Sec 10, accessibility), mobile-patterns (Sec 6-7, offline/performance), social (Sec 8, safety), stakeholder-experiences (privacy) |
| Implementation Priority | All 9 documents (roadmap sections consolidated) |
| KPI Dashboard | flow-state (Sec 13), habit-loops (Sec 5), onboarding (Sec 16), social (Sec 16), gamification (Sec 13), mobile-patterns (Sec 17) |
| Competitive Advantage | gamification (Sec 11), social (Sec 12), learning-science (Sec 14), intelligence-layer (Sec 4) |
| Risk Register | habit-loops (Sec 4.4), gamification (Sec 10), social (Sec 8), flow-state (Sec 6) |
| Bibliography | All 9 documents (citations consolidated) |

---

*This document is the single source of truth for CENA's mobile UX psychology strategy. For implementation details on any specific domain, refer to the source document listed in the cross-reference table above.*
