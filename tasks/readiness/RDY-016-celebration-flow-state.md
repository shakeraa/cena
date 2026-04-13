# RDY-016: Celebration Animations + Flow State UX

- **Priority**: High — zero delight in the learning experience
- **Complexity**: Mid frontend engineer
- **Source**: Expert panel audit — Lior (UX), Nadia (Pedagogy)
- **Tier**: 2
- **Effort**: 1 week

## Problem

Zero celebration animations exist. No confetti, no level-up fanfare, no sound effects. Flow state tokens exist (warming/approaching/inFlow/disrupted/fatigued) with `FlowAmbientBackground.vue` component, but no API drives the transitions. The "designed but not wired" pattern.

**Ship-gate compliance**: Celebrations must be fixed-ratio (confetti on level-up = OK). Banned: streaks, loss-aversion, variable-ratio rewards (ADR ship-gate).

## Scope

### 1. Celebration animations

- **Correct answer**: Subtle green pulse + "+N XP" with bounce animation
- **Level up**: Confetti burst (lightweight lib like canvas-confetti, ~5KB) + "Level N!" modal
- **Mastery milestone** (crossing 0.85): Trophy animation + "You've mastered [concept]!" toast
- **Session complete**: Summary card with animated stats reveal

### 2. Audio feedback (optional, user-togglable)

- Correct answer: short chime
- Wrong answer: gentle tone (not punitive)
- Level up: fanfare
- All audio gated by user preference (default: off for a11y)

### 3. Flow state trigger API

Wire `FlowAmbientBackground.vue` to actual flow state:
- Backend `CognitiveLoadService` already computes fatigue level
- Map fatigue + accuracy trend → flow state (warming/approaching/inFlow/disrupted/fatigued)
- Expose via session state in SignalR push or REST polling
- Background transitions on state change

### 4. Flow state user feedback

- When `inFlow`: subtle "You're in the zone" indicator (not disruptive)
- When `fatigued`: "Good time for a break" CTA with optional pause
- When `disrupted`: difficulty adjustment message ("Let's try something easier")

### 5. Accessibility

- All animations respect `prefers-reduced-motion` (disable or simplify)
- Celebrations announced via `aria-live` ("Level up! You reached level 6")
- Audio optional and off by default

## Files to Modify

- `src/student/full-version/src/components/session/AnswerFeedback.vue` — add correct/wrong animations
- `src/student/full-version/src/components/gamification/XpProgressCard.vue` — level-up confetti
- `src/student/full-version/src/components/session/FlowAmbientBackground.vue` — wire to real state
- New: `src/student/full-version/src/composables/useCelebration.ts` — shared celebration logic
- New: `src/student/full-version/src/composables/useFlowState.ts` — flow state management

## Acceptance Criteria

- [ ] Correct answer shows animated XP badge
- [ ] Level-up triggers confetti + modal
- [ ] Mastery milestone (0.85) triggers celebration toast
- [ ] FlowAmbientBackground driven by real backend state
- [ ] Fatigue state shows "Take a break" CTA
- [ ] All animations disabled when `prefers-reduced-motion` is set
- [ ] Audio feedback toggleable in settings (default: off)
- [ ] No banned mechanics (streaks, loss-aversion, variable-ratio rewards)
- [ ] Confetti avoids full-viewport parallax effects (vestibular disorder safety)
- [ ] Animation duration <= 500ms (avoid prolonged vestibular stimulation)
- [ ] No motion-sickness triggers (spinning, rapid direction changes)
- [ ] Users with vestibular disorders can opt for static celebration (badge highlight only)
- [ ] Level-up announced via `aria-live="assertive"`: "Level up! You reached level N"
- [ ] Mastery milestone announced via `aria-live`: "You've mastered [concept]!"

> **Cross-review (Tamar)**: Original task had no vestibular disorder considerations beyond `prefers-reduced-motion`. Added animation duration cap, parallax ban, static celebration fallback, and `aria-live` announcements for all celebration events.
