# Flow State Design & Deep Engagement for Educational Mobile Apps

> **Status:** Research document
> **Date:** 2026-03-31
> **Applies to:** Cena Platform -- LearningSessionActor, FocusDegradationService, CognitiveLoadService, MicrobreakScheduler, GamificationRotationService, DisengagementClassifier, StagnationDetectorActor
> **Purpose:** Comprehensive flow psychology research applied to Cena's actor-based learning platform, with concrete screen designs, algorithms, session architectures, and measurable KPIs

---

## Table of Contents

1. [Csikszentmihalyi's Flow Theory Applied to Learning Apps](#1-csikszentmihalyis-flow-theory-applied-to-learning-apps)
2. [Adaptive Difficulty Systems](#2-adaptive-difficulty-systems)
3. [Session Design for Flow](#3-session-design-for-flow)
4. [Immersive UI Patterns](#4-immersive-ui-patterns)
5. [Micro-Flow States](#5-micro-flow-states)
6. [Anti-Flow Patterns to Avoid](#6-anti-flow-patterns-to-avoid)
7. [Deep Work Integration](#7-deep-work-integration)
8. [Engagement Loops vs Flow](#8-engagement-loops-vs-flow)
9. [Re-engagement After Flow Break](#9-re-engagement-after-flow-break)
10. [Measuring Flow in the App](#10-measuring-flow-in-the-app)
11. [Flow-Optimized Screen Designs](#11-flow-optimized-screen-designs)
12. [Actor Architecture for Flow](#12-actor-architecture-for-flow)
13. [KPIs and Success Metrics](#13-kpis-and-success-metrics)

---

## 1. Csikszentmihalyi's Flow Theory Applied to Learning Apps

### 1.1 The Nine Conditions of Flow and Their Platform Implementations

Csikszentmihalyi (1990, *Flow: The Psychology of Optimal Experience*, Harper & Row) identified nine conditions for flow states. Each condition maps to a concrete system behavior in Cena:

| Flow Condition | Platform Implementation | Owning Component |
|---|---|---|
| **1. Challenge-skill balance** | ZPD-targeted item selection: select questions where predicted correctness probability is 0.60-0.75 (below Squirrel AI's 0.70-0.75 target, adjusted for the difficulty of Bagrut STEM content) | `LearningSessionActor.HandleNextQuestion` |
| **2. Merging of action and awareness** | Full-screen immersive mode during problem-solving. Hide all navigation, status bars, and gamification chrome. Student sees only the problem and input area. | Flutter immersive mode (`SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersive)`) |
| **3. Clear goals** | Before each session: display target concept node on knowledge graph with text "Today's goal: master [concept name]". Within session: per-question goal is implicit (answer correctly). | Session start screen + knowledge graph highlight |
| **4. Immediate feedback** | Answer evaluation response time target: <800ms P50 (already specified in SLA). Visual: correct answers produce an immediate green pulse and mastery meter advance. Wrong answers produce gentle orange with explanation offer. No delay between feedback and next question. | `LearningSessionActor.HandleEvaluateAnswer` + Flutter animation layer |
| **5. Concentration on the task** | Notification suppression during session (OS-level DND integration). No in-app notifications, badge counts, or social updates during active problem-solving. | Flutter `FlutterLocalNotificationsPlugin` DND mode |
| **6. Sense of control** | "Change approach" button always visible but unobtrusive. Student can skip any question without penalty. Student chooses session length (with system-recommended default). Student controls microbreak timing. | Existing `SkipQuestionMessage` + methodology switch UI |
| **7. Loss of self-consciousness** | No peer comparison during active session. Leaderboards and social features are only visible on the dashboard, never during problem-solving. No "other students got this right" messaging. | UI architecture rule: gamification chrome hidden during session |
| **8. Transformation of time** | No visible clock during immersive session. Progress indicator shows concept completion percentage, not time elapsed. Session time only shown in end-of-session summary. | Session UI removes clock; uses concept progress bar |
| **9. Autotelic experience** | Knowledge graph growth animation on concept mastery. The graph is the intrinsic reward -- watching nodes light up is satisfying without external incentives. Minimize XP/streak messaging during learning; emphasize discovery and mastery. | Knowledge graph reveal animation (per system-overview.md Step 3) |

### 1.2 The Flow Channel: Mathematical Model

The flow channel is the region where challenge (C) approximately equals skill (S). Outside this region:
- C >> S: anxiety, frustration, giving up
- C << S: boredom, disengagement, seeking stimulation elsewhere
- C ~ S with both high: flow

Cena already models this through `DifficultyGap.cs`:

```
DifficultyFrame.Stretch:     gap > +0.3   (C >> S -- anxiety zone)
DifficultyFrame.Challenge:   gap +0.1..+0.3 (upper flow channel)
DifficultyFrame.Appropriate: gap -0.1..+0.1 (center flow channel)
DifficultyFrame.Expected:    gap -0.3..-0.1 (lower flow channel / easy)
DifficultyFrame.Regression:  gap < -0.3   (C << S -- boredom zone)
```

For flow state, the target is `Challenge` and `Appropriate` combined -- a gap of -0.1 to +0.3. This is the flow channel expressed as a difficulty gap range.

The `FocusDegradationService` already classifies `FocusLevel.Flow` at score >= 0.8, which corresponds to high attention, high engagement, improving accuracy trend, and sustained vigilance. This is Cena's operational definition of flow: the student is focused, engaged, learning, and not fatigued.

### 1.3 Flow State Duration Research

- **Kotler (2014, *The Rise of Superman*)**: Flow states in expert performers last 90-120 minutes, but these are physical/creative performers, not students.
- **Classroom research (Bunce et al., 2010, J. Chemical Education)**: Students' attention lapses begin at 5-7 minutes in lectures, but active learning extends this significantly.
- **Digital learning (Smart Learning Environments, 2021)**: Behavioral flow in gamified learning systems averages 8-12 minutes per flow episode, with multiple episodes per session separated by micro-transitions.
- **Adolescent attention (Rideout et al., 2022, Common Sense Media)**: Typical sustained attention for ages 16-18 on digital tasks is 12-18 minutes before seeking novelty.

For Cena's target population (Israeli 16-18 year olds on mobile), the realistic flow episode target is **8-15 minutes**, with session designs that create 2-3 flow episodes per 25-minute session.

---

## 2. Adaptive Difficulty Systems

### 2.1 Zone of Proximal Development (Vygotsky)

Vygotsky (1978, *Mind in Society*, Harvard University Press) defined the ZPD as "the distance between the actual developmental level as determined by independent problem solving and the level of potential development as determined through problem solving under adult guidance or in collaboration with more capable peers."

In Cena's implementation, the ZPD translates to the item selection algorithm in `LearningSessionActor.HandleNextQuestion`:

```
// Current implementation: prioritize concepts closest to P(known) = 0.5
// This maximizes information gain per question
double zpdScore = Math.Abs(mastery - 0.5);
```

**Research-informed refinement:** The optimal target is not exactly P(known) = 0.5 for flow. The 0.5 target maximizes diagnostic information gain (CAT theory), but flow research shows optimal engagement at a slightly higher success rate:

| Target P(correct) | Effect | Source |
|---|---|---|
| 0.50 | Maximum information gain (CAT optimal) | Lord (1980), *Applications of IRT* |
| 0.60-0.65 | Engagement sweet spot for difficult content | Squirrel AI internal target |
| 0.70-0.75 | Duolingo/ALEKS target for retention | Settles & Meeder (2016, ACL) |
| 0.80-0.85 | "Desirable difficulty" ceiling | Bjork (2011) |
| 0.85+ | Too easy -- boredom risk | Csikszentmihalyi (1990) |

**Recommendation for Cena:** Use a dynamic target that shifts based on detected focus level:

```
Flow state (FocusLevel.Flow):        target P(correct) = 0.55-0.65 (challenge them)
Engaged (FocusLevel.Engaged):       target P(correct) = 0.65-0.75 (standard ZPD)
Drifting (FocusLevel.Drifting):      target P(correct) = 0.75-0.85 (ease up, rebuild confidence)
Fatigued (FocusLevel.Fatigued):      target P(correct) = 0.85-0.90 (gentle, success-oriented)
```

This maps to the existing `CognitiveLoadService.RecommendDifficultyAdjustment` but with finer granularity.

### 2.2 Detecting Flow vs Frustration vs Boredom

Cena already has this in the `DisengagementClassifier`. The three states and their behavioral signatures:

**Flow (FocusLevel.Flow, score >= 0.8):**
- Response times are consistent (low RT variance -- high attention score)
- Accuracy is moderate-to-high and stable or improving
- Student is voluntarily interacting (hints, annotations -- high engagement score)
- No app backgrounding
- Session time past personal peak but focus sustained

**Frustration (mapped to ConfusionState.ConfusionStuck + high fatigue):**
- Rising response times (RT ratio > 1.3)
- Declining accuracy (below 0.5)
- High backspace count and answer change count
- Wrong on previously mastered concepts (`_lastWrongOnMastered`)
- Hint requests increasing but not helping

**Boredom (DisengagementType.Bored_TooEasy or Bored_NoValue):**
- Fast correct answers (RT ratio < 0.7 + accuracy > 0.85)
- No voluntary interactions (low hint rate, no annotations)
- App backgrounding rate > 0.3
- Declining engagement trend despite high accuracy

### 2.3 Difficulty Curves

**Linear difficulty progression:** Question difficulty increases at a constant rate per concept. Simple to implement but ignores student ability. Used only for fixed diagnostic quizzes.

**Logarithmic difficulty progression:** Difficulty increases rapidly at first (when student has many easy concepts to learn) and slows as mastery deepens. Matches the natural learning curve where early gains are large and late gains are incremental.

```
difficulty_target = base + (max - base) * log(1 + mastery * k) / log(1 + k)
```

Where `k` controls the curve steepness (default k=5). This produces rapid early progression and a gradual approach to maximum difficulty.

**Dynamic difficulty (Cena's approach):** Difficulty is not a fixed curve but responds to real-time signals. The `CognitiveLoadService` already provides the core mechanism:

```
fatigue >= 0.7 -> Ease (reduce difficulty)
fatigue <= 0.3 AND currentDifficulty < 8 -> Increase
otherwise -> Maintain
```

The flow-optimized enhancement adds focus-level-aware targeting per the table in Section 2.1.

### 2.4 Item Response Theory (IRT) for Question Calibration

Cena's existing `intelligence-layer.md` Flywheel 3 specifies IRT 2-parameter logistic model calibration:

```
P(correct | theta, a, b) = 1 / (1 + exp(-a * (theta - b)))
```

Where:
- `theta` = student ability (from BKT mastery probability)
- `a` = item discrimination (how well the question distinguishes high from low ability)
- `b` = item difficulty (the ability level where P(correct) = 0.5)

For flow-optimized item selection, the algorithm becomes:

```
1. Compute flow-adjusted target probability T from focus level (Section 2.1)
2. For each candidate item i, compute P_i = IRT(theta_student, a_i, b_i)
3. Select item where |P_i - T| is minimized
4. Tiebreaker: prefer items with higher discrimination (a), then items covering
   spaced-repetition-due concepts
```

This ensures the student always receives questions that are appropriately challenging for their current focus state, maintaining the flow channel.

### 2.5 DifficultyActor Per Student

The actor hierarchy for difficulty management maps to the existing architecture:

```
StudentActor (long-lived, per-student)
  |-- LearningSessionActor (session-scoped)
  |     |-- TutorActor (conversation-scoped, for confusion/questions)
  |-- StagnationDetectorActor (cross-session analysis)
```

A dedicated `DifficultyActor` is NOT needed as a separate actor because difficulty management is already distributed across:

1. **Item selection** (ZPD scoring) lives in `LearningSessionActor.HandleNextQuestion`
2. **Difficulty adjustment recommendations** live in `CognitiveLoadService.RecommendDifficultyAdjustment`
3. **Difficulty gap classification** lives in `DifficultyGap.cs`
4. **Cross-session difficulty calibration** lives in Flywheel 3 (IRT parameter refinement)

Adding a separate DifficultyActor would introduce an extra message hop on the hot path without architectural benefit. The difficulty state is inherently session-scoped (within-session adjustments) and student-profile-scoped (cross-session learning), both of which are already covered by the existing actor hierarchy.

**However**, a `FlowMonitorActor` is architecturally justified as a new child of `LearningSessionActor`. See Section 12 for its design.

---

## 3. Session Design for Flow

### 3.1 Optimal Session Length by Age Group

Research-informed session length recommendations:

| Age Group | Grade Range | Optimal Session | Warm-up | Core Challenge | Cool-down | Source |
|---|---|---|---|---|---|---|
| 5-7 (K-2) | N/A for Cena | 5-8 min | 1 min | 3-5 min | 1-2 min | Ruff & Lawson (1990), Developmental Psychology |
| 8-11 (3-5) | N/A for Cena | 10-15 min | 2 min | 6-10 min | 2-3 min | Bunce et al. (2010); Rideout (2022) |
| 12-15 (6-9) | N/A for Cena | 15-20 min | 3 min | 10-14 min | 2-3 min | Wilson & Korn (2007) |
| **16-18 (10-12)** | **Cena target** | **20-30 min** | **3-5 min** | **12-20 min** | **3-5 min** | Bunce (2010); Rideout (2022); Cena default 25 min |
| 18+ (adult) | Future expansion | 25-50 min | 3-5 min | 15-35 min | 5-10 min | Pomodoro: 25 min; Cal Newport: 60-90 min deep work blocks |

Cena's existing `DefaultSessionMinutes = 25` and `MaxSessionMinutes = 45` align with the 16-18 age group research. The per-student adaptive range of 12-30 minutes (from `system-overview.md`) correctly captures individual variation.

### 3.2 Session Architecture: The Flow Arc

Every Cena session follows a three-phase arc designed to build, sustain, and gracefully exit flow:

#### Phase 1: Warm-Up (3-5 minutes, ~3-4 questions)

**Purpose:** Activate prior knowledge, build confidence, establish rhythm.

**Difficulty:** Below ZPD center. Target P(correct) = 0.80-0.90. Questions should feel achievable but not insulting.

**Question selection algorithm:**
```
1. Select from spaced-repetition review-due concepts (already mastered, due for recall)
2. If no reviews due, select recently-mastered concepts for reinforcement
3. Never start with new material -- always begin with known territory
```

**UI behavior:**
- Subtle background transition from neutral to learning environment
- Knowledge graph visible but small in background, with the target concept highlighted
- First question auto-presented (no "tap to start" delay)
- Feedback is generous: "Great recall!" on correct, gentle redirect on wrong
- No gamification chrome visible yet

**Flow psychology rationale:** Csikszentmihalyi found that flow requires a "runway" -- skill confidence must be established before challenge increases. Starting with easy familiar material activates the student's competence schema and produces small dopamine rewards that prime the brain for sustained engagement.

#### Phase 2: Core Challenge (12-20 minutes, ~8-15 questions)

**Purpose:** Sustained learning in the flow channel. This is where new mastery happens.

**Difficulty:** Progressive from ZPD center to upper ZPD. Target P(correct) follows the focus-aware dynamic from Section 2.1.

**Question selection algorithm:**
```
1. Select concept with lowest mastery among session targets
2. Within concept, select item closest to focus-adjusted P(correct) target
3. Interleave concepts every 3-4 questions (Rohrer et al., 2015: interleaving produces
   better long-term retention than blocked practice)
4. Insert spaced-repetition review items every 5th question (prevents cognitive fatigue
   from constant new learning)
```

**Progressive challenge escalation:**
```
Questions 1-3 in core phase:  target P(correct) = 0.70-0.75  (establish competence)
Questions 4-6:                target P(correct) = 0.65-0.70  (push into challenge)
Questions 7-9:                target P(correct) = 0.60-0.65  (peak challenge)
Questions 10+:                target P(correct) = 0.65-0.75  (sustain, don't over-push)
```

If `FocusLevel` drops below `Engaged`, reduce by one tier. If `FocusLevel` reaches `Flow`, maintain or gently increase.

**Microbreak integration:** The existing `MicrobreakScheduler` triggers every 8 questions or 10 minutes. During core phase, microbreaks are 60 seconds by default, 90 seconds if focus is drifting. The critical flow protection rule: **never interrupt FocusLevel.Flow with a microbreak** (already implemented in `MicrobreakScheduler.ShouldTrigger`).

**UI behavior:**
- Full-screen immersive mode: only the question and answer input visible
- Progress indicator is a thin, ambient bar at the top showing concept mastery advancement
- Correct answer: green pulse radiates outward, mastery bar advances
- Wrong answer: gentle orange glow, explanation offered (gated by `DeliveryGate`)
- Concept mastery threshold crossed (P(known) >= 0.85): celebration micro-animation (2 seconds, non-disruptive), node lights up on miniature graph

#### Phase 3: Cool-Down (3-5 minutes, ~2-4 questions)

**Purpose:** Consolidate learning, provide closure, set up re-engagement hook.

**Difficulty:** Below ZPD center. Return to P(correct) = 0.80-0.90. End on success.

**Question selection algorithm:**
```
1. Select 1-2 questions from concepts mastered during this session (retrieval practice)
2. Select 1 question from the warm-up concepts (bookend structure)
3. Final question should be the easiest of the set -- end on a win
```

**UI behavior:**
- Gradual transition back from immersive mode: navigation elements fade back in
- Knowledge graph expands to show full session progress
- Session summary screen:
  - "You mastered [N] concepts"
  - "Your graph grew by [N] nodes"
  - Knowledge graph animation showing new nodes lighting up
  - Tomorrow's preview: "Next up: [concept name]" with the node highlighted on the graph
  - Streak counter update (if applicable)
  - Microbreak compliance note: "You took [N] breaks -- great for your focus"

**Flow psychology rationale:** The "end peak" effect (Kahneman, 1999, *Well-Being: Foundations of Hedonic Psychology*) means the final moments of an experience disproportionately shape memory of the entire session. Ending on easy successes with a satisfying summary creates a positive memory that increases return probability.

### 3.3 "Just One More" Moments

The "just one more" mechanic is the single most important engagement driver in Duolingo's design. It works by creating a state of incomplete closure -- the student has momentum and stopping feels like leaving value on the table.

**Cena implementation: Mastery Proximity Nudge**

When the student's mastery on a concept reaches 0.70-0.84 (close to the 0.85 mastery threshold) at the end of the scheduled session:

```
Display: "You're 2 questions away from mastering [concept name]! Keep going?"
[Continue (2 min)]  [End Session]
```

**Rules:**
- Only trigger once per session (never chain multiple "just one more" prompts)
- Only trigger if `FocusLevel` is `Engaged` or `Flow` (never for `Drifting`/`Fatigued`)
- Only trigger if total session time < `MaxSessionMinutes` (45 min)
- The "2 questions" estimate must be accurate: compute from BKT parameters how many correct answers are needed to cross 0.85
- If student chooses "Continue", shift to cool-down mode immediately after mastery is achieved

**Additional "just one more" hooks:**
- **Streak extension:** "You're on a 5-question streak! One more to beat your record?"
- **Concept connection:** "Mastering [concept] unlocks [next concept] -- want to see what's next?"
- **Review completion:** "You have 3 review items left for today. Knock them out? (~2 min)"

### 3.4 Session Completion Satisfaction Design

**The Summary Screen** is not just a report -- it is a reward. Design principles:

1. **Show growth, not grades.** Display mastery advancement (e.g., "+12% on calculus integration"), not scores (e.g., "7/10 correct").
2. **Animate the knowledge graph.** New nodes light up in sequence. New edges appear with a connecting animation. This is the visual payoff for the session's effort.
3. **Compare to past self, not peers.** "Your accuracy on this concept improved 15% since last week" -- never "You scored better than 72% of students."
4. **Set tomorrow's hook.** Preview the next concept with a single sentence: "Tomorrow: the chain rule connects derivatives to compositions." The preview primes retrieval and creates anticipatory motivation.
5. **Respect the exit.** One-tap dismissal. No mandatory sharing, rating requests, or upsell at this moment.

### 3.5 Break Reminders That Do Not Break Flow

The existing `MicrobreakScheduler` and `FocusDegradationService.RecommendBreak` handle this well. The key flow-preservation rules:

**Rule 1: Never interrupt Flow.** If `FocusLevel.Flow` and `FocusScore >= 0.85`, suppress all break suggestions. The student is in the optimal state -- breaking it is worse than the marginal fatigue benefit.

**Rule 2: Suggest, don't mandate.** Microbreaks are always skippable. The student retains control (flow condition 6). After 3 consecutive skips, stop suggesting for the rest of the session (already implemented: `MaxConsecutiveSkipsBeforeDisable = 3`).

**Rule 3: Transition, don't interrupt.** Break suggestions appear between questions, never mid-problem. They slide in gently from the bottom of the screen and auto-dismiss after 5 seconds if the student begins the next question.

**Rule 4: Communicate benefit, not obligation.** "60-second stretch -- your focus resets" is better than "You've been studying for 15 minutes, take a break." The former frames the break as a performance enhancer; the latter frames it as a limitation.

**Rule 5: Re-entry is instant.** After a microbreak, the next question is pre-loaded and waiting. No transition screen, no "loading," no "welcome back" delay. One tap to resume.

---

## 4. Immersive UI Patterns

### 4.1 Full-Screen Immersive Learning Mode

During core challenge phase, the Flutter app enters immersive mode:

```
SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky)
```

**What is hidden:**
- System status bar (time, battery, signal strength)
- System navigation bar
- App navigation (bottom tabs, sidebar)
- Gamification chrome (XP counter, streak indicator)
- Chat/messaging indicators
- Knowledge graph (miniaturized or hidden)

**What remains visible:**
- The question content (text, images, diagrams, formulas)
- Answer input area
- A thin (4px) progress bar at the very top showing concept mastery advancement
- A small, unobtrusive "pause" icon in the top-right corner (tap to access menu, skip, end session)
- Hint button (only if scaffolding level permits hints)

**Transition animation:** When entering immersive mode from the dashboard, the knowledge graph shrinks and fades into the top-left corner while the question card expands to fill the screen. Duration: 400ms, ease-out curve. The reverse happens when exiting.

### 4.2 Distraction Elimination

**During active problem-solving (question displayed, awaiting answer):**
- OS-level Do Not Disturb requested (Flutter: `flutter_dnd` plugin or iOS Focus mode API)
- Push notifications suppressed (not queued -- they still arrive but are silently held until session pause or end)
- If the student backgrounds the app: on return, display the exact same question state without any "welcome back" overlay. Immediate continuation.
- No "session paused" screen unless the app has been backgrounded for > 5 minutes

**Between questions (brief transition window):**
- Navigation controls briefly accessible (subtle slide-in from edges)
- Microbreak suggestions can appear
- Session progress visible for 1-2 seconds before next question loads

### 4.3 Progress Indicators That Do Not Distract

**The Mastery Whisper Bar:**
A 4px-tall gradient bar at the very top of the screen. Colors shift from left to right:
- Dark gray (0% mastery) through amber (50%) through green (85%+ mastered)
- The bar advances smoothly after each correct answer (CSS/Flutter animation, 300ms ease-out)
- The bar never decreases during a session (even on wrong answers, it holds position -- decline is tracked internally but the visual only shows forward progress to maintain motivation)
- No numbers, no labels, no percentage text. Just color and position.

**Why this works:** The bar provides condition 4 (immediate feedback) and condition 8 (time transformation) simultaneously. The student peripherally senses progress without allocating attention to parse numbers. The lack of decline prevents the emotional penalty that explicit score decreases produce.

**Concept Completion Burst:**
When a concept's mastery crosses the 0.85 threshold:
- Brief (1.5 second) particle burst animation emanating from the mastery bar
- Subtle haptic feedback (iOS: `UIImpactFeedbackGenerator.impactOccurred(.medium)`)
- The bar segment for that concept shifts from amber to green
- No text overlay, no popup, no modal. The animation is the celebration.
- A miniature knowledge graph node pulses once in the top-left corner

### 4.4 Ambient Feedback (Color Shifts and Subtle Animations)

**Background Color Temperature:**
The background subtly shifts color temperature based on difficulty frame:

| Difficulty Frame | Background Shift | Rationale |
|---|---|---|
| Stretch | Warm (very subtle amber tint, +2K) | Warmth = encouragement for hard challenges |
| Challenge | Neutral to slightly warm (+1K) | Slight warmth maintains motivation |
| Appropriate | Pure neutral (design system default) | No distraction at ZPD center |
| Expected | Neutral to slightly cool (-1K) | Coolness = calm for easy material |
| Regression | No shift (neutral) | Don't signal "easy" visually -- investigation pending |

These shifts are barely perceptible consciously but contribute to emotional calibration. The shift happens over 800ms to prevent conscious detection.

**Answer Feedback Animations:**

- **Correct answer:** Green pulse radiates from the answer area outward (200ms). The question card slides up smoothly and the next question slides in from the right (300ms).
- **Wrong answer:** Soft orange glow around the answer area (300ms). No harsh red, no X mark, no "wrong" text. The card stays in place (the student is not ejected from the problem). Explanation offer slides up from the bottom if not suppressed by the `DeliveryGate`.
- **Correct after hint:** Teal pulse (between green and blue). Acknowledges success while noting the hint assistance. BKT credit is already reduced by `IHintAdjustedBktService`.

### 4.5 Sound Design for Flow

**Principles:**
- Sound is opt-in. Default state: ambient sounds OFF, feedback sounds ON (low volume).
- No mandatory audio. All learning works in silent mode.
- Audio cues must be brief (< 500ms) and non-startling.

**Feedback Sounds (when enabled):**

| Event | Sound | Duration | Notes |
|---|---|---|---|
| Correct answer | Soft ascending chime (C5-E5-G5 arpeggio) | 300ms | Gentle, not triumphant |
| Wrong answer | Low, warm tone (G3) | 200ms | Not a buzzer. Not negative. Just "hmm." |
| Concept mastered | Melodic phrase (C5-E5-G5-C6) | 800ms | The "level up" equivalent |
| Streak milestone (5, 10) | Two quick notes (G4-C5) | 250ms | Acknowledges without interrupting |
| Session complete | Descending warm chord (C5-G4-E4-C4) | 1200ms | Resolution, closure |
| Microbreak start | Soft bell | 400ms | Alerting but calm |

**Ambient Sounds (optional, "Study Atmosphere" mode):**
- Lo-fi instrumental music (no lyrics -- lyrics activate language processing and compete with math/science cognition)
- White/brown noise options
- Library atmosphere (quiet page turns, distant murmur)
- Rain sounds
- Volume auto-reduces during question display and increases during transitions/breaks
- Audio continues through microbreaks (maintains atmosphere continuity)

---

## 5. Micro-Flow States

### 5.1 Two-Minute Flow States for Quick Review

**"Quick Review" Mode:** For spaced repetition review items, design 2-minute micro-sessions:

```
Target: 5-8 review items in 2 minutes
Question type: recall-focused (no multi-step problems)
Difficulty: P(correct) = 0.75-0.85 (recall, not new learning)
UI: Stacked card layout (swipe right = correct, swipe left = unsure)
Feedback: Immediate (card animates off screen with green/orange edge)
Timer: Visible countdown (unlike full sessions, urgency is part of the design)
```

**Flow characteristics of Quick Review:**
- Time pressure creates artificial challenge even for easy items (flow condition 1)
- Rapid card cycling produces action-awareness merging (condition 2)
- Clear goal: "Review 8 items before the timer runs out" (condition 3)
- Instant swipe feedback (condition 4)
- 2 minutes is short enough that focus is never an issue (no fatigue risk)

**When to offer Quick Review:**
- App open but student has not started a full session (casual engagement capture)
- Notification-driven: "3 concepts due for review -- 2-minute quick recall"
- Post-session bonus: "Review what you just learned? 2 minutes."

### 5.2 Flashcard Flow Design

Flashcards are a micro-flow opportunity when designed for speed and rhythm:

**Card presentation rhythm:**
- Card appears: 0ms
- Student reads and processes: student-controlled (no timer)
- Student taps "Show Answer" or swipes
- Answer revealed: immediate (0ms transition)
- Student self-rates (correct/unsure/wrong): one tap
- Next card: 200ms slide-in animation

**Key: the rhythm itself is the flow enabler.** Consistent timing (tap-reveal-rate-next) creates a predictable cadence that allows the student to fall into automatic processing, freeing cognitive resources for actual recall.

**Haptic rhythm:** Each card tap produces light haptic feedback (`UIImpactFeedbackGenerator.impactOccurred(.light)`). The haptic creates a physical rhythm that anchors the flow state in the body, not just the mind.

### 5.3 Speed Drill Engagement

**"Challenge Mode" -- Voluntary Speed Drill:**

Available after mastering a concept (P(known) >= 0.85). Not for learning -- for consolidation and automaticity.

```
Format: 10 questions, all from mastered concepts, timed
Time per question: 15 seconds for MCQ, 30 seconds for numeric
Scoring: points per correct answer * speed bonus (faster = more points)
Leaderboard: opt-in, anonymous, weekly reset
```

**Flow enablers:**
- High skill + time pressure = challenge-skill balance at high level
- Score counter visible (unlike learning sessions -- this is a game, not a lesson)
- Streak counter within the drill ("5 in a row!")
- Personal best tracking ("Your best: 8/10 in 45 seconds")

**Safeguard:** Speed drills are only available for mastered content. The system never gamifies new learning with time pressure, as time pressure on novel material produces anxiety, not flow.

### 5.4 Quick Quiz Flow Patterns

**"Daily Checkpoint" -- 5-question mixed quiz:**

```
Duration: 3-5 minutes
Question selection: 2 review items + 2 recent-session items + 1 diagnostic probe
Difficulty: moderate (P(correct) = 0.70 target)
Purpose: engagement touch-point, retention check, data collection for BKT
```

**Flow design:**
- Auto-starts on app open if the student has been away > 24 hours
- No session setup screen -- first question appears within 1 second of opening
- Results screen is the knowledge graph with subtle updates
- Segues naturally into "Want to continue with a full session?"

---

## 6. Anti-Flow Patterns to Avoid

### 6.1 Patterns That Destroy Flow and Must Be Prohibited

| Anti-Pattern | Why It Kills Flow | Cena Mitigation |
|---|---|---|
| **Interstitial ads between questions** | Breaks action-awareness merging (condition 2). Forces context switch. | Cena has no ads. Premium model eliminates this risk entirely. |
| **Forced notifications during session** | Destroys concentration (condition 5). Student must process irrelevant information. | DND mode during sessions. All notifications held until session end. |
| **Mandatory social sharing** | Forces self-consciousness (violates condition 7). Interrupts task focus. | All social features are opt-in, never mid-session. |
| **Loading screens between questions** | Breaks temporal flow (condition 8). Creates dead time the student must wait through. | Pre-fetch next question during answer evaluation. Target: 0ms perceived load time. |
| **"You got it wrong!" modal dialogs** | Negative emotional spike disrupts the flow channel. Creates approach anxiety. | No modals. Wrong answers use ambient feedback (orange glow). Explanations are inline. |
| **Mandatory tutorials on first question** | Delays engagement. Student wants to learn, not learn about learning. | Tutorial elements are contextual tooltips that appear on first encounter of each feature, not upfront. |
| **"Rate this app" prompts during session** | Maximum flow disruption. Commercial interest overrides user experience. | Never prompt during sessions. Rate prompts only after 10+ completed sessions, on the dashboard. |
| **Complex navigation to find content** | Cognitive overhead unrelated to learning. Violates clear goals (condition 3). | Session content is system-selected. Student does not navigate to find questions. |
| **Gamification popups mid-question** | "You earned 50 XP!" popup during problem-solving forces attention split. | XP/badge notifications are silent during questions. Shown in end-of-session summary. |
| **Countdown timers on learning questions** | Time pressure on novel material causes anxiety, not flow. | No timers during learning. Timers only in voluntary speed drills on mastered content. |

### 6.2 Subtle Anti-Patterns to Monitor

| Pattern | Risk | Detection | Response |
|---|---|---|---|
| Too-easy warm-up (> 5 easy questions) | Boredom in warm-up prevents flow onset | Track warm-up abandonment rate | Limit warm-up to 3-4 questions; escalate faster |
| Explanation verbosity | Long explanations break session rhythm | Track "skip explanation" rate | Use `DifficultyGap.SuggestedMaxTokens` to cap length |
| Hint over-scaffolding | Too many hints remove challenge (kills flow) | Track hint-dependent success rate | Already handled by `ScaffoldingService.GetScaffoldingMetadata.MaxHints` |
| Achievement notification clustering | Multiple badges earned in quick succession overwhelm | Track badges/minute rate | Queue achievements; show max 1 per session transition |
| Repetitive question types | Same format creates habituation (boredom) | Track question-type distribution per session | Enforce minimum 3 different question types per session |

---

## 7. Deep Work Integration

### 7.1 Deep Focus Mode (Cal Newport Integration)

Cal Newport (2016, *Deep Work: Rules for Focused Success in a Distracted World*, Grand Central Publishing) defines deep work as "professional activities performed in a state of distraction-free concentration that push your cognitive capabilities to their limit."

For Cena's target population (16-18 year olds preparing for Bagrut), deep work translates to extended study sessions of 45-90 minutes for exam preparation:

**"Deep Study" Mode:**

```
Duration: 45-90 minutes (student-selected, with system recommendations)
Structure: 2-3 standard flow-arc sessions back-to-back with 5-minute recovery breaks
Content: Single subject focus (no subject switching)
UI: Maximum immersion (no notifications, no gamification, minimal chrome)
Scheduling: Student can schedule deep work blocks in advance
DND: Full OS-level Do Not Disturb activated
```

**Session structure within Deep Study:**
```
Block 1 (25 min): Standard flow arc (warm-up -> challenge -> cool-down)
Recovery break (5 min): System-guided (stretch, water, eyes rest)
Block 2 (25 min): Standard flow arc (starts with review of Block 1 mastery)
Recovery break (5 min): System-guided
Block 3 (optional, 15-25 min): Shortened arc focused on weakest areas from Blocks 1-2
Summary: Combined progress review across all blocks
```

**Microbreak handling in Deep Study:**
- Within each block: standard microbreak scheduling (every 8 questions or 10 minutes)
- Between blocks: mandatory 5-minute recovery break (not skippable -- the science supports this: Biwer et al., 2023, found systematic breaks improve efficiency + mood)
- If student tries to skip the recovery break: "Your focus resets better with the full break. You'll learn faster afterward." (framed as performance enhancement, not restriction)

### 7.2 Scheduled Deep Work Blocks

Integration with the student's study schedule:

**Calendar-style scheduling:**
- Student picks days and times for Deep Study sessions
- System suggests optimal times based on past session performance by time-of-day (Flywheel 6 data: circadian rhythm analysis)
- Push notification 10 minutes before scheduled block: "[Subject] deep study starts in 10 min. Find a quiet spot."
- Notification 2 minutes before: "Ready? Silence your phone and open Cena."
- If student does not open the app within 15 minutes of scheduled time, reschedule notification appears

**Optimal time-of-day recommendations:**

Research (Neuroscience News 2025, from `engagement-signals-research.md`) shows 9-34% performance variation based on time of day. Cena builds a per-student circadian profile:

```
circadian_score[hour] = mean_accuracy[hour] * (1 - mean_fatigue_score[hour])
```

Computed from historical session data. The system recommends scheduling Deep Study during the student's top 2 circadian hours.

### 7.3 Digital Wellbeing Integration

**Screen Time Awareness Without Guilt:**

Cena is a learning app, not social media. The digital wellbeing approach must differ fundamentally from doom-scrolling prevention. Principles:

1. **Learning time is productive time.** Never treat study sessions as "screen time" in the negative sense. If the student has spent 90 minutes in Deep Study, the app should celebrate, not warn.
2. **But total daily screen time matters.** If the student has been on their phone for 6+ hours (detectable via iOS Screen Time API / Android Digital Wellbeing API), and it is late evening, suggest: "You've had a long day on screens. Tomorrow's session will be more effective with fresh eyes."
3. **Respect quiet hours.** No push notifications between 22:00-07:00 (configurable by student/parent). The `OutreachSchedulerActor` already respects quiet hours.
4. **Study timer visibility (opt-in).** Students who want accountability can enable a visible session timer and daily study time tracker on the dashboard. This is never forced -- some students find timers motivating; others find them anxiety-inducing.
5. **Weekly study report.** "This week: 3h 42min of focused study across 8 sessions. You mastered 12 concepts." Framed as accomplishment, not consumption.

---

## 8. Engagement Loops vs Flow

### 8.1 Short Engagement Loops (Daily)

The daily engagement loop drives Day 1 to Day 7 retention:

```
Trigger: Push notification or app open
  -> Quick checkpoint quiz (2-3 min, see Section 5.4)
  -> See updated knowledge graph
  -> See streak counter advance
  -> "Start a session?" prompt
  -> Full flow-arc session (20-25 min)
  -> Session summary with graph growth
  -> Streak reinforcement
  -> Tomorrow's preview
  -> Exit
```

**Loop timing:**
- Total daily engagement target: 20-30 minutes (one full session)
- Minimum daily engagement for streak: 5 minutes (1 checkpoint quiz)
- Maximum daily engagement before wellbeing check: 90 minutes (3 sessions)
- Above 90 minutes: subtle suggestion to take an extended break (not a block)

### 8.2 Long Engagement Loops (Semester)

The semester loop drives long-term retention and goal completion:

```
Semester goal: "Master all 5-unit Math topics by June Bagrut"
  -> Broken into monthly milestones: "March: complete derivatives unit"
  -> Broken into weekly targets: "This week: chain rule + implicit differentiation"
  -> Broken into daily sessions: "Today: 3 questions on chain rule"
  -> Daily actions feed back up: weekly progress ring, monthly milestone badge
  -> Monthly review: "You're 65% through the derivatives unit -- on track for June"
```

**Long loop gamification elements:**
- **Monthly milestone badges:** Visual badge earned for completing a knowledge cluster (e.g., "Calculus: Derivatives"). Displayed on profile and (optionally) knowledge graph.
- **Progress rings:** Weekly ring fills as daily study targets are met. 7/7 produces a "Perfect Week" badge.
- **Forecast tracker:** "At your current pace, you'll complete Math by May 15." Updated weekly. Shows as a timeline on the dashboard.

### 8.3 Sustained Engagement Without Addiction

The ethical line between engagement and addiction:

**Engagement (acceptable):**
- Student wants to continue because the material is interesting and they feel progress
- Student returns daily because they have a genuine goal (Bagrut preparation)
- Student feels satisfied after a session and closes the app
- Gamification reinforces real achievement (concept mastery)

**Addiction patterns (prohibited in Cena):**
- Loss aversion as primary motivator ("Your streak will break if you don't study today!")
- Variable reward schedules designed for compulsive checking (slot machine mechanics)
- Social pressure to maintain status (public leaderboards with real identity)
- FOMO-driven notifications ("Your friend just mastered [concept]!")
- Artificial scarcity ("Only 3 lives remaining!")

**Cena's ethical engagement design:**

1. **Streaks are soft.** Streaks can be "frozen" for up to 2 days per week without breaking. The message is "Life happens -- your streak is safe" rather than "YOU MUST STUDY TODAY."
2. **Notifications are educational, not guilt-based.** "You have 3 concepts due for review" (factual) not "You're falling behind!" (emotional manipulation).
3. **Leaderboards are anonymous and optional.** Weekly reset. No real names. No friend-vs-friend competition. Fully disabled by default for students under 16.
4. **Session end is clean.** No "infinite scroll" of content. Sessions have a clear beginning and end. The app does not auto-start the next session.
5. **Gamification decays by design.** The existing `GamificationRotationService` (FOC-011) rotates elements as they lose novelty (Zeng et al., 2024 meta-analysis: gamification effects decline after 1 semester). This prevents gamification from becoming the primary motivation source.
6. **Learning outcomes are the north star.** All A/B tests are evaluated against "Time Spent Learning Well" (TSLW, borrowed from Duolingo's approach) -- a metric that measures quality of learning, not raw engagement time.

### 8.4 The Engagement-Flow Relationship

Engagement loops and flow states are complementary but different:

| Dimension | Engagement Loop | Flow State |
|---|---|---|
| Time scale | Minutes to months | 8-15 minutes |
| Mechanism | Habit formation, goal setting, reward | Challenge-skill balance, immersion |
| Consciousness | Aware (checking streaks, setting goals) | Unaware (lost in the task) |
| Motivation source | External (streaks, XP) + internal (goals) | Purely internal (autotelic) |
| System design | Triggers, rewards, progress tracking | UI immersion, difficulty calibration |

**How they interact in Cena:**
- Engagement loops get the student to open the app and start a session
- Flow state keeps the student in the session and maximizes learning during it
- Post-flow engagement hooks (session summary, graph growth) close the loop and set up the next return
- Over time, as the student develops intrinsic motivation (autotelic experience), the external engagement loop elements become less important -- this is by design, and the `GamificationRotationService` reduces gamification weight accordingly

---

## 9. Re-engagement After Flow Break

### 9.1 "Welcome Back" Patterns

When a student returns to the app after being away, the re-engagement pattern depends on the absence duration:

| Absence Duration | Pattern | UI |
|---|---|---|
| < 5 minutes (app backgrounded briefly) | **Invisible resume.** No "welcome back." Just the same question, same state. | Question displayed exactly as left. No overlay. |
| 5-30 minutes (short break) | **Soft nudge.** Minimal transition. | "Ready to continue?" over the last question. One tap resumes. |
| 30 min - 24 hours (session expired) | **Quick recap.** | "Last time: you were working on [concept]. You got 6/8 right." + one-tap "Continue" to start new session targeting same concepts. |
| 1-3 days (normal gap) | **Checkpoint entry.** | Quick checkpoint quiz (3 questions) to re-establish mastery levels, then new session. Knowledge graph highlights what was recently learned. |
| 3-7 days (extended absence) | **Streak-aware return.** | "Welcome back! Your streak frozen successfully." Quick diagnostic on recent concepts. Spaced repetition items prioritized. |
| > 7 days (lapsed user) | **Re-onboarding.** | Mini-diagnostic (5 questions) to re-calibrate mastery. Knowledge graph shows "Your knowledge: strong areas in green, areas that need refresh in amber." Start session targeting amber areas. |

### 9.2 Quick Recap of Where Student Left Off

The `ResumeSessionRequest` message in `LearningSessionActor` already handles session state restoration:

```csharp
public record ResumeSessionRequest(
    string SessionId,
    string StudentId,
    string Subject,
    string Methodology,
    DateTimeOffset OriginalStartedAt,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double FatigueScoreAtCheckpoint);
```

For the Flutter client, the resume flow is:

```
1. App opens
2. Client checks local storage for suspended session state
3. If found AND < 30 minutes old:
   a. Send ResumeSessionRequest to actor
   b. Display last question state immediately from local cache
   c. When actor responds with ResumeSessionResponse, reconcile if needed
4. If found AND 30+ minutes old:
   a. Show recap card: "You were working on [concept]. Ready to pick up?"
   b. On "Continue": start new session targeting same concepts
   c. On "Something else": go to dashboard
5. If not found: standard session start flow
```

### 9.3 Low-Friction Resume (One Tap to Continue)

**The "Continue" Button:**
- Prominent, centered, primary color
- Text: "Continue: [concept name]" (not just "Continue" -- the student should know what they are returning to)
- Tapping it starts the session with zero additional screens (no subject picker, no goal setter, no warm-up screen)
- The warm-up phase uses review items from the previous session's concepts

**"Continue" is the default action everywhere:**
- Dashboard: largest button is "Continue" (not "Start New Session")
- Push notification: tap goes directly to "Continue" state
- Widget (iOS/Android home screen): shows "Continue: [concept]" and taps directly into session

### 9.4 Session State Preservation

Session state is preserved at three levels:

1. **Client-side (Flutter/local storage):** Current question, answer draft, scroll position, immersive mode state. Survives app backgrounding and brief termination. Restored instantly.

2. **Actor-side (LearningSessionActor state):** Session ID, questions attempted, fatigue score, methodology. Persisted via parent `StudentActor` to Marten event store. The `TutoringSessionDocument` stores the checkpoint.

3. **Offline queue (offline-sync-protocol.md):** If the student was offline, the sync protocol handles reconciliation with full three-tier event classification.

---

## 10. Measuring Flow in the App

### 10.1 Flow Proxy Metrics

Flow is a subjective experience that cannot be directly measured in a mobile app. However, behavioral proxies correlate strongly with self-reported flow (Smart Learning Environments, 2021):

| Metric | Flow Proxy | Measurement | Expected Value in Flow |
|---|---|---|---|
| **Session duration** | Time in flow channel | Total time where `FocusLevel == Flow` | > 8 minutes per 25-min session |
| **Voluntary continuation** | Autotelic motivation | Student continues past scheduled session end without prompting | > 30% of sessions |
| **Completion rate** | Task commitment | Percentage of sessions ended by student vs. system | > 85% student-initiated end |
| **Question-to-question latency** | Action-awareness merging | Mean time between question presentation and first interaction | < 5 seconds (student engages immediately) |
| **App backgrounding rate** | Concentration | Fraction of session time app was backgrounded | < 5% during flow episodes |
| **Hint avoidance in mastery zone** | Sense of control | Hint request rate when mastery > 0.5 | < 0.05 hints/question |
| **Answer change rate** | Confidence | Number of answer changes before submission | < 0.5 changes/question in flow |

### 10.2 Completion Rates

Track three completion metrics:

1. **Session completion rate:** Sessions where the student reaches the cool-down phase / total sessions started. Target: > 75%.
2. **Session arc completion rate:** Sessions where the student completes all three phases (warm-up + core + cool-down) / total sessions. Target: > 65%.
3. **Goal completion rate:** Concept mastery events achieved / concepts targeted at session start. Target: > 0.3 concepts mastered per session.

### 10.3 Voluntary Continuation

The strongest flow signal is the student choosing to continue without any prompt:

```
voluntary_continuation_rate = sessions_extended_beyond_scheduled_end / total_sessions
```

Where "extended" means the student answered at least 2 more questions after the system suggested ending. This is tracked as the `SessionExtended_V1` event (to be added -- not yet in event schemas).

### 10.4 Error Rate Patterns (U-Shaped = Good Difficulty)

Within a well-calibrated session, the error rate follows a U-shaped curve:

```
Phase 1 (warm-up):    Error rate ~15% (easy material, occasional carelessness)
Phase 2a (core start): Error rate drops to ~10% (student settling in)
Phase 2b (core peak):  Error rate rises to ~30-40% (challenging new material)
Phase 2c (core end):   Error rate ~25% (adaptation to challenge level)
Phase 3 (cool-down):   Error rate drops to ~10% (easy review material)
```

The U-shape across the session indicates proper difficulty calibration. A flat error rate suggests the difficulty is not being adjusted. A steadily increasing error rate suggests the student is being pushed too hard without recovery.

**Monitoring:**
- Compute per-session error curve (error rate per question index)
- Classify as U-shaped, flat, ascending, or descending
- U-shaped: target (70% of sessions)
- Ascending: session was too hard -- trigger difficulty recalibration
- Descending: session was too easy -- trigger difficulty increase
- Flat-high (> 40%): student is frustrated -- should have triggered methodology switch
- Flat-low (< 15%): student is bored -- should have triggered difficulty increase

### 10.5 Behavioral Signals of Frustration vs Boredom

The `DisengagementClassifier` already handles this. Summary for flow measurement:

**Frustration signals (flow is breaking upward -- challenge too high):**
- RT increasing + accuracy decreasing simultaneously
- High backspace/deletion count (uncertainty about answers)
- Answer changes before submission (anxiety)
- Wrong answers on previously mastered concepts
- Hint requests increasing but accuracy not improving

**Boredom signals (flow is breaking downward -- challenge too low):**
- Fast correct answers (RT ratio < 0.7)
- High accuracy (> 0.85) sustained for 5+ questions without difficulty increase
- No voluntary interactions (no hints, no annotations)
- App backgrounding between questions
- Short response times with low engagement trend

**Flow loss is detected within 2-3 questions** by the `FocusDegradationService`. The response time for intervention (difficulty adjustment or methodology switch) is the next question after detection -- no multi-question delay.

---

## 11. Flow-Optimized Screen Designs

### 11.1 Learning Activity Type: Multiple Choice Question (MCQ)

```
+--------------------------------------------------+
| [mastery whisper bar - 4px gradient]              |
|                                          [pause]  |
|                                                   |
|                                                   |
|   What is the derivative of sin(2x)?              |
|                                                   |
|   [ A ] 2cos(2x)           [ B ] cos(2x)         |
|                                                   |
|   [ C ] -2cos(2x)          [ D ] 2sin(2x)        |
|                                                   |
|                                                   |
|                                                   |
|                                                   |
|                                [hint] (if avail)  |
+--------------------------------------------------+
```

**Flow features:**
- No header, no navigation, no footer
- Question text is large (18sp) with generous line spacing
- Answer options are large touch targets (48dp minimum, 56dp recommended)
- One-tap answer: tapping an option immediately submits
- No "submit" button (reduces decision friction)
- Feedback appears inline: selected option pulses green or orange

### 11.2 Learning Activity Type: Numeric Input

```
+--------------------------------------------------+
| [mastery whisper bar]                             |
|                                          [pause]  |
|                                                   |
|   Evaluate the integral:                          |
|                                                   |
|        2                                          |
|       / (3x^2 + 1) dx = ?                        |
|       0                                           |
|                                                   |
|   +------------------------------------------+   |
|   |  [answer input field]                    |   |
|   +------------------------------------------+   |
|                                                   |
|                         [Submit]                  |
|                                                   |
|                                [hint] (if avail)  |
+--------------------------------------------------+
```

**Flow features:**
- Math rendering via Flutter-compatible LaTeX renderer (flutter_math_fork or katex_flutter)
- Numeric keypad auto-displayed (no keyboard switching)
- "Submit" button is explicit here (unlike MCQ) because partial entry needs protection
- Input field shows real-time LaTeX rendering of what student types
- Backspace count tracked silently for confusion detection

### 11.3 Learning Activity Type: Socratic Dialogue

```
+--------------------------------------------------+
| [mastery whisper bar]                             |
|                                          [pause]  |
|                                                   |
|   [Mentor avatar - small, unobtrusive]            |
|                                                   |
|   "You said the derivative of sin(x) is           |
|    -cos(x). Let's think about what happens         |
|    when x increases slightly from 0..."            |
|                                                   |
|   +------------------------------------------+   |
|   |  [student response input]                |   |
|   +------------------------------------------+   |
|   [Send]                                         |
|                                                   |
|   [I don't understand]    [Let me think]          |
+--------------------------------------------------+
```

**Flow features:**
- Conversational UI (chat-style) but without the distraction of a full chat interface
- Mentor avatar is small and consistent (not animated during conversation -- animation draws attention away from content)
- "I don't understand" maps to `AddAnnotationMessage(Kind: "confusion")` which triggers `TutorActor`
- "Let me think" dismisses the prompt and gives the student time without pressure
- Typing indicator during LLM response (< 1.5s P50 target per SLA)

### 11.4 Learning Activity Type: Knowledge Graph Exploration

```
+--------------------------------------------------+
| [Navigation bar - visible in exploration mode]    |
|                                                   |
|            o-green                                |
|           / \                                     |
|      o-green  o-amber ---- o-gray                 |
|         |      |                                  |
|      o-green  o-amber                             |
|                 \                                 |
|                  o-gray ---- o-gray               |
|                                                   |
|  [concept info panel]                             |
|  "Implicit Differentiation"                       |
|  Mastery: 72% (3 more questions to master)        |
|  Prerequisites: Chain Rule (mastered),            |
|                 Partial Derivatives (78%)          |
|  [Start Session on This Concept]                  |
+--------------------------------------------------+
```

**Flow features:**
- This is NOT a learning activity -- it is a navigation/motivation screen
- No immersive mode (full navigation visible)
- Tap any node to see concept info panel
- Tap "Start Session" to enter flow-arc session targeting that concept
- Node colors: green (mastered, P(known) >= 0.85), amber (in progress, 0.30-0.84), gray (not started, < 0.30)
- Graph is force-directed layout with pinch-zoom and pan
- Mastered nodes glow subtly. New nodes added during this visit pulse once.

### 11.5 Transition Animations That Maintain Flow

All transitions between screens within a session use consistent, fast animations:

| Transition | Animation | Duration | Easing |
|---|---|---|---|
| Dashboard to session start | Knowledge graph zooms into target concept, then fades to question | 500ms | ease-out |
| Question to question (correct) | Current card slides up; next slides in from right | 300ms | ease-in-out |
| Question to question (wrong, with explanation) | Explanation panel slides up from bottom; tapping "Next" slides both up together | 300ms | ease-out |
| Session to summary | Question card shrinks and floats to knowledge graph position; graph expands | 600ms | ease-out |
| Microbreak enter | Overlay slides up from bottom with blurred background | 400ms | ease-out |
| Microbreak exit | Overlay slides down; question is already loaded behind it | 300ms | ease-out |

**Critical rule: no animation may exceed 600ms.** Longer animations break temporal continuity and the student becomes aware of the transition as an event rather than experiencing it as a continuous flow.

---

## 12. Actor Architecture for Flow

### 12.1 FlowMonitorActor Design

A new `FlowMonitorActor` is proposed as a child of `LearningSessionActor`, responsible for synthesizing flow-state assessments from multiple signals and recommending session-level interventions.

**Actor placement:**
```
StudentActor
  |-- LearningSessionActor
  |     |-- TutorActor (existing)
  |     |-- FlowMonitorActor (new)
  |-- StagnationDetectorActor (existing)
```

**Messages:**

```csharp
// Input: sent by LearningSessionActor after each question
public record UpdateFlowState(
    double FocusScore,
    FocusLevel FocusLevel,
    double FatigueScore,
    DifficultyFrame DifficultyFrame,
    double AccuracyLast5,
    double RtVarianceLast5,
    int QuestionsInCurrentPhase, // warm-up, core, cool-down
    SessionPhase CurrentPhase,
    bool VoluntaryContinuation,
    int MicrobreaksTaken,
    int MicrobreaksSkipped);

// Output: responded to LearningSessionActor
public record FlowAssessment(
    FlowChannelPosition Position, // InFlow, AboveFlow (frustrated), BelowFlow (bored)
    double FlowScore,             // 0-1, composite
    double FlowDurationMinutes,   // cumulative time in flow this session
    int FlowEpisodesCount,        // number of distinct flow episodes
    SessionPhaseRecommendation PhaseRecommendation, // Stay, TransitionToCore, TransitionToCoolDown, EndSession
    DifficultyDirection DifficultyRecommendation,   // Increase, Maintain, Decrease
    bool ShouldSuggestContinuation, // "just one more" trigger
    string? ContinuationPrompt);    // localized prompt text

public enum FlowChannelPosition { InFlow, AboveFlow, BelowFlow, Transitioning }
public enum SessionPhase { WarmUp, Core, CoolDown }
public enum DifficultyDirection { Increase, Maintain, Decrease }
```

**Flow score computation:**

```csharp
// FlowMonitorActor.ComputeFlowScore
double flowScore =
    0.30 * focusScore +
    0.25 * challengeSkillBalance +    // 1.0 when DifficultyFrame is Challenge/Appropriate
    0.20 * consistencyScore +          // RT variance relative to baseline (low = good)
    0.15 * (1.0 - fatigueScore) +     // Inverse fatigue
    0.10 * voluntaryEngagement;        // Hints, annotations, approach changes

FlowChannelPosition position = flowScore switch
{
    >= 0.70 => FlowChannelPosition.InFlow,
    _ when difficultyFrame is DifficultyFrame.Stretch => FlowChannelPosition.AboveFlow,
    _ when difficultyFrame is DifficultyFrame.Expected or DifficultyFrame.Regression
        => FlowChannelPosition.BelowFlow,
    _ => FlowChannelPosition.Transitioning
};
```

**Session phase transitions:**

```csharp
SessionPhaseRecommendation DeterminePhaseRecommendation(
    SessionPhase current, int questionsInPhase, FlowChannelPosition position, double fatigueScore)
{
    return (current, questionsInPhase, position, fatigueScore) switch
    {
        (SessionPhase.WarmUp, >= 4, _, _) => SessionPhaseRecommendation.TransitionToCore,
        (SessionPhase.WarmUp, >= 3, FlowChannelPosition.InFlow, _)
            => SessionPhaseRecommendation.TransitionToCore, // Early transition if already in flow
        (SessionPhase.Core, _, _, >= 0.7) => SessionPhaseRecommendation.TransitionToCoolDown,
        (SessionPhase.Core, >= 15, _, _) => SessionPhaseRecommendation.TransitionToCoolDown,
        (SessionPhase.CoolDown, >= 3, _, _) => SessionPhaseRecommendation.EndSession,
        _ => SessionPhaseRecommendation.Stay
    };
}
```

### 12.2 SessionActor Flow Integration

The `LearningSessionActor` gains flow-aware item selection by incorporating `FlowAssessment`:

```csharp
// Enhanced HandleNextQuestion with flow-aware targeting
private Task HandleNextQuestion(IContext context, RequestNextQuestion req)
{
    // Get latest flow assessment
    var flowAssessment = _lastFlowAssessment;

    // Dynamic P(correct) target based on flow position
    double targetPCorrect = flowAssessment.Position switch
    {
        FlowChannelPosition.InFlow => 0.60,      // Challenge: keep them stretching
        FlowChannelPosition.AboveFlow => 0.75,    // Ease up: reduce frustration
        FlowChannelPosition.BelowFlow => 0.55,    // Challenge more: combat boredom
        _ => 0.65                                  // Default ZPD center
    };

    // Select concept and item closest to target
    // (enhanced version of existing ZPD scoring)
    string? bestConcept = null;
    double bestScore = double.MaxValue;

    foreach (var (conceptId, mastery) in req.MasteryMap)
    {
        if (mastery >= MasteryConstants.ProgressionThreshold) continue;

        // Distance from flow-adjusted target
        double distance = Math.Abs(mastery - (1.0 - targetPCorrect));

        // Phase-aware boosting
        if (_currentPhase == SessionPhase.WarmUp && req.ReviewDueConcepts.Contains(conceptId))
            distance *= 0.3; // Strong preference for review in warm-up

        if (_currentPhase == SessionPhase.CoolDown && mastery > 0.7)
            distance *= 0.5; // Prefer near-mastered for cool-down

        if (distance < bestScore)
        {
            bestScore = distance;
            bestConcept = conceptId;
        }
    }

    context.Respond(new NextQuestionResponse(
        ConceptId: bestConcept,
        Methodology: _methodology,
        FatigueScore: _fatigueScore));

    return Task.CompletedTask;
}
```

### 12.3 StagnationDetectorActor Flow Integration

The `StagnationDetectorActor` can use flow metrics as an additional stagnation signal:

**Flow absence as stagnation indicator:** If a student has not achieved `FocusLevel.Flow` in the last 5 sessions for a concept cluster, this is a strong signal that the current methodology is not creating engagement. Weight: 0.15 (reallocated from annotation sentiment's 0.10 and 0.05 from response time drift).

```csharp
// New signal: FlowAbsence
// Normalized: 1.0 if zero flow episodes in 5 sessions, 0.0 if flow in every session
double flowAbsenceSignal = 1.0 - (sessionsWithFlow / Math.Min(totalSessions, 5.0));
```

---

## 13. KPIs and Success Metrics

### 13.1 Flow State Achievement KPIs

| KPI | Definition | Target | Measurement |
|---|---|---|---|
| **Flow Episode Rate** | % of sessions containing at least one flow episode (FocusLevel.Flow for >= 3 minutes) | > 60% | `FocusScoreUpdated_V1` events |
| **Mean Flow Duration** | Average cumulative time in FocusLevel.Flow per session | > 8 minutes (per 25-min session) | Computed from FocusScoreUpdated_V1 time series |
| **Flow-to-Mastery Conversion** | % of concepts mastered during flow episodes vs non-flow | Track (expect > 1.5x) | Cross-reference ConceptMastered_V1 timestamps with flow windows |
| **Voluntary Continuation Rate** | % of sessions where student continues past scheduled end | > 25% | New event: SessionExtended_V1 |
| **Session Completion Rate** | % of sessions reaching cool-down phase | > 75% | SessionEnded_V1 with reason = "completed" |
| **Microbreak Compliance** | % of microbreak suggestions accepted | 40-70% (too high = too many breaks; too low = breaks annoying) | MicrobreakTaken_V1 / MicrobreakSuggested_V1 |

### 13.2 Adaptive Difficulty KPIs

| KPI | Definition | Target | Measurement |
|---|---|---|---|
| **ZPD Hit Rate** | % of questions where student's P(correct) was 0.55-0.80 | > 65% | Computed from IRT parameters + actual outcomes |
| **U-Curve Session Rate** | % of sessions with U-shaped error curve | > 50% | Per-session error rate analysis |
| **Difficulty Adjustment Latency** | Questions between focus degradation detection and difficulty response | <= 1 question | FlowAssessment.DifficultyRecommendation timing |
| **Frustration Episode Rate** | % of sessions with 3+ consecutive wrong answers on same concept | < 15% | ConceptAttempted_V1 analysis |
| **Boredom Episode Rate** | % of sessions with 5+ consecutive correct answers with RT ratio < 0.7 | < 10% | ConceptAttempted_V1 + behavioral signal analysis |

### 13.3 Session Design KPIs

| KPI | Definition | Target | Measurement |
|---|---|---|---|
| **Warm-up Duration** | Mean warm-up phase duration | 3-5 minutes | Phase transition tracking |
| **Cool-down Completion** | % of sessions that include cool-down phase | > 70% | Phase tracking |
| **"Just One More" Acceptance** | % of mastery proximity nudges accepted | > 40% | ContinuationPrompt tracking |
| **Day-1-to-Day-7 Retention** | % of new users returning on Day 7 | > 35% | User analytics |
| **Monthly Active Session Count** | Mean sessions per active user per month | > 15 | Session analytics |

### 13.4 Engagement Quality KPIs

| KPI | Definition | Target | Measurement |
|---|---|---|---|
| **Time Spent Learning Well (TSLW)** | Minutes where FocusLevel >= Engaged AND error rate in flow-appropriate range | > 15 min/session | Composite of focus + accuracy metrics |
| **Mastery Velocity** | Concepts mastered per hour of TSLW | Track (varies by subject difficulty) | ConceptMastered_V1 / TSLW |
| **Engagement Sustainability** | Ratio of Month-3 TSLW to Month-1 TSLW per student | > 0.8 (< 20% decay) | Longitudinal cohort analysis |
| **Gamification Dependency Ratio** | Sessions started via gamification trigger (streak, XP) vs self-initiated | < 0.4 (ideally students are self-motivated) | Session start trigger tracking |
| **Ethics Score** | Composite of: session length < 90min, notifications within hours, no loss-aversion language, streak freeze usage | > 0.90 | Automated compliance check |

### 13.5 Flow Events to Add to Event Schema

The following events should be added to `docs/event-schemas.md`:

```protobuf
message FlowEpisodeStarted_V1 {
  string student_id = 1;
  string session_id = 2;
  double flow_score = 3;       // score when flow was entered
  int32 question_number = 4;   // which question triggered flow
  string concept_id = 5;       // concept being studied at flow onset
}

message FlowEpisodeEnded_V1 {
  string student_id = 1;
  string session_id = 2;
  double duration_seconds = 3;  // how long the flow episode lasted
  string exit_reason = 4;       // "fatigue" | "boredom" | "frustration" | "session_end" | "microbreak" | "app_background"
  int32 questions_in_flow = 5;
  int32 concepts_mastered_in_flow = 6;
}

message SessionPhaseTransition_V1 {
  string student_id = 1;
  string session_id = 2;
  string from_phase = 3;        // "warm_up" | "core" | "cool_down"
  string to_phase = 4;
  int32 questions_in_phase = 5;
  double flow_score_at_transition = 6;
}

message SessionExtended_V1 {
  string student_id = 1;
  string session_id = 2;
  string continuation_trigger = 3; // "mastery_proximity" | "streak" | "self_initiated"
  int32 additional_questions = 4;
  double flow_score_at_extension = 5;
}
```

---

## Sources

### Flow Psychology
- Csikszentmihalyi, M. (1990). *Flow: The Psychology of Optimal Experience*. Harper & Row.
- Csikszentmihalyi, M. (1997). *Finding Flow: The Psychology of Engagement with Everyday Life*. Basic Books.
- Nakamura, J. & Csikszentmihalyi, M. (2002). "The Concept of Flow." In Snyder & Lopez (Eds.), *Handbook of Positive Psychology*, pp. 89-105. Oxford University Press.
- Kahneman, D. (1999). "Objective Happiness." In Kahneman, Diener & Schwarz (Eds.), *Well-Being: Foundations of Hedonic Psychology*. Russell Sage Foundation.

### Attention and Vigilance
- Warm, J.S. (1984). "An Introduction to Vigilance." In *Sustained Attention in Human Performance*. Wiley.
- Parasuraman, R. (1986). "Vigilance, monitoring, and search." *Handbook of Human Perception and Performance*, Vol. II. Wiley.
- Thomson, D.R. et al. (2022). "A vigilance decrement comes along with an executive control decrement." *Psychonomic Bulletin & Review*. DOI: 10.3758/s13423-022-02089-x
- Bunce, D.M., Flens, E.A. & Neiles, K.Y. (2010). "How Long Can Students Pay Attention in Class?" *Journal of Chemical Education*, 87(12), 1438-1443.
- Wilson, K. & Korn, J.H. (2007). "Attention During Lectures: Beyond Ten Minutes." *Teaching of Psychology*, 34(2), 85-89.

### Learning Science
- Vygotsky, L.S. (1978). *Mind in Society: The Development of Higher Psychological Processes*. Harvard University Press.
- Bjork, R.A. (2011). "On the symbiosis of learning, remembering, and forgetting." In Benjamin (Ed.), *Successful Remembering and Successful Forgetting*. Psychology Press.
- Rohrer, D., Dedrick, R.F. & Stershic, S. (2015). "Interleaved practice improves mathematics learning." *Journal of Educational Psychology*, 107(3), 900-908.
- Kapur, M. (2008). "Productive Failure in Mathematical Problem Solving." *Cognition and Instruction*, 26(3), 379-424.
- Renkl, A. & Atkinson, R.K. (2003). "Structuring the transition from example study to problem solving." *Educational Psychologist*, 38(1), 15-22.

### Deep Work and Digital Wellbeing
- Newport, C. (2016). *Deep Work: Rules for Focused Success in a Distracted World*. Grand Central Publishing.
- Rideout, V. et al. (2022). *The Common Sense Census: Media Use by Tweens and Teens*. Common Sense Media.
- Biwer, F. et al. (2023). "Study smart breaks: The systematic effect of micro-breaks on learning." *Educational Psychology Review*.

### Gamification
- Zeng, J. et al. (2024). Meta-analysis: gamification overall effect g = 0.822, but interventions > 1 semester show negligible/negative effect.
- Pekrun, R. (2006). "The Control-Value Theory of Achievement Emotions." *Educational Psychology Review*, 18, 315-341.
- Baker, R.S.J.d. et al. (2010). "Better to Be Frustrated than Bored." *International Journal of Human-Computer Studies*, 68(4), 223-241.

### Adaptive Learning Architecture
- Lord, F.M. (1980). *Applications of Item Response Theory to Practical Testing Problems*. Lawrence Erlbaum Associates.
- Settles, B. & Meeder, B. (2016). "A Trainable Spaced Repetition Model for Language Learning." *ACL*.
- Corbett, A.T. & Anderson, J.R. (1994). "Knowledge tracing: Modeling the acquisition of procedural knowledge." *User Modeling and User-Adapted Interaction*, 4, 253-278.

### Microbreaks
- Frontiers in Psychology (2025). "Sustaining student concentration." Cohen's d = 1.784 for 90-second micro-breaks every 10 minutes.
- Kitayama, S. et al. (2022). Systematic microbreaks produce positive performance outcomes.
- Biwer, F. et al. (2023). Systematic breaks improve efficiency and mood restoration.

### Behavioral Flow Detection
- Smart Learning Environments (2021). Behavioral log data predicts flow experience in gamified educational systems without biometrics.
- Duckworth, A.L. et al. (2007). "Grit: Perseverance and Passion for Long-Term Goals." *J. Personality and Social Psychology*, 92(6), 1087-1101.
- Esterman, M. et al. (2013). "In the Zone or Zoning Out?" *Cerebral Cortex*, 23(11), 2712-2723.
