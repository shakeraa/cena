# Cognitive Load Theory & Progressive Disclosure for Educational Mobile Apps

> **Status:** Research reference document
> **Date:** 2026-03-31
> **Context:** Cena adaptive learning platform -- Flutter mobile app for Israeli 14-18 year olds (Hebrew primary, Arabic, English)
> **Scope:** Cognitive psychology principles mapped to mobile UI patterns, with specific recommendations for CENA's actor-based architecture

---

## Table of Contents

1. [Cognitive Load Theory (Sweller)](#1-cognitive-load-theory-sweller)
2. [Working Memory Limitations](#2-working-memory-limitations-millers-law-updated)
3. [Progressive Disclosure in Learning Apps](#3-progressive-disclosure-in-learning-apps)
4. [Scaffolding Patterns](#4-scaffolding-patterns)
5. [Attention Management](#5-attention-management)
6. [Information Architecture for Learning](#6-information-architecture-for-learning)
7. [Dual Coding Theory (Paivio)](#7-dual-coding-theory-paivio)
8. [Spacing & Interleaving on Mobile](#8-spacing--interleaving-on-mobile)
9. [Error Handling & Feedback in Learning](#9-error-handling--feedback-in-learning)
10. [Accessibility & Cognitive Load](#10-accessibility--cognitive-load)
11. [Anti-Patterns That Increase Cognitive Load](#11-anti-patterns-that-increase-cognitive-load)
12. [Progressive Disclosure Mapped to Actor Messages](#12-progressive-disclosure-mapped-to-actor-messages)
13. [Screen-by-Screen Flow Descriptions](#13-screen-by-screen-flow-descriptions)
14. [Typography, Spacing, and Color Guidelines](#14-typography-spacing-and-color-guidelines)
15. [Age-Specific Considerations](#15-age-specific-considerations)

---

## 1. Cognitive Load Theory (Sweller)

### 1.1 The Three Types of Cognitive Load

John Sweller's Cognitive Load Theory (CLT), first formalized in 1988 and refined through 2024, identifies three types of load that compete for the same limited working memory:

**Intrinsic Load** -- complexity inherent in the learning material itself. Determined by element interactivity: how many information elements must be processed simultaneously to understand the concept. Cannot be reduced without simplifying the content, but can be managed through sequencing.

- Low element interactivity: vocabulary words, arithmetic facts (each element is independent)
- High element interactivity: algebraic proofs, physics word problems (many elements must be held simultaneously)
- Cena context: a Bagrut-level calculus problem has inherently high intrinsic load; the app cannot eliminate this, but can manage it through chunking and scaffolding

**Extraneous Load** -- cognitive processing caused by poor instructional design. This is the enemy. Every pixel of unnecessary UI, every extra tap, every confusing navigation path, every animation that serves no learning purpose adds extraneous load. On mobile screens, extraneous load is amplified because the constrained viewport forces more scrolling, smaller text, and more navigation layers.

- Sources in mobile apps: cluttered screens, split attention between related information, redundant information across modalities, unclear navigation, decorative animations, unnecessary screen transitions
- The design mandate: eliminate extraneous load completely. Every UI element must earn its screen space.

**Germane Load** -- cognitive processing that contributes to schema construction and automation. This is the productive cognitive work: connecting new information to existing knowledge, building mental models, practicing retrieval. Maximize this by directing freed-up cognitive resources (from reduced extraneous load) toward learning-productive activities.

- Sources: worked examples that reveal problem structure, self-explanation prompts, interleaved practice that forces discrimination between similar concepts, retrieval practice
- Cena context: hint progression (clue -> bigger clue -> worked solution) is a germane load strategy -- it forces the student to process the problem before seeing the answer

### 1.2 The Total Capacity Constraint

```
Total Cognitive Load = Intrinsic + Extraneous + Germane
```

Total load must not exceed working memory capacity. When it does, learning stops. The student experiences overwhelm, frustration, and disengagement. On mobile, where screen real estate is scarce and distraction is one swipe away, this threshold is reached faster.

**Research basis:**

- Sweller, Ayres & Kalyuga (2011). *Cognitive Load Theory*. Springer. The definitive synthesis of 25+ years of CLT research.
- Sweller (2020). "Cognitive load theory and educational technology." *Educational Technology Research and Development*, 68, 1-16. Updated CLT for digital learning environments.
- Paas, Renkl & Sweller (2003). "Cognitive load theory and instructional design: Recent developments." *Educational Psychologist*, 38(1), 1-4.

### 1.3 How Mobile Screen Constraints Amplify Cognitive Load

Mobile learning creates unique cognitive load challenges that desktop interfaces do not:

**Viewport constraint (split attention amplification):** When content that should be viewed together cannot fit on a single screen, the student must scroll between related elements, holding earlier information in working memory while viewing later information. This is a textbook split attention effect. For math, where a question stem, diagram, and answer options must be comprehended together, scrolling between them fragments comprehension.

**Thumb zone limitations:** Steven Hoober's research (2013, updated 2017) shows that comfortable one-handed phone use restricts interaction to a ~60% arc in the lower half of the screen. Placing critical interactions outside this zone forces awkward grip changes, which divert attention from the learning task.

**Context switching cost:** Push notifications, app switching, and ambient distractions on mobile devices impose a context switching cost of 15-25 seconds to regain prior focus (Mark, Gonzalez & Harris, 2005; Mark, Wang & Niiya, 2014). Mobile learning sessions are inherently fragmented.

**Small text readability cost:** Text below 16px on mobile requires additional cognitive effort to parse (Beymer, Russell & Orton, 2008). Math notation is particularly affected because subscripts, superscripts, and fraction bars demand fine visual discrimination.

**Interaction modality cost:** Touch targets under 44x44pt cause targeting errors. Each failed tap wastes cognitive resources on motor control rather than learning (Parhi, Karlson & Bederson, 2006).

### 1.4 Specific Patterns to Reduce Extraneous Load on Small Screens

| Pattern | CLT Principle | Cena Implementation |
|---------|--------------|---------------------|
| **Single-concept screens** | Reduce element interactivity per view | Each question card shows ONE question, not a list of questions |
| **Proximity grouping** | Contiguity effect: related info together | Question stem + diagram + options on same visible area (no scroll to see options) |
| **Eliminate decoration** | Reduce extraneous processing | No background patterns, no decorative illustrations on question screens |
| **Progressive disclosure** | Manage intrinsic load by sequencing | Hints revealed one at a time, not all at once |
| **Consistent layout** | Reduce orientation cost | Question card always in same screen position; answer always below |
| **Signaling** | Direct attention to relevant elements | Highlight key terms in question stem; color-code difficulty badge |
| **Modality match** | Dual channel optimization | Math notation rendered inline (visual); audio explanations use speech (auditory) |
| **Worked example fading** | Transition from study to practice | Show full worked solution first, then fade steps progressively |

### 1.5 The Worked Example Effect (Critical for Math)

Sweller & Cooper (1985) and subsequent research show that novices learn more from studying worked examples than from solving equivalent problems. This is because worked examples reduce extraneous load (no problem-solving search) while maintaining intrinsic load (the concept complexity remains).

**Key conditions:**

- The effect reverses for experts (the "expertise reversal effect" -- Kalyuga et al., 2003). Expert learners find worked examples redundant, which adds extraneous load.
- Cena's BKT mastery score can control this: students with P(Known) < 0.5 get worked examples; students with P(Known) > 0.7 get practice problems.
- Intermediate students benefit most from "fading" -- start with full worked examples, progressively remove steps until the student is solving independently.

**Mobile implementation:**

```
// Actor message that controls worked example fading
QuestionPresented {
  exerciseId: "calc-derivative-001",
  scaffoldingLevel: 2,  // 0=full practice, 1=partial hints, 2=full worked example
  fadedSteps: [3, 4],   // Steps 3 and 4 are faded (student must fill in)
  totalSteps: 5
}
```

---

## 2. Working Memory Limitations (Miller's Law Updated)

### 2.1 From 7+-2 to 4+-1

Miller's (1956) original "magical number seven, plus or minus two" has been refined by subsequent research. The current scientific consensus:

- **Cowan (2001, 2010):** Working memory capacity is approximately 4 chunks (3-5 range) for novel information, not 7. Miller's original estimate conflated working memory with short-term memory plus rehearsal strategies.
- **Oberauer et al. (2018):** Working memory for novel items without rehearsal or chunking is approximately 3-4 items. Trained chunking strategies can effectively expand this to 7+ by grouping items into meaningful units.
- **Barrouillet & Camos (2015):** Working memory capacity is not just about storage but about the time-based resource sharing between processing and maintenance. Cognitively demanding tasks (like math problem-solving) consume processing time that would otherwise maintain items in memory.

**Implication for mobile learning interfaces:** Design every screen assuming the student can hold 4 independent pieces of information in mind at once. Any more and you are guaranteed to exceed capacity for a significant portion of users.

### 2.2 Chunking Information in Learning Interfaces

Chunking is the process of grouping individual items into meaningful units. A phone number (0524567890) is 10 items; chunked as 052-456-7890, it's 3 items. Interface design must facilitate chunking:

**Visual chunking patterns for mobile:**

| Technique | Example | Working Memory Cost |
|-----------|---------|-------------------|
| Group options semantically | MCQ options sorted by similarity | 4 options = 2-3 chunks (related options chunk together) |
| Use consistent visual containers | Card = one logical unit | 1 chunk per card, regardless of internal elements |
| Hierarchical headings | "Algebra > Quadratics > Factoring" | 1 chunk (the hierarchy itself is the chunk) |
| Progress indicators | 3/10 questions, 45% complete | 1 chunk (ratio/percentage is pre-chunked) |
| Icon + label pairs | Star icon + "XP" | 1 chunk (icon reinforces label, not a separate item) |

**Anti-chunking (increases load):**

- Showing raw numbers without context (372 XP -- what does this mean?)
- Mixing unrelated information in the same visual group
- Using inconsistent visual grouping across screens
- Showing both absolute and relative metrics side by side (37 correct, 78.7%, Level 4, 2340 XP -- 4 separate chunks competing for attention)

### 2.3 Navigation Depth vs Breadth Tradeoffs on Mobile

**The research consensus:**

- **Zaphiris et al. (2002):** For information-seeking tasks, broad+shallow navigation (many options per level, few levels deep) outperforms deep+narrow navigation in speed and error rate. But breadth beyond 8-10 items per level shows diminishing returns.
- **Miller & Remington (2004):** Optimal depth-breadth tradeoff depends on information scent (how well labels predict content). Strong scent enables broader menus; weak scent requires narrower, deeper menus with more context.
- **Cockburn & McKenzie (2001):** On small screens, breadth is even more important because deep navigation requires remembering path history across screen transitions.

**For CENA (educational context):**

The content hierarchy is Course -> Module -> Lesson -> Activity. This is 4 levels deep, which is at the upper limit. Navigation design must ensure:

- Level 1 (Course/Subject): 5 subjects max -- fits one screen, one tap to enter
- Level 2 (Module/Topic): 6-8 modules visible -- scrollable list with strong labels
- Level 3 (Lesson/Concept): 5-10 concepts -- each with mastery indicator
- Level 4 (Activity): The student arrives directly here via adaptive routing; they rarely navigate here manually

**Recommendation:** Students should almost never navigate deeper than 2 taps from home. The adaptive engine (actor system) should present the right activity directly. Manual navigation is a fallback, not the primary path.

### 2.4 Tab Bar Design (Max 5 Items)

Apple's HIG and Google's Material guidelines both recommend 3-5 bottom navigation items. This aligns with Cowan's 4+-1 working memory limit: each tab label must be held in mind as a potential destination.

**CENA's current bottom nav (from HomeScreen):**

```
Home | Sessions | Progress | Settings
```

This is 4 tabs -- within the optimal range. Analysis:

| Tab | What it maps to | Cognitive role |
|-----|----------------|---------------|
| Home | Dashboard, greeting, quick-start | Orientation ("where am I, what should I do") |
| Sessions | History + active session entry | Core action ("learn") |
| Progress | XP, badges, knowledge graph | Reflection ("how am I doing") |
| Settings | Language, account, preferences | Meta ("configure") |

**Recommendation:** Keep 4 tabs. Do NOT add a 5th tab. The Knowledge Graph, if exposed, should live within the Progress tab as a sub-view (bottom sheet or nested navigation), not as a separate tab. Adding a 5th tab for the graph would push the count to the cognitive limit and fragment the "how am I doing" mental model across two tabs.

### 2.5 Bottom Sheet vs Full Screen for Supplementary Info

Progressive disclosure often requires showing supplementary information (hints, explanations, methodology descriptions) without losing context of the primary view (the question). The choice between bottom sheet and full screen is a CLT decision:

**Bottom sheet (DraggableScrollableSheet in Flutter):**

- Use when: the student needs to reference the primary content while reading the supplementary content (split attention mitigation)
- Example: showing a hint while the question is still visible above the sheet
- Max height: 60% of screen (40% of question visible above)
- Use cases: hints, brief explanations, methodology selection, approach change

**Full screen:**

- Use when: the supplementary content requires full attention and the primary context is not needed simultaneously
- Example: worked solution with step-by-step walkthrough, cognitive load break, session summary
- Use cases: detailed solutions, video micro-lessons, settings, onboarding

**Cena's current pattern (correct):** The `_ApproachSheet` in `action_buttons.dart` uses `showModalBottomSheet` for methodology selection. This is correct -- the student can see the question above while choosing an approach. The `CognitiveLoadBreak` uses full screen. Also correct -- during a break, the question context is irrelevant.

---

## 3. Progressive Disclosure in Learning Apps

### 3.1 The Four Levels of Progressive Disclosure

Progressive disclosure is the practice of revealing information in order of importance and necessity. In educational mobile apps, this maps to four distinct levels:

**Level 1: Core Action** (always visible, primary focus)

The single thing the student is doing right now. On the question screen: the question stem, options/input, and submit button. Nothing else competes for attention. This level occupies 60-70% of the viewport.

Screen real estate budget:
- Question stem: 30-40% of viewport
- Answer options/input: 20-30% of viewport
- Submit button: clear, bottom of visible area, in thumb zone

**Level 2: Context** (one tap away, reveals on demand)

Supporting information that enriches the learning experience but is not required to complete the core action. Hints, difficulty indicator, methodology badge. Accessed via tap on a clearly labeled trigger (the hint button, the difficulty badge).

- Hint button (tap to reveal progressive hints)
- Difficulty badge (tap to see "why this difficulty")
- Question type indicator (informational, no action needed)
- Timer/progress bar (ambient awareness, not action-requiring)

**Level 3: Deep Dive** (requires deliberate navigation)

Content for students who want to understand deeply. Full worked solutions, related theory, prerequisite concept explanations, micro-lessons. Accessed after answering (correct or incorrect), or via an explicit "learn more" action.

- Worked solution (shown in feedback overlay, expandable)
- Related concept links (shown after the session, in progress tab)
- Micro-lesson trigger ("I need help understanding this concept")
- Error type explanation ("You made a conceptual error. Here's why...")

**Level 4: Meta** (accessible but never interrupting)

Analytics, settings, account management, knowledge graph, achievement history. Never shown during an active learning session unless explicitly requested. Lives in the Progress and Settings tabs.

- XP and level progress
- Badge collection
- Knowledge graph visualization
- Session history
- Language settings
- Account management

### 3.2 How to Layer These on Mobile Without Overwhelming

The key principle: **each level should be invisible until needed, but discoverable when wanted.**

**Level 1 -> Level 2 transition patterns:**

| Trigger | UI Pattern | Flutter Widget |
|---------|-----------|----------------|
| Tap hint button | Animated expand beneath question | `AnimatedContainer` with `AnimatedCrossFade` |
| Tap difficulty badge | Tooltip or small overlay | `Tooltip` or custom `OverlayEntry` |
| Auto-show after incorrect answer | Slide-up feedback card | `SlideTransition` or `AnimatedPositioned` |

**Level 2 -> Level 3 transition patterns:**

| Trigger | UI Pattern | Flutter Widget |
|---------|-----------|----------------|
| Tap "show full solution" | Bottom sheet (60% height) | `DraggableScrollableSheet` |
| Tap "learn this concept" | Full screen push | `GoRouter.push()` to micro-lesson |
| Tap error type badge | Expanding card with explanation | `ExpansionTile` or `AnimatedContainer` |

**Level 3 -> Level 4 transition patterns:**

| Trigger | UI Pattern | Flutter Widget |
|---------|-----------|----------------|
| Navigate to Progress tab | Tab switch (bottom nav) | `BottomNavigationBar` index change |
| Tap "view knowledge graph" | Full screen push from Progress | `GoRouter.push()` |
| Session end -> summary | Automatic transition | State-driven (`SessionState.isActive == false`) |

### 3.3 The Disclosure Budget Per Screen

At any moment, the student should see at most:

- 1 primary action (answer the question)
- 2-3 ambient indicators (progress, time, fatigue emoji)
- 2-3 secondary actions (hint, skip, change approach)
- 0-1 contextual information (current hint, methodology badge)

**Total visible elements: 6-8.** This maps to two chunks in working memory: "the question stuff" (1 chunk) and "the meta stuff" (1 chunk). Well within the 4+-1 limit.

---

## 4. Scaffolding Patterns

### 4.1 Training Wheels Mode for New Users

**Research basis:** Carroll & Carrithers (1984) demonstrated that "training wheels" interfaces -- versions of software with advanced features disabled -- reduced learning time by 50% and errors by 75% compared to full-featured interfaces.

**Cena onboarding implementation (MOB-013 already partially implements this):**

The five-page onboarding flow (Welcome -> Subject -> Grade -> Diagnostic -> Ready) is a training wheels pattern. After onboarding, the first 3 sessions should operate in a reduced-complexity mode:

| Feature | Session 1-3 (Training Wheels) | Session 4+ (Full) |
|---------|------------------------------|-------------------|
| Question types | MCQ only | All types enabled |
| Hint system | Auto-show first hint after 30s | Hint button only, student-initiated |
| Methodology selection | Hidden (system chooses) | "Change Approach" button visible |
| Difficulty range | 1-4 (easy only) | Full range (1-10) |
| Progress bar | Simple progress only (no fatigue) | Full bar with fatigue indicator |
| Session duration | Fixed 15 min | Configurable 12-30 min |
| Knowledge graph | Hidden | Accessible from Progress tab |
| XP/Gamification | Full (motivational from day 1) | Full |

**Actor message for scaffolding state:**

```
// The StudentActor maintains scaffolding state
message StudentScaffoldState {
  int32 sessionsCompleted = 1;
  ScaffoldLevel currentLevel = 2;    // 0=full, 1=intermediate, 2=training wheels
  repeated string unlockedFeatures = 3;
  map<string, bool> featureFirstSeen = 4;  // Track what's been introduced
}

// ScaffoldLevel determines which UI features are visible
enum ScaffoldLevel {
  TRAINING_WHEELS = 0;  // Sessions 1-3
  INTERMEDIATE = 1;     // Sessions 4-10
  FULL = 2;             // Session 11+
}
```

### 4.2 Gradual Feature Reveal Based on Usage

**Research basis:** Kellogg & Whiteaker (2009) found that interface complexity should scale with user competence. Revealing features too early leads to "feature shock"; revealing too late leads to frustration when users know they need a capability but cannot find it.

**CENA's graduated feature reveal schedule:**

| Session Count | New Feature Revealed | Reveal Method |
|--------------|---------------------|---------------|
| 1 | Basic question answering, XP | Onboarding teaches these |
| 3 | Hint button | Pulsing animation + tooltip: "Need help? Tap here" |
| 5 | Skip question | Subtle appearance with first-use tooltip |
| 7 | Fatigue indicator | First appearance when fatigueScore > 0.3 |
| 10 | Change Approach button | First-use bottom sheet with explanation |
| 15 | Knowledge graph (Progress tab) | Progress tab badge: "New! See your knowledge map" |
| 20 | Free-text/numeric question types | Gradual introduction alongside MCQ |

**The reveal animation pattern (for new features):**

```dart
// Pulsing "new feature" attention cue
class NewFeatureHighlight extends StatelessWidget {
  // 1. Show a subtle glow/pulse animation around the new element
  // 2. Show a brief tooltip on first interaction
  // 3. Mark as "seen" in local storage
  // 4. Remove highlight after first use
}
```

### 4.3 Contextual Help That Appears Exactly When Needed

**Research basis:** Lim & Finkelstein (2012) showed that proactive contextual help (appearing at the moment of difficulty) is 2-3x more effective than reactive help (user seeks help after failing). The timing of help delivery is as important as the help content itself.

**Cena contextual help triggers:**

| Trigger Signal | Help Delivered | UI Pattern |
|---------------|----------------|------------|
| 2 consecutive wrong answers on same concept | "Let me show you a different way to think about this" | Animated card slides up from bottom |
| Response time > 2x average for this student | "Take your time. Here's a hint to get started" | Gentle pulse on hint button |
| Student scrolls up and down rapidly (confusion signal) | "The question asks about [key term]. Focus on this part" | Highlight key phrase in question |
| Student taps submit without selecting an option | "Don't forget to choose an answer first" | Error banner with warm tone |
| Student uses all hints and still wrong | "Let's learn this concept first" + micro-lesson link | Full feedback with micro-lesson CTA |
| First encounter with a new question type | Brief animated tutorial (3s) | Overlay with "Got it" dismiss |

### 4.4 Fading Scaffolds as Competence Increases

**Research basis:** Wood, Bruner & Ross (1976) defined scaffolding as temporary support that is gradually withdrawn as the learner becomes capable. Renkl & Atkinson (2003) formalized this as the "fading effect" for worked examples: start with fully worked examples, progressively remove steps.

**Fading schedule tied to BKT mastery:**

```
P(Known) < 0.3:  Full scaffold (worked examples, auto-hints, easy difficulty)
P(Known) 0.3-0.5: Partial scaffold (faded worked examples, hint available, medium difficulty)
P(Known) 0.5-0.7: Minimal scaffold (hint available but not prompted, adaptive difficulty)
P(Known) 0.7-0.85: No scaffold (standard question, hints only on request)
P(Known) > 0.85: Mastery mode (harder questions, spaced review, no scaffolding)
```

**How the actor tracks this:**

The `StudentActor` holds the BKT parameters per concept. When the mastery probability crosses a threshold, the actor changes the scaffolding level in the next `QuestionPresented` message. The Flutter client reads `scaffoldingLevel` and adjusts which UI elements are visible.

### 4.5 The Expertise Reversal Effect in Scaffolding

**Research basis:** Kalyuga, Ayres, Chandler & Sweller (2003). Instructional techniques that reduce cognitive load for novices (worked examples, integrated formats, hints) can increase cognitive load for experts. The explanation: experts have pre-existing schemas that conflict with the instructional scaffolding, creating redundancy.

**Cena implementation:** When BKT shows mastery (P(Known) > 0.85), the system must NOT show hints, worked examples, or methodology badges. These become extraneous load for the mastered student. The UI should be stripped to the essential: question + answer + submit. Nothing else.

---

## 5. Attention Management

### 5.1 Focus Mode Design (Distraction-Free Learning)

**Research basis:** Rosen, Carrier & Cheever (2013) found that students studying with phones nearby (even silenced) performed worse than students with phones in another room. The mere presence of the phone as a potential distraction consumes cognitive resources.

Since CENA is a phone app, the app itself is the potential distractor. The session screen must be designed as a "focus room":

**Focus mode principles:**

1. **No notifications during active session.** The app should request Do Not Disturb permission and enable it during sessions.
2. **No visible navigation away from the session.** The home icon, bottom nav, and other navigation elements should be hidden during an active session. Only the question, progress bar, and action buttons should be visible.
3. **No gamification pop-ups during problem-solving.** XP awards and badge notifications should queue and display BETWEEN questions (in the feedback overlay), never DURING a question.
4. **Minimal animation.** During the question-answering phase, no elements should animate. Animation is reserved for transitions (question -> feedback -> next question).
5. **Consistent visual layout.** The question card should occupy the same screen position for every question. The student's eyes should never have to search for where the content is.

**What Cena already does well:**

- The `SessionScreen` hides the bottom nav during active sessions (only the session scaffold is shown)
- The `FeedbackOverlay` is a full-screen overlay that prevents interaction with the question
- The `CognitiveLoadBreak` is a separate full-screen experience

**What Cena should improve:**

- The `ProgressBar` with stats, timer, and fatigue indicator sits at the top of the session screen. For some students, the timer is a stress-inducing distraction. Consider making the timer opt-in (visible only if the student taps the progress area).
- The methodology badge (`_MethodologyBadge`) adds cognitive load during problem-solving. It tells the student what learning method is being used, which is meta-information they do not need while solving a problem. Consider moving it to the feedback overlay (shown after the answer, not during).

### 5.2 Split Attention Effect

**Research basis:** Chandler & Sweller (1991, 1992). When information sources that must be mentally integrated are physically separated, the learner must split attention between them, consuming working memory to maintain one source while processing the other.

**Critical violations on mobile (and how to fix them):**

| Split Attention Problem | Mobile Manifestation | Solution |
|------------------------|---------------------|----------|
| Question stem above the fold, options below | Student scrolls between question and options | Ensure question + all options fit on ONE viewport (use smaller text if needed; never split) |
| Diagram on question card, related text below | Student scrolls between diagram and text | Place diagram immediately adjacent to relevant text, or use an interactive overlay that shows text on the diagram |
| Hint text in a separate area from the question | Student must remember the hint while reading the question | Show hints inline, integrated into the question card (Cena's `_HintDisplay` is correctly placed below the question) |
| Error type badge separate from explanation | "Conceptual error" badge alone is meaningless | Always show the error type WITH its explanation in the same visual container |

**The contiguity principle (Mayer, 2001):** Related text and images should be placed physically close together, not separated by other content or by a page boundary. This reduces the need to mentally integrate spatially separated sources.

**CENA-specific: RTL layout implications.** For Hebrew and Arabic layouts, the spatial contiguity principle still holds but the direction reverses. Labels should appear to the right of their associated elements in RTL. Cena's `CenaTheme` already handles RTL via Flutter's `Directionality`, but custom layouts must be audited for contiguity violations when mirrored.

### 5.3 Redundancy Effect

**Research basis:** Sweller (2005). When multiple sources of information that are self-contained are presented together, the redundant source creates extraneous processing. The learner must process both sources and verify they say the same thing.

**Mobile-specific manifestation:** Showing the same information in text AND as a separate visual element (e.g., "You are 75% through the session" as text alongside a progress bar showing 75%). The progress bar alone is sufficient. The text is redundant.

**CENA's current redundancy:**

- The `ProgressBar` shows both a numeric accuracy percentage ("78%") AND a colored progress indicator. The percentage is sufficient; the color is a signal, not a redundancy, because it encodes fatigue (a different dimension). This is acceptable.
- The `_SummaryTile` in the session-ended view shows icon + label + value. This is efficient: icon supports quick scanning, label provides semantic meaning, value provides precision. Not redundant -- each element serves a different function.
- **Potential redundancy to watch for:** Do not show both "Question 7 of 40" AND a progress bar AND a percentage. Pick two at most.

### 5.4 Signaling (Visual Cues That Guide Attention)

**Research basis:** de Koning, Tabbers, Rikers & Paas (2009). Signaling (also called cueing) uses visual cues -- bold text, arrows, color highlighting, icons -- to direct the learner's attention to important information. Signaling reduces extraneous processing by reducing visual search.

**Mobile signaling patterns for CENA:**

| Signal Type | Use Case | Implementation |
|------------|----------|----------------|
| **Color coding** | Difficulty levels (green/yellow/red) | `_DifficultyBadge` already implements this correctly |
| **Bold key terms** | Highlight mathematical keywords in question stems | Detect keywords in question text server-side; wrap in `TextSpan` with `FontWeight.w700` |
| **Icon prefixes** | Action buttons with semantic icons | Hint (lightbulb), Skip (skip icon), Change Approach (tune icon) -- already implemented |
| **Progress color shift** | Fatigue awareness through bar color | Green -> yellow -> red gradient -- already implemented in `ProgressBar._fatigueColor()` |
| **Pulsing animation** | New/available features | Subtle pulse on hint button when a hint is available but not yet used |
| **Size hierarchy** | Question stem is largest text on screen | Ensure `bodyLarge` (16sp) for question text, `bodyMedium` (14sp) for options |

### 5.5 Mobile-Specific: Thumb Zones, Visual Hierarchy, and Scroll Patterns

**Thumb zone research (Hoober, 2013; Hoober & Patt, 2017):**

Three zones on a phone screen held in one hand (right-handed):
- **Natural zone** (easy reach): Bottom-center arc, ~40% of screen. Primary actions go here.
- **Stretch zone** (reachable with effort): Top and far sides, ~30%. Secondary actions go here.
- **Ow zone** (hard to reach): Top-left corner, ~10%. Never put critical actions here.

**CENA session screen thumb zone audit:**

| Element | Current Position | Thumb Zone | Recommendation |
|---------|-----------------|-----------|----------------|
| Submit button | Below answer options | Natural zone | Correct |
| Hint button | Below answer area | Natural zone | Correct |
| Skip button | Below answer area | Natural zone | Correct |
| End Session | Top right | Stretch zone | Acceptable (destructive action should require effort) |
| Progress bar | Top of screen | Ow zone | Acceptable (ambient info, no tap needed) |
| Question text | Center-top | Stretch zone | Acceptable (read-only, no tap needed) |
| MCQ options | Center | Natural-to-stretch | Correct |

**Scroll pattern recommendation:** Use the F-pattern for information screens (home, progress) and the single-column pattern for action screens (session, question). The F-pattern supports scanning; the single-column pattern supports focused attention.

---

## 6. Information Architecture for Learning

### 6.1 Content Hierarchy: Course -> Module -> Lesson -> Activity

The cognitive science of learning hierarchies aligns with how students build knowledge schemas. Each level of the hierarchy maps to a different grain size of understanding:

```
Course (Subject):     Mathematics, Physics, Chemistry, Biology, CS
  Module (Topic):     Algebra, Geometry, Calculus, Statistics, Probability
    Lesson (Concept): Quadratic equations, Factoring, Completing the square
      Activity:       Practice problem, Worked example, Micro-lesson, Assessment
```

**Research basis:** Reigeluth (1999) Elaboration Theory. Instruction should start with a general overview (epitome) and progressively elaborate. Each level of elaboration adds detail without losing the context of the whole.

**Mobile mapping:** Each level of the hierarchy maps to a navigation level:

| Hierarchy Level | Screen Type | Navigation Pattern |
|----------------|-------------|-------------------|
| Course (Subject) | Home tab, Subject grid | Tap card -> enter subject |
| Module (Topic) | Scrollable list within subject | Tap topic -> see concepts |
| Lesson (Concept) | Concept card with mastery indicator | Usually auto-selected by actor; manual browse available |
| Activity | Full-screen session (question card) | Presented by actor, no navigation needed |

### 6.2 Flat vs Deep Navigation for Different Age Groups

**Research basis:**

- Younger students (10-14): Prefer flat, visual navigation with large tap targets and minimal text. Icons should be self-explanatory without labels. Navigation depth should be limited to 2 levels.
- Older students (15-18, CENA's target): Tolerate 3-4 levels of depth. Text labels are acceptable. Can handle more information density. Still prefer visual cues over text-only menus.
- Adults (18+): Tolerate deepest navigation (5+ levels). Text-heavy navigation is acceptable. Breadcrumbs are useful.

**For CENA (14-18 year olds):**

- Maximum navigation depth: 3 taps from home to activity
- Use icon+label combinations (never icon-only or label-only)
- Subject selection: large visual cards (current `_SubjectGrid` is correct)
- Concept selection: list with mastery indicators (color dots or progress bars)

### 6.3 Search vs Browse for Educational Content

**Research basis:** Marchionini (1995). Browse is preferred when the user has a vague goal ("I want to study math"). Search is preferred when the user has a specific goal ("I need to review quadratic factoring").

**CENA recommendation:**

- **Primary path: Actor-driven (no navigation needed).** The student taps "Start Learning" and the actor selects the optimal activity. This eliminates all navigation decisions.
- **Secondary path: Browse by subject/topic.** For students who want to choose what to study.
- **Tertiary path: Search.** For students who know exactly what concept they need. Search is a Level 3 progressive disclosure feature -- available but not prominent.

### 6.4 "Where Am I in the Curriculum" Patterns

Students need to maintain a sense of orientation within the knowledge space. Without this, they feel lost, which increases anxiety (extraneous load).

**Patterns for curriculum orientation on mobile:**

| Pattern | Description | CENA Implementation |
|---------|-------------|---------------------|
| **Breadcrumb trail** | "Math > Algebra > Quadratics" at top of screen | Use in browse mode, not during active session |
| **Mastery map** | Visual grid of concepts colored by mastery level | Knowledge graph screen (Progress tab) |
| **Progress percentage** | "You've mastered 23 of 45 concepts in Algebra" | Show on subject/topic browse screens |
| **Session context badge** | "Currently studying: Quadratic Equations" | Show in progress bar during session |
| **Milestone markers** | "3 more concepts to complete this module" | Show on topic list screens |

**Do NOT use breadcrumbs during an active session.** They provide navigational context at the cost of attention fragmentation. The session screen should be a self-contained universe: the student knows they are in a session, that is enough context.

---

## 7. Dual Coding Theory (Paivio)

### 7.1 Combining Text + Visuals for Better Retention

**Research basis:** Paivio (1986). Information encoded in both verbal and visual channels creates two independent memory traces, making retrieval more likely. This is the multimedia principle (Mayer, 2001): people learn better from words + pictures than from words alone.

**Critical caveat:** The benefit only occurs when text and visuals present complementary, non-redundant information. Identical information in both channels creates the redundancy effect (Section 5.3).

**CENA math-specific dual coding:**

| Content Type | Verbal Channel | Visual Channel | Combined Effect |
|-------------|----------------|----------------|-----------------|
| Algebraic equation | "Solve for x in 2x + 6 = 14" | LaTeX-rendered equation | Text provides context; equation provides precision |
| Geometric proof | Proof steps in text | Diagram with labeled parts | Text drives logic; diagram provides spatial reference |
| Function behavior | "The function increases then decreases" | Graph showing the curve | Text summarizes; graph provides shape |
| Data analysis | "The median is 45" | Histogram with marked median | Text provides value; graph provides distribution context |

### 7.2 When to Use Animation vs Static Images

**Research basis:**

- Tversky, Morrison & Betrancourt (2002): Animations are NOT inherently superior to static images for learning. Animations are beneficial only when the content is inherently about change over time (processes, procedures) and when the animation allows learner control (pause, replay, step).
- Lowe & Schnotz (2022): Complex animations can actually harm learning if they exceed the learner's processing capacity. Animations should be simple, short, and controllable.

**CENA animation decision matrix:**

| Content | Use Animation | Use Static | Rationale |
|---------|:------------:|:----------:|-----------|
| Step-by-step solution walkthrough | Yes | | Process unfolds over time; animation shows sequence |
| Geometric constructions | Yes | | Construction process is temporal; animation shows how |
| Function graph | No | Yes | Static graph with labeled features is sufficient |
| Diagram for a word problem | No | Yes | Spatial relationships are static |
| Data visualization | Sometimes | Sometimes | Animated only if showing change over time (time series) |
| Answer feedback (correct/incorrect) | Yes | | Short celebratory/corrective animation aids emotional encoding |

### 7.3 Audio + Visual Combinations

**Research basis:** The modality effect (Mayer, 2001). Presenting text auditorily (narration) while showing visuals uses both working memory channels, reducing load on either. But presenting text visually (on screen) while showing visuals overloads the visual channel.

**CENA implementation:**

For micro-lessons (when implemented):
- Narrated explanation + diagram/animation: optimal combination
- On-screen text + diagram: acceptable if no narration is possible
- On-screen text + narration of the same text: NEVER (redundancy effect)

For the current question-answering loop:
- Audio is not used (correct for assessment). Questions are text + math notation + occasional diagrams.
- Haptic feedback on answer submission (already implemented via `HapticFeedback.heavyImpact()` / `lightImpact()`) provides a non-visual confirmation channel.

### 7.4 Mobile-Specific: Landscape for Video, Portrait for Reading

**Research basis:**

- Kim et al. (2014): Mobile users prefer landscape orientation for video content (wider aspect ratio matches video frames) and portrait for text-heavy content (more vertical space for reading).
- Rello & Baeza-Yates (2013): Reading speed on mobile is 10-15% slower than on desktop. Shorter line lengths (40-60 characters) in portrait mode improve readability.

**CENA orientation strategy:**

| Content Type | Orientation | Lock? |
|-------------|-------------|-------|
| Question answering (text + MCQ) | Portrait | Yes (lock to portrait) |
| Micro-lesson video | Landscape (auto-rotate) | No (let user rotate) |
| Knowledge graph | Either (responsive layout) | No |
| Session summary | Portrait | Yes |
| Onboarding | Portrait | Yes |

**Flutter implementation:** Use `SystemChrome.setPreferredOrientations()` per screen.

---

## 8. Spacing & Interleaving on Mobile

### 8.1 How to Design Spaced Review into the UX Flow

**Research basis:**

- Cepeda et al. (2006): Distributing practice over time (spacing effect) improves long-term retention by 10-30% compared to massed practice. The optimal spacing interval depends on the retention interval: for Bagrut exams months away, review intervals of days-to-weeks are optimal.
- Kornell & Bjork (2008): Students consistently prefer massed practice (it feels more fluent) but spaced practice produces better outcomes. The app must override the student's preference for massed practice.

**UX patterns for spacing in CENA:**

**1. "Review Due" concept badges on the home screen:**

```
Home Screen:
  [Greeting Card]
  [Review Due: 3 concepts need review] <-- Prominent, above "Start Session"
    - Quadratic factoring (last practiced 5 days ago)
    - Linear equations (last practiced 7 days ago)
    - Graph interpretation (last practiced 3 days ago)
  [Start Session] <-- Normal session button
```

The "Review Due" section surfaces concepts whose HLR (Half-Life Regression) model predicts memory strength has dropped below the retrieval threshold. This is not a separate "review mode" -- it is a contextual prompt that feeds into the normal session's question selection.

**2. Interleaved review within sessions:**

The actor system already supports methodology switching. Spaced review can be implemented as a methodology: every N questions, the actor inserts a review question for a concept with declining memory strength. The student never explicitly "does a review session" -- review is woven into their normal learning.

**3. Post-session review prompt:**

After a session ends, before returning to home, show a brief (3 question) retrieval practice set for concepts from previous sessions. Frame it as "Quick brain boost" rather than "Review."

### 8.2 Mixed Practice Presentation

**Research basis:**

- Rohrer & Taylor (2007): Interleaving different problem types (e.g., mixing addition, subtraction, and multiplication problems) produces better discrimination and transfer than blocked practice (all addition, then all subtraction). Effect size: d = 0.43 for delayed retention.
- Birnbaum et al. (2013): Interleaving helps because it forces the student to identify which strategy applies, which is a key component of transfer.

**CENA's actor-driven interleaving:**

The `Methodology.interleaved` methodology already exists in the system. The UI should support interleaving by:

1. Showing the concept label on each question card so the student knows which domain the question comes from (this aids discrimination learning)
2. NOT grouping questions by topic visually (no "Chapter 3 Questions" headers)
3. Showing a brief methodology badge ("Mixed Practice") so the student understands why topics are jumping around

### 8.3 Review Scheduling UI Patterns

**Research basis:** The FSRS (Free Spaced Repetition Scheduler) algorithm, successor to SM-2 (Anki), provides optimal review intervals based on difficulty, stability, and retrievability parameters. Cena's HLR model serves the same purpose.

**UI patterns for review awareness:**

| Pattern | Description | When to Show |
|---------|-------------|-------------|
| **Decay heat map** | Grid of concepts, color-coded by predicted memory strength (green=strong, yellow=fading, red=forgotten) | Knowledge graph screen; concept list in browse mode |
| **Daily review count** | "5 concepts need review today" badge on home screen | Home tab, updated daily |
| **Streak integration** | "Keep your streak by reviewing 3 concepts" | Push notification (streak at risk) |
| **Session-end prompt** | "3 concepts from last week could use a refresh" | Session summary screen |

### 8.4 "Knowledge Decay" Visualizations

**Research basis:** Ebbinghaus (1885) forgetting curve. Memory strength decays exponentially after learning, with each successful retrieval resetting the curve and flattening the decay rate.

**Mobile visualization for students:**

The forgetting curve is a powerful metacognitive tool -- showing students WHY they need to review. But the visualization must be simple enough to not create cognitive load itself.

**Recommended visualization:** Per-concept "strength bars" that visually shrink over time:

```
Concept: Quadratic Equations
[========= ] 90% strength (reviewed yesterday)

Concept: Linear Equations
[======    ] 60% strength (reviewed 5 days ago)

Concept: Trigonometry
[===       ] 30% strength (reviewed 12 days ago, review needed!)
```

Use color coding: green (>70%), yellow (40-70%), red (<40%). This maps the abstract forgetting curve to a concrete, glanceable visual.

---

## 9. Error Handling & Feedback in Learning

### 9.1 Immediate vs Delayed Feedback Design

**Research basis:**

- Shute (2008), meta-analysis: The timing of feedback depends on the learning goal:
  - **Immediate feedback** is better for procedural tasks (arithmetic, formula application) where the student needs to correct errors before they become habitual.
  - **Delayed feedback** is better for conceptual understanding where the student benefits from struggling with the problem before seeing the answer (productive failure, Kapur 2008).
- Attali & van der Kleij (2017): In computer-based testing, immediate feedback improves performance on similar items but delayed feedback improves transfer to dissimilar items.

**CENA feedback timing strategy:**

| Context | Feedback Timing | Rationale |
|---------|----------------|-----------|
| MCQ correct answer | Immediate (< 500ms) | Reinforce correct schema immediately |
| MCQ wrong answer | Immediate (< 500ms) | Prevent error consolidation |
| Free-text correct | Immediate (< 1s) | Confirm understanding |
| Free-text wrong (conceptual error) | Brief delay (2-3s) | Let student see their answer against the correct one; time to self-correct |
| Productive struggle detected | No feedback until student requests hint | Productive failure -- interruption harms learning |
| Proof/multi-step | Step-level feedback after each step (if micro-lesson mode) | VanLehn (2011): step-level feedback is 2x more effective than problem-level |

### 9.2 Constructive Error Messages That Teach

**Research basis:**

- Narciss & Huth (2004): Elaborated feedback (explaining WHY the answer is wrong and HOW to fix it) produces better learning than simple knowledge-of-results feedback (just "correct/incorrect"). But only when the elaboration is relevant to the student's specific error.
- Cena already implements this: the `FeedbackOverlay` shows error type badges (`ErrorType.conceptual`, `ErrorType.procedural`, `ErrorType.careless`, etc.) and a worked solution.

**Error message design principles for mobile:**

1. **Name the error type** (already implemented via `_ErrorTypeBadge`)
2. **Explain what went wrong** in one sentence, using the student's language (not mathematical jargon for novices)
3. **Show the correct path** (worked solution, already implemented via `_WorkedSolutionCard`)
4. **Offer a next step** ("Try a similar problem" or "Review this concept first")

**Example error feedback flow (mobile screens):**

```
Screen 1 (FeedbackOverlay):
  "Not quite right" (red overlay)
  Error type: "Procedural Error"
  "You applied the formula correctly but made an arithmetic mistake in step 3"
  [Tap to continue]

Screen 2 (Expanded feedback, optional):
  Worked solution with step 3 highlighted
  "In step 3, 2 * (-3) = -6, not -3"
  [Got it] [Try Similar Problem]
```

### 9.3 Try-Again Patterns vs Reveal-Answer Patterns

**Research basis:**

- Finn & Metcalfe (2010): When students make a high-confidence error (they were sure of a wrong answer), corrective feedback produces stronger learning than when they made a low-confidence error. The "hypercorrection effect" suggests that the surprise of being wrong enhances memory encoding.
- Pashler et al. (2005): Multiple attempts are beneficial only when feedback or hints are provided between attempts. Blind retry (just "try again" with no additional information) does not help.

**CENA try-again strategy:**

| Confidence Level | Strategy | UI Pattern |
|-----------------|----------|------------|
| Low (quick answer, low difficulty) | Reveal answer immediately | Show correct answer + brief explanation |
| Medium (moderate time, moderate difficulty) | One retry with hint | Show "Not quite. Here's a hint: [hint text]. Try again." |
| High (long deliberation, high difficulty) | Reveal answer with full explanation | Hypercorrection opportunity: show why the wrong answer feels right but is wrong |

**The actor determines confidence from behavioral signals:**

```
// Response time relative to student's baseline indicates confidence
if (responseTime < 0.5 * studentBaseline) -> low confidence (guessing)
if (responseTime > 2.0 * studentBaseline) -> high confidence (deliberated)
```

### 9.4 Hint Progression (Clue -> Bigger Clue -> Solution)

CENA already implements progressive hints via the `_HintDisplay` widget and `ActionButtons._HintButton`. The research basis and design are sound. The key principles:

**Research basis:** Chi et al. (2001). Self-explanation is one of the strongest predictors of learning gain. Progressive hints work because each hint level reduces the problem space while still requiring the student to do the final reasoning.

**CENA's 3-level hint progression:**

```
Level 0 (no hint):     Student works independently
Level 1 (orientation): Points to the relevant concept area
                        "Think about what happens when you distribute the negative sign"
Level 2 (strategy):    Suggests a specific approach
                        "Try expanding (x - 3)(x + 2) first, then simplify"
Level 3 (solution):    Reveals the worked solution
                        "Step 1: ... Step 2: ... Step 3: ..."
```

**UI progression:**

- Level 1: Small text card below question (inline, `_HintDisplay`)
- Level 2: Expanded card with more detail
- Level 3: Bottom sheet with full worked solution + "I understand" confirmation

**Cost to student:** Each hint costs a fraction of the XP they would earn for a correct answer. This is a gentle disincentive that preserves the "productive struggle" window:
- 0 hints used: 100% XP
- 1 hint used: 80% XP
- 2 hints used: 50% XP
- 3 hints used (solution revealed): 20% XP

---

## 10. Accessibility & Cognitive Load

### 10.1 Dyslexia-Friendly Typography

**Research basis:**

- Rello & Baeza-Yates (2013): Sans-serif fonts (Arial, Verdana, Helvetica) are significantly easier to read for people with dyslexia than serif fonts. Monospace fonts also performed well.
- Rello, Pielot & Marcos (2016): Font size of 18px or larger significantly improved reading performance for people with dyslexia on mobile.
- British Dyslexia Association guidelines: Line spacing of 1.5x, word spacing of 0.35em minimum, avoid fully justified text (use left-aligned or, for RTL, right-aligned).

**CENA typography for dyslexia support:**

| Parameter | Standard Mode | Dyslexia Mode |
|-----------|--------------|---------------|
| Font family | Heebo (Hebrew), Noto Sans Arabic, Inter (Latin) | Same (all are sans-serif, already compliant) |
| Body text size | 14-16sp | 18sp minimum |
| Line height | 1.5 | 1.8 |
| Letter spacing | Default | +0.05em |
| Word spacing | Default | +0.1em |
| Text alignment | Right (RTL) | Right (RTL), never justified |
| Background color | White (#FFFFFF) | Cream (#FFF8E7) -- reduces contrast glare |
| Paragraph width | Full width | Max 60 characters per line |

**Implementation:** Add a `DyslexiaMode` toggle in Settings. When enabled, apply a `ThemeData` overlay that adjusts the text theme. Use `SharedPreferences` to persist.

### 10.2 ADHD-Accommodating Interface Patterns

**Research basis:**

- Barkley (2015): ADHD affects executive function, working memory, and sustained attention. Interfaces should minimize the number of decisions required and provide clear, immediate feedback.
- Salomone et al. (2020): Digital interventions for ADHD benefit from: clear visual structure, minimal distractions, frequent positive reinforcement, short task durations, and visible progress.

**CENA ADHD accommodations:**

| Feature | Implementation | Rationale |
|---------|---------------|-----------|
| **Shorter default session** | 12 min default (vs 25 min standard) | Attention span is shorter; more frequent breaks |
| **More frequent breaks** | Break suggestion at fatigueScore 0.5 (vs 0.7) | Earlier intervention before attention collapses |
| **Stronger visual structure** | Higher contrast borders on cards, clearer section dividers | Reduces visual search effort |
| **Immediate gamification** | XP popup immediately after every correct answer | More frequent dopamine reward |
| **Micro-progress indicators** | "2 more questions to complete this set!" | Creates proximal goals; reduces perceived task magnitude |
| **Reduced option count** | 3 MCQ options instead of 4 for early sessions | Reduces decision load |
| **Timer visibility toggle** | Option to hide timer | Timer creates time-pressure anxiety for ADHD students |
| **Focus sounds** | Optional ambient sounds (rain, white noise) during sessions | Auditory masking reduces distraction from environment |

### 10.3 Color-Blind Safe Palettes

**Research basis:**

- ~8% of males and ~0.5% of females have color vision deficiency (CVD). The most common type is red-green (deuteranopia/protanopia).
- Never use color as the ONLY differentiator. Always pair color with shape, text, or pattern.

**CENA color audit:**

| Current Color Use | Color-Blind Issue | Fix |
|------------------|-------------------|-----|
| Green = correct, Red = incorrect | Red-green indistinguishable for deuteranopes | Add checkmark icon for correct, X icon for incorrect (already done in `FeedbackOverlay`) |
| Difficulty: green/yellow/red | Green and red may merge | Add text label ("Easy", "Medium", "Hard") alongside color (already done in `_DifficultyBadge`) |
| Subject colors | 5 distinct colors, some may merge | Subject cards use icon + label + color; color is supplementary, not sole identifier |
| Fatigue indicator: green -> red gradient | Gradient may appear as brightness change only | Fatigue indicator uses emoji (already done: face icons convey state without relying on color) |

**CENA is already largely color-blind safe** because the design consistently pairs color with icons and text labels. The key principle to maintain: never introduce a new color-coded element without an accompanying non-color signal.

### 10.4 Motor Accessibility for Young Children

**Note:** CENA's target age is 14-18, so young children are not the primary audience. However, motor accessibility principles benefit all users, including those with motor disabilities.

**Research basis:**

- Fitts's Law (1954): The time to acquire a target is a function of the distance to and size of the target. Larger targets closer to the current touch point are faster and more accurate to tap.
- Apple HIG: Minimum touch target 44x44pt. Google Material: Minimum 48x48dp.

**CENA motor accessibility audit:**

| Element | Current Size | Recommendation |
|---------|-------------|----------------|
| MCQ option tiles | Full-width, ~48dp height | Compliant. Good. |
| Hint button | ~36dp height (OutlinedButton) | Increase to 48dp minimum via padding |
| Submit button | Full-width, 48dp height | Compliant. Good. |
| Badge cells in gamification grid | 54dp circle | Compliant. Good. |
| Difficulty badge | ~24dp height | Tap target should be at least 44dp (use padding to expand) |
| Close/back icons in AppBar | 48dp (default AppBar icon size) | Compliant. |

---

## 11. Anti-Patterns That Increase Cognitive Load

### 11.1 Comprehensive Anti-Pattern Catalog

| Anti-Pattern | CLT Violation | Example | Fix |
|-------------|--------------|---------|-----|
| **Information overload screen** | Total load exceeds capacity | Showing question + all hints + solution + timer + XP + badge + methodology + difficulty + concept name all at once | Progressive disclosure: show only question + options; reveal rest on demand |
| **Mystery meat navigation** | Extraneous load from decoding | Unlabeled icons that require trial-and-error to understand | Always pair icon with text label |
| **Deep nesting** | Working memory consumed by path memory | Settings > Account > Privacy > Data > Export > Confirm | Flatten to 2 levels max |
| **Modal fatigue** | Extraneous load from dismissal overhead | Popup -> popup -> popup requiring 3 dismissals to get back to content | Use inline UI elements; max 1 modal at a time |
| **Scroll jacking** | Disrupts spatial mental model | Custom scroll behavior that moves at different speeds or snaps unexpectedly | Use native Flutter scrolling; never override physics |
| **Auto-playing distraction** | Extraneous load from unwanted stimulus | Animated mascot bouncing while student reads a question | No animation during problem-solving phase |
| **Inconsistent layouts** | Re-orientation cost each screen | Question cards with different layouts for different question types | Unified card structure: always stem-on-top, options/input-below |
| **Redundant confirmation** | Unnecessary decision load | "Are you sure you want to select option B?" for every option | Confirm only for destructive actions (end session); never for selections |
| **Status bar overload** | Competing signals | Showing 5+ status indicators simultaneously | Max 3 ambient indicators: progress, time, fatigue |
| **Font mixing** | Extraneous visual processing | Using 4 different font styles on one screen | Max 2 font families (body + mono for math); 3 weights (regular, medium, bold) |
| **Invisible state changes** | User loses mental model | Methodology switching without any visual indicator | Always signal state changes with a brief, non-disruptive indicator |
| **Notification interrupt** | Context-switching cost | Showing "New badge earned!" while student is mid-problem | Queue notifications; show between questions only |
| **Giant carousels** | Working memory overload | Horizontal scrolling carousel of 20 concepts | Grid layout with 6-8 items visible; scroll for more |
| **Percentage overload** | Numeric cognitive load | "78.3% accuracy, 62.1% mastery, 45.7% completion, 89.2% retention" | Round to nearest 5%; show max 2 percentages per view |
| **Forced registration wall** | Task-irrelevant load | Requiring full profile before first question | Allow 3 questions before registration (Cena's diagnostic quiz approach) |

### 11.2 Anti-Patterns Specific to RTL Languages

| Anti-Pattern | Issue | Fix |
|-------------|-------|-----|
| **Hardcoded LTR icons** | Arrow icons pointing the wrong direction in RTL | Use Flutter's `Directionality.of(context)` to flip directional icons |
| **Mixed text direction** | Hebrew text with inline English/math creating bidi rendering issues | Use `Bidi` text handling; ensure math notation is isolated in LTR spans |
| **Progress bar direction** | Bar filling left-to-right in RTL context feels wrong | Mirror progress bar direction based on locale |
| **Number formatting** | Western digits (1, 2, 3) in Arabic text vs Eastern Arabic digits | Use locale-aware `NumberFormat` from `intl` package |

---

## 12. Progressive Disclosure Mapped to Actor Messages

### 12.1 How Each Disclosure Level Maps to CENA's Actor Architecture

The progressive disclosure model maps directly to the actor message protocol. The `StudentActor` on the server controls what information is available at each level; the Flutter client controls when and how that information is revealed.

**Level 1 (Core Action) -- Actor Messages:**

```
// Server -> Client: Present the question (Level 1 content)
QuestionPresented {
  exercise: Exercise {
    id, content, questionType, options,
    difficulty, conceptId, diagram?
  }
  // Note: hints, workedSolution, and metadata are NOT sent yet
  // They are fetched on demand (Level 2/3)
}

// Client -> Server: Submit the answer (Level 1 action)
AttemptConcept {
  sessionId, exerciseId, conceptId, answer, timeSpentMs, idempotencyKey
}
```

**Level 2 (Context) -- Actor Messages:**

```
// Client -> Server: Request a hint (Level 2 on demand)
RequestHint {
  sessionId, exerciseId, hintLevel
}

// Server -> Client: Deliver progressive hint
HintDelivered {
  exerciseId, hintLevel, hintText
}

// Server -> Client: Methodology context (ambient Level 2)
MethodologySwitched {
  methodology: "spacedRepetition" | "interleaved" | ...
}

// Server -> Client: Fatigue awareness (ambient Level 2)
CognitiveLoadWarning {
  fatigueScore: 0.72
}
```

**Level 3 (Deep Dive) -- Actor Messages:**

```
// Server -> Client: Full answer evaluation (Level 3, after answer)
AnswerEvaluated {
  result: AnswerResult {
    isCorrect, feedback, workedSolution?,
    errorType, xpEarned, conceptMastery
  }
}

// Client -> Server: Request micro-lesson (Level 3 on demand)
RequestMicroLesson {
  conceptId, methodology, studentMasteryLevel
}

// Server -> Client: Micro-lesson content
MicroLessonDelivered {
  conceptId, contentType: "video" | "interactive" | "walkthrough",
  contentUrl, durationSeconds, checkpoints[]
}
```

**Level 4 (Meta) -- Actor Messages:**

```
// Server -> Client: Session summary (Level 4, end of session)
SessionSummary {
  questionsAttempted, questionsCorrect, accuracy,
  conceptsMastered[], conceptsStruggledWith[],
  xpEarned, streakStatus, badgesEarned[]
}

// Client -> Server: Request knowledge graph (Level 4 on demand)
RequestKnowledgeMap {
  studentId, subject?
}

// Server -> Client: Knowledge graph state
KnowledgeMapDelivered {
  concepts: [{ id, name, mastery, decayRate, lastPracticed }],
  edges: [{ from, to, relationship }]
}
```

### 12.2 The Lazy Loading Pattern

The actor messages follow a lazy loading pattern that mirrors progressive disclosure:

1. **Session start:** Only session metadata and the first question are sent (Level 1)
2. **During question:** No additional data is sent until the student acts (requests hint or submits answer)
3. **Hint request:** Server sends only the requested hint level, not all hints (Level 2)
4. **Answer submitted:** Server sends evaluation + feedback + worked solution (Level 3)
5. **Session end:** Server sends full summary (Level 4)

This reduces bandwidth, prevents information overload, and ensures the client has exactly the information it needs at each stage -- nothing more.

### 12.3 Scaffolding State as Actor State

The `StudentActor` maintains a persistent scaffolding state that determines how much information each message carries:

```
// Actor-internal state (persisted via event sourcing)
StudentState {
  scaffoldLevel: ScaffoldLevel,  // TRAINING_WHEELS | INTERMEDIATE | FULL
  sessionsCompleted: int,
  featuresIntroduced: Set<Feature>,

  // When scaffoldLevel == TRAINING_WHEELS:
  //   - QuestionPresented only sends MCQ types
  //   - Difficulty capped at 4
  //   - First hint auto-delivered after 30s
  //   - MethodologySwitched is never sent (methodology is hidden)

  // When scaffoldLevel == FULL:
  //   - All question types
  //   - Full difficulty range
  //   - Hints only on request
  //   - Methodology badge visible
}
```

---

## 13. Screen-by-Screen Flow Descriptions

### 13.1 Onboarding Flow (5 screens, training wheels entry)

**Screen 1: Welcome**
- Content: App logo, welcome message in detected locale, language selector
- Cognitive budget: 2 items (logo/message + language selector)
- Progressive disclosure level: 1 (core action: choose language)
- Transition: Swipe or "Next" button

**Screen 2: Subject Selection**
- Content: 5 subject cards in grid, only Math is "available" (others show "coming soon")
- Cognitive budget: 1 group (subjects) + 1 action (next)
- Progressive disclosure level: 1 (core action: select subject)
- CLT principle: Don't overwhelm with 5 equal choices; visually emphasize the available one

**Screen 3: Grade & Track**
- Content: Grade picker (9-12), Bagrut track selector (3/4/5 units)
- Cognitive budget: 2 items (grade + track)
- Progressive disclosure level: 1 (core actions: pick grade and track)
- CLT principle: Use segmented controls, not dropdowns (fewer taps, visible options)

**Screen 4: Diagnostic Quiz (Optional)**
- Content: 5 MCQ questions, one at a time
- Cognitive budget: 1 item per screen (single question)
- Progressive disclosure level: 1 (core action: answer question)
- CLT principle: Single-question screens, no timer, no difficulty label, no hints
- Scaffolding: This is the most stripped-down question experience, establishing the mental model for later sessions

**Screen 5: Ready**
- Content: Summary of selections, personality/archetype if quiz taken, "Start Learning" button
- Cognitive budget: 1 summary + 1 action
- Progressive disclosure level: 1-2 (summary is Level 2 context shown after Level 1 is complete)

### 13.2 Home Screen Flow

**Home Tab:**
- Greeting card (time-aware salutation, streak indicator)
- Review Due section (if any concepts need review -- Level 2, contextual)
- Subject quick-start grid (5 cards)
- "Start Learning" button (primary CTA)
- Cognitive budget: 3-4 groups (greeting, review, subjects, CTA)

**Sessions Tab:**
- Session history list (chronological, with date/accuracy/duration per session)
- Empty state with CTA for first-time users
- Cognitive budget: 1 list (each item is 1 chunk)

**Progress Tab:**
- XP & Level card (level badge, progress bar)
- Streak widget (current streak, freeze status)
- Badge grid (4 columns, locked badges grayed)
- Recent achievements list
- Cognitive budget: 4 sections, scrollable, each is 1 visual chunk

**Settings Tab:**
- Language selector
- Account section (profile, sign out)
- Accessibility toggle (dyslexia mode, timer visibility)
- Cognitive budget: 2-3 card groups

### 13.3 Active Session Flow

**Phase 1: Configuration (pre-session)**
- Subject selection chips
- Duration slider (12-30 min)
- Session info card (max questions, mastery threshold)
- Start button
- Cognitive budget: 3 groups (subject, duration, info) + 1 action

**Phase 2: Question Loop (active session)**
- Progress bar at top (questions, accuracy, fatigue, timer)
- Methodology badge (training wheels: hidden; full: visible)
- Question card (stem + options/input)
- Action buttons (hint, change approach)
- Cognitive budget per question: 1 ambient (progress) + 1 primary (question) + 1-2 secondary (actions)

**Phase 3: Feedback (between questions)**
- Full-screen overlay (correct: green, incorrect: red, partial: yellow)
- Result icon with scale animation
- XP award badge
- Error type badge (if incorrect)
- Feedback text
- Worked solution (if incorrect, expandable)
- "Tap to continue" prompt
- Auto-dismiss after 3 seconds (configurable)

**Phase 4: Break (fatigue threshold reached)**
- Full-screen green overlay
- Breathing animation circle (4s cycle)
- Countdown timer
- "Continue" and "End Session" buttons
- No question content visible (complete context switch)

**Phase 5: Summary (session ended)**
- Trophy icon
- Questions attempted, accuracy, time
- XP earned, concepts mastered
- "Return Home" button

---

## 14. Typography, Spacing, and Color Guidelines

### 14.1 Typography Scale (Research-Backed)

**Base principles:**

- **Minimum body text: 16sp.** Below this, reading speed drops 10-15% on mobile (Beymer, Russell & Orton, 2008).
- **Math notation: 18sp minimum.** Subscripts and superscripts need extra room for legibility.
- **Maximum 3 type sizes per screen.** More creates visual noise that increases extraneous load (Williams, 2015).
- **Line height 1.5x for body, 1.3x for headings.** Optimal for reading comprehension (Ling & van Schaik, 2007).

**CENA's type scale (from `TypographyTokens`):**

| Role | Size | Weight | Use |
|------|------|--------|-----|
| `displayLarge` | 32sp | w800 | Session ended title, onboarding headlines |
| `displayMedium` | 28sp | w700 | Break screen timer, large numbers |
| `headlineLarge` | 24sp | w700 | Screen titles, section headers |
| `headlineMedium` | 20sp | w600 | Card titles, modal headers |
| `titleLarge` | 18sp | w600 | Question card subheadings |
| `titleMedium` | 16sp | w500 | List item titles |
| `bodyLarge` | 16sp | w400 | **Question stem text, primary reading** |
| `bodyMedium` | 14sp | w400 | MCQ option text, descriptions |
| `bodySmall` | 12sp | w400 | Tertiary info, timestamps |
| `labelLarge` | 14sp | w500 | Button labels, badges |
| `labelMedium` | 12sp | w500 | Chips, stat labels |
| `labelSmall` | 10sp | w500 | Footnotes, minimal text |

**Font selection rationale:**

- **Heebo** (Hebrew): Sans-serif with good weight range. Round, friendly letterforms reduce math anxiety.
- **Noto Sans Arabic** (Arabic): Full diacritics support (critical for mathematical Arabic text). Excellent Unicode coverage.
- **Inter** (Latin/English): Designed for UI readability at small sizes. Excellent for mixed Hebrew+English contexts.
- **JetBrains Mono** (Math/Code): Monospace for consistent digit alignment in equations.

### 14.2 Spacing System (Research-Backed)

**Base unit: 8dp.** The 8-point grid is the most widely adopted spacing system (Google Material, Apple HIG) because it aligns with common screen densities (mdpi, hdpi, xhdpi) and produces visually harmonious layouts.

**CENA's spacing tokens (from `SpacingTokens`):**

| Token | Value | Use |
|-------|-------|-----|
| `xxs` | 2dp | Minimal separation (icon-to-badge gap) |
| `xs` | 4dp | Tight spacing (inline elements within a row) |
| `sm` | 8dp | Standard element spacing within a card |
| `md` | 16dp | Card internal padding, section spacing |
| `lg` | 24dp | Major section spacing |
| `xl` | 32dp | Screen-level padding |
| `xxl` | 48dp | Large separations (between major content blocks) |
| `xxxl` | 64dp | Maximum spacing (rarely used) |

**Spacing principles for cognitive load:**

1. **Whitespace is not wasted space.** Adequate spacing reduces visual crowding, which reduces extraneous load (Larson & Picard, 2005).
2. **Group related elements with tight spacing (8dp); separate unrelated elements with wide spacing (24dp+).** This leverages Gestalt proximity to create visual chunks.
3. **Consistent internal padding.** All cards use `md` (16dp) internal padding. All screen edges use `md` (16dp) margin. Consistency eliminates re-orientation cost.

### 14.3 Color Guidelines (Research-Backed)

**Research basis:**

- Kwallek, Lewis & Robbins (1988): Blue and green environments produce better cognitive performance and lower stress than red environments. Red increases arousal but impairs complex cognitive tasks.
- Mehta & Zhu (2009): Blue enhances creative thinking; red enhances detail-oriented tasks. For math (detail-oriented), red accents for error signaling are appropriate, but the dominant palette should be blue/green.
- Boyatzis & Varghese (1994): Children associate warm colors (yellow, orange) with positive emotions. Cool colors (blue, green) with calm. This holds through adolescence.

**CENA color system:**

| Color Role | Hex | Research Basis |
|-----------|-----|---------------|
| Primary (Teal) | `#0097A7` | Blue-green: combines focus (blue) with calm (green). Math-associated in CENA's subject palette. |
| Secondary (Amber) | `#FF8F00` | Warm accent for CTAs and highlights. Attracts attention without the stress of red. |
| Error (Red) | `#D32F2F` | Standard error color. Always paired with icon (X) for color-blind safety. |
| Correct (Green) | `#4CAF50` | Standard success color. Always paired with icon (checkmark) for color-blind safety. |
| Partial (Orange) | `#FF9800` | Intermediate state. Distinct from both correct-green and error-red. |
| Surface (White) | `#FFFFFF` | Clean, high-contrast background for reading. |
| Break screen (Light green) | `#E8F5E9` | Calming, associated with nature and relaxation. |

**Subject color palette (from `SubjectColorTokens`):**

| Subject | Primary | Background | Rationale |
|---------|---------|-----------|-----------|
| Math | Teal `#0097A7` | Light cyan `#E0F7FA` | Cool, precise, analytical |
| Physics | Amber `#FF8F00` | Cream `#FFF8E1` | Energetic, dynamic (like the subject) |
| Chemistry | Green `#388E3C` | Light green `#E8F5E9` | Nature, organic, experimental |
| Biology | Purple `#7B1FA2` | Light purple `#F3E5F5` | Living systems, complex, creative |
| CS | Gray `#616161` | Light gray `#F5F5F5` | Technical, neutral, systematic |

### 14.4 Animation Guidelines

**Research basis:**

- Nielsen (1993): Response time guidelines. <100ms feels instantaneous; <1000ms maintains flow; <10s maintains attention. UI animations should complete within these bounds.
- Chang & Ungar (1993): Animations serve learning only when they provide information about state changes. Decorative animation is extraneous load.

**CENA animation tokens (from `AnimationTokens`):**

| Token | Duration | Use | CLT Rationale |
|-------|----------|-----|---------------|
| `fast` | 150ms | Micro-interactions (option select, state toggles) | Below perception threshold; feels instantaneous |
| `normal` | 300ms | Standard transitions (card expand, page push) | Provides orientation cue without delay |
| `slow` | 600ms | Emphasis transitions (feedback overlay, XP bar fill) | Draws attention to important state change |
| `celebration` | 1000ms | Level up, badge earned | Emotional reinforcement; used sparingly |

**Animation rules:**

1. Never animate during the question-answering phase (except option selection feedback: 150ms)
2. Reserve 600ms+ animations for transitions between questions (feedback overlay entrance)
3. Use `Curves.easeInOut` for natural motion; avoid `Curves.linear` (feels mechanical)
4. The breathing animation in `CognitiveLoadBreak` (4s cycle) is intentionally slow: it's a physiological pacing tool, not a UI animation

---

## 15. Age-Specific Considerations

### 15.1 Cognitive Development by Age Group

**Research basis:**

- Piaget (1952): Formal operational stage begins ~11-12 years. By 14, most students can handle abstract reasoning, hypothetical thinking, and systematic problem-solving.
- Keating (2004): Adolescent cognitive development is not just about capacity but about metacognition -- the ability to think about one's own thinking. This develops throughout ages 14-18.
- Luna et al. (2004): Executive function (working memory, inhibitory control, cognitive flexibility) continues developing until ~25. Adolescents (14-18) have adult-like capacity but less efficient processing.

**Implications for CENA (target: 14-18):**

| Cognitive Factor | 14-15 year olds | 16-18 year olds | Design Implication |
|-----------------|-----------------|-----------------|-------------------|
| Working memory capacity | ~3 chunks (lower end of adult range) | ~4 chunks (adult range) | Younger students need simpler screens; use training wheels longer |
| Metacognition | Developing; poor at judging own understanding | More accurate self-assessment | Younger students need more auto-scaffolding; older can self-select difficulty |
| Sustained attention | 15-20 min before significant decrement | 20-30 min | Session duration default: 20 min for younger, 25 min for older |
| Risk tolerance | Higher; willing to guess randomly | More strategic | Younger students need guardrails against random tapping |
| Abstract reasoning | Can handle algebra, basic proofs | Can handle calculus, complex proofs | Content difficulty adjustment, not interface adjustment |
| Reward sensitivity | Very high; dopamine system peaks in adolescence | Still high but more modulated | Gamification is effective for both; younger benefit from more frequent smaller rewards |

### 15.2 Adolescent-Specific Design Patterns

**Social comparison sensitivity:**

- Adolescents are highly sensitive to social comparison (Steinberg, 2008). Leaderboards can be motivating OR demotivating depending on the student's position.
- CENA design rule: Leaderboards are opt-in (behind feature flag `leaderboardEnabled`). When shown, emphasize improvement ("You climbed 3 places!") not absolute position ("You are #47 of 200").

**Autonomy need (Self-Determination Theory):**

- Deci & Ryan (2000): Autonomy, competence, and relatedness are basic psychological needs. Adolescents have a particularly strong autonomy need.
- CENA design rule: The student can choose subject, session duration, and learning approach. The system adapts, but the student feels in control. The "Change Approach" bottom sheet serves this need.

**Identity sensitivity:**

- Adolescents are developing academic identity. Being labeled "bad at math" can create math anxiety that persists into adulthood (Hembree, 1990).
- CENA design rule: Never show failure counts, ranking against peers, or labels like "struggling." Use growth language: "You've improved 15% in quadratics this week." Frame difficulty as challenge, not judgment.

### 15.3 Bilingual Cognitive Considerations (Hebrew/Arabic)

**Research basis:**

- Sweller, Ayres & Kalyuga (2011): Bilingual processing imposes measurable extraneous cognitive load, particularly for technical vocabulary in a non-dominant language.
- Clarkson (2007): Bilingual students who can flexibly code-switch between languages for math perform better than those restricted to one language.

**CENA-specific considerations:**

For Arabic-speaking students taking Hebrew-language Bagrut exams:

1. **Primary instruction in Arabic** reduces cognitive load during concept acquisition
2. **Hebrew terminology bridging** as a separate, explicit learning step (not mixed into the same question)
3. **Math notation is language-neutral** (LaTeX renders identically in both locales) -- this is an advantage
4. **UI labels in the student's chosen language** (already implemented via `AppLocales`)
5. **Exam-mode toggle** that switches question language to Hebrew for practice, simulating the actual test conditions

---

## Summary of Key Recommendations for CENA

### Immediate Actions (No Code Changes, Design Decisions)

1. **Keep 4 bottom nav tabs.** Do not add a 5th.
2. **Hide methodology badge during question-answering.** Move to feedback phase.
3. **Make timer opt-in during sessions** for ADHD accommodation.
4. **Ensure question + all options fit on one viewport** without scrolling (split attention prevention).

### Short-Term Actions (Minor Code Changes)

5. **Add scaffolding state to actor messages.** `ScaffoldLevel` enum controls what UI elements are visible.
6. **Implement feature reveal schedule.** Track sessions completed; pulse-animate new features on first appearance.
7. **Add "Review Due" section to home screen.** Surface concepts with decaying memory strength.
8. **Add dyslexia mode toggle** in Settings (font size, line height, background color adjustments).
9. **Increase hint button touch target** to minimum 48dp.

### Medium-Term Actions (Feature Development)

10. **Implement lazy loading of hints.** Don't send all hints with the question; fetch on demand.
11. **Add post-session retrieval practice** (3 review questions from past sessions).
12. **Implement knowledge decay visualization** (strength bars per concept in Progress tab).
13. **Add ADHD accommodation profile** (shorter sessions, earlier breaks, more frequent rewards).
14. **Implement orientation locking** (portrait for questions, landscape for video micro-lessons).

### Long-Term Actions (Architecture)

15. **Implement micro-lesson integration** with progressive disclosure Levels 2-3.
16. **Add confidence estimation** from response time analysis for adaptive feedback timing.
17. **Build the two-phase Arabic-Hebrew terminology bridge** for bilingual students.
18. **Implement worked example fading** controlled by BKT mastery level per concept.

---

## Research Bibliography

### Cognitive Load Theory
- Sweller, J. (1988). "Cognitive load during problem solving: Effects on learning." *Cognitive Science*, 12(2), 257-285.
- Sweller, J., Ayres, P. & Kalyuga, S. (2011). *Cognitive Load Theory*. Springer.
- Sweller, J. (2020). "Cognitive load theory and educational technology." *Educational Technology Research and Development*, 68, 1-16.
- Paas, F., Renkl, A. & Sweller, J. (2003). "Cognitive load theory and instructional design." *Educational Psychologist*, 38(1), 1-4.
- Chandler, P. & Sweller, J. (1991). "Cognitive load theory and the format of instruction." *Cognition and Instruction*, 8(4), 293-332.
- Kalyuga, S., Ayres, P., Chandler, P. & Sweller, J. (2003). "The expertise reversal effect." *Educational Psychologist*, 38(1), 23-31.

### Working Memory
- Miller, G.A. (1956). "The magical number seven, plus or minus two." *Psychological Review*, 63(2), 81-97.
- Cowan, N. (2001). "The magical number 4 in short-term memory." *Behavioral and Brain Sciences*, 24(1), 87-114.
- Cowan, N. (2010). "The magical mystery four." *Current Directions in Psychological Science*, 19(1), 51-57.
- Oberauer, K. et al. (2018). "Benchmarks for models of short-term and working memory." *Psychological Bulletin*, 144(9), 885-958.
- Barrouillet, P. & Camos, V. (2015). *Working Memory: Loss and Reconstruction*. Psychology Press.

### Progressive Disclosure & Scaffolding
- Carroll, J.M. & Carrithers, C. (1984). "Training wheels in a user interface." *Communications of the ACM*, 27(8), 800-806.
- Wood, D., Bruner, J.S. & Ross, G. (1976). "The role of tutoring in problem solving." *Journal of Child Psychology and Psychiatry*, 17(2), 89-100.
- Renkl, A. & Atkinson, R.K. (2003). "Structuring the transition from example study to problem solving." *Journal of Experimental Education*, 70(4), 293-315.

### Multimedia Learning
- Mayer, R.E. (2001). *Multimedia Learning*. Cambridge University Press.
- Paivio, A. (1986). *Mental Representations: A Dual Coding Approach*. Oxford University Press.
- Tversky, B., Morrison, J.B. & Betrancourt, M. (2002). "Animation: Can it facilitate?" *International Journal of Human-Computer Studies*, 57(4), 247-262.
- de Koning, B.B., Tabbers, H.K., Rikers, R.M.J.P. & Paas, F. (2009). "Towards a framework for attention cueing in instructional animations." *Educational Psychology Review*, 21(2), 113-140.

### Spacing & Interleaving
- Cepeda, N.J. et al. (2006). "Distributed practice in verbal recall tasks." *Psychological Bulletin*, 132(3), 354-380.
- Rohrer, D. & Taylor, K. (2007). "The shuffling of mathematics problems improves learning." *Instructional Science*, 35(6), 481-498.
- Kornell, N. & Bjork, R.A. (2008). "Learning concepts and categories." *Psychological Science*, 19(6), 585-592.
- Ebbinghaus, H. (1885). *Memory: A Contribution to Experimental Psychology*.

### Feedback & Error Handling
- Shute, V.J. (2008). "Focus on formative feedback." *Review of Educational Research*, 78(1), 153-189.
- Narciss, S. & Huth, K. (2004). "How to design informative tutoring feedback." *Instructional Design for Multimedia Learning*, 181-195.
- Chi, M.T.H. et al. (2001). "Learning from human tutoring." *Cognitive Science*, 25(4), 471-533.
- Kapur, M. (2008). "Productive failure in mathematical problem solving." *Cognition and Instruction*, 26(3), 379-424.
- Finn, B. & Metcalfe, J. (2010). "Scaffolding feedback to maximize long-term error correction." *Memory & Cognition*, 38(7), 951-961.

### Attention & Focus
- Warm, J.S. (1984). "An Introduction to Vigilance." In *Sustained Attention in Human Performance*. Wiley.
- Mark, G., Gonzalez, V.M. & Harris, J. (2005). "No task left behind?" *CHI 2005*.
- Rosen, L.D., Carrier, L.M. & Cheever, N.A. (2013). "Facebook and texting made me do it." *Computers in Human Behavior*, 29(3), 948-958.
- Csikszentmihalyi, M. (1990). *Flow: The Psychology of Optimal Experience*. Harper & Row.

### Mobile UX
- Hoober, S. (2013). "How Do Users Really Hold Mobile Devices?" *UXmatters*.
- Rello, L. & Baeza-Yates, R. (2013). "Good Fonts for Dyslexia." *ASSETS 2013*.
- Beymer, D., Russell, D.M. & Orton, P.Z. (2008). "An eye tracking study of how font size and type influence online reading." *BCS-HCI 2008*.
- Nielsen, J. (1993). "Response Times: The 3 Important Limits." *Nielsen Norman Group*.

### Adolescent Cognition
- Steinberg, L. (2008). "A social neuroscience perspective on adolescent risk-taking." *Developmental Review*, 28(1), 78-106.
- Deci, E.L. & Ryan, R.M. (2000). "Self-Determination Theory and the facilitation of intrinsic motivation." *American Psychologist*, 55(1), 68-78.
- Luna, B. et al. (2004). "Maturation of cognitive processes from late childhood to adulthood." *Child Development*, 75(5), 1357-1372.
- Hembree, R. (1990). "The nature, effects, and relief of mathematics anxiety." *Journal for Research in Mathematics Education*, 21(1), 33-46.

### Accessibility
- British Dyslexia Association. "Dyslexia Style Guide 2018."
- Barkley, R.A. (2015). *Attention-Deficit Hyperactivity Disorder: A Handbook for Diagnosis and Treatment*. Guilford Press.
- Salomone, S. et al. (2020). "Digital tools for ADHD." *European Child & Adolescent Psychiatry*.
