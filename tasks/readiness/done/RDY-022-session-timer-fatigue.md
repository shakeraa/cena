# RDY-022: Session Timer + Fatigue Detection UI

- **Priority**: Medium — backend exists, no frontend
- **Complexity**: Mid frontend engineer
- **Source**: Expert panel audit — Lior (UX), Nadia (Pedagogy)
- **Tier**: 3
- **Effort**: 3 days

## Problem

No visible timer during sessions. No "How are you feeling?" prompt. Backend `CognitiveLoadService` computes fatigue with a 3-factor model and recommends cooldown — but nothing surfaces in UI. Students don't know how long they've been practicing or when to stop.

## Scope

### 1. Session timer display

- Show elapsed time in session header (discreet, not anxiety-inducing)
- At selected duration milestone (e.g., 25 minutes), show soft suggestion: "Good stopping point. Continue or wrap up?"
- Timer is informational only — does not auto-end session

### 2. Fatigue check prompt

- After 10 questions: show brief energy check ("How are you feeling?")
- Options: energized / okay / tired
- Maps to backend fatigue assessment
- If "tired": suggest break with estimated cooldown from CognitiveLoadService

### 3. Pause session

- Add "Pause" button to session header
- Pauses timer, saves state via `useSessionPersistence()`
- Resume card appears on home page (ResumeSessionCard already exists, needs wiring)

## Files to Modify

- `src/student/full-version/src/views/apps/session/index.vue` — add timer + pause
- New: `src/student/full-version/src/components/session/SessionTimer.vue`
- New: `src/student/full-version/src/components/session/FatigueCheck.vue`
- `src/student/full-version/src/components/common/ResumeSessionCard.vue` — wire up

## Acceptance Criteria

- [ ] Elapsed time visible during session
- [ ] Soft suggestion at 25 minutes (configurable)
- [ ] Fatigue check after 10 questions
- [ ] "Tired" response → break suggestion with cooldown estimate
- [ ] Pause button saves state and shows resume card on home
- [ ] Timer does not auto-end session (informational only)
- [ ] Timer element uses `role="timer"` and `aria-live="off"` (no constant announcements)
- [ ] 25-minute milestone announced via `aria-live="polite"` once
- [ ] Fatigue check dialog is keyboard-accessible with focus trap

> **Cross-review (Tamar)**: Timer must use `role="timer"` with `aria-live="off"` to prevent screen readers from constantly announcing elapsed time. Milestone alerts use `aria-live="polite"` as one-shot announcements.
