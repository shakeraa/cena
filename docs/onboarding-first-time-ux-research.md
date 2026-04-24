# Onboarding, First-Time UX & User Activation Research

> **Status:** Research complete
> **Date:** 2026-03-31
> **Scope:** Mobile onboarding for CENA adaptive learning platform (Flutter, Israeli Bagrut market)
> **Applies to:** Students (primary), teachers, parents
> **Existing implementation:** `src/mobile/lib/features/onboarding/` (5-page flow, Riverpod state)
> **Backend actors:** `StudentActor` (event-sourced, Proto.Actor), `DiagnosticEngine` (KST-based)

---

## Table of Contents

1. [First 60 Seconds](#1-first-60-seconds)
2. [Onboarding Flow Patterns](#2-onboarding-flow-patterns)
3. [Complete Flow Designs (Student, Teacher, Parent)](#3-complete-flow-designs)
4. [Goal Setting During Onboarding](#4-goal-setting-during-onboarding)
5. [Diagnostic Assessment](#5-diagnostic-assessment)
6. [Activation Milestones & Funnel](#6-activation-milestones--funnel)
7. [Invitation & Referral Flows](#7-invitation--referral-flows)
8. [Permission Requests](#8-permission-requests)
9. [Empty State Design](#9-empty-state-design)
10. [Re-Onboarding](#10-re-onboarding)
11. [Multi-Device Onboarding](#11-multi-device-onboarding)
12. [A/B Testing Onboarding](#12-ab-testing-onboarding)
13. [Accessibility in Onboarding](#13-accessibility-in-onboarding)
14. [Flutter Implementation Patterns](#14-flutter-implementation-patterns)
15. [Actor System Integration](#15-actor-system-integration)
16. [Activation Funnel Metrics & Benchmarks](#16-activation-funnel-metrics--benchmarks)
17. [Industry Examples Analysis](#17-industry-examples-analysis)

---

## 1. First 60 Seconds

### 1.1 The Psychology of App Store to First Open

The install-to-value window is the single highest-leverage moment in the product lifecycle. Research from Appsflyer (2025) shows that 77% of users who churn from education apps do so within the first 3 days, and 25% never return after the first session. The first 60 seconds determine whether a student categorizes CENA as "homework obligation" or "personal tool that helps me."

**App Store Expectations Set:**
- The app store listing already primes expectations. Screenshots showing game-like elements (XP, streaks, knowledge graph visualization) create anticipation for interactive experience, not a textbook PDF viewer.
- Key promise in the app store description: "Start learning in 30 seconds" -- this must be literally true.

**Install-to-First-Open Gap:**
- Average gap between install and first open: 6 hours for education apps (vs. 1 hour for social apps).
- Implication: The push notification permission should NOT be the first thing requested, but a well-timed install confirmation notification via the OS default (silent channel) can remind the user to open the app within the first day.

### 1.2 Splash Screen That Builds Anticipation

The splash screen is not dead time -- it is emotional priming.

**Design principles:**
- Duration: 1.5-2.5 seconds (long enough to build anticipation, short enough to not frustrate).
- Animation: The CENA logo should animate (e.g., subtle particle effect of knowledge nodes connecting, echoing the knowledge graph concept). This primes the student for the visual language they will see later.
- No loading spinner. If actual loading is needed, use a determinate progress bar disguised as the animation filling up.
- On repeat opens: reduce to 0.8 seconds (brand flash, not full animation). Track `isFirstLaunch` via SharedPreferences (already stored as `onboarding_complete`).

**Copy on splash (Hebrew, RTL):**
```
[CENA Logo animating]
"המסע שלך מתחיל כאן"
(Your journey starts here)
```

On subsequent launches:
```
[Quick logo flash]
[Directly into last screen or home]
```

### 1.3 First Screen Decision: Sign Up vs. Explore

This is the highest-stakes UX decision in the onboarding flow. Research from Nir Eyal's Hook Model and analysis of Duolingo, Headspace, and TikTok reveals a clear winner:

**Let them explore first. Sign up later.**

Rationale:
- Duolingo lets you take a placement test and complete an entire lesson before ever creating an account. Result: 4x higher Day 7 retention vs. their previous signup-first flow.
- Headspace lets you do one guided meditation before account creation. Their A/B test showed 20% higher subscription conversion.
- TikTok shows content immediately. Account creation is deferred until the user tries to interact (like, comment, follow).

**Recommended for CENA:**
The current implementation has Welcome -> Subjects -> Grade -> Diagnostic -> Ready. This is already a reasonable progressive flow. The critical change is: **insert one real question between the welcome screen and the sign-up requirement.**

Flow should be:
1. Welcome (brand, language) -- 10 seconds
2. "Try a question" (one sample math question, immediate feedback) -- 20 seconds
3. Subject selection -- 15 seconds
4. Grade + Bagrut track -- 15 seconds
5. Optional diagnostic (5 adaptive questions) -- 2 minutes
6. Account creation (phone auth, which already exists)
7. Knowledge graph reveal + "Start Learning"

The key insight: the student answers a real question BEFORE they invest in account setup. This is the "aha moment" -- they see that CENA gives immediate, interactive feedback (not a boring textbook), and they see the gamification elements (XP animation, correct/incorrect feedback with explanation). Now they are motivated to create an account to save their progress.

### 1.4 Reducing Time-to-Value

**Target: First learning interaction within 30 seconds of app open.**

Time-to-value benchmarks from top education apps:
| App | Time to first interaction | What the interaction is |
|-----|--------------------------|------------------------|
| Duolingo | 15 seconds | First translation exercise |
| Khan Academy | 45 seconds | First video plays |
| Photomath | 5 seconds | Point camera at problem |
| Quizlet | 20 seconds | First flashcard flip |
| CENA (current) | ~90 seconds | First diagnostic question (page 4) |
| CENA (target) | 25 seconds | Sample question after welcome |

**Implementation:** Insert a `_TryQuestionPage` between `_WelcomePage` and `_SubjectsPage`. Show one well-chosen math question (simple enough that a 9th grader should get it right, providing positive reinforcement). On correct answer: XP animation, encouraging message. On incorrect: gentle "no worries, let's find your level" framing that leads naturally into the diagnostic.

### 1.5 "Aha Moment" Identification and Acceleration

The "aha moment" is the instant the user understands the product's core value. For CENA, this is:

**"This app knows where I am in math and shows me exactly what to learn next."**

This is realized when the student sees their personal knowledge graph for the first time, with concepts lit up based on their diagnostic results. The concepts they know are green, the concepts they need to learn are highlighted, and the path forward is clear.

**Accelerating the aha moment:**
- Show a miniature knowledge graph preview even during the diagnostic: "You have unlocked 3 concepts so far" with nodes lighting up in real-time as they answer questions.
- After the diagnostic: full-screen animated reveal of the knowledge graph (MOB-013.3 spec already describes this -- green/yellow/gray overlay).
- The "Start Learning" button targets the first concept in the frontier, so the student immediately begins learning something at their level.

**Aha moment metrics to track:**
- `event: knowledge_graph_first_view` -- timestamp when the student first sees their personalized graph.
- `event: first_concept_frontier_tap` -- timestamp when they tap into their first recommended concept.
- Correlation analysis: users who reach the knowledge graph view within the first session have 3-5x higher Day 7 retention (expected, based on industry benchmarks).

---

## 2. Onboarding Flow Patterns

### 2.1 Progressive Onboarding (Learn by Doing)

The cardinal rule: **never explain what you can demonstrate.**

Anti-pattern (tutorial overlay):
```
"Tap here to select your subject"
[Arrow pointing to button]
[Got it!]
```

Better pattern (contextual discovery):
```
[Subject cards are already visible]
[Math card pulses gently with a subtle glow]
[Student taps it naturally]
[Card animates to selected state with satisfying haptic feedback]
[Next button appears]
```

**Progressive disclosure timeline for CENA:**

| When | What to teach | How to teach it |
|------|--------------|-----------------|
| Onboarding Q1 | "Questions look like this" | Show a real question, let them answer |
| Onboarding Q2 | "You get XP for answers" | Show XP popup after answering |
| First session Q1 | "Swipe to skip" | Show ghost hand swipe animation on first unanswered question for 2 seconds, then fade |
| First session Q3 | "Tap the hint button" | Pulse the hint icon after 10 seconds of no interaction |
| After session 1 | "Check your streak" | Highlight streak widget on home screen with temporary badge |
| Day 2 return | "Your streak is alive" | Push notification: "Day 2! Keep your streak going" |
| After 3 sessions | "See your knowledge graph" | Unlock graph tab with "New" badge |

### 2.2 Coach Marks vs. Tooltips vs. Full Tutorials

| Method | When to use | CENA application |
|--------|------------|------------------|
| **Coach marks** (spotlight + text) | Complex screens with many actions (home dashboard) | First time viewing home screen after onboarding |
| **Tooltips** (small popover on specific element) | Single feature discovery | Hint button, streak freeze button, XP multiplier |
| **Full tutorial** (multi-step walkthrough) | Never during first launch. Only for complex features | Knowledge graph exploration (optional, triggered by "?" icon) |
| **Contextual empty states** | Screens with no data yet | First dashboard, first session history, first badge shelf |
| **Inline instruction** (text within the UI, not overlaid) | Always preferable to overlays | "Answer 3 more questions to unlock your daily badge" |

**Recommendation:** CENA should avoid full tutorials entirely. Every feature should be discoverable through use. The knowledge graph is the only feature complex enough to warrant an optional walkthrough, and even that should be "try tapping a concept" rather than a slide deck.

### 2.3 Interactive Tutorials

Instead of explaining, let the user do:

**During onboarding (Page 2 in proposed flow):**
```
Screen title: "?בוא/י ננסה שאלה"
(Let's try a question)

[A real math question appears]
[Student answers]

If correct:
  [XP animation: +10 XP flies up]
  [Text: "!מעולה — ככה זה עובד" (Excellent -- that's how it works)]
  [Subtle confetti burst]

If incorrect:
  [Text: "לא נורא! בוא/י נמצא את הנקודה שלך" (No worries! Let's find your starting point)]
  [Smooth transition to diagnostic]
```

This single interactive moment communicates four things simultaneously: (1) the question format, (2) the feedback mechanism, (3) the gamification layer, (4) the tone (encouraging, not judgmental).

### 2.4 Skip-able Onboarding

**Every step of onboarding must be skip-able. Never force it.**

Implementation approach:
- "Skip" link in the top-right corner of every onboarding page (subtle, not prominent -- you want most users to go through, but never trap anyone).
- Skipping the diagnostic: defaults the student to grade-appropriate starting level (use grade + Bagrut units to estimate). The system will adapt quickly within the first 3-5 real sessions.
- Skipping subject selection: defaults to Mathematics (only subject currently available).
- Skipping goal setting: defaults to "15 minutes daily" (the median goal across education apps).

**Data from Duolingo:** Only 12% of users skip their placement test. The vast majority want to be placed correctly -- the skip option exists for psychological safety, not because most users want it.

**Current CENA implementation note:** The `skipDiagnostic` field already exists in `OnboardingSelections`. This is correct. Ensure skipping still triggers a `DiagnosticSkipped` event on the `StudentActor` so the backend knows to use grade-level defaults.

### 2.5 Personalization During Onboarding

Personalization is the secret weapon of retention. When a user invests effort in configuring the app for themselves, they develop ownership (the IKEA effect).

**Current CENA personalization points:**
1. Language selection (Hebrew/Arabic/English) -- Page 1
2. Subject selection (up to 3) -- Page 2
3. Grade level (9th-12th) -- Page 3
4. Bagrut units (3/4/5 units) -- Page 3
5. Diagnostic quiz results -- Page 4

**Recommended additions:**
6. "What is your goal?" (pass the Bagrut / improve grades / get ahead / prepare for university)
7. "How much time can you study daily?" (5 min / 15 min / 30 min / 60 min)
8. "When do you usually study?" (morning / afternoon / evening / late night) -- feeds the ChronotypeDetectorActor (FOC-007)
9. Avatar/display name selection (optional, but creates identity attachment)

**Implementation:** These additional fields should be added to `OnboardingSelections` in `onboarding_state.dart` and persisted to the backend via a `ProfileConfigured` event on the `StudentActor`.

### 2.6 Role-Based Onboarding

CENA serves three distinct personas. Each needs a different onboarding path.

**Student (primary path, described in detail above):**
Welcome -> Try Question -> Subjects -> Grade -> Goals -> Diagnostic -> Knowledge Graph Reveal -> Start Learning

**Teacher:**
Welcome -> "I am a teacher" button -> School + class selection -> Subject + grade -> Invite students (class code generation) -> Dashboard preview -> "View your class"

**Parent:**
Welcome -> "I am a parent" button -> Link to child's account (via code from child's settings or QR code) -> Privacy settings overview -> Dashboard preview -> "Set family goals"

The role selection should happen on the Welcome page via clear, distinct buttons -- not a dropdown or radio group. Three large cards with icons:

```
[Student icon]           [Teacher icon]         [Parent icon]
"אני תלמיד/ה"           "אני מורה"             "אני הורה"
(I'm a student)         (I'm a teacher)        (I'm a parent)
```

---

## 3. Complete Flow Designs

### 3.1 Student Onboarding Flow (Screen-by-Screen)

#### Screen 1: Welcome & Language (10 seconds)

**Layout:** Centered, minimal. Brand-forward.

```
[Top: Progress dots - 1 of 7 highlighted]

[Center: CENA logo - 96x96px, animated entrance]

"ברוכים הבאים ל-Cena"
(Welcome to Cena)

"המאמן האישי שלך ללמידה"
(Your personal learning coach)

[Language selector: עברית | عربية | English]

[Role cards:]
  [Student - highlighted default]  [Teacher]  [Parent]

[Button: "התחל/י" (Start)]
```

**State changes:** Sets `locale` in app config, sets `role` in onboarding state.

**Analytics event:** `onboarding_started`, `language_selected`, `role_selected`

#### Screen 2: Try a Question (25 seconds)

**Layout:** A single well-chosen question. Simple enough for most students to answer correctly.

```
[Top: Progress dots - 2 of 7]

"?בוא/י ננסה שאלה"
(Let's try a question)

[Question card with math rendering:]
"מה הפתרון של המשוואה 2x + 6 = 14?"
  (A) x = 2
  (B) x = 4  <-- correct
  (C) x = 6
  (D) x = 8

[On answer:]
  Correct: "+10 XP!" animation, "!מעולה" with green check
  Incorrect: "!לא נורא" with gentle redirect text

[Skip link: "דלג/י" (Skip)]
[Button: "המשך" (Continue)]
```

**Purpose:** Demonstrates the core interaction loop. Shows gamification (XP). Builds confidence or gently frames the diagnostic.

**Analytics event:** `onboarding_sample_question_answered`, `onboarding_sample_question_correct/incorrect`

#### Screen 3: Subject Selection (15 seconds)

**Layout:** 2x2 grid of subject cards. Only Math available in V1.

```
[Top: Progress dots - 3 of 7]

"בחר/י מקצועות לימוד"
(Choose your subjects)

"ניתן לבחור עד 3 מקצועות"
(You can choose up to 3 subjects)

[2x2 Grid:]
  [Math - blue, enabled, icon: functions]
  [Physics - orange, disabled, "בקרוב" (Coming soon)]
  [Chemistry - green, disabled, "בקרוב"]
  [Biology - purple, disabled, "בקרוב"]

[Back: "חזרה" | Next: "המשך" (enabled when >= 1 selected)]
```

**Already implemented:** `_SubjectsPage` in current codebase. No changes needed beyond adding the "Try Question" page before it.

#### Screen 4: Grade & Bagrut Level (15 seconds)

**Layout:** Two selection groups. Clear, large tap targets.

```
[Top: Progress dots - 4 of 7]

"באיזה שלב את/ה?"
(What stage are you at?)

[Grade selector - horizontal chips:]
  כיתה ט  |  כיתה י  |  כיתה יא  |  כיתה יב

"כמה יחידות לימוד?"
(How many study units?)

[Bagrut units - vertical cards with descriptions:]
  [3 יחידות — בסיסי]    "עבור תלמידים שרוצים לעבור"
  [4 יחידות — מורחב]    "עבור תלמידים שרוצים להצטיין"
  [5 יחידות — גבוה]     "עבור תלמידים שמכוונים לטופ"

[Back | Next (enabled when both selected)]
```

**Already implemented:** `_GradePage` in current codebase.

#### Screen 5: Goal Setting (20 seconds)

**Layout:** Friendly, empowering. Not a form.

```
[Top: Progress dots - 5 of 7]

"מה המטרה שלך?"
(What's your goal?)

[Selectable cards with illustrations:]
  [Icon: Trophy]  "לעבור את הבגרות"     (Pass the Bagrut)
  [Icon: Star]    "לשפר ציונים"          (Improve grades)
  [Icon: Rocket]  "להתקדם מעבר לכיתה"    (Get ahead of class)
  [Icon: Grad cap] "להתכונן לאוניברסיטה"  (Prepare for university)

"כמה זמן ביום?"
(How much time per day?)

[Slider or segmented control:]
  5 דק'  |  15 דק'  |  30 דק'  |  60 דק'
  (recommended: 15 דק' highlighted with "מומלץ" badge)

[Back | Next]
```

**Not yet implemented.** New page needed. Add `LearningGoal` enum and `dailyMinutesTarget` int to `OnboardingSelections`.

#### Screen 6: Diagnostic Quiz (2-3 minutes)

**Layout:** One question at a time. Progress bar at top. Adaptive (backend-driven via DiagnosticEngine when available, fixed 5 questions as fallback).

```
[Top: Progress dots - 6 of 7]
[Sub-header: "שאלה 3 מתוך ~10" (Question 3 of ~10)]
[Progress bar: 30% filled]

[Question card:]
"מה שטח המשולש שבסיסו 8 ס"מ וגובהו 5 ס"מ?"
  (A) 20 סמ"ר  <-- correct
  (B) 25 סמ"ר
  (C) 40 סמ"ר
  (D) 13 סמ"ר

[As student answers, mini knowledge graph updates in background]
[Subtle: "כבר גילינו 3 מושגים שאת/ה יודע/ת" (We've already discovered 3 concepts you know)]

[Skip question: "דלג/י על שאלה"]
[Skip diagnostic entirely: "דלג/י על האבחון" (Skip diagnostic)]
```

**Framing matters:** Never call it a "test." Frame it as "finding your starting point" or "discovery."

**Already implemented:** `_DiagnosticPage` with 5 fixed questions. Backend MST-013 specifies the adaptive KST engine for 10-15 questions.

#### Screen 7: Knowledge Graph Reveal + Ready (15 seconds)

**Layout:** Full-screen animated reveal. This is the aha moment.

```
[Top: Progress dots - 7 of 7]

[Full-screen animated knowledge graph:]
  [Concepts fade in one by one]
  [Mastered concepts glow green]
  [In-progress concepts glow yellow]
  [Unstarted concepts are gray]
  [The frontier (next concepts to learn) pulses gently]

"המפה שלך מוכנה!"
(Your map is ready!)

[Summary card:]
  "גילינו ש..." (We discovered that...)
  "את/ה יודע/ת 12 מושגים" (You know 12 concepts)
  "המושג הבא שלך: פתרון משוואות ליניאריות"
  (Your next concept: Solving linear equations)

[Large CTA button:]
"!בוא/י נתחיל" (Let's start!)
```

**Already specified:** MOB-013.3 Knowledge Graph Reveal.

### 3.2 Teacher Onboarding Flow

#### Screen T1: Welcome + Role Selection
Same as Student Screen 1. Teacher taps "I'm a teacher."

#### Screen T2: School & Class Setup
```
"ברוך/ה הבא/ה, מורה!"
(Welcome, teacher!)

[School name input field]
[Class name or section: e.g., "כיתה יא-2"]
[Subject taught: Math (dropdown)]
[Grade: 9-12]

[Next]
```

#### Screen T3: Invite Students
```
"הזמן/י תלמידים לכיתה"
(Invite students to your class)

[Large class code display:]
  "קוד הכיתה שלך: MATH-7X2K"

[Share options:]
  [Copy code]  [Share via WhatsApp]  [Share via email]

"תלמידים יוכלו להצטרף באפליקציה עם הקוד הזה"
(Students can join in the app with this code)

[QR code for quick scanning]

[Skip: "אעשה את זה אחר כך" (I'll do this later)]
[Next]
```

#### Screen T4: Dashboard Preview
```
"ככה תראה/י את הכיתה שלך"
(This is how you'll see your class)

[Mock dashboard showing:]
  - Class average mastery
  - Students at risk
  - Recent activity feed
  - Assignment creation button

"כשתלמידים יצטרפו, תראה/י את ההתקדמות שלהם כאן"
(When students join, you'll see their progress here)

[CTA: "לדשבורד שלי" (To my dashboard)]
```

### 3.3 Parent Onboarding Flow

#### Screen P1: Welcome + Role Selection
Same as Student Screen 1. Parent taps "I'm a parent."

#### Screen P2: Link to Child
```
"חבר/י את החשבון של הילד/ה"
(Link your child's account)

[Two options:]
  [Option A: "יש לי קוד מהילד/ה" (I have a code from my child)]
    -> Enter 6-digit link code
  [Option B: "הילד/ה עוד לא נרשם/ה" (My child hasn't signed up yet)]
    -> "שלח/י הזמנה" (Send invitation)
    -> WhatsApp / SMS share with signup link

[Skip: "אדלג בינתיים" (I'll skip for now)]
```

#### Screen P3: Privacy Overview
```
"הפרטיות של הילד/ה חשובה לנו"
(Your child's privacy is important to us)

[Visual showing what parent CAN see:]
  - Mastery progress (%)
  - Study time
  - Streak
  - Bagrut readiness

[Visual showing what parent CANNOT see:]
  - Individual questions/answers
  - Session details
  - AI tutor conversations
  - Personal notes

"אנחנו מאמינים שלמידה דורשת מרחב בטוח"
(We believe learning requires a safe space)

[Next]
```

#### Screen P4: Set Family Goals
```
"הגדר/י יעד משפחתי"
(Set a family goal)

[Goal selector:]
  "אני רוצה שהילד/ה יהיה מוכן/ה לבגרות ב-____ עד ____"
  (I want my child to be ready for the Bagrut in ___ by ___)

  [Subject dropdown] [Date picker]

  Or: "בוא/י נראה קודם את ההתקדמות" (Let's see the progress first)

[CTA: "לדשבורד ההורים" (To parent dashboard)]
```

---

## 4. Goal Setting During Onboarding

### 4.1 Making Goal-Setting Feel Empowering

The difference between a motivating goal screen and a tedious form is framing. The student should feel they are making choices about their future, not filling out a registration form.

**Principles:**
1. Use first person: "What do I want to achieve?" not "Select your goals."
2. Visual, not textual: Icon cards with one-line descriptions, not checkbox lists.
3. Limit choices: 4 goals maximum. Choice paralysis kills completion rates.
4. Default to something reasonable: Pre-select "15 minutes daily" so the student can just tap "Next" if they don't care.
5. Show the payoff immediately: "At 15 minutes/day, you'll be ready for the Bagrut in ~6 months" (calculated dynamically based on their diagnostic results and the concept graph size).

### 4.2 Daily Time Commitment

This should feel like a commitment, not a form field. Use a visual slider or segmented control.

```
"כמה זמן ביום את/ה רוצה ללמוד?"
(How much time per day do you want to study?)

[Segmented control with illustrations:]

  5 דק'        15 דק'         30 דק'         60 דק'
  "מזדמן"      "מומלץ"        "רציני"        "אינטנסיבי"
  (Casual)    (Recommended)   (Serious)     (Intensive)
```

The "recommended" label on 15 minutes uses social proof (anchoring effect). Most users will select either the recommended or one step above.

### 4.3 Subject and Topic Selection

Already well-implemented in the current flow. The subject card grid is visually appealing and grade-appropriate.

### 4.4 Difficulty Self-Assessment

The diagnostic quiz handles this empirically. A self-assessment is unreliable (Dunning-Kruger effect is especially strong in students). The diagnostic is vastly better than asking "How would you rate your math level?"

If a self-assessment is desired as a secondary signal:
```
"איך היית מדרג/ת את עצמך במתמטיקה?"
(How would you rate yourself in math?)

[Emoji scale, not numbers:]
  [Struggling face] "מתקשה"
  [Neutral face] "בסדר"
  [Confident face] "טוב/ה"
  [Star face] "מצטיין/ת"
```

This feeds a `selfConfidenceInitial` signal to the `StudentActor`. When combined with diagnostic results, mismatches (high confidence + low diagnostic, or vice versa) provide valuable pedagogical signals.

### 4.5 Learning Style Preferences

**Controversial but useful.** Learning style theory (visual/auditory/kinesthetic) is debunked in educational research. However, *preference* matters for engagement even if it does not affect learning outcomes.

Do NOT ask "Are you a visual learner?" Instead:
```
"מה עוזר לך ללמוד?"
(What helps you learn?)

[Multi-select cards:]
  [Icon: Play] "סרטוני הסבר"        (Explanation videos)
  [Icon: Pen] "תרגול שאלות"         (Practice questions)
  [Icon: Diagram] "תרשימים ודיאגרמות" (Diagrams and charts)
  [Icon: Chat] "הסברים צעד אחר צעד"  (Step-by-step explanations)
```

This maps to methodology preferences in the `MethodologyMap` on the `StudentActor`, influencing which `Methodology` is tried first.

---

## 5. Diagnostic Assessment

### 5.1 Placement Tests That Feel Like Games

The diagnostic must not feel like an exam. It must feel like a game.

**Game-like elements to apply:**
1. **Progress visualization:** A mini knowledge graph that lights up as the student answers. Each correct answer adds a glowing node. This provides visual reward for effort.
2. **No score display:** Never show "3 out of 7 correct." The student should not feel scored. Show "We've discovered 3 concepts you know" instead (reframing accuracy as exploration).
3. **Adaptive difficulty creates flow:** The KST-based adaptive engine (MST-013) naturally creates flow state by presenting questions at the boundary of the student's knowledge. Too easy = boring, too hard = frustrating. The boundary = engaging.
4. **Skip as a valid action:** Skipping a question should feel acceptable, not shameful. "Skip" button text: "עוד לא למדתי את זה" (I haven't learned this yet) instead of "Skip."
5. **Celebrate completion:** After the last question, a brief celebratory animation (confetti, achievement sound) before the knowledge graph reveal.

### 5.2 Adaptive Initial Assessment

The existing MST-013 specification is architecturally sound. Key implementation details for the UX layer:

**Question selection strategy:**
- Start with a medium-difficulty question (concept at the middle of the topological ordering of the student's grade-appropriate subgraph).
- If correct, jump up in difficulty. If incorrect, jump down. This is classical computerized adaptive testing (CAT) behavior, which the KST engine achieves through maximum entropy reduction.

**Question count communication:**
- Show "Question 3 of ~10" (the tilde communicates that the count is approximate, since the engine may terminate early).
- After early termination: "We got what we need in only 7 questions!" (framing early termination as efficiency, not cutting corners).

### 5.3 Starting Students at the Right Level

The diagnostic result maps directly to the `MasteryMap` on the `StudentActor`:

```
DiagnosticResult {
  MasteredConcepts: {"addition", "subtraction", "basic_algebra", ...}
  GapConcepts: {"quadratic_equations", "trigonometry", ...}
  Confidence: 0.87
}
```

This initializes the `MasteryMap` with `P(known) = 0.9` for mastered concepts and `P(known) = 0.1` for gap concepts. The learning frontier is then computed from the knowledge graph: concepts where all prerequisites are mastered but the concept itself is not.

**If diagnostic is skipped:** Initialize based on grade + Bagrut units:
- 9th grade, 3 units: assume mastery of all middle-school concepts
- 12th grade, 5 units: assume mastery through 11th grade advanced concepts
- This is a coarse estimate that the BKT updates will correct within 5-10 real sessions.

### 5.4 Avoiding Discouragement

**Frame assessment as discovery, not judgment.**

Copy principles:
- NEVER: "You got 3 wrong."
- ALWAYS: "We found 5 concepts you already know!"
- NEVER: "Your level is: Beginner."
- ALWAYS: "Here's your starting point on the knowledge map."

If the student scores very low (fewer than 2 concepts mastered):
```
"!נקודת ההתחלה שלך מושלמת"
(Your starting point is perfect!)

"יש לנו הרבה מקום ללמוד ביחד. בוא/י נתחיל מהיסודות"
(We have lots of room to learn together. Let's start from the basics.)
```

The knowledge graph should still look visually interesting even with few mastered nodes. The unmastered nodes represent opportunity, not failure. Use a warm gray (not red or dark) for unmastered concepts.

### 5.5 Framing Assessment

The assessment should be called by different names depending on context:

| Context | What to call it | What NOT to call it |
|---------|----------------|---------------------|
| Student-facing | "סיור גילוי" (Discovery Tour) | "מבחן אבחוני" (Diagnostic Test) |
| Teacher-facing | "מיפוי ידע ראשוני" (Initial Knowledge Mapping) | "מבחן מיון" (Placement Test) |
| Parent-facing | "הערכת נקודת התחלה" (Starting Point Assessment) | "בדיקת רמה" (Level Check) |
| Internal/backend | DiagnosticEngine, DiagnosticResult | (technical terms are fine internally) |

---

## 6. Activation Milestones & Funnel

### 6.1 Activation Milestone Definitions

Each milestone represents a deepening commitment from the student. The activation funnel tracks progression through these milestones.

| # | Milestone | Description | Target Time | Analytics Event |
|---|-----------|-------------|-------------|-----------------|
| M1 | App Opened | First launch after install | T+0 | `app_first_open` |
| M2 | Onboarding Started | Tapped "Start" on welcome | T+10s | `onboarding_started` |
| M3 | First Question Answered | Answered sample question or diagnostic Q1 | T+30s | `first_question_answered` |
| M4 | Onboarding Completed | Reached knowledge graph reveal | T+5min | `onboarding_completed` |
| M5 | Account Created | Phone auth completed | T+6min | `account_created` |
| M6 | First Lesson Started | Started first real learning session | T+7min | `first_session_started` |
| M7 | First Lesson Completed | Finished first learning session (5+ questions) | T+15min | `first_session_completed` |
| M8 | First Badge Earned | Earned "First Steps" badge | T+15min | `first_badge_earned` |
| M9 | Day 2 Return | Returned on the second day | T+24h | `day_2_return` |
| M10 | First Streak (3 days) | Three consecutive days of activity | T+72h | `streak_3_achieved` |
| M11 | First Social Interaction | Joined class or invited friend | T+varies | `first_social_interaction` |
| M12 | First Week Complete | 7 days since install, still active | T+7d | `week_1_complete` |

### 6.2 Activation Definition

A user is "activated" when they reach milestone M7 (first lesson completed) AND M9 (Day 2 return). This is the inflection point where retention curves flatten -- users who reach this point have 60-70% Day 30 retention vs. 10-15% for those who do not.

### 6.3 Retention Benchmarks

Industry benchmarks for education apps (Adjust 2025 Mobile App Trends Report, Appsflyer Education Vertical Report):

| Timeframe | Average Education App | Top Quartile Education App | CENA Target |
|-----------|----------------------|---------------------------|-------------|
| Day 1 retention | 25-30% | 40-50% | 45% |
| Day 3 retention | 15-18% | 25-35% | 30% |
| Day 7 retention | 10-12% | 18-25% | 22% |
| Day 14 retention | 7-9% | 14-20% | 17% |
| Day 30 retention | 5-7% | 10-15% | 13% |
| Day 90 retention | 2-4% | 6-10% | 8% |

**Duolingo benchmark:** ~45% Day 1, ~30% Day 7, ~15% Day 30, ~10% Day 90. CENA should target Duolingo-level retention because the product model is similar (daily practice, gamification, streaks).

### 6.4 Day 1 Activation Strategy

Day 1 is about proving value and creating a habit trigger.

**Sequence:**
1. Complete onboarding (5-7 minutes)
2. Complete first real learning session (5-10 minutes)
3. See first badge + XP summary
4. Get prompt: "Set a reminder for tomorrow?" (notification permission request, contextually justified)
5. See streak widget: "Day 1! Come back tomorrow to keep your streak"

**First session design:**
- 5-10 questions at the frontier of their knowledge map
- Mix of easy wins (70% expected accuracy) and challenges (30% expected accuracy)
- After each question: immediate feedback with short explanation
- After session: summary screen with XP earned, concepts touched, streak status
- "First Steps" badge auto-awarded after first session

### 6.5 Day 3 Strategy

Day 3 is about habit formation.

**Levers:**
- Push notification at the student's preferred study time (from onboarding): "Your streak is at 3 days! Don't break it."
- New content: introduce a second concept cluster to keep things fresh
- Social proof (if class code used): "3 of your classmates studied today"

### 6.6 Day 7 Strategy

Day 7 is about deepening engagement.

**Levers:**
- Weekly summary screen: "This week you learned X concepts, earned Y XP, and moved up Z levels"
- Unlock knowledge graph tab (if not already explored)
- Parent digest goes out (if parent is linked), creating social accountability
- Introduce harder content to prevent boredom

### 6.7 Day 30 Strategy

Day 30 is about demonstrating long-term value.

**Levers:**
- Monthly progress report: "One month in! You've mastered 45 concepts and are 23% ready for the Bagrut"
- Milestone badge: "30-Day Scholar"
- Introduce social features: study groups, leaderboards
- Bagrut countdown integration (if exam date known)

---

## 7. Invitation & Referral Flows

### 7.1 Class Code Joining (Teacher to Student)

**Teacher side:**
1. Teacher creates class during onboarding (or from dashboard later)
2. System generates unique 8-character class code (format: `SUBJ-XXXX`, e.g., `MATH-7X2K`)
3. Teacher shares code via WhatsApp group (primary channel in Israel), email, or classroom projection
4. Code is also available as a QR code for in-classroom scanning

**Student side:**
1. During onboarding, after subject selection: "Do you have a class code?" (optional)
2. Student enters code or scans QR
3. System validates code, shows class name and teacher name for confirmation
4. Student auto-joins class; teacher sees new student in dashboard

**Implementation:**
```dart
// In onboarding_state.dart, add:
class OnboardingSelections {
  // ... existing fields ...
  final String? classCode;
  // ...
}
```

**Backend:** Class code validation is a REST call to the Admin API, which resolves the code to a `ClassId` and `TenantId`, then publishes a `StudentJoinedClass` event via NATS to the `StudentActor`.

### 7.2 Student-to-Student Invitations

**Trigger:** After the student completes their first week (Day 7), show a prompt:

```
"יש לך חברים שלומדים לבגרות?"
(Do you have friends studying for the Bagrut?)

[Share button -> WhatsApp deep link]
"הזמן/י חברים ושניכם תקבלו 100 XP בונוס!"
(Invite friends and you'll both get 100 bonus XP!)
```

**Deep link format:** `https://cena.app/invite/{referralCode}`
- Referral code maps to the inviting student's ID
- When the invited student installs and completes onboarding, both students receive 100 XP bonus
- Track via `firebase_dynamic_links` or custom deep link handler (already implemented in `deep_link_service.dart`)

### 7.3 Parent Onboarding from Student Invitation

**Flow:**
1. Student goes to Settings -> "Invite Parent"
2. System generates a 6-digit parent link code
3. Student shares code with parent (verbally, WhatsApp, etc.)
4. Parent installs app, selects "I'm a parent" role, enters code
5. Accounts are linked; parent sees child's dashboard

**Privacy consideration:** The student initiates the link. The parent cannot link without the student's code. This respects the student's autonomy (critical for teenagers).

### 7.4 School-Wide Deployment

For institutional sales, CENA needs a bulk onboarding path:

1. School admin receives deployment link from CENA sales
2. Admin uploads student roster (CSV: name, grade, class, parent phone)
3. System creates pre-provisioned accounts
4. Students receive SMS/WhatsApp with personalized onboarding link
5. On first open, the student's grade and class are pre-filled (one fewer onboarding step)
6. Teachers are auto-assigned to their classes

**Backend:** This is an Admin API feature. The roster upload creates pending `StudentActor` instances with pre-set grade/class data. On first login, the actor activates with the pre-provisioned state.

### 7.5 Bulk User Creation

For school deployments:

```
POST /api/admin/students/bulk
Content-Type: application/json

{
  "schoolId": "sch_abc123",
  "students": [
    { "name": "דני לוי", "grade": 11, "classId": "cls_math_11_2", "phone": "+972501234567" },
    ...
  ]
}
```

Response includes per-student onboarding deep links and QR codes.

---

## 8. Permission Requests

### 8.1 When to Ask for Notification Permission

**Rule: NEVER during onboarding.**

The notification permission prompt is a one-shot opportunity on iOS. If the user declines, re-asking requires navigating to system settings. The optimal moment is when the user has experienced enough value to understand why notifications help.

**Optimal timing for CENA:**
- **After completing the first learning session.** At this point, the student has experienced the core value and can see that a daily reminder would help their streak.
- Frame: "Do you want a daily reminder to keep your streak alive?"
- Show a preview of what the notification will look like.

```
[After first session completion screen:]

"רוצה תזכורת יומית?"
(Want a daily reminder?)

[Preview of notification:]
  CENA
  "הרצף שלך: 1 ימים! בוא/י ללמוד 15 דקות"
  (Your streak: 1 day! Come learn for 15 minutes)

[Button: "כן, תזכרו אותי" (Yes, remind me)]
[Link: "לא עכשיו" (Not now)]
```

**Data:** Apps that request notification permission after demonstrating value see 50-60% opt-in rates vs. 35-40% for pre-value permission requests (OneSignal 2024 report).

### 8.2 Camera/Microphone Permission

Needed for: future head-pose attention detection (FOC-002), voice-based answers (future feature).

**When to ask:** NEVER during onboarding. Only when the feature is first used.

**Frame:** When the student first encounters a voice-input question:
```
"שאלה זו דורשת תשובה קולית"
(This question requires a voice answer)

"CENA צריכה גישה למיקרופון כדי לשמוע את התשובה שלך"
(CENA needs microphone access to hear your answer)

[Allow] [Type instead]
```

### 8.3 Location Permission

**Do not request.** Location is not needed for CENA's core experience. Even the "classroom features" concept (proximity-based class joining) is better served by class codes and QR codes, which do not require location permission.

### 8.4 Contacts Permission

**Do not request.** Friend-finding via contacts is invasive and unnecessary. Use class codes and direct share links instead.

### 8.5 Permission Request Summary

| Permission | When to ask | How to explain | Fallback if denied |
|------------|------------|----------------|-------------------|
| Notifications | After first session | "Daily streak reminders" | In-app banner reminder |
| Camera | When attention feature is first toggled on | "Helps track when you look away" | Feature stays disabled |
| Microphone | When voice answer question appears | "Voice answers for oral practice" | Text input option |
| Location | Never | N/A | N/A |
| Contacts | Never | N/A | N/A |

---

## 9. Empty State Design

### 9.1 First Dashboard with No Data

The home screen after onboarding completion shows no session history, no streak, no badges. This is the most critical empty state.

**Anti-pattern:** A blank screen with "No sessions yet."
**Pattern:** A welcoming screen that guides the student to their first action.

```
[Home Screen - First Visit]

"!שלום, [שם]"
(Hi, [name]!)

[Hero card - large, centered:]
  [Knowledge graph mini-preview with frontier concept highlighted]
  "המושג הבא שלך"
  (Your next concept)
  "[Concept name: e.g., פתרון משוואות ליניאריות]"
  [Button: "!בוא/י נתחיל" (Let's start!)]

[Streak widget - shows Day 0:]
  "יום 0 — התחל/י את הרצף שלך!"
  (Day 0 -- Start your streak!)

[XP widget - shows 0 XP:]
  "0 XP — ענה/י על שאלה ראשונה לקבל נקודות!"
  (0 XP -- Answer your first question to earn points!)

[Badge shelf - shows locked badges:]
  [3 locked badge outlines with names visible:]
  "צעדים ראשונים"  "רצף של 3"  "שליט/ת האלגברה"
  (First Steps)    (3-Day Streak) (Algebra Master)
```

### 9.2 Suggested First Actions

After onboarding, the system should present a clear "what to do next" checklist. This borrows from Notion's onboarding checklist pattern.

```
[Collapsible "Getting Started" card:]

  "4 צעדים להתחלה מושלמת"
  (4 steps for a perfect start)

  [x] סיים/י את הרישום  (Complete registration) -- auto-checked
  [ ] ענה/י על 5 שאלות  (Answer 5 questions) -- link to session
  [ ] בדוק/י את מפת הידע  (Check your knowledge map) -- link to graph
  [ ] הזמן/י חבר/ה  (Invite a friend) -- link to share

  [Progress: 1/4 complete]
```

This checklist disappears after all items are completed (or after Day 7, whichever comes first).

### 9.3 Quick-Start Content Recommendations

Based on diagnostic results, the home screen should show 2-3 concept cards at the student's frontier:

```
"מומלץ בשבילך"
(Recommended for you)

[Horizontal scroll of concept cards:]
  [Card 1: "משוואות ליניאריות" - 0% mastery - "5 דק'" estimated time]
  [Card 2: "שברים פשוטים" - 0% mastery - "8 דק'" estimated time]
  [Card 3: "סדר פעולות חשבון" - 0% mastery - "4 דק'" estimated time]
```

### 9.4 Making Empty States Exciting

Empty states should communicate potential, not absence. Use:

1. **Ghost data:** Show what the dashboard WILL look like with semi-transparent mock data and a "Start learning to fill this in" label.
2. **Progress previews:** Show locked achievements with clear paths to unlock.
3. **Countdown to first milestone:** "Answer 5 questions to earn your first badge!" with a small progress ring.
4. **Social proof:** "12,340 students are learning on CENA right now" (when true).

---

## 10. Re-Onboarding

### 10.1 Return After Long Absence

Students who return after weeks or months need a tailored re-entry experience. The worst thing is dumping them back where they left off with stale data.

**Detection:** The `StudentActor` tracks `LastInteraction` on each `ConceptMasteryState`. If the last session was more than 14 days ago, trigger re-onboarding flow.

**Flow:**

```
[Welcome Back Screen]

"!ברוך/ה השב/ה, [שם]"
(Welcome back, [name]!)

"עברו [X] ימים מאז שלמדת"
(It's been [X] days since you last studied)

[Option cards:]
  [Icon: Refresh] "התחל/י מאפס" (Start fresh)
    -> Re-run diagnostic, rebuild mastery map

  [Icon: Resume] "המשך/י מאיפה שהפסקת" (Continue where you left off)
    -> Goes to adjusted frontier, with decay factored in

  [Icon: Quick] "סיור גילוי מהיר" (Quick discovery tour)
    -> 5-question refresher to calibrate what was forgotten
```

### 10.2 Skill Decay Acknowledgment

The Half-Life Regression (HLR) model on the `StudentActor` predicts which concepts have decayed during absence. The re-onboarding flow should transparently communicate this:

```
"בזמן שלא היית כאן, כמה מושגים צריכים רענון"
(While you were away, some concepts need refreshing)

[Knowledge graph with decay visualization:]
  [Previously green nodes now show as yellow/orange]
  [Tooltip: "סיכוי שאת/ה עדיין זוכר/ת: 45%"]
  (Chance you still remember: 45%)

"אל תדאג/י — ריענון מהיר ותחזור/י לרמה"
(Don't worry -- a quick refresh and you'll be back on track)

[Button: "!בוא/י נרענן" (Let's refresh!)]
```

This triggers a targeted review session: questions only on concepts where `RecallProbability < 0.5`.

### 10.3 "Welcome Back" Experiences

**Emotional tone:** Warm, no guilt. Never "You missed 45 days." Always "Welcome back, we're glad you're here."

**Re-engagement sequence:**
1. Welcome back screen (above) -- immediate
2. Short refresher quiz (3-5 questions) -- 2 minutes
3. Updated knowledge graph showing current state -- 10 seconds
4. First full session with adjusted difficulty -- 5 minutes
5. Re-streak initiation: "Day 1 of your new streak!"

**Push notification strategy for lapsed users (managed by OutreachSchedulerActor):**
- Day 3 of absence: "We miss you! Your math map is waiting."
- Day 7: "Your streak freeze expires tomorrow. Come back to save it!"
- Day 14: "New content added: [topic name]. Check it out!"
- Day 30: Final attempt: "[Name], your Bagrut is in [X] months. Ready to get back on track?"
- After Day 30: Stop messaging. Respect their decision. Re-engage only on next app open.

### 10.4 New Feature Introductions

When the user returns after a significant absence, highlight new features with a "What's New" screen:

```
"מה חדש ב-CENA"
(What's new in CENA)

[Feature card 1: "תרשימים אינטראקטיביים" (Interactive diagrams)]
[Feature card 2: "שיחה עם מורה AI" (Chat with AI tutor)]

[Dismiss: "הבנתי" (Got it)]
```

**Only show for features released during the user's absence.** Track last-seen app version in SharedPreferences.

---

## 11. Multi-Device Onboarding

### 11.1 Web to Mobile Continuity

If a student starts onboarding on the web (future feature) and continues on mobile:

1. Account is already created (Firebase Auth shared across platforms)
2. Onboarding state is stored server-side (on the `StudentActor`)
3. Mobile app detects partial onboarding: resumes from last completed step
4. Diagnostic progress syncs: if 5 of 10 questions were answered on web, mobile resumes at question 6

**Implementation:** The `onboarding_complete` flag should come from the server (via `StudentActor` state), not only from SharedPreferences. SharedPreferences serves as a local cache, with the server as source of truth.

```dart
final onboardingCompleteProvider = FutureProvider<bool>((ref) async {
  // Check local cache first (fast)
  final prefs = await SharedPreferences.getInstance();
  final localComplete = prefs.getBool('onboarding_complete') ?? false;
  if (localComplete) return true;

  // Check server state (authoritative)
  final api = ref.read(apiClientProvider);
  final serverState = await api.getOnboardingState();
  if (serverState.isComplete) {
    await prefs.setBool('onboarding_complete', true);
    return true;
  }

  return false;
});
```

### 11.2 Cross-Platform Session Continuity

The `LearningSessionActor` already handles session state server-side. If a student starts a session on one device and opens another device, the second device should:

1. Show a notice: "You have an active session on another device. Continue here?"
2. If yes: close session on device A (via WebSocket/SignalR signal), transfer to device B
3. If no: open home screen (cannot have two concurrent sessions)

### 11.3 QR Code Pairing for Classroom Devices

For shared classroom tablets:

1. Teacher projects QR code on classroom screen
2. Students scan QR with their phones
3. QR contains a temporary session token linking the student's account to the shared tablet
4. Student's personal progress is used on the shared device
5. Session ends when class ends or student logs out

### 11.4 Tablet vs. Phone Experience

| Element | Phone (< 600dp width) | Tablet (>= 600dp width) |
|---------|----------------------|-------------------------|
| Onboarding layout | Single-column, scrollable | Two-column: illustration left, content right |
| Subject cards | 2x2 grid | 2x3 or 3x3 grid |
| Knowledge graph | Simplified cluster view | Full node-level view |
| Question cards | Full-width | Centered, max-width 600dp |
| Diagnostic | Portrait only | Landscape support with side panel |

**Flutter implementation:** Use `LayoutBuilder` or `MediaQuery` to switch between compact and expanded layouts. Do not use separate screens; use responsive breakpoints within the same widget tree.

---

## 12. A/B Testing Onboarding

### 12.1 What to Test

**High-impact tests (run first):**

| Test | Variant A | Variant B | Primary Metric |
|------|-----------|-----------|---------------|
| Sample question timing | Show question on page 2 (before signup) | Show question on page 4 (during diagnostic) | Day 1 retention |
| Onboarding length | 5 pages (current) | 7 pages (with goals + try question) | Onboarding completion rate |
| Diagnostic framing | "Diagnostic Quiz" | "Discovery Tour" | Diagnostic skip rate |
| Goal setting | Include goals page | Skip goals page | Day 7 retention |
| Personalization depth | Grade + Bagrut only | Grade + Bagrut + goals + study time + style | Day 30 retention |
| Knowledge graph reveal | Full animated reveal | Simple summary text | First session start rate |
| Social proof | No social proof | "12,340 students learning now" on welcome | Onboarding completion rate |
| Time commitment | Default to 15 min | Let user choose freely | Daily session completion rate |

**Low-risk tests (run continuously):**

| Test | Variants | Primary Metric |
|------|----------|---------------|
| CTA button text | "Start" vs. "Let's go!" vs. "Begin your journey" | Tap rate |
| Progress indicator | Dots vs. progress bar vs. none | Completion rate |
| Subject card style | Grid vs. list | Selection speed |
| Color scheme | Blue primary vs. green primary | Completion rate |

### 12.2 Key Metrics

| Metric | Definition | Target |
|--------|-----------|--------|
| Onboarding completion rate | % of users who reach "Start Learning" | > 80% |
| Diagnostic completion rate | % of users who complete diagnostic (vs. skip) | > 85% |
| Time-to-first-question | Seconds from app open to first question answered | < 30s |
| Onboarding-to-activation | % of completed onboarding who reach M7 (first session) | > 60% |
| Day 1 retention | % who return on day 2 | > 45% |
| Day 7 retention | % who return on day 8 | > 22% |
| Onboarding drop-off rate per page | % who abandon at each onboarding page | < 10% per page |

### 12.3 Common Onboarding Failures in Education Apps

| Failure | Why it happens | How to prevent |
|---------|---------------|----------------|
| Too many pages | Designers love thorough onboarding | Cap at 7 pages max; A/B test shorter variants |
| Mandatory signup before value | Product team fears anonymous users | Let users try before signing up |
| Information overload | Trying to explain everything upfront | Progressive disclosure; teach features in context |
| No personalization | Engineering cost of adaptive flows | Even minimal personalization (grade + goal) outperforms generic |
| Diagnostic feels like a test | Wrong framing in copy | "Discovery tour" framing + visual progress (graph lighting up) |
| Skip = guilt | "Are you sure you want to skip?" dialogs | Make skip a first-class action with no judgment |
| Empty dashboard after onboarding | No suggested first action | Always show "what to do next" CTA |
| Notifications requested too early | PM wants Day 0 notifications | Wait until after first session completion |

### 12.4 A/B Testing Infrastructure

**Firebase A/B Testing (already in stack):**
- Use Firebase Remote Config to serve variants
- Track events via Firebase Analytics (already implemented in `analytics_service.dart`)
- Minimum sample size per variant: 500 users (for education apps with ~25% Day 1 retention)

**Implementation pattern:**
```dart
// In onboarding_screen.dart, read variant from Remote Config:
final showGoalsPage = ref.watch(remoteConfigProvider).getBool('onboarding_show_goals');

// Conditionally include goals page:
children: [
  _WelcomePage(...),
  _TryQuestionPage(...),
  _SubjectsPage(...),
  _GradePage(...),
  if (showGoalsPage) _GoalsPage(...),
  _DiagnosticPage(...),
  _ReadyPage(...),
],
```

---

## 13. Accessibility in Onboarding

### 13.1 Screen Reader Compatibility

All onboarding screens must be fully navigable via TalkBack (Android) and VoiceOver (iOS).

**Requirements:**
- Every interactive element has a `Semantics` widget with `label`, `hint`, and `value`
- Subject cards: `Semantics(label: "מתמטיקה, זמין", hint: "הקש/י פעמיים לבחירה")`
- Progress dots: `Semantics(label: "שלב 3 מתוך 7")`
- Diagnostic questions: each option reads its text and selection state
- Knowledge graph: accessible summary text for screen readers (the visual graph is supplemented with a text list)

**Flutter implementation:**
```dart
Semantics(
  label: 'מתמטיקה',
  hint: isDisabled ? 'לא זמין כעת' : 'הקש/י פעמיים לבחירה',
  selected: isSelected,
  enabled: !isDisabled,
  child: _SubjectCard(...),
)
```

### 13.2 Language Selection for Multilingual Students

Already partially implemented: Hebrew, Arabic, English on the welcome page.

**Additional considerations:**
- RTL support (Hebrew, Arabic) must be thorough: all text, all layouts, all animations mirror correctly
- Arabic-speaking students in Israel (20% of population) must have full Arabic onboarding, not Hebrew with Arabic subtitles
- English is a secondary option for immigrants and English-speaking households
- Language selection must persist across sessions (stored in SharedPreferences and on the `StudentActor`)
- Questions and content must be available in the selected language (this is a content pipeline concern, not an onboarding concern, but the onboarding should only show languages for which content exists)

### 13.3 Age-Appropriate Reading Level

CENA targets 9th-12th grade (ages 14-18). Onboarding text should:
- Use clear, conversational Hebrew (not formal/literary)
- Avoid jargon: "knowledge graph" -> "learning map" in student-facing text
- Keep sentences short: max 15 words per sentence in onboarding copy
- Use familiar cultural references: Bagrut (everyone knows), not "standardized assessment"

### 13.4 Visual-Only Onboarding

Not directly applicable to CENA (target audience is 14-18, fully literate). However, accessibility principles apply:

- All onboarding steps should be completable with icons + gestures alone (for students with reading difficulties)
- Subject selection uses icons (functions, speed, science, biotech) alongside text
- Grade selection uses large, clear numbers
- Diagnostic questions include visual elements where possible (diagrams, figures)

### 13.5 Color and Contrast

- All text meets WCAG AA contrast ratio (4.5:1 for normal text, 3:1 for large text)
- Subject cards do not rely on color alone to communicate state (selected cards have a border + checkmark, not just a color change)
- The current implementation uses `Border.all` for selected state -- good. Ensure the checkmark icon is also present.
- Knowledge graph mastery colors (green/yellow/gray) should be supplemented with patterns or labels for color-blind users

---

## 14. Flutter Implementation Patterns

### 14.1 PageView-Based Onboarding

The current implementation uses `PageView` with `NeverScrollableScrollPhysics` (programmatic page transitions only). This is the correct pattern for guided onboarding.

**Recommended packages:**

| Package | Purpose | Notes |
|---------|---------|-------|
| `smooth_page_indicator` | Animated page dots | More visually appealing than hand-rolled dots |
| `lottie` | Splash + celebration animations | For knowledge graph reveal, confetti, etc. |
| `confetti_widget` | Confetti burst on correct answers | Lightweight, fun |
| `shimmer` | Loading states during async operations | For API calls during onboarding |
| `flutter_animate` | Orchestrated entrance animations | For staggered card reveals |
| `go_router` (already used) | Navigation after onboarding | Gate logic already implemented |
| `shared_preferences` (already used) | Local onboarding state | Already storing completion flag |
| `flutter_riverpod` (already used) | State management | `OnboardingNotifier` already implemented |

### 14.2 Onboarding State Architecture

The current `OnboardingNotifier` (Riverpod StateNotifier) is well-structured. Recommended extensions:

```dart
// Add to OnboardingSelections:
class OnboardingSelections {
  // ... existing fields ...

  // New fields for enhanced onboarding:
  final LearningGoal? learningGoal;
  final int dailyMinutesTarget;  // default 15
  final StudyTimePreference? studyTimePreference;
  final String? classCode;
  final bool triedSampleQuestion;
  final bool sampleQuestionCorrect;

  // ... copyWith, etc.
}

enum LearningGoal {
  passbagrut,
  improveGrades,
  getAhead,
  prepareUniversity,
}

enum StudyTimePreference {
  morning,
  afternoon,
  evening,
  lateNight,
}
```

### 14.3 Animation Patterns

**Splash screen:**
```dart
class SplashScreen extends StatefulWidget {
  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 2000),
    )..forward();

    // Navigate after animation completes
    _controller.addStatusListener((status) {
      if (status == AnimationStatus.completed) {
        context.go(CenaRoutes.onboarding);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return FadeTransition(
      opacity: _controller,
      child: Center(
        child: Icon(Icons.school_rounded, size: 96),
      ),
    );
  }
}
```

**Knowledge graph reveal animation:**
```dart
// Use staggered animations for nodes appearing
class _KnowledgeGraphReveal extends StatefulWidget { ... }

class _KnowledgeGraphRevealState extends State<_KnowledgeGraphReveal>
    with TickerProviderStateMixin {
  late final List<AnimationController> _nodeControllers;

  @override
  void initState() {
    super.initState();
    // Create staggered controllers for each node
    _nodeControllers = List.generate(
      widget.conceptCount,
      (i) => AnimationController(
        vsync: this,
        duration: const Duration(milliseconds: 300),
      ),
    );

    // Stagger: each node appears 100ms after the previous
    for (int i = 0; i < _nodeControllers.length; i++) {
      Future.delayed(Duration(milliseconds: i * 100), () {
        if (mounted) _nodeControllers[i].forward();
      });
    }
  }
}
```

**XP popup animation:**
```dart
// Already exists in gamification_widgets.dart
// Trigger during onboarding sample question:
void _onSampleQuestionAnswered(bool correct) {
  if (correct) {
    // Show XP popup (reuse existing XpPopup widget)
    OverlayEntry entry = OverlayEntry(
      builder: (context) => XpPopup(amount: 10),
    );
    Overlay.of(context).insert(entry);
    Future.delayed(const Duration(seconds: 2), entry.remove);
  }
}
```

### 14.4 Responsive Layout Pattern

```dart
class OnboardingPage extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final isTablet = constraints.maxWidth >= 600;

        if (isTablet) {
          return Row(
            children: [
              Expanded(child: _IllustrationPanel()),
              Expanded(child: _ContentPanel()),
            ],
          );
        }

        return SingleChildScrollView(
          child: _ContentPanel(),
        );
      },
    );
  }
}
```

### 14.5 Deep Link Handling for Onboarding

The existing `DeepLinkService` and `DeferredDeepLink` pattern handles the case where a user arrives via a deep link (e.g., class invite) but needs onboarding first. The router redirect logic already stores the intended path and replays it after onboarding. No changes needed.

---

## 15. Actor System Integration

### 15.1 Onboarding Events on StudentActor

The `StudentActor` is the event-sourced aggregate root. Onboarding produces these domain events:

```csharp
// Events to add to the StudentActor event stream:

public sealed record OnboardingStarted(
    string StudentId,
    string Locale,
    string DeviceType,   // phone/tablet
    DateTimeOffset Timestamp
);

public sealed record OnboardingProfileConfigured(
    string StudentId,
    GradeLevel Grade,
    BagrutUnits Units,
    List<Subject> SelectedSubjects,
    LearningGoal? Goal,
    int DailyMinutesTarget,
    StudyTimePreference? PreferredStudyTime,
    string? ClassCode,
    DateTimeOffset Timestamp
);

public sealed record DiagnosticCompleted(
    string StudentId,
    IReadOnlySet<string> MasteredConcepts,
    IReadOnlySet<string> GapConcepts,
    float Confidence,
    int QuestionsAsked,
    TimeSpan Duration,
    DateTimeOffset Timestamp
);

public sealed record DiagnosticSkipped(
    string StudentId,
    string Reason,    // "user_choice" | "timeout" | "app_crash_resume"
    DateTimeOffset Timestamp
);

public sealed record OnboardingCompleted(
    string StudentId,
    TimeSpan TotalDuration,
    int PagesViewed,
    bool DiagnosticCompleted,
    DateTimeOffset Timestamp
);

public sealed record ClassJoined(
    string StudentId,
    string ClassId,
    string ClassCode,
    DateTimeOffset Timestamp
);
```

### 15.2 Actor State Transitions During Onboarding

```
StudentActor lifecycle during onboarding:

1. OnboardingStarted event received
   -> Actor activates (virtual grain, first message)
   -> State: { Status: Onboarding, MasteryMap: empty }

2. OnboardingProfileConfigured event received
   -> State: { Grade, BagrutUnits, Subjects populated }

3. DiagnosticCompleted event received
   -> Initialize MasteryMap from diagnostic results
   -> Compute initial learning frontier
   -> Start StagnationDetectorActor child
   -> State: { Status: Active, MasteryMap: populated }

4. DiagnosticSkipped event received
   -> Initialize MasteryMap from grade-level defaults
   -> State: { Status: Active, MasteryMap: grade-defaults }

5. OnboardingCompleted event received
   -> Start OutreachSchedulerActor child (for streak reminders)
   -> State: { OnboardingCompletedAt: now }

6. ClassJoined event received
   -> Add ClassId to student's class list
   -> Publish StudentJoinedClass to NATS (teacher dashboard picks up)
```

### 15.3 ProfileActor Consideration

The current architecture does not have a separate `ProfileActor`. Profile data (name, grade, subjects, preferences) is stored on the `StudentActor` itself. This is correct for CENA's scale -- there is no need for a separate profile actor when:

1. Profile reads are served from the `StudentActor`'s in-memory state (zero DB round-trips)
2. Profile writes are infrequent (mostly during onboarding and settings changes)
3. The `StudentActor` already event-sources all state changes

If profile data grows complex (avatar uploads, social profiles, achievement showcases), a future extraction into a `ProfileActor` child would make sense, but it is premature now.

### 15.4 OnboardingActor Consideration

Similarly, a separate `OnboardingActor` is not needed. Onboarding is a one-time flow that produces events on the `StudentActor`. The onboarding state machine lives on the Flutter client (via `OnboardingNotifier`), not on the server. The server receives the completed results, not intermediate states.

Exception: If partial onboarding save/resume across devices is required (Section 11.1), then the server needs to store intermediate onboarding state. This can be stored as a non-event-sourced record (simple key-value in Redis or SharedPreferences-equivalent on server) keyed by `StudentId`. When onboarding completes, the interim state is deleted and replaced by the proper events.

### 15.5 NATS Events During Onboarding

Events published to NATS during onboarding:

| NATS Subject | Event | Consumer |
|-------------|-------|----------|
| `cena.events.student.onboarding.completed` | OnboardingCompleted | Analytics pipeline |
| `cena.events.student.diagnostic.completed` | DiagnosticCompleted | Analytics pipeline |
| `cena.events.student.class.joined` | ClassJoined | Teacher dashboard, class aggregator |
| `cena.events.student.profile.configured` | OnboardingProfileConfigured | Analytics, recommendations |

---

## 16. Activation Funnel Metrics & Benchmarks

### 16.1 Full Funnel Visualization

```
App Install
    |
    v
App First Open .............. 100%
    |
    v
Onboarding Started .......... 95% (5% bounce on splash/welcome)
    |
    v
Sample Question Answered ..... 88% (7% skip or drop off)
    |
    v
Subjects Selected ........... 85%
    |
    v
Grade Selected .............. 83%
    |
    v
Goals Set ................... 80%
    |
    v
Diagnostic Started .......... 78%
    |
    v
Diagnostic Completed ........ 72% (6% skip, 0% drop off from diagnostic)
    |
    v
Knowledge Graph Viewed ...... 72%
    |
    v
Account Created ............. 68%
    |
    v
ONBOARDING COMPLETE ......... 68%
    |
    v
First Session Started ....... 60%
    |
    v
First Session Completed ..... 55%
    |
    v
ACTIVATED (M7) .............. 55%
    |
    v
Day 2 Return ................ 45%
    |
    v
FULLY ACTIVATED (M7+M9) ..... 40%
    |
    v
Day 7 Return ................ 22%
    |
    v
Day 30 Return ............... 13%
```

### 16.2 Drop-off Analysis

| Stage | Expected Drop-off | Root Cause | Mitigation |
|-------|-------------------|------------|------------|
| Install -> Open | 5% | Forgot about the app | Silent install notification |
| Welcome -> Subjects | 7% | Not interested after seeing the brand | Show a question first (try-before-buy) |
| Subjects -> Grade | 2% | Only 1 subject available | Show "coming soon" to set expectations |
| Grade -> Diagnostic | 2% | Intimidated by "quiz" framing | "Discovery tour" framing |
| Diagnostic -> Complete | 6% | Too long, lost interest | Adaptive shortening (terminate early) |
| Complete -> Account | 4% | Friction of phone auth | Allow guest mode (limited features) |
| Account -> Session | 8% | Lost momentum after signup | Auto-route to first session |
| Session -> Day 2 | 15% | No reminder, forgot | Notification permission + streak |

### 16.3 Firebase Analytics Event Schema

```dart
// Events to log at each funnel step:

await analytics.logEvent('onboarding_started', params: {
  'locale': 'he',
  'role': 'student',
  'device_type': 'phone',
});

await analytics.logEvent('onboarding_sample_question', params: {
  'correct': true,
  'time_to_answer_ms': 4500,
});

await analytics.logEvent('onboarding_subjects_selected', params: {
  'subjects': 'math',
  'subject_count': 1,
});

await analytics.logEvent('onboarding_grade_selected', params: {
  'grade': 11,
  'bagrut_units': 5,
});

await analytics.logEvent('onboarding_goals_set', params: {
  'goal': 'pass_bagrut',
  'daily_minutes': 15,
  'study_time': 'evening',
});

await analytics.logEvent('diagnostic_started');

await analytics.logEvent('diagnostic_completed', params: {
  'questions_asked': 12,
  'concepts_mastered': 8,
  'confidence': 0.87,
  'duration_seconds': 145,
});

await analytics.logEvent('diagnostic_skipped', params: {
  'questions_answered': 3,  // how many before skipping
});

await analytics.logEvent('knowledge_graph_viewed', params: {
  'mastered_count': 8,
  'gap_count': 42,
  'total_concepts': 50,
});

await analytics.logEvent('onboarding_completed', params: {
  'total_duration_seconds': 320,
  'pages_viewed': 7,
  'diagnostic_completed': true,
});

await analytics.logEvent('first_session_started', params: {
  'source': 'onboarding_cta',  // vs. 'home_screen' vs. 'push_notification'
});

await analytics.logEvent('first_session_completed', params: {
  'questions_answered': 8,
  'xp_earned': 120,
  'duration_seconds': 480,
});

await analytics.logEvent('first_badge_earned', params: {
  'badge_id': 'first_steps',
});
```

---

## 17. Industry Examples Analysis

### 17.1 Duolingo

**What they do right:**
- Immediate value: first translation exercise within 15 seconds
- No account required to start learning
- Placement test is a game (same UI as regular lessons)
- Streak mechanic creates daily habit (most powerful retention tool in education)
- Personalization: language selection, daily goal selection, placement level
- Empty states never feel empty: always a clear "Next Lesson" CTA

**What CENA should adopt:**
- Try-before-signup pattern (show a question before account creation)
- Streak as the primary retention lever (already implemented)
- "Discovery tour" framing for diagnostic (not "test")
- Clear daily CTA on home screen (never "what should I do?")

**What CENA should NOT adopt:**
- Hearts system (punitive, creates anxiety -- counterproductive for Bagrut prep)
- Aggressive notifications (Duolingo's passive-aggressive notifications are memeable but also drive uninstalls)
- Gamification over substance (CENA is preparation for a real exam; gamification supports learning, not replaces it)

### 17.2 Headspace

**What they do right:**
- One guided meditation before account creation (try-before-signup)
- Minimal onboarding: just pick a goal and get started
- Progress visualization: "minutes meditated" map that fills over time
- Daily reminder at user-chosen time
- Calming, low-pressure tone throughout

**What CENA should adopt:**
- Tone: Headspace's calm, encouraging tone is perfect for students anxious about the Bagrut
- Goal selection UI: simple, visual, 4 options max
- "Just 5 minutes" framing to lower activation energy

### 17.3 TikTok

**What they do right:**
- Zero onboarding: content plays immediately
- Algorithm learns preferences from behavior, not from explicit configuration
- Account creation is deferred until the user tries to interact
- Infinite scroll creates "one more" behavior

**What CENA should adopt:**
- Immediate content delivery principle: the first question should appear within 30 seconds
- Behavioral learning: the adaptive engine should start learning from the first question, not wait for explicit configuration
- Low-friction entry: minimize required inputs before first interaction

**What CENA should NOT adopt:**
- Infinite scroll (addictive, unstructured -- CENA needs structured learning sessions)
- Zero personalization input (CENA needs grade/Bagrut level for correct content)

### 17.4 Instagram

**What they do right:**
- Role-based onboarding (personal vs. business accounts)
- Profile setup feels lightweight (name, photo, done)
- Social graph import as engagement driver
- Tutorial tooltips for new features (Stories, Reels)

**What CENA should adopt:**
- Role-based onboarding (student/teacher/parent -- already recommended)
- Lightweight profile setup
- Contextual tooltips for new features (only when the feature is first encountered)

### 17.5 Notion

**What they do right:**
- Checklist-based onboarding: "5 things to try" with progress tracking
- Templates for quick starts (not a blank page)
- Inline tutorials (the tutorial IS a real Notion page, not a separate overlay)
- "Getting Started" workspace with pre-filled content

**What CENA should adopt:**
- Onboarding checklist on first home screen (Section 9.2)
- Never show a blank screen -- always have suggested content
- Tutorial by doing: the onboarding IS real learning, not a simulation

### 17.6 Summary: Best Practices Matrix

| Practice | Duolingo | Headspace | TikTok | Instagram | Notion | CENA Target |
|----------|----------|-----------|--------|-----------|--------|-------------|
| Try before signup | Yes | Yes | Yes | No | No | Yes |
| Time to first interaction | 15s | 30s | 0s | 60s | 120s | 25s |
| Onboarding pages | 5 | 3 | 0 | 4 | 1 | 7 |
| Personalization inputs | 3 | 1 | 0 | 3 | 2 | 5-7 |
| Diagnostic/placement | Yes | No | No | No | No | Yes |
| Goal setting | Yes | Yes | No | No | No | Yes |
| Streak mechanic | Yes | Yes | No | No | No | Yes |
| Role-based paths | No | No | No | Yes | Yes | Yes |
| Empty state design | Excellent | Good | N/A | Good | Excellent | Target: Excellent |
| Re-onboarding | Good | Good | N/A | Minimal | Minimal | Target: Excellent |
| Permission timing | After lesson 1 | After meditation 1 | After 3+ views | After follow 1 | After create 1 | After session 1 |

---

## Appendix A: Onboarding State Machine

```
States:
  WELCOME        -> (tap Start)           -> TRY_QUESTION
  TRY_QUESTION   -> (answer or skip)      -> SUBJECTS
  SUBJECTS       -> (select >= 1 subject) -> GRADE
  GRADE          -> (select grade + units) -> GOALS
  GOALS          -> (select goal + time)   -> DIAGNOSTIC
  DIAGNOSTIC     -> (complete or skip)     -> REVEAL
  REVEAL         -> (tap Start Learning)   -> COMPLETE

Transitions:
  Any state + Back -> Previous state
  Any state + Skip -> Next state (with defaults)
  DIAGNOSTIC + App Kill -> DIAGNOSTIC (resume from last question, via SQLite)
  COMPLETE -> (triggers: completeOnboarding(), navigate to auth/home)
```

## Appendix B: Copy Reference (Hebrew)

| Screen | Title | Subtitle | CTA |
|--------|-------|----------|-----|
| Welcome | ברוכים הבאים ל-Cena | המאמן האישי שלך ללמידה | התחל/י |
| Try Question | ?בוא/י ננסה שאלה | — | המשך |
| Subjects | בחר/י מקצועות לימוד | ניתן לבחור עד 3 מקצועות | המשך |
| Grade | באיזה שלב את/ה? | — | המשך |
| Goals | מה המטרה שלך? | — | המשך |
| Diagnostic | סיור גילוי | נמצא את נקודת ההתחלה שלך | — |
| Reveal | !המפה שלך מוכנה | גילינו ש... | !בוא/י נתחיל |

## Appendix C: Notification Schedule

| Trigger | Timing | Message (Hebrew) | Channel |
|---------|--------|-------------------|---------|
| First install, no open | +6 hours | המסע שלך מחכה! פתח/י את CENA | Push |
| First session complete | Immediate | !כל הכבוד! ענית על 8 שאלות היום | In-app |
| Day 1 evening | User's preferred study time | הרצף שלך: 1 ימים! בוא/י ללמוד 15 דקות | Push |
| Day 2 morning | 8:00 AM | יום 2! שמור/י על הרצף שלך | Push |
| Day 3 | User's preferred time | רצף של 3 ימים! את/ה על המסלול | Push |
| Day 7 | Evening | שבוע שלם! בדוק/י כמה למדת | Push |
| Day 14 absence | Morning | !חסרת לנו. בוא/י לרענן את מפת הידע | Push |
| Day 30 absence | Morning | הבגרות ב-[X] חודשים. מוכן/ה לחזור? | Push |

## Appendix D: Key File Paths in CENA Codebase

| File | Purpose |
|------|---------|
| `src/mobile/lib/features/onboarding/onboarding_screen.dart` | Flutter onboarding UI (5-page PageView) |
| `src/mobile/lib/features/onboarding/onboarding_state.dart` | Riverpod state (OnboardingSelections, OnboardingNotifier) |
| `src/mobile/lib/core/router.dart` | GoRouter with onboarding gate redirect |
| `src/mobile/lib/core/services/analytics_service.dart` | Firebase Analytics wrapper |
| `src/mobile/lib/core/services/push_notification_service.dart` | FCM push notification lifecycle |
| `src/mobile/lib/core/services/deep_link_service.dart` | Deep link parsing for invite flows |
| `src/mobile/lib/core/state/gamification_state.dart` | XP, streak, badge providers |
| `src/mobile/lib/features/gamification/gamification_widgets.dart` | XP popup, streak widget, badge display |
| `contracts/actors/student_actor.cs` | StudentActor contract (event-sourced aggregate) |
| `contracts/actors/actor_system_topology.cs` | Full actor hierarchy |
| `tasks/mobile/done/MOB-013-onboarding.md` | MOB-013 task: diagnostic quiz, partial save, graph reveal |
| `tasks/mastery/done/MST-013-onboarding-diagnostic.md` | MST-013 task: KST diagnostic engine backend |
| `docs/mastery-engine-architecture.md` | ConceptMasteryState, BKT, HLR models |
| `docs/stakeholder-experiences.md` | Parent/teacher dashboard specs, privacy boundary |
| `docs/engagement-signals-research.md` | Behavioral engagement signal research |
