# Microinteractions, Emotional Design & Delight -- Cena Mobile App

## Document Context

This document specifies every microinteraction, animation, sound effect, haptic pattern, and emotional design guideline for the Cena adaptive learning mobile app (Flutter). It is grounded in Dan Saffer's microinteraction framework, Don Norman's three levels of emotional design, and deep analysis of the existing codebase.

**Existing codebase state as of this document:**
- Theme: Material 3 with locale-aware fonts (Heebo/Noto Sans Arabic/Inter), light and dark themes, RTL support
- Gamification: XP/level system, streak widget with pulsing flame, badge catalogue (10 badges, 4 categories), achievement feed, XP popup with bounce-in animation
- Session: Progress bar with fatigue-aware color gradient, question card with animated option selection, feedback overlay (correct/wrong/partial) with elasticOut scaling and haptics, cognitive load break with breathing animation
- Onboarding: 5-page flow with diagnostic quiz
- Navigation: GoRouter with bottom nav (Home, Sessions, Progress, Settings)
- Design tokens: SpacingTokens (8px grid), RadiusTokens, AnimationTokens (fast 150ms, normal 300ms, slow 600ms, celebration 1000ms), SubjectColorTokens
- Current packages: shimmer, fl_chart, flutter_svg, cached_network_image, flutter_math_fork
- Missing: Lottie, Rive, audioplayers, haptic_feedback (only uses HapticFeedback.heavyImpact/lightImpact), confetti

---

## Part 1: Dan Saffer's Microinteraction Framework Applied to Every Learning Interaction

### 1.1 Answering a Question (MCQ)

**Trigger:**
- Manual: Student taps an MCQ option tile (`OptionTile` in `question_card.dart`)
- System: None -- selection is always student-initiated

**Rules:**
1. Only one option can be selected at a time
2. Selection is blocked while `isSubmitting == true`
3. Tapping an already-selected option deselects it (currently NOT implemented -- should be added)
4. The submit button enables only when `selectedOptionIndex != null`

**Feedback:**
- Visual: `AnimatedContainer` with `AnimationTokens.fast` (150ms) transitions background from `colorScheme.surface` to `colorScheme.primary.withValues(alpha: 0.1)`, border from `outlineVariant` to `primary` with width 1 to 2, Hebrew label circle fills with primary color
- Haptic: Add `HapticFeedback.selectionClick()` on option tap (currently missing)
- Audio: Subtle "tick" sound (see Sound Design section)
- State: Checkmark icon appears at trailing edge; label text weight shifts from normal to w600

**Loops & Modes:**
- Loop: Each question presents the same interaction cycle (select -> submit -> feedback -> next)
- Mode: When submitting, all options become non-interactive (`isEnabled: false`), submit button shows `CircularProgressIndicator`
- Over time: Students develop muscle memory for the tap-submit-next rhythm. The cycle should remain consistent across thousands of interactions to build automaticity

### 1.2 Answering a Question (Free Text / Numeric / Proof)

**Trigger:**
- Manual: Student begins typing in `TextField` or `_NumericInput`

**Rules:**
1. Submit enabled only when text is non-empty (after trim)
2. Numeric input enforces `FilteringTextInputFormatter.allow(RegExp(r'^-?\d*\.?\d*$'))`
3. Unit selector is separate from numeric value
4. Proof input supports multiline with `TextInputAction.newline`
5. Keyboard must not obscure the input field (see Navigation Microinteractions)

**Feedback:**
- Visual: Input field border transitions to `focusedBorder` (primary color, 2px width) on focus
- Haptic: None on typing (would be distracting)
- Audio: None on typing
- State: Submit button text shows character/word count for proof type, "Checking..." with spinner during evaluation

**Loops & Modes:**
- Loop: Type -> review -> submit -> feedback -> next question
- Mode: During LLM evaluation of open-ended responses (1-3s), show "Analyzing your response..." with a thinking animation (see Loading States)

### 1.3 Receiving Answer Feedback

**Trigger:**
- System: Server returns `AnswerResult` via SignalR, `_pendingFeedback` is set in `SessionScreen`

**Rules:**
1. Correct: green overlay (`0xFF4CAF50`), heavy haptic, "+N XP" badge
2. Wrong: red overlay (`0xFFF44336`), light haptic, error type badge, worked solution if available
3. Partial: yellow overlay (`0xFFFF9800`), medium haptic, partial credit feedback
4. Auto-dismiss after 3 seconds OR on tap
5. For correct answers on streaks of 3+, add confetti particles (currently missing)
6. Scale celebration proportional to achievement importance (see section 3)

**Feedback:**
- Visual: `FadeTransition` + `ScaleTransition` with `Curves.elasticOut`, icon at 96px
- Haptic: `HapticFeedback.heavyImpact()` for correct, `HapticFeedback.lightImpact()` for wrong (currently implemented)
- Audio: Correct answer chime, wrong answer soft buzz, partial credit mid-tone (currently missing)
- XP: `XpPopup` floats upward 60px, bounces in with TweenSequence (0.6 -> 1.3 -> 1.0), fades out in second half of 900ms animation

**Loops & Modes:**
- Loop: Repeated every question. The feedback overlay is the primary emotional moment in each cycle
- Mode: After 5+ correct answers in a row, escalate feedback to include streak celebration
- Over time: The 3-second auto-dismiss should shorten to 2 seconds for experienced students (reduce to `AnimationTokens.fast` after 50 total answers)

### 1.4 Earning XP

**Trigger:**
- System: `AnswerResult.xpEarned > 0` after correct/partial answer

**Rules:**
1. Base XP: 10 per correct answer
2. Bonus XP: 2x during first-session bonus (first 5 questions daily)
3. Streak multiplier: 1.5x after 7-day streak, 2x after 30-day streak
4. Difficulty multiplier: 1.0 (easy), 1.5 (medium), 2.0 (hard)
5. XP is always a positive integer, never negative

**Feedback (layered, all concurrent):**
1. `XpPopup` overlay: "+N XP" text with gold color (`0xFFFFD700`), bounce-in scale, float up 60px, fade out over 900ms
2. Daily XP badge update: chip in `_XpLevelCard` updates with new total ("+N today")
3. XP progress bar: `TweenAnimationBuilder` animates from old to new progress value with `Curves.easeOutCubic` over `AnimationTokens.slow` (600ms)
4. If XP crosses level boundary: trigger level-up celebration (see section 3.4)

**Loops & Modes:**
- Loop: Every correct answer
- Mode: First session of the day shows bonus indicator
- Over time: XP amounts grow with difficulty. Students should feel increasing reward magnitude as they advance

### 1.5 Requesting a Hint

**Trigger:**
- Manual: Student taps "Hint" button (`_HintButton` in `action_buttons.dart`)

**Rules:**
1. Progressive revelation: hints revealed one at a time (up to `exercise.hints.length`, max 3)
2. Button disabled when all hints are exhausted
3. Badge counter shows number of hints used (positioned at top-right of button, -6px offset)
4. Each hint use may reduce XP reward for the question (server-side logic)

**Feedback:**
- Visual: Hint container (`_HintDisplay`) slides in with `SlideTransition` from bottom, numbered hints appear sequentially
- Haptic: `HapticFeedback.mediumImpact()` on hint reveal (currently missing)
- Audio: "Page turn" or "reveal" sound -- subtle paper unfolding
- State: Hint counter badge increments, hint container expands to show new hint
- Animation (to add): Each new hint should animate in with a staggered `FadeTransition` + `SlideTransition` (offset from bottom by 20px, duration 300ms)

**Loops & Modes:**
- Loop: Up to 3 hints per question
- Mode: After all hints used, button becomes greyed out with "No more hints" tooltip
- Over time: Frequent hint users should see encouraging messages ("Hints help you learn! Don't worry about using them.")

### 1.6 Completing a Session

**Trigger:**
- System: Session timer expires, or max questions reached
- Manual: Student taps "End session" and confirms via `_EndSessionDialog`

**Rules:**
1. Session summary shows: questions attempted, accuracy %, elapsed time
2. If accuracy >= 80%: celebratory summary with trophy icon
3. If first session ever: "First Step" badge earned
4. If streak extended: streak counter update
5. XP earned during session is already reflected in gamification state

**Feedback:**
- Visual: Trophy icon (`Icons.emoji_events_rounded`, 80px, gold), session stats in `_SummaryTile` rows, "Session complete!" headline
- Haptic: `HapticFeedback.heavyImpact()` on summary screen appearance
- Audio: Session completion fanfare (3 ascending tones)
- Animation (to add): Stats should count up from 0 to final value using `TweenAnimationBuilder` over 1.5 seconds. Trophy should scale in with `elasticOut`. Background should have subtle confetti for accuracy >= 80%

**Loops & Modes:**
- Loop: One per session
- Mode: First-ever session gets enhanced celebration. 10th session unlocks "Dedicated Learner" badge
- Over time: Students who complete sessions consistently should see their improvement trend ("Your accuracy has improved 15% this week!")

### 1.7 Streak Maintenance

**Trigger:**
- System: Student opens app; streak state is loaded from server
- System: Midnight approaches and no session has been completed

**Rules:**
1. Streak counter increments when at least one session is completed on a calendar day
2. Streak freezes (max 2 stored) auto-deploy when a day is missed
3. Vacation mode pauses streak entirely
4. "Streak at risk" banner appears when `streakSafe == false && streak > 0`

**Feedback:**
- Visual: Flame icon (`Icons.local_fire_department_rounded`) with pulsing animation (scale 0.85 -> 1.15, 800ms cycle, `Curves.easeInOut`) -- already implemented
- Flame color progression: amber (1-6 days) -> deep orange (7-29) -> red-hot (30+) -- already implemented
- Calendar strip shows last 7 days with check marks for active days
- "New record!" badge appears when current streak equals longest streak
- At risk banner: red container with warning icon and text

**Loops & Modes:**
- Loop: Daily check on app open
- Mode: Vacation mode replaces at-risk banner with beach icon and end date
- Over time: As streak grows, flame animation should become more intense (add particle effects at 14+, add glow at 30+)

---

## Part 2: Don Norman's Three Levels of Emotional Design

### 2.1 Visceral Level (First Impression, Visual Beauty, Gut Reaction)

The visceral level operates pre-cognitively. The student's gut reaction within the first 50-200ms of seeing any screen determines their emotional baseline.

**Current strengths:**
- Material 3 with clean color scheme (`_primarySeed: 0xFF0097A7` teal, `_secondarySeed: 0xFFFF8F00` amber)
- Locale-aware typography (Heebo for Hebrew, Noto Sans Arabic, Inter for English)
- Subject-specific color palettes creating visual diversity across disciplines
- Zero-elevation card design with subtle border, modern and clean

**Where visceral design must be applied:**

| Screen | Visceral Element | Implementation |
|--------|-----------------|----------------|
| Home | Greeting card with time-aware message | Already implemented. Add: gradient header with subject accent color matching most recent subject |
| Session config | Subject selection chips with icons | Already implemented. Add: animated background pattern that shifts with selected subject |
| Active session | Question card with difficulty/type badges | Already implemented. Add: subtle color tint on card border matching subject |
| Feedback overlay | Full-screen color wash (green/red/yellow) | Already implemented. Add: particle effects for correct answers, ripple for wrong |
| Gamification | XP card with gradient level circle | Already implemented. Add: shimmer effect on XP bar when close to level-up (>90% progress) |
| Onboarding | Page-by-page flow | Currently minimal. Add: illustrated backgrounds per page (see Character Design) |
| Loading states | Content loading | Currently `CircularProgressIndicator`. Replace with: skeleton screens with shimmer |

**Design principles for visceral appeal in Cena:**
1. Color saturation: Use the `SubjectColorTokens` primary colors at full saturation for interactive elements, desaturated (background variants) for containers
2. Depth: Material 3 tonalElevation provides visual hierarchy without shadows. Use `surfaceContainerHighest` for inactive elements, `surface` for primary content, `primaryContainer` for highlighted content
3. White space: The 8px grid (`SpacingTokens`) is well-calibrated. Never compress below `SpacingTokens.sm` (8px) between interactive elements
4. Motion: Every state change must have a transition. No instant jumps. Minimum `AnimationTokens.fast` (150ms) for any visual change
5. RTL-aware layouts: All spacing, icon positions, and text alignment must respect `Directionality.of(context)`. Test every screen in both he and ar locales

**Age-appropriate visceral design (Israeli high school, ages 15-18):**
- Avoid childish illustrations or overly cartoonish mascots
- Use geometric/abstract patterns rather than figurative illustration
- Color palette should feel confident and modern, not pastel/cute
- Typography should feel authoritative but not corporate -- the Heebo font achieves this well
- Gamification elements should feel earned, not patronizing -- badges should look like achievements, not stickers

### 2.2 Behavioral Level (Usability, Functionality, Feeling of Control)

The behavioral level is about the experience of use -- does the student feel in control? Do interactions respond predictably?

**Control mechanisms in Cena:**

| Mechanism | Current State | Enhancement |
|-----------|--------------|-------------|
| Answer selection | Tap to select, tap submit -- 2-step confirmation | Good. Students feel safe making mistakes before committing |
| Skip with confirmation | `_SkipDialog` requires explicit confirmation | Good. Prevents accidental skips |
| Hint progression | Progressive reveal with counter badge | Good. Student controls pace of help |
| Methodology switching | Bottom sheet with 5 options, descriptions, current indicator | Good. Student has agency over learning approach |
| Session duration | Slider from 12-30 minutes, configurable | Good. Respects student's available time |
| Break suggestion | Non-blocking, student can continue or end | Excellent. Respects autonomy |
| End session | Confirmation dialog with "progress saved" message | Good. Reduces anxiety about lost work |

**Behavioral principles for every screen:**

1. **Predictability**: The same gesture should always produce the same result. Tapping an MCQ option always selects it. Tapping submit always sends the answer. No modal surprises.

2. **Reversibility**: Before submission, any action should be undoable. Option selection can be changed. Text can be edited. Only "Submit" is irreversible (by design -- this creates meaningful commitment).

3. **Feedback latency targets:**
   - Touch response: < 16ms (next frame at 60fps)
   - Selection state change: `AnimationTokens.fast` (150ms)
   - Content transition: `AnimationTokens.normal` (300ms)
   - Celebration animation: `AnimationTokens.celebration` (1000ms)
   - Server response: show loading after 200ms delay (avoid flash for fast responses)

4. **Error prevention**: The submit button is disabled until a valid answer is provided. Numeric input enforces format via `FilteringTextInputFormatter`. Skip requires confirmation. End session requires confirmation.

5. **Progressive disclosure**: Hints reveal one at a time. The approach sheet shows descriptions only after tap. Badge details show on tap. Session stats expand on session end.

### 2.3 Reflective Level (Self-Image, Meaning, Memory, Pride)

The reflective level is about what the student thinks about themselves while using Cena. This is where the app builds long-term attachment.

**Reflective design elements:**

| Element | Emotional Target | Implementation |
|---------|-----------------|----------------|
| Level system | "I am growing" | Level badge with gradient circle, "Level N" displayed prominently, level-up celebration |
| Streak counter | "I am consistent" | Flame icon with escalating intensity, "New record!" badge, calendar strip showing consistency |
| Badge collection | "I have achieved" | Grid display of earned/locked badges, detail dialog with earn date, category labels |
| Mastery map | "I understand more every day" | Knowledge graph showing mastered concepts (green nodes) growing over time |
| Session summary | "I did well today" | Trophy icon, accuracy percentage, improvement trend |
| Error type classification | "I know what to improve" | Hebrew labels for error types (conceptual, procedural, careless, notation, incomplete) help students develop metacognition |

**Reflective design principles:**

1. **Frame progress, not performance**: Show "You've mastered 12 concepts this month" rather than "You got 73% correct." Absolute performance numbers can discourage; growth metrics encourage.

2. **Celebrate improvement over absolute scores**: "Your accuracy improved 8% this week" is more motivating than "87% accuracy." Use trend arrows and comparison to personal history, never to peers (unless leaderboard is explicitly opted into).

3. **Make effort visible**: The calendar strip, streak counter, and XP total are all "effort mirrors" that show the student their own dedication. These should be prominently displayed, not hidden in a sub-screen.

4. **Create meaningful milestones**: The badge system defines 10 milestones across 4 categories. Each should feel significant. The badge unlock ceremony (see section 3.5) is the key reflective moment.

5. **Support identity formation**: "I am a learner" is a powerful identity. The app should reinforce this through:
   - Consistent language: "Your learning session" (not "quiz" or "test")
   - Achievement language: "You mastered..." (not "You passed...")
   - Error language: "Let's look at this differently" (not "Wrong answer")

---

## Part 3: Celebration & Reward Animations

### 3.1 Celebration Scale

Not all celebrations should be equal. Over-celebrating trivial events ("You answered a question!") trains students to ignore celebrations. Under-celebrating significant events misses the emotional payoff.

**Celebration tiers:**

| Tier | Achievement | Animation Duration | Elements | Haptic | Sound |
|------|------------|-------------------|----------|--------|-------|
| 1 - Micro | Correct answer | 900ms | XP popup float + bounce | `selectionClick` | Soft chime |
| 2 - Small | 3-in-a-row streak | 1.2s | XP popup + subtle sparkle particles | `mediumImpact` | Ascending double-tone |
| 3 - Medium | Badge earned, session complete, concept mastered | 2s | Scale-in icon + confetti burst (50 particles) + XP counter | `heavyImpact` | Achievement fanfare (3 notes) |
| 4 - Large | Level up, streak milestone (7/14/30), first session | 3s | Full-screen confetti + animated icon + pulsing glow + counter | `heavyImpact` x2 | Extended fanfare (5 notes) + crowd cheer |
| 5 - Epic | Course completion, graduation, 100-day streak | 5s | Full-screen animated scene + flying confetti + fireworks + personalised message | `heavyImpact` x3 | Full celebration soundtrack |

### 3.2 Correct Answer Animation (Tier 1)

**Current state**: `XpPopup` with float + bounce + fade. `FeedbackOverlay` with scale + fade.

**Enhanced specification:**

```
Timeline (900ms total):
  0ms:   XP text appears at center, scale 0.6
  0-270ms: Scale overshoot to 1.3 (Curves.easeOut)
  270-900ms: Scale settle to 1.0 (Curves.bounceOut)
  0-900ms: Float upward 60px (Curves.easeOut)
  450-900ms: Opacity fade 1.0 -> 0.0 (Curves.easeIn)

  0ms:   FeedbackOverlay appears with FadeTransition + ScaleTransition(elasticOut)
  0ms:   Result icon scales from 0 to 1 with elasticOut
  0ms:   Haptic: HapticFeedback.heavyImpact()
  3000ms: Auto-dismiss or tap to dismiss
```

**Addition for 3+ correct streak**: After the XP popup, emit 15-25 sparkle particles from the center point, each following a random trajectory outward with gravity, fading over 600ms. Use `CustomPainter` for particle rendering (not individual widgets -- per the UX review's performance concerns).

**Flutter implementation approach:**
```dart
// Use CustomPainter for particles, not individual widgets
class SparkleParticlePainter extends CustomPainter {
  // Pre-allocate particle positions and velocities
  // Update positions per frame using vsync-driven AnimationController
  // Render as small circles with radial gradient (gold center, transparent edge)
}
```

### 3.3 Streak Milestone Celebration (Tier 4)

**Trigger**: Streak reaches 3, 7, 14, 30, 60, or 100 days.

**Animation sequence (3s):**
```
Phase 1 - Entrance (0-500ms):
  Background dims to 0.4 opacity black overlay
  Streak flame icon scales from 0 to 1.5 with elasticOut
  Flame color pulses through amber -> orange -> red-hot

Phase 2 - Number reveal (500-1500ms):
  Streak number counter rapidly counts up from 0 to N
  Use TweenAnimationBuilder with StepTween for integer counting
  Number text scales 1.0 -> 1.2 -> 1.0 with bounceOut on final number
  "N-Day Streak!" text fades in below

Phase 3 - Confetti + Close (1500-3000ms):
  Confetti burst from top (100 particles, flame colors: amber, orange, red)
  "Keep going!" message fades in
  Tap-to-dismiss hint at bottom

Haptic: heavyImpact at 0ms, heavyImpact at 500ms
Sound: Ascending 5-note fanfare
```

### 3.4 Level-Up Animation (Tier 4)

**Trigger**: XP crosses level boundary.

**Animation sequence (3s):**
```
Phase 1 - Flash (0-200ms):
  Bright white flash overlay (opacity 0 -> 0.3 -> 0)
  Old level number scales out (1.0 -> 0 with easeIn)

Phase 2 - New level reveal (200-1200ms):
  Level circle expands from 0 to final size with elasticOut
  Gradient animates through color spectrum
  New level number counts up from old level to new level
  "Level N!" text scales in with bounceOut

Phase 3 - Reward reveal (1200-2500ms):
  "You unlocked:" text fades in
  Any new feature/reward slides up from bottom
  Confetti burst (75 particles, primary + secondary colors)

Phase 4 - Dismiss (2500-3000ms):
  "Tap to continue" fades in
  All elements can be dismissed by tap

Haptic: heavyImpact at 0ms, heavyImpact at 200ms, selectionClick at 1200ms
Sound: Level-up jingle (major chord arpeggio ascending, 1.5s)
```

### 3.5 Badge Unlock Ceremony (Tier 3)

**Trigger**: Badge requirements met (streak count, mastery count, session count, level).

**Current state**: `BadgeDetailDialog` shows badge with glow shadow for earned badges. No unlock animation.

**Enhanced specification (2s):**
```
Phase 1 - Teaser (0-400ms):
  Badge circle appears locked (greyed out, lock icon)
  Crack/shatter effect on lock: 3 pieces fly outward with physics-based trajectories

Phase 2 - Reveal (400-1200ms):
  Badge circle fills with category color (left-to-right wipe)
  Icon fades from grey to white
  Scale overshoot: 0.8 -> 1.2 -> 1.0
  Category-colored glow radiates outward (BoxShadow blur 8 -> 16 -> 8)

Phase 3 - Info (1200-2000ms):
  Badge name slides up from bottom
  "Badge earned!" label fades in
  Confetti burst (30 particles, category color palette)

Haptic: mediumImpact at 0ms, heavyImpact at 400ms
Sound: Badge unlock sound (crystalline ascending tones, 0.8s)
```

### 3.6 Session Completion Celebration (Tier 3)

**Current state**: Trophy icon (static, 80px), three summary tiles, "Go home" button.

**Enhanced specification (2s before stats reveal):**
```
Phase 1 - Trophy entrance (0-600ms):
  Trophy icon scales from 0 to 1 with elasticOut
  Gold shimmer effect sweeps left-to-right across icon
  "Session Complete!" text fades in with slide from bottom

Phase 2 - Stats count-up (600-2100ms):
  Each stat tile animates in sequence with 200ms stagger:
    Questions: counter 0 -> N over 500ms (StepTween)
    Accuracy: counter 0% -> N% over 500ms
    Time: counter 00:00 -> MM:SS over 500ms
  Each tile slides in from the right with FadeTransition

Phase 3 - Rewards (2100-3000ms):
  XP earned today counter
  Any badges earned during session
  Streak update if applicable
  "Go home" button scales in

Haptic: heavyImpact at 0ms
Sound: Session complete fanfare (warm, satisfying, 1.2s)
```

### 3.7 Course / Subject Completion (Tier 5)

**Trigger**: All concepts in a subject reach mastery threshold.

**This is the rarest and most significant celebration. It should feel genuinely special.**

```
Phase 1 - Scene build (0-1000ms):
  Full-screen gradient background in subject color
  Subject icon appears at center, scales with elasticOut
  Particle ring orbits the icon

Phase 2 - Message (1000-2500ms):
  "You mastered [Subject]!" text types out character-by-character
  Personalised stat: "N concepts, N sessions, N days"
  Fireworks effect: 5 sequential bursts across screen

Phase 3 - Certificate (2500-5000ms):
  Certificate card slides up from bottom
  Student name, subject, date rendered on card
  "Share" button for social
  "Keep practicing" button

Haptic: 3x heavyImpact at 0ms, 1000ms, 2500ms
Sound: Full orchestral celebration (3s)
```

### 3.8 Flutter Animation Implementation

**Recommended packages to add to `pubspec.yaml`:**

```yaml
# Animation & Delight
rive: ^0.13.0                    # Vector animations for characters/celebrations
lottie: ^3.1.0                   # After Effects animations for celebrations
confetti_widget: ^0.4.0          # Confetti particle effects

# Sound
audioplayers: ^6.0.0             # Sound effect playback
```

**Performance constraints:**
- All animations must run at 60fps on Samsung A14 (Mali-G57 GPU)
- Never use more than 200 particles simultaneously
- Use `CustomPainter` for particle effects, NOT individual `AnimatedWidget` instances
- Use `RepaintBoundary` around animated widgets to limit repaint regions
- Pre-warm animations in `initState`, not on first trigger
- Use `vsync: this` with `TickerProviderStateMixin` for all controllers
- Dispose ALL `AnimationController` instances in `dispose()`
- Use `addPostFrameCallback` for celebration sequences that depend on layout

**Animation architecture pattern:**
```dart
/// Base class for celebration overlays.
/// All celebrations follow the same lifecycle:
///   1. Build phase (construct visual elements)
///   2. Enter phase (animate elements in)
///   3. Hold phase (display for duration)
///   4. Exit phase (dismiss on tap or timeout)
abstract class CelebrationOverlay extends StatefulWidget {
  final VoidCallback onDismiss;
  final Duration holdDuration;
}
```

**Rive vs Lottie decision matrix:**

| Factor | Rive | Lottie |
|--------|------|--------|
| File size | 5-50KB per animation | 50-500KB per animation |
| Runtime perf | GPU-accelerated state machine | CPU-rendered frame-by-frame |
| Interactivity | State machines, input triggers | Timeline only |
| Design tool | Rive Editor (web) | After Effects + Bodymovin |
| Best for | Character animations, interactive | Pre-rendered celebrations |
| Recommendation | Mascot/companion, interactive badges | Confetti, fireworks, level-up effects |

---

## Part 4: Feedback Microinteractions (Detailed Specifications)

### 4.1 Button Press Feedback

Every tappable element must provide immediate feedback on press.

**Primary buttons (FilledButton):**
- Visual: `InkWell` splash + Material 3 `stateLayerColor` at 8% opacity
- Haptic: `HapticFeedback.lightImpact()` on tap down (not tap up)
- Scale: None for standard buttons (Material 3 handles elevation)
- Sound: Subtle "click" (see Sound Design)

**Secondary buttons (OutlinedButton):**
- Visual: `InkWell` splash only
- Haptic: None (too frequent for outlined buttons like hint/skip)
- Sound: None

**Icon buttons:**
- Visual: `InkWell` with circular splash
- Haptic: None
- Sound: None

**MCQ option tiles (custom InkWell):**
- Visual: `AnimatedContainer` border + background transition (150ms)
- Haptic: `HapticFeedback.selectionClick()` on selection
- Sound: Subtle "tick"

**Bottom navigation bar items:**
- Visual: Material 3 indicator pill animation (built-in)
- Haptic: `HapticFeedback.selectionClick()` on tab change
- Sound: None

### 4.2 Answer Submission Feedback

The transition from "submitting" to "result" is the most emotionally charged moment in each learning cycle.

**Submission phase (200ms - 3s):**
```
0ms:     Submit button text changes to "Checking..." ("...בודק")
0ms:     Submit button icon replaced with CircularProgressIndicator (18x18, strokeWidth 2)
0ms:     All input elements become non-interactive
200ms:   If still loading, option tiles fade to 60% opacity
1000ms:  If still loading (LLM evaluation), show "Analyzing your response..." text below input
```

**Result phase (instant transition):**
```
0ms:     FeedbackOverlay enters (FadeTransition + ScaleTransition)
0ms:     Haptic fires based on result type
0ms:     Sound plays based on result type
16ms:    XpPopup triggers if XP > 0
0ms:     Input elements are hidden behind overlay
3000ms:  Auto-dismiss (or tap)
```

### 4.3 Progress Bar Micro-Animations

**Current state**: `TweenAnimationBuilder` on `LinearProgressIndicator` with `AnimationTokens.normal` (300ms) and `Curves.easeOut`. Fatigue color interpolation from green through yellow to red.

**Enhancements:**

1. **Milestone pulse**: When progress bar crosses 25%, 50%, 75%, emit a brief pulse (scale 1.0 -> 1.05 -> 1.0, 200ms) on the progress track
2. **Completion approach**: When progress reaches 90%, add a subtle shimmer sweep on the filled portion (use `ShaderMask` with a gradient that translates left-to-right over 2s, repeating)
3. **Fatigue indicator transition**: The emoji-based fatigue indicator should transition between states with a `AnimatedSwitcher` (crossfade, 300ms) rather than instant replacement

### 4.4 Score Counting Animations

Used in session summary, level-up, and badge progress.

**Implementation pattern:**
```dart
class CountUpAnimation extends StatelessWidget {
  final int targetValue;
  final String suffix; // "%" or " XP" or ""
  final Duration duration;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<int>(
      tween: IntTween(begin: 0, end: targetValue),
      duration: duration,
      curve: Curves.easeOutCubic,
      builder: (context, value, _) {
        return Text('$value$suffix', style: style);
      },
    );
  }
}
```

**Timing**: Count from 0 to target over 1.5 seconds using `Curves.easeOutCubic` (fast start, slow finish for dramatic effect on the final digits).

### 4.5 Error / Wrong Answer Feedback

**Design philosophy**: Wrong answers in a learning context are information, not failure. The feedback must be:
- Informative (what went wrong)
- Encouraging (this is normal)
- Actionable (how to improve)
- NOT punishing (no harsh sounds, no red X slamming into view)

**Current implementation**: Red overlay with cancel icon, error type badge in Hebrew, worked solution card. This is good.

**Enhancements:**

1. **Soften the visual**: Instead of 92% opacity red (`0xFFF44336`), use a warmer red-orange (`0xFFE57373` at 85% opacity) for wrong answers. Reserve the harsh red for truly incorrect answers; use amber for "almost correct" partial credit.

2. **Error type visualization**: Map error types to visual metaphors:
   - Conceptual (`שגיאה מושגית`): lightbulb icon with exclamation
   - Procedural (`שגיאה בהליך`): numbered list with wrong step highlighted
   - Careless (`שגיאת רשלנות`): glasses icon (look more carefully)
   - Notation (`שגיאת סימון`): pen/edit icon
   - Incomplete (`תשובה חלקית`): progress bar at partial fill

3. **Encouraging phrases** (rotate randomly, Hebrew):
   - "קרוב! בוא ננסה גישה אחרת" (Close! Let's try a different approach)
   - "כל טעות היא צעד ללמידה" (Every mistake is a step toward learning)
   - "זה נושא מאתגר. בוא נצלול פנימה" (This is a challenging topic. Let's dive in)
   - "הרמז הבא עשוי לעזור" (The next hint might help)
   - "אתה מתקדם -- גם אם לא תמיד מרגישים את זה" (You're progressing -- even if it doesn't always feel like it)

4. **Worked solution reveal**: Animate the worked solution card sliding up from the bottom with a `SlideTransition` (offset 0.3 -> 0.0, duration 400ms, delay 800ms after overlay appearance). This creates a natural pause before showing the solution, preventing the student from immediately seeing the answer.

### 4.6 Hint Reveal Animation

**Current state**: Hints appear in a yellow container (`_HintDisplay`) with no entrance animation.

**Enhanced specification:**
```
Each hint appears with staggered animation:
  Hint N appears at: (N-1) * 200ms delay

  Per hint:
    0ms:   Container height 0, opacity 0
    0-200ms: Height expands to content height (SizeTransition)
    0-200ms: Opacity fades in (FadeTransition)
    100-300ms: Text content slides in from left (SlideTransition, offset -0.1 -> 0.0)
    200ms: Lightbulb icon pulses once (scale 1.0 -> 1.3 -> 1.0)
```

### 4.7 Timer Animations for Timed Activities

**Session elapsed timer** (`_TimerDisplay`):
- Currently: static text `MM:SS` with monospace font, rebuilds every second via `Timer.periodic`
- Enhancement: When approaching target duration (90%+), text color should transition to warning (`colorScheme.error`) and timer should pulse gently (scale 1.0 -> 1.02 -> 1.0, 1s cycle)

**Cognitive load break countdown** (`CognitiveLoadBreak._timerLabel`):
- Currently: static text, updates every second
- Enhancement: The last 10 seconds should count down with each digit scaling in with `bounceOut` (200ms per digit transition)

---

## Part 5: Navigation Microinteractions

### 5.1 Page Transitions

**Current state**: `PageTransitionsTheme` uses `FadeUpwardsPageTransitionsBuilder` on Android and `CupertinoPageTransitionsBuilder` on iOS.

**Recommendations:**
- Keep platform-appropriate defaults (matches user expectations)
- For session screen entry: Override with `SlideTransition` from bottom (the session is a focused mode, entering from bottom signals "entering a workspace")
- For feedback overlay: In-place overlay (already implemented as `Stack` child)
- For onboarding: horizontal `PageView` swipe (already implied by the 5-page flow)

**Custom transition for session entry:**
```dart
GoRoute(
  path: CenaRoutes.session,
  pageBuilder: (context, state) {
    return CustomTransitionPage(
      child: const SessionScreen(),
      transitionsBuilder: (context, animation, secondaryAnimation, child) {
        return SlideTransition(
          position: Tween<Offset>(
            begin: const Offset(0, 1), // from bottom
            end: Offset.zero,
          ).animate(CurvedAnimation(
            parent: animation,
            curve: Curves.easeOutCubic,
          )),
          child: child,
        );
      },
    );
  },
),
```

### 5.2 Pull-to-Refresh

**Applicable screens:** Home tab (refresh greeting + streak), Progress tab (refresh badges/XP from server).

**Specification:**
- Use `RefreshIndicator` wrapping `ListView`
- Trigger threshold: 80px pull distance
- Animation: Material 3 default (rotating indicator)
- Content update: Refresh gamification state from server
- Haptic: `HapticFeedback.mediumImpact()` at trigger threshold

### 5.3 Swipe Gestures

**Flashcard mode (future feature):**
- Swipe right: "I know this" (correct self-assessment)
- Swipe left: "Need more practice" (incorrect self-assessment)
- Swipe velocity threshold: 1000 px/s for quick dismissal, slower swipes show distance-based opacity
- Card follows finger with `Transform.rotate` based on horizontal offset (max +/- 15 degrees)
- Use `Dismissible` widget or custom `GestureDetector` with `AnimationController`

**Onboarding pages:**
- Horizontal swipe between pages using `PageView`
- Page indicator dots at bottom with animated width transition (active dot width 24px, inactive 8px)
- Swipe physics: `BouncingScrollPhysics` for natural feel

### 5.4 Bottom Navigation Bar Animations

**Current state**: Standard `BottomNavigationBar` with `BottomNavigationBarType.fixed`.

**Enhancements:**
- Active tab indicator: Material 3 pill indicator (width animates from 0 to label width over 200ms)
- Icon transition: Active icon scales from 1.0 to 1.1 with `Curves.easeOutBack` (subtle overshoot)
- Label transition: Active label transitions to `w600` weight
- Haptic: `HapticFeedback.selectionClick()` on tab change
- Badge notification dots: Red dot on Sessions tab when a session is resumable, on Progress tab when new badge earned

### 5.5 Modal Presentation Patterns

**Bottom sheet** (approach selector in `action_buttons.dart`):
- Enter: slide up from bottom with `Curves.easeOutCubic` (built-in `showModalBottomSheet`)
- Background: scrim at 40% opacity
- Drag-to-dismiss: enabled (default)
- Corner radius: `RadiusTokens.xl` (16px) on top corners

**Dialog** (badge detail, skip confirmation, end session):
- Enter: `ScaleTransition` from 0.8 to 1.0 with `Curves.easeOutCubic`
- Background: scrim at 40% opacity
- Corner radius: `RadiusTokens.xl` (16px) all corners
- Dismiss: fade out over 200ms

### 5.6 Hero Animations

**Badge grid to badge detail:**
Currently, tapping a badge opens `BadgeDetailDialog` via `showDialog`. This is a modal, not a Hero transition.

**Enhancement**: Wrap the badge circle in both `_BadgeCell` and `BadgeDetailDialog` with `Hero(tag: 'badge_${definition.id}')`. When the dialog opens, the badge circle will smoothly animate from its grid position to the center of the dialog.

```dart
// In _BadgeCell
Hero(
  tag: 'badge_${definition.id}',
  child: Container(/* badge circle */),
)

// In BadgeDetailDialog -- requires using Navigator.push instead of showDialog
// for Hero animations to work. Switch to a full-screen route overlay.
```

**Subject card to session screen:**
When a subject card on the home screen is tapped and the session screen opens, the subject icon could Hero-animate from the card to the session header.

---

## Part 6: Loading & Empty States

### 6.1 Skeleton Screens

**Current state**: `CircularProgressIndicator` is used for loading states in session screen and elsewhere. The `shimmer` package is listed as a dependency but not observed in use.

**Specification for skeleton screens:**

```dart
/// Skeleton shimmer wrapper. Applies a left-to-right shimmer sweep
/// to its child (typically a placeholder Container).
class SkeletonShimmer extends StatelessWidget {
  const SkeletonShimmer({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Shimmer.fromColors(
      baseColor: Theme.of(context).colorScheme.surfaceContainerHighest,
      highlightColor: Theme.of(context).colorScheme.surface,
      period: const Duration(milliseconds: 1500),
      child: child,
    );
  }
}
```

**Skeleton layouts by screen:**

| Screen | Skeleton Shape |
|--------|---------------|
| Question card loading | Card-shaped container (h: 200px) + 4 option rectangles (h: 48px each) |
| Home screen loading | Greeting card skeleton + 5 subject card skeletons |
| Gamification screen loading | XP card skeleton + streak card skeleton + badge grid skeleton (8 circles) |
| Knowledge graph loading | Full-screen circular shimmer pattern |
| Session summary loading | Trophy placeholder + 3 stat row skeletons |

### 6.2 Educational Loading Tips

When loading takes more than 1 second, display a rotating set of study tips.

**Implementation:**
```dart
class EducationalLoadingTip extends StatefulWidget {
  // Rotates through tips every 3 seconds
  static const tips = [
    'Did you know? Spaced repetition helps you remember 200% longer.',
    'Taking breaks actually helps you learn faster.',
    'The Bagrut uses similar question formats to what you practice here.',
    'Mistakes activate more brain regions than correct answers!',
    'Students who study 15 minutes daily outperform 2-hour weekend crammers.',
  ];
}
```

Display below the loading indicator with `FadeTransition` between tips.

### 6.3 Empty State Illustrations

**Current state**: `_EmptyActivityState` shows text only ("Complete a session to see your achievements here."). `_SessionsTabContent` shows a history icon at 50% opacity.

**Enhanced empty states:**

| Screen | Current | Enhanced |
|--------|---------|----------|
| Recent activity (empty) | Text only | SVG illustration of a student starting to study + encouraging text + "Start session" CTA button |
| Session history (empty) | Icon + text + button | SVG illustration of a path/journey beginning + "Your learning journey starts here" |
| Badge grid (no badges earned) | All badges show locked | First 2-3 badges pulse with subtle glow to show they are achievable soon |
| Knowledge graph (no mastery) | Empty canvas | Single "Start here" node that pulses, with tooltip "Master your first concept to grow your knowledge map" |

**Empty state design principles:**
1. Always include a call-to-action button (never a dead end)
2. Use the subject color palette for illustrations
3. Keep illustrations abstract/geometric for the 15-18 age group
4. Text should be encouraging and specific about what action to take
5. Use `flutter_svg` for illustrations (already a dependency, vector = resolution-independent)

### 6.4 First-Time Use States (FTUE)

**Onboarding tooltips for first-time users:**

| Feature | Trigger | Tooltip Content |
|---------|---------|----------------|
| First question | First question presented | "Read the question carefully. Tap an option to select it." |
| First hint | First session, question 3+ | "Stuck? Tap 'Hint' for a clue. You can get up to 3 hints per question." |
| First wrong answer | First wrong answer | "Don't worry! Wrong answers help you learn. Check the explanation below." |
| Streak | First day 2 | "You're on a 2-day streak! Come back tomorrow to keep it going." |
| Badge earned | First badge | "You earned your first badge! Check your Progress tab to see all badges." |

**Implementation**: Use `Tooltip` or a custom spotlight/coach overlay. Track FTUE state in `SharedPreferences` with keys like `ftue_hint_shown`, `ftue_streak_shown`.

### 6.5 Offline State Design

**Current state**: Offline sync service exists with event queue, connectivity monitoring, and clock skew detection. The UI has no visible offline indicator.

**Specification:**

1. **Offline banner**: When connectivity is lost during a session, show a non-blocking banner at the top of the screen:
   - Background: `colorScheme.tertiaryContainer`
   - Icon: `Icons.cloud_off_rounded`
   - Text: "Working offline. Your answers will sync when you reconnect."
   - Animation: Slide down from top (200ms)

2. **Sync indicator**: When reconnected and syncing pending events:
   - Small sync icon in the app bar
   - Progress: "Syncing N answers..."
   - Completion: "All synced!" with checkmark, auto-dismiss after 2s

3. **Offline limitations**: Some features are unavailable offline:
   - LLM hints: Show "Hints require internet connection" instead of hint content
   - Knowledge graph: Show cached snapshot with "Last updated N hours ago" label
   - Badge unlock: Queue badge display for when sync completes

---

## Part 7: Sound Design

### 7.1 Sound Palette

All sounds must be short (< 2 seconds), mixed at low volume relative to system media, and respect the device mute switch.

**Sound categories and specifications:**

| Sound | Duration | Frequency Range | Emotional Tone | Usage |
|-------|----------|----------------|----------------|-------|
| Option select tick | 50ms | 2-4 kHz | Neutral/confirming | MCQ option tap |
| Submit click | 100ms | 1-3 kHz | Decisive | Submit button press |
| Correct chime | 400ms | 800-2000 Hz, ascending | Warm/satisfied | Correct answer |
| Wrong buzz | 300ms | 200-500 Hz, descending | Gentle/informative | Wrong answer |
| Partial tone | 350ms | 500-1200 Hz, flat | Neutral/acknowledging | Partial credit |
| Hint reveal | 200ms | 3-5 kHz, descending | Curious/discovery | Hint button press |
| Badge unlock | 800ms | 400-3000 Hz, ascending arpeggio | Proud/achievement | Badge earned |
| Level up | 1500ms | 200-4000 Hz, ascending chord | Triumphant | Level boundary crossed |
| Session complete | 1200ms | 300-2500 Hz, resolved chord | Satisfied/accomplished | Session end |
| Streak milestone | 1000ms | 500-3000 Hz, fanfare | Celebratory | Streak at 7/14/30 |
| Navigation tap | 30ms | 4-6 kHz | Nearly silent | Bottom nav tap |
| Error dismiss | 150ms | 1-2 kHz, neutral | Neutral | Dismiss feedback overlay |

### 7.2 Sound Implementation

**Package**: `audioplayers: ^6.0.0`

**Architecture:**
```dart
/// Centralised sound manager. Pre-loads all sound assets on init.
/// Respects system mute and app-level sound preference.
class SoundManager {
  static final SoundManager instance = SoundManager._();
  SoundManager._();

  final AudioPlayer _player = AudioPlayer();
  bool _soundEnabled = true;

  /// Pre-load all sound assets from assets/sounds/
  Future<void> init() async {
    // Pre-warm audio player
    await _player.setSource(AssetSource('sounds/silence.wav'));
  }

  /// Play a named sound effect. No-op if sound is disabled.
  Future<void> play(CenaSound sound) async {
    if (!_soundEnabled) return;
    await _player.setSource(AssetSource(sound.assetPath));
    await _player.setVolume(sound.volume);
    await _player.resume();
  }

  void setSoundEnabled(bool enabled) => _soundEnabled = enabled;
}

enum CenaSound {
  optionTick('sounds/option_tick.wav', 0.3),
  submitClick('sounds/submit_click.wav', 0.4),
  correctChime('sounds/correct_chime.wav', 0.6),
  wrongBuzz('sounds/wrong_buzz.wav', 0.4),
  partialTone('sounds/partial_tone.wav', 0.5),
  hintReveal('sounds/hint_reveal.wav', 0.3),
  badgeUnlock('sounds/badge_unlock.wav', 0.7),
  levelUp('sounds/level_up.wav', 0.7),
  sessionComplete('sounds/session_complete.wav', 0.7),
  streakMilestone('sounds/streak_milestone.wav', 0.7);

  const CenaSound(this.assetPath, this.volume);
  final String assetPath;
  final double volume;
}
```

**Sound file format**: WAV (lossless, instant decode) for effects under 1s. OGG Vorbis for effects over 1s (smaller file size, acceptable decode latency).

### 7.3 Ambient Study Sounds

**Optional feature**: "Study atmosphere" ambient background sounds that play during active sessions.

**Options:**
- Rain
- Coffee shop murmur
- Library quiet
- Lo-fi instrumental
- Nature (birds, water)
- None (default)

**Implementation**: Use a separate `AudioPlayer` instance for ambient audio, with crossfade between tracks. Volume control via slider in settings. Auto-pause when app backgrounds.

### 7.4 Sound Accessibility

**Every sound must have a visual alternative:**

| Sound | Visual Alternative |
|-------|-------------------|
| Correct chime | Green overlay + checkmark icon |
| Wrong buzz | Red overlay + X icon |
| Badge unlock | Badge glow animation + "Badge earned!" text |
| Level up | Level circle animation + "Level up!" text |
| Streak milestone | Streak counter animation + "N-day streak!" text |

**Implementation**: Sound should be opt-in in settings. Default: ON for first install, with a "Sound preferences" option on onboarding page 1 (welcome).

### 7.5 Age-Appropriate Sound Design

For 15-18 year old Israeli high school students:
- Sounds should feel clean and modern, NOT childish (no cartoon boinks or Mario coins)
- Sounds should be short and non-intrusive (students often study in shared spaces)
- Musical sounds should use minor/neutral tonality for learning, major tonality for celebrations
- Avoid overly "gamey" sounds that would embarrass a teenager in a library
- The sound palette should feel closer to Headspace/Calm than to Candy Crush

---

## Part 8: Haptic Feedback Patterns

### 8.1 iOS Taptic Engine Patterns

iOS provides richer haptic vocabulary than Android. Map to appropriate patterns:

| Action | iOS Haptic | Android Haptic | When |
|--------|-----------|----------------|------|
| Option select | `.selection` | `HapticFeedback.selectionClick()` | Tap MCQ option |
| Submit answer | `.light` | `HapticFeedback.lightImpact()` | Tap submit button |
| Correct answer | `.success` | `HapticFeedback.heavyImpact()` | Result = correct |
| Wrong answer | `.warning` | `HapticFeedback.lightImpact()` | Result = wrong |
| Partial credit | `.light` | `HapticFeedback.mediumImpact()` | Result = partial |
| Hint reveal | `.light` | `HapticFeedback.mediumImpact()` | Hint button tap |
| Badge earned | `.success` + `.selection` | `HapticFeedback.heavyImpact()` x2 | Badge unlock |
| Level up | `.success` + `.success` | `HapticFeedback.heavyImpact()` x2 | Level boundary |
| Streak milestone | `.success` + `.heavy` | `HapticFeedback.heavyImpact()` x2 | Streak 7/14/30 |
| Tab switch | `.selection` | `HapticFeedback.selectionClick()` | Bottom nav tap |
| Pull-to-refresh trigger | `.medium` | `HapticFeedback.mediumImpact()` | Pull threshold reached |
| Error / validation | `.error` | `HapticFeedback.vibrate()` | Form validation failure |

### 8.2 Haptic Implementation

```dart
/// Centralised haptic manager with platform-aware feedback.
class HapticManager {
  static Future<void> selection() async {
    await HapticFeedback.selectionClick();
  }

  static Future<void> light() async {
    await HapticFeedback.lightImpact();
  }

  static Future<void> medium() async {
    await HapticFeedback.mediumImpact();
  }

  static Future<void> heavy() async {
    await HapticFeedback.heavyImpact();
  }

  static Future<void> success() async {
    await HapticFeedback.heavyImpact();
    await Future.delayed(const Duration(milliseconds: 100));
    await HapticFeedback.selectionClick();
  }

  static Future<void> error() async {
    await HapticFeedback.lightImpact();
  }

  static Future<void> celebration() async {
    await HapticFeedback.heavyImpact();
    await Future.delayed(const Duration(milliseconds: 150));
    await HapticFeedback.heavyImpact();
  }
}
```

### 8.3 When NOT to Use Haptics

- Typing in text fields (every keystroke would be overwhelming)
- Scrolling through content
- Timer ticking
- Background state changes (streak loaded, XP synced)
- Continuous animations (breathing circle, progress bar)
- Audio playback feedback (redundant with sound)

---

## Part 9: Character & Mascot Design

### 9.1 Learning Companion Concept

**Not Duolingo**: Cena targets 15-18 year olds preparing for a high-stakes national exam. A cartoon owl would be patronizing. The companion should be abstract, minimal, and feel more like a study partner than a pet.

**Recommendation: "Cee" -- an abstract geometric companion**

**Design language:**
- Shape: Rounded hexagon or pentagon (mathematical, not childish)
- Style: Flat 2D with subtle gradient, not 3D or skeuomorphic
- Colors: Uses the primary teal (`0xFF0097A7`) with accent variations
- Size: 40-60px in UI contexts, 120px in celebration contexts
- Face: Minimal -- two dots for eyes, optional curved line for expression. No mouth, ears, or limbs.

**Why geometric/abstract:**
- Culturally neutral (works across Hebrew, Arabic, English audiences)
- Doesn't trigger "this is for kids" reaction in teenagers
- Aligns with math/science STEM branding
- Easier to animate (fewer articulation points)
- Rive state machine can handle expressions with just eye shape and body scale

### 9.2 Character Emotional States

| State | Visual | Trigger |
|-------|--------|---------|
| Neutral/Ready | Eyes centered, body at rest | Default, waiting for input |
| Curious | Eyes looking at the question, slight tilt | New question presented |
| Encouraging | Eyes bright (slightly larger), gentle bounce | After wrong answer, before retry |
| Celebrating | Eyes as happy crescents, upward bounce + sparkles | Correct answer |
| Proud | Eyes bright, slight "nod" (vertical bob) | Badge earned, level up |
| Thinking | Eyes looking up-right, slight pulse | Loading / server evaluation |
| Sleepy | Eyes half-closed, slow sway | Cognitive load break suggestion |
| Alarm | Eyes wide, body vibrates | Streak at risk |

**Rive implementation:**
Each state is a Rive state machine state. Transitions between states are triggered by input values from Dart. The state machine handles all tweening between expressions internally.

```dart
class CeeCompanion extends StatefulWidget {
  final CompanionState state;
  // Rive controller manages state transitions
}
```

### 9.3 Customizable Companions

**Unlockable via the gamification system:**

| Unlock | Variant | How |
|--------|---------|-----|
| Default | Teal Cee | Available from start |
| Level 5 | Color variants (amber, green, purple, grey) | Choose in settings |
| Level 10 | Particle trail effect | Companion leaves sparkle trail |
| 30-day streak | "On fire" variant | Warm gradient, ember particles |
| All subjects practiced | "Rainbow" variant | Cycling gradient |
| Level 20 | Custom expression pack | Additional emotional states |

### 9.4 When the Mascot Helps vs Annoys

**Show Cee:**
- On onboarding pages (guides the new user)
- In empty states (makes blank screens feel less lonely)
- During cognitive load breaks (companionship during rest)
- On session completion (celebrates with the student)
- On badge unlock (proud reaction)
- When streak is at risk (alarm state, urgency without being pushy)

**Do NOT show Cee:**
- During active question-answering (distracting from the task)
- On the progress/gamification screen (data should speak for itself)
- In settings (too playful for a utility screen)
- On every correct answer (over-exposure breeds contempt)
- In error states that are the app's fault (server error + mascot = tone-deaf)

### 9.5 Character-Driven Encouragement Messages

Pair Cee's emotional state with text messages:

**After wrong answer (encouraging state):**
- "This is a tricky one. Try looking at the problem from a different angle."
- "Almost! The right approach is close. Want a hint?"
- "Don't sweat it -- this concept trips up a lot of students."

**After 3+ wrong in a row (concerned state):**
- "Hey, let's slow down and review the basics on this topic."
- "Would you like to try a different approach? (switch methodology button)"
- "Sometimes a 5-minute break helps. Want to take one?"

**After 5+ correct in a row (celebrating state):**
- "You're on fire!"
- "This is your topic. Keep going!"
- "Mastery in progress..."

**Rules for encouragement messages:**
1. Never say "Good try!" (patronizing)
2. Never mention intelligence ("You're so smart!") -- praise effort and strategy
3. Keep messages short (< 15 words)
4. Rotate messages -- never show the same one twice in a session
5. Match language to locale (Hebrew/Arabic/English)
6. Respect `GamificationIntensity` setting: `minimal` = no messages, `standard` = occasional, `full` = frequent

---

## Part 10: Emotional Curve Design

### 10.1 Session Emotional Arc

A well-designed learning session follows an emotional arc:

```
Emotional
Intensity
  ^
  |           **** Breakthrough moment
  |          *    *
  |    ***  *      *  Challenge
  |   *   **        *
  |  *                * Resolution
  | * Warm-up          *
  |*                    * Wind down
  +----------------------------> Time
  |  2 min  | 5-15 min  | 2 min |
```

**Phase 1: Warm-up (questions 1-2)**
- Start with questions 1-2 difficulty levels below the student's current mastery
- Purpose: build confidence, activate prior knowledge
- Emotional target: "I can do this"
- Celebration level: minimal (Tier 1 only)
- Sound: no celebration sounds, just confirmation ticks

**Phase 2: Challenge zone (questions 3+)**
- Difficulty adjusts to Zone of Proximal Development (managed by `QuestionSelector`)
- Purpose: productive struggle, genuine learning
- Emotional target: "This is hard but I'm making progress"
- Celebration level: proportional to difficulty (harder questions = bigger celebration)
- The 3-correct-streak sparkle effect kicks in here

**Phase 3: Breakthrough moments**
- When the student answers a hard question correctly after previous wrong attempts on the same concept
- Purpose: crystallize the "aha" feeling
- Emotional target: "I GOT IT!"
- Celebration level: Tier 3 (confetti + sound + haptic)
- Cee companion shows proud state

**Phase 4: Wind-down (last 2-3 minutes)**
- Difficulty decreases slightly
- Purpose: end on a positive note
- Emotional target: "That was a good session"
- Session summary celebration triggers

### 10.2 Designing "Aha Moments"

The "aha moment" is the most valuable emotional experience in learning. It cannot be manufactured, but it can be facilitated:

**Detection heuristics (server-side, reflected in UI):**
1. Student gets concept X wrong 2+ times, then correct
2. Student uses 2+ hints on concept X, then answers without hints
3. Student's mastery on concept X crosses 0.6 threshold

**UI response to detected "aha moment":**
1. Enhanced correct-answer celebration (Tier 3 instead of Tier 1)
2. Cee companion shows "proud" state
3. Message: "You cracked it! [Concept name] is clicking for you."
4. XP bonus: 2x for the breakthrough question
5. Add concept to "Breakthroughs this session" in session summary

### 10.3 Frustration Detection and Intervention

**Frustration signals (combined heuristic):**
1. 3+ wrong answers in a row on the same concept
2. Response time increasing (each answer takes 20%+ longer)
3. Hint usage increasing (using all 3 hints per question)
4. Skip frequency increasing
5. Erratic tap patterns (rapid random option selection)

**Graduated intervention:**

| Signal Level | Intervention | UI Element |
|-------------|-------------|------------|
| Mild (2 wrong in a row) | Encouraging message | Cee + text overlay, auto-dismiss |
| Moderate (3 wrong or 2 skips) | Suggest methodology switch | Bottom sheet with approach options |
| Strong (4+ wrong or response time 2x baseline) | Suggest break | Cognitive load break screen |
| Severe (5+ wrong + erratic taps) | Auto-suggest session end | "You've been working hard. Want to save progress and return later?" |

**Never:**
- Lock the student out of continuing (autonomy must be respected)
- Show frustration-detection UI (meta-awareness of being "measured" is stressful)
- Reduce difficulty automatically without telling the student (feels patronizing when noticed)

### 10.4 Tone of Voice Guidelines

**Hebrew (primary):**
- Register: Informal but respectful. Use second person singular (אתה/את), not plural or formal
- Avoid: Academic jargon, condescension, exaggerated excitement
- Model: A smart older sibling who's been through Bagrut, not a teacher
- Error messages: Frame as shared problem ("Let's look at this together") not personal failure ("You got it wrong")

**Arabic:**
- Register: Informal, MSA for academic terms but colloquial structure
- Cultural note: Honor/dignity is important. Never phrase errors as shameful
- Encouragement should reference effort and persistence, not innate ability

**English (fallback):**
- Register: Casual, direct
- Model: Study buddy, not professor

**Examples across tone spectrum:**

| Situation | Too cold | Right | Too warm |
|-----------|----------|-------|----------|
| Correct answer | "Correct." | "Nice work!" | "OMG AMAZING!!!" |
| Wrong answer | "Incorrect." | "Not quite. Let's see why." | "Aww no worries you'll get it!!" |
| Session end | "Session terminated." | "Good session! Here's your summary." | "WOW what a FANTASTIC session!!!" |
| Streak at risk | "Streak expires today." | "Your streak is at risk. Practice today to keep it going." | "OH NO your precious streak!!!" |

---

## Part 11: Dark Mode & Theming

### 11.1 Dark Mode

**Current state**: `CenaTheme.dark(locale)` exists with `ColorScheme.fromSeed` using dark brightness. Both light and dark themes are fully implemented.

**Auto dark mode specification:**
- Default: Follow system setting (`ThemeMode.system`)
- Manual override: In settings, allow Light / Dark / System toggle
- Time-based override: Option to auto-switch to dark mode between 20:00-06:00 (common study hours for Israeli high schoolers who study at night)

**Dark mode specific adjustments:**
1. Celebration animations: Confetti particles should use brighter/lighter colors in dark mode (gold/amber instead of subtle pastels)
2. Feedback overlay opacity: Reduce from 0.92 to 0.85 in dark mode (dark background already provides contrast)
3. Subject colors: Use the `Secondary` variant of subject colors (lighter) for dark mode backgrounds, `Primary` for text/icons
4. Streak flame: Glow effect (BoxShadow) should be more visible in dark mode (increase spread by 2px)
5. XP popup: Gold text should add a subtle outer glow in dark mode for visibility

### 11.2 Theme Customization as Reward

**Unlockable themes via gamification:**

| Theme | Unlock Condition | Description |
|-------|-----------------|-------------|
| Default Light | Free | Teal + amber, clean white backgrounds |
| Default Dark | Free | Same palette, dark surface |
| Subject Theme | Practice 10 sessions in one subject | Entire app tinted to subject's primary color |
| High Contrast | Free (accessibility) | Higher contrast ratios, bolder borders |
| Night Owl | 30 sessions after 20:00 | Deep navy/midnight blue dark theme with star accents |
| Minimal | Level 15 | Reduced visual chrome, distraction-free |

### 11.3 Age-Appropriate Color Palettes

The existing `SubjectColorTokens` are well-chosen for the 15-18 age group:

| Subject | Primary | Emotional Association | Notes |
|---------|---------|----------------------|-------|
| Math | Teal `0xFF0097A7` | Clarity, logic, precision | Cool tone for analytical thinking |
| Physics | Amber `0xFFFF8F00` | Energy, motion, warmth | Warm tone for dynamic subjects |
| Chemistry | Green `0xFF388E3C` | Nature, experimentation | Growth-oriented for lab science |
| Biology | Purple `0xFF7B1FA2` | Complexity, life, depth | Rich tone for biological systems |
| CS | Grey `0xFF616161` | Technology, systems | Neutral for technical subject |

**Color accessibility**: All color pairs must meet WCAG 2.1 AA contrast ratio (4.5:1 for text, 3:1 for large text). The current theme uses Material 3 `ColorScheme.fromSeed` which auto-generates accessible contrast pairs.

### 11.4 Seasonal Themes and Limited-Time Visual Events

**Concept**: Limited-time visual modifications that add novelty without disrupting usability.

| Event | Timing | Visual Change |
|-------|--------|--------------|
| Start of school year | September | "Welcome back!" splash + school-themed empty states |
| Hanukkah | December | Subtle candle animation on streak widget (day 1-8 candles appear) |
| Purim | March | Optional confetti theme for celebrations (brighter colors, more particles) |
| Pre-Bagrut | April-June | "Exam mode" focus theme -- minimal distractions, timer prominent |
| Summer break | July-August | Relaxed theme, beach-themed empty states, "Summer review" mode |

**Implementation**: Server-driven feature flag (`seasonal_theme: "hanukkah"`) that modifies specific visual elements without changing core interaction patterns.

---

## Part 12: Gesture Design

### 12.1 Natural Gestures for Learning

| Gesture | Context | Action | Feedback |
|---------|---------|--------|----------|
| Single tap | MCQ option | Select answer | Animation + haptic |
| Single tap | Submit button | Submit answer | Loading state |
| Single tap | Badge | Open detail dialog | Hero transition |
| Single tap | Subject card | Open session with subject | Navigation transition |
| Long press | Term/concept in question text | Show definition tooltip | Haptic + tooltip |
| Long press | Achievement event | Show detail modal | Haptic + expand |
| Double tap | Question card | Bookmark question (future) | Heart icon animation + haptic |
| Swipe left/right | Flashcard (future) | Know/don't know | Card physics animation |
| Swipe down | Pull-to-refresh | Refresh data | Material indicator |
| Pinch | Knowledge graph | Zoom in/out | InteractiveViewer |
| Pan | Knowledge graph | Navigate graph | InteractiveViewer |
| Drag | Ordering questions (future) | Reorder items | Drag handle + shadow |

### 12.2 Drawing/Writing Gestures for Math

**For diagram and proof question types** (currently scaffold only):

**Handwriting recognition (future):**
- Canvas widget with pressure-sensitive stroke rendering
- Stroke color: subject primary color
- Stroke width: 2-4px based on pressure
- Undo: swipe left with two fingers, or undo button
- Clear: shake device, or clear button
- Submit: converts strokes to image, sends to LLM for recognition

**Number pad overlay for numeric input:**
- Custom number pad instead of system keyboard for `QuestionType.numeric`
- Includes: digits, decimal point, negative sign, pi, sqrt, powers
- Positioned at bottom of screen, does not obscure question
- Haptic feedback on each key press

### 12.3 Long-Press for Definitions

**Specification:**
- Any concept term in question text can be long-pressed to show its definition
- Detection: Terms are wrapped in `<term>` tags by the server
- UI: `Tooltip`-like overlay above the pressed word, arrow pointing down
- Content: Concept name + 1-2 sentence definition from the knowledge graph
- Duration: Shows while finger is held, auto-dismisses 3 seconds after release
- Haptic: `HapticFeedback.selectionClick()` on appearance

### 12.4 Double-Tap for Bookmarking

**Specification (future feature):**
- Double-tap anywhere on the question card to bookmark it
- Visual: Heart/bookmark icon scales in at the center of the card (0 -> 1.3 -> 1.0, 400ms)
- Haptic: `HapticFeedback.mediumImpact()`
- State: Bookmarked questions appear in a "Review later" section
- De-bookmark: Double-tap again (icon scales out)

---

## Part 13: Emotional Design Guidelines per Screen Type

### 13.1 Onboarding Screens

**Emotional target**: Curiosity, excitement, confidence
**Visceral**: Clean illustrations, subject color accents, progress dots
**Behavioral**: Clear next/back navigation, skip option always available
**Reflective**: "You chose Math. Great choice!" (validation of their selection)

### 13.2 Home Screen

**Emotional target**: Welcome, readiness, progress awareness
**Visceral**: Time-aware greeting, warm subject card colors, streak flame
**Behavioral**: One-tap to start session, clear navigation
**Reflective**: Streak counter, daily XP, "You're on a roll" messaging

### 13.3 Active Session Screen

**Emotional target**: Focus, flow, productive challenge
**Visceral**: Clean question card, minimal chrome, fatigue-aware colors
**Behavioral**: Clear submission flow, progressive hints, methodology control
**Reflective**: Question counter shows progress, methodology badge shows learning strategy

### 13.4 Feedback Overlay

**Emotional target**: Correct = pride/joy. Wrong = information/encouragement. Partial = acknowledgment.
**Visceral**: Full-screen color wash, large result icon
**Behavioral**: Tap to dismiss, auto-dismiss fallback, XP display
**Reflective**: Error type classification, worked solution, encouraging messages

### 13.5 Cognitive Load Break

**Emotional target**: Calm, recovery, self-care
**Visceral**: Soft green/blue breathing circle, gentle animation
**Behavioral**: Non-blocking (can skip), countdown timer, continue/end options
**Reflective**: "We noticed you need a break" validates their tiredness

### 13.6 Gamification / Progress Screen

**Emotional target**: Pride, achievement, growth awareness
**Visceral**: Gold/amber XP accents, badge grid with glow effects
**Behavioral**: Tap badges for detail, scroll through achievements
**Reflective**: Level number, streak record, badge collection, activity feed

### 13.7 Session Summary

**Emotional target**: Accomplishment, closure, anticipation for next session
**Visceral**: Trophy icon, stat count-up animations
**Behavioral**: Clear "go home" path, no dead ends
**Reflective**: Accuracy trend, improvement metrics, "come back tomorrow" for streak

### 13.8 Settings

**Emotional target**: Control, trust, transparency
**Visceral**: Clean list layout, no visual flourishes
**Behavioral**: Clear labels, immediate effect of changes
**Reflective**: Sound/theme/language choices make the app "mine"

---

## Part 14: Performance Considerations

### 14.1 60fps Animation Budget

On a mid-range device (Samsung A14, Mali-G57 GPU), the frame budget at 60fps is 16.67ms per frame.

**Rules:**
1. Never instantiate `Widget` objects inside animation callbacks (use `builder` pattern with `child` parameter)
2. Use `RepaintBoundary` around animated widgets to limit repaint regions
3. Use `CustomPainter` for particle effects (max 200 particles)
4. Use `AnimatedBuilder` (not `AnimatedWidget` subclass) for animations that paint within a `Stack`
5. Pre-calculate all animation values in `initState`, not in `build`
6. Use `vsync: this` with `TickerProviderStateMixin` for all controllers
7. Dispose all controllers in `dispose()`
8. Use `Curves.easeOutCubic` for most transitions (fast start, smooth finish)

### 14.2 Animation Controller Count

The existing codebase already creates multiple controllers per screen:
- `StreakWidget`: 1 controller (pulse, 800ms repeat)
- `XpPopup`: 1 controller (float/fade/scale, 900ms)
- `FeedbackOverlay`: 1 controller (scale/fade)
- `CognitiveLoadBreak`: 1 controller (breathing, 4s repeat)
- `_AnimatedXpBar`: uses `TweenAnimationBuilder` (implicit, no explicit controller)

**Maximum concurrent controllers**: 8 per screen. Beyond that, investigate whether animations can be merged into a single controller with `Interval` curves.

### 14.3 Image and Asset Loading

- Pre-cache celebration Rive/Lottie files in `main.dart` or `app.dart` `initState`
- Use `precacheImage` for any PNG/SVG used in celebrations
- Sound files should be pre-loaded via `SoundManager.init()` on app startup
- Keep total celebration asset size under 2MB (all Rive + Lottie + sounds combined)

### 14.4 Shimmer Performance

The `shimmer` package uses `ShaderMask` with a `LinearGradient` animation. This triggers GPU shader compilation on first render, causing a one-time jank spike of 50-100ms. Mitigate by:
1. Pre-warming shimmer on splash screen (render one invisible shimmer widget)
2. Using shimmer only on content-loading screens (not during active session)

---

## Part 15: Reference Examples from Leading Apps

### 15.1 Duolingo

**What Cena should adopt:**
- Streak mechanics: Daily practice requirement with freeze protection (already implemented)
- XP system with levels (already implemented)
- Celebratory animations scaled to achievement importance (needs enhancement)
- Wrong answer feedback that teaches, not punishes (partially implemented)

**What Cena should NOT adopt:**
- Owl mascot with guilt-tripping push notifications ("These reminders don't seem to be working. We'll stop sending them." -- manipulative)
- Hearts system that limits practice after mistakes (punishes learning)
- Leaderboard as default (competitive pressure is counterproductive for test anxiety)
- Overly childish visual design (wrong age group)

### 15.2 Headspace

**What Cena should adopt:**
- Breathing animation design language for cognitive load breaks (partially implemented with the breathing circle)
- Calm, non-intrusive sound design
- Dark mode that feels genuinely restful
- Session duration flexibility with visual timer

**What Cena should NOT adopt:**
- Subscription wall (Cena is educational, not wellness)
- Minimalist to the point of hiding features

### 15.3 Calm

**What Cena should adopt:**
- Ambient sound options during study sessions
- "Daily calm" concept mapped to "daily session" nudge
- Time-of-day adaptive UI (greeting already implemented)

### 15.4 Notion

**What Cena should adopt:**
- Keyboard shortcuts and quick-actions for power users (future, relevant for CS students)
- Clean card-based layout with minimal borders (already aligned)
- Toggle/switch animations for settings

### 15.5 Todoist

**What Cena should adopt:**
- "Karma" XP system that motivates daily completion (aligned with Cena's XP system)
- Streak visualization with calendar (already implemented)
- Task completion animation (checkmark draws itself)
- Achievement celebrations that match achievement importance

---

## Part 16: Package Dependencies and Asset Structure

### 16.1 Packages to Add

```yaml
# pubspec.yaml additions under dependencies:

# Animation & Delight
rive: ^0.13.0                      # Vector animations (mascot, interactive celebrations)
lottie: ^3.1.0                     # After Effects animations (confetti, fireworks, level-up)
confetti_widget: ^0.4.0            # Quick confetti particle effects

# Sound
audioplayers: ^6.0.0               # Sound effect playback

# Haptics (enhanced)
# flutter/services.dart HapticFeedback is sufficient for basic patterns
# No additional package needed -- use HapticFeedback from dart:services
```

### 16.2 Asset Directory Structure

```
assets/
  animations/          # Already declared in pubspec.yaml
    celebrations/
      confetti.json          # Lottie: general confetti burst
      level_up.json          # Lottie: level-up effect
      fireworks.json         # Lottie: course completion fireworks
      streak_flame.json      # Lottie: streak milestone flame burst
    mascot/
      cee.riv                # Rive: companion character state machine
    loading/
      thinking.json          # Lottie: "analyzing response" animation
  sounds/
    effects/
      option_tick.wav        # 50ms, 2-4 kHz
      submit_click.wav       # 100ms, 1-3 kHz
      correct_chime.wav      # 400ms, ascending
      wrong_buzz.wav         # 300ms, descending
      partial_tone.wav       # 350ms, flat
      hint_reveal.wav        # 200ms, discovery
      badge_unlock.wav       # 800ms, arpeggio
      level_up.wav           # 1500ms, ascending chord
      session_complete.wav   # 1200ms, resolved chord
      streak_milestone.wav   # 1000ms, fanfare
      silence.wav            # 10ms, pre-warm player
    ambient/
      rain.ogg
      coffee_shop.ogg
      library.ogg
      lofi.ogg
      nature.ogg
  images/               # Already declared in pubspec.yaml
    empty_states/
      no_sessions.svg
      no_badges.svg
      no_activity.svg
      journey_start.svg
    onboarding/
      welcome.svg
      subjects.svg
      diagnostic.svg
      ready.svg
  icons/                 # Already declared in pubspec.yaml
  fonts/                 # Already declared in pubspec.yaml
```

### 16.3 Total Asset Budget

| Category | Target Size | Justification |
|----------|------------|---------------|
| Rive files | 100-300KB total | State machine format is compact |
| Lottie files | 200-500KB total | JSON-based, optimize with Bodymovin |
| Sound effects | 200-400KB total | WAV at 22kHz mono, short durations |
| Ambient sounds | 2-4MB total | OGG at 128kbps, 30-60s loops |
| SVG illustrations | 100-300KB total | Optimized vector |
| **Total** | **3-5MB** | Acceptable for initial download |

---

## Part 17: Implementation Priority

### Phase 1: Foundation (Week 1-2)
1. Add `SoundManager` service with pre-loading
2. Add `HapticManager` centralised service
3. Implement skeleton screens using `shimmer` package
4. Add haptic feedback to MCQ option selection, submit button, tab switches
5. Add sound effects for correct/wrong answers

### Phase 2: Celebrations (Week 3-4)
1. Add `confetti_widget` for correct answer streaks (3+)
2. Implement session completion count-up animation
3. Implement badge unlock ceremony animation
4. Add level-up celebration overlay
5. Add streak milestone celebration

### Phase 3: Character & Polish (Week 5-6)
1. Design and implement Cee companion in Rive
2. Add FTUE tooltips for first-time users
3. Implement enhanced empty states with SVG illustrations
4. Add offline state indicator UI
5. Add ambient study sounds option

### Phase 4: Advanced (Week 7-8)
1. Implement "aha moment" detection and enhanced celebration
2. Add frustration detection and graduated intervention
3. Implement seasonal themes infrastructure
4. Add theme customization as gamification rewards
5. Add Hero transitions for badge grid -> detail

---

## Appendix A: Animation Token Extensions

The existing `AnimationTokens` should be extended:

```dart
abstract class AnimationTokens {
  // Existing
  static const Duration fast = Duration(milliseconds: 150);
  static const Duration normal = Duration(milliseconds: 300);
  static const Duration slow = Duration(milliseconds: 600);
  static const Duration celebration = Duration(milliseconds: 1000);

  // New
  static const Duration micro = Duration(milliseconds: 50);       // Haptic-only feedback
  static const Duration xpFloat = Duration(milliseconds: 900);    // XP popup lifecycle
  static const Duration countUp = Duration(milliseconds: 1500);   // Number counting animations
  static const Duration badgeUnlock = Duration(milliseconds: 2000); // Badge ceremony
  static const Duration levelUp = Duration(milliseconds: 3000);   // Level-up sequence
  static const Duration epicCelebration = Duration(milliseconds: 5000); // Course completion

  // Stagger delays
  static const Duration stagger = Duration(milliseconds: 100);    // Between sequential items
  static const Duration staggerLong = Duration(milliseconds: 200); // Between celebration phases
}
```

## Appendix B: Celebration Overlay Widget Specification

```dart
/// Base specification for celebration overlays.
///
/// All celebration widgets should extend this pattern:
/// 1. Receive [onDismiss] callback
/// 2. Auto-dismiss after [duration]
/// 3. Allow tap-to-dismiss
/// 4. Play sound and haptic on enter
/// 5. Use [AnimationController] with [vsync]
/// 6. Clean up in [dispose]
///
/// Usage in SessionScreen or HomeScreen:
///   if (showCelebration)
///     CelebrationOverlay(
///       type: CelebrationType.levelUp,
///       data: {'newLevel': 5},
///       onDismiss: () => setState(() => showCelebration = false),
///     )
///
/// Implementation file: src/mobile/lib/core/widgets/celebration_overlay.dart
```

## Appendix C: Sound Design Specification Sheet

| Sound Name | Notes | BPM | Key | Duration | Volume | File |
|-----------|-------|-----|-----|----------|--------|------|
| option_tick | Single wooden click | n/a | n/a | 50ms | 0.3 | WAV 22kHz mono |
| submit_click | Soft button press | n/a | n/a | 100ms | 0.4 | WAV 22kHz mono |
| correct_chime | C5-E5-G5 ascending | n/a | C major | 400ms | 0.6 | WAV 44kHz stereo |
| wrong_buzz | E3-C3 descending minor 3rd | n/a | A minor | 300ms | 0.4 | WAV 22kHz mono |
| partial_tone | Single G4 tone, soft attack | n/a | n/a | 350ms | 0.5 | WAV 22kHz mono |
| hint_reveal | Paper unfolding + chime | n/a | n/a | 200ms | 0.3 | WAV 22kHz mono |
| badge_unlock | Crystalline arpeggio C4-E4-G4-C5 | n/a | C major | 800ms | 0.7 | WAV 44kHz stereo |
| level_up | Brass fanfare C4-E4-G4-C5-E5 | n/a | C major | 1500ms | 0.7 | WAV 44kHz stereo |
| session_complete | Warm chord resolution Am-Dm-G-C | 80 | C major | 1200ms | 0.7 | WAV 44kHz stereo |
| streak_milestone | Ascending 5-note fanfare with percussion | 120 | D major | 1000ms | 0.7 | WAV 44kHz stereo |
