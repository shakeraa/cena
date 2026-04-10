# 05 — Learning Session

## Overview

The learning session is the **heart of the product**. Everything else — home, progress, tutor, social — exists to serve or respond to the session. This doc is intentionally the longest because session implementation is the most task-dense and behavior-rich surface of the app.

A session is a stateful, realtime, immersive experience:

- The student answers a sequence of adaptive questions.
- A virtual `LearningSessionActor` on the backend holds authoritative state.
- The Flutter mobile app talks to the actor via SignalR + NATS.
- The student web app talks to the **same** hub using the **same** contracts.

## Mobile Parity — Source Files

The mobile feature is decomposed into ~25 files. Every behavior below has a Flutter reference:

**Screen & state**:
- [session_screen.dart](../../src/mobile/lib/features/session/session_screen.dart)
- [quick_review_button.dart](../../src/mobile/lib/features/session/quick_review_button.dart)

**Widgets**:
- [answer_input.dart](../../src/mobile/lib/features/session/widgets/answer_input.dart)
- [question_card.dart](../../src/mobile/lib/features/session/widgets/question_card.dart)
- [math_text.dart](../../src/mobile/lib/features/session/widgets/math_text.dart)
- [feedback_overlay.dart](../../src/mobile/lib/features/session/widgets/feedback_overlay.dart)
- [hint_chip.dart](../../src/mobile/lib/features/session/widgets/hint_chip.dart)
- [action_buttons.dart](../../src/mobile/lib/features/session/widgets/action_buttons.dart)
- [progress_bar.dart](../../src/mobile/lib/features/session/widgets/progress_bar.dart)
- [phase_indicator.dart](../../src/mobile/lib/features/session/widgets/phase_indicator.dart)
- [flow_ambient_indicator.dart](../../src/mobile/lib/features/session/widgets/flow_ambient_indicator.dart)
- [cognitive_load_break.dart](../../src/mobile/lib/features/session/widgets/cognitive_load_break.dart)
- [confidence_rating.dart](../../src/mobile/lib/features/session/widgets/confidence_rating.dart)
- [concept_summary_card.dart](../../src/mobile/lib/features/session/widgets/concept_summary_card.dart)
- [deep_dive_sheet.dart](../../src/mobile/lib/features/session/widgets/deep_dive_sheet.dart)
- [teach_back_prompt.dart](../../src/mobile/lib/features/session/widgets/teach_back_prompt.dart)
- [review_session_screen.dart](../../src/mobile/lib/features/session/widgets/review_session_screen.dart)
- [coach_mark_overlay.dart](../../src/mobile/lib/features/session/widgets/coach_mark_overlay.dart)
- [deep_study_config.dart](../../src/mobile/lib/features/session/widgets/deep_study_config.dart)
- [boss_battle_screen.dart](../../src/mobile/lib/features/session/widgets/boss_battle_screen.dart)
- [boss_battle_result.dart](../../src/mobile/lib/features/session/widgets/boss_battle_result.dart)

**Models**:
- [session_flow_arc.dart](../../src/mobile/lib/features/session/models/session_flow_arc.dart)
- [boss_battle.dart](../../src/mobile/lib/features/session/models/boss_battle.dart)
- [context_session.dart](../../src/mobile/lib/features/session/models/context_session.dart)
- [deep_study_session.dart](../../src/mobile/lib/features/session/models/deep_study_session.dart)
- [teach_back_models.dart](../../src/mobile/lib/features/session/models/teach_back_models.dart)
- [training_wheels_config.dart](../../src/mobile/lib/features/session/models/training_wheels_config.dart)

**Supporting services**:
- [flow_monitor_service.dart](../../src/mobile/lib/core/services/flow_monitor_service.dart)
- [session_notifier.dart](../../src/mobile/lib/core/state/session_notifier.dart)
- [websocket_service_impl.dart](../../src/mobile/lib/core/services/websocket_service_impl.dart)

## Session Types

All share the same base flow but differ in configuration, goal, and UI chrome.

| Type | Description | Entry point |
|------|-------------|-------------|
| **Standard** | Adaptive question stream in chosen subject(s) | Home → Start Session |
| **Review** | SRS-driven, questions from due items only | Home → Review Now |
| **Deep Study** | 45–90 min multi-block with recovery breaks | Session launcher → Deep Study |
| **Boss Battle** | High-stakes challenge on a mastery milestone | Challenges → Boss Battle |
| **Diagnostic** | 5–10 questions to seed mastery (used in onboarding) | Onboarding step 6 |
| **Teacher-assigned** | Teacher-configured question set, graded | Notification or LMS deep link |

Each type sets flags in `LearningSessionActor` on start (`mode`, `timeBudget`, `targetConcepts`, etc.) and the UI reads the same flags to adjust chrome.

## Session Launcher (`/session`)

Before the live session begins, the student configures it.

Components:
- `<SessionSubjectPicker>` — multi-select, defaults to student's current subjects.
- `<SessionTimeSlider>` — 5/10/15/30/45/60/90 min options.
- `<SessionModeToggle>` — Standard / Review / Deep Study / Boss Battle / Diagnostic.
- `<DeepStudyConfigPanel>` — block count, recovery break length, content focus (see `deep_study_config.dart`).
- `<TrainingWheelsPanel>` — hint generosity, timer strictness, confidence-rating prompt (see `training_wheels_config.dart`).
- `<SessionPreviewCard>` — estimates XP, question count, expected completion time.
- `[Start Session]` primary button.

On submit → `POST /api/sessions/start` → server returns `{ sessionId }` → navigate to `/session/:sessionId`.

If an active session already exists the launcher shows a warning: "Resume existing session or end and start a new one?"

## Live Session (`/session/:sessionId`) — Layout

Layout: `blank` (no sidebar, no app bar). Enters a focused, distraction-free mode with an ambient background tint driven by flow state.

```
┌───────────────────────────────────────────────────────────────────────┐
│  ▲ Exit                Session · Math · 14/30 · 12m left         🔊 ⋯ │ ← top bar (auto-hides)
├───────────────────────────────────────────────────────────────────────┤
│                                                                       │
│                         [FlowAmbientBackground]                       │
│                                                                       │
│   ┌─────────────────────────────────────────────────────────────┐     │
│   │                                                             │     │
│   │                        QUESTION CARD                        │     │
│   │   Phase: Core · Concept: Quadratic formula                  │     │
│   │                                                             │     │
│   │   If  ax² + bx + c = 0,  solve for x.                       │     │
│   │                                                             │     │
│   │   [Diagram / Figure if present]                             │     │
│   │                                                             │     │
│   └─────────────────────────────────────────────────────────────┘     │
│                                                                       │
│   ┌─────────────────────────────────────────────────────────────┐     │
│   │                     ANSWER INPUT                            │     │
│   │   Multiple choice / numeric / free-text / match / drag      │     │
│   └─────────────────────────────────────────────────────────────┘     │
│                                                                       │
│   [Hint]  [Skip]  [Submit]                                            │
│                                                                       │
├───────────────────────────────────────────────────────────────────────┤
│  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░  Progress                                 │ ← progress bar
└───────────────────────────────────────────────────────────────────────┘
```

**Immersive mode rules** (mirrors mobile):
- Top bar auto-hides after 4 s of inactivity; reappears on mouse-move.
- Progress bar is the only constant visual; it is the ambient timer replacement.
- Flow-state color wash is the only other ambient cue.
- No popups except for feedback overlay and cognitive-load break.

## Core Flow (State Machine)

```
Idle
 │ POST /sessions/start
 ▼
Starting ───► ActorActivated ───► QuestionDelivered
                                          │
                                          ▼
                              ┌─────── Answering ◄──────────┐
                              │          │                   │
                 hint request │          │ submit            │
                              ▼          ▼                   │
                         HintShown   Evaluating              │
                                         │                   │
                                         ▼                   │
                                  AnswerEvaluated            │
                                         │                   │
                                         ▼                   │
                                   FeedbackShown             │
                                         │                   │
                           ┌─────────────┼──────────────┐    │
                           │             │              │    │
                           ▼             ▼              ▼    │
                      NextQuestion  DeepDive      TeachBack  │
                           │             │              │    │
                           └─────────────┴──────────────┘    │
                                         │                   │
                                         └───────────────────┘
 SessionEnded ◄── student exits or time up
```

Events come over SignalR in both directions:

| Direction | Event | Purpose |
|-----------|-------|---------|
| Server → Client | `QuestionDelivered` | Show new question |
| Server → Client | `AnswerEvaluated` | Show feedback, update mastery |
| Server → Client | `HintDelivered` | Append hint to chip list |
| Server → Client | `PhaseChanged` | Update phase indicator |
| Server → Client | `FlowScoreUpdated` | Update ambient tint |
| Server → Client | `CognitiveLoadHigh` | Suggest break |
| Server → Client | `XpAwarded` | Trigger XP popup |
| Server → Client | `BadgeEarned` | Trigger badge celebration |
| Server → Client | `SessionEnded` | Navigate to summary |
| Client → Server | `SubmitAnswer` | Submit current answer |
| Client → Server | `RequestHint` | Ask for a hint |
| Client → Server | `SkipQuestion` | Skip current question |
| Client → Server | `EndSession` | Voluntarily end |
| Client → Server | `PauseSession` | Pause (tab hidden / explicit) |
| Client → Server | `ResumeSession` | Resume after pause |
| Client → Server | `SubmitConfidenceRating` | Rate confidence post-feedback |
| Client → Server | `SubmitTeachBack` | Submit teach-back explanation |

Contracts are defined in `src/api/Cena.Api.Host/Hubs/HubContracts.cs` — reuse as-is.

## Answer Input Types

The `<AnswerInput>` component switches on `question.answerType` (matches mobile):

| Type | UI |
|------|-----|
| `multipleChoice` | 2–5 option buttons, keyboard 1–5 shortcut |
| `multipleSelect` | Same as above with checkboxes, keyboard toggle |
| `numeric` | Number field with unit selector if present |
| `freeText` | Single-line or multi-line text |
| `match` | Drag items from column A to column B |
| `order` | Drag to reorder a list |
| `fillBlank` | Inline blanks within the question text |
| `mathExpression` | KaTeX-rendered input with a math keyboard (see web enhancements) |
| `diagramAnnotate` | Click-to-mark on an SVG diagram |
| `teachBack` | Structured free-text prompting the student to explain |

All input types support **Enter-to-submit** (Cmd+Enter in multi-line) and **Esc-to-clear**.

## Feedback Overlay

After each submission, `<FeedbackOverlay>` slides up with:
- Correct / incorrect headline
- Canonical answer (always shown, even on correct)
- Worked explanation (markdown with KaTeX)
- Related concepts (chips → link to knowledge graph)
- XP delta and mastery delta (with micro-animations)
- "Ask the tutor" CTA → opens tutor in side panel (web-only enhancement)
- Primary button: "Next question"

`Enter` advances to the next question.

## Hint System

Hints are **tiered** (matches mobile):

1. **Nudge** — small cue, no content revealed.
2. **Guided hint** — partial solution step.
3. **Worked example** — full alternate example.

Each hint request costs XP (configurable by training wheels) and is logged for mastery calculation. Hint button disabled if no hints remain.

## Flow Monitor & Ambient Feedback

The web client reuses the **same algorithm** as mobile's `FlowMonitorService`. Implementation:

- `useFlowMonitor()` composable computes the flow score from the same 5 signals.
- `<FlowAmbientBackground>` renders a 600 ms cross-fade color wash matching mobile `_colorForState`.
- Flow state is also published over SignalR from the server (actor is authoritative); client-side calc is a fast fallback.

## Cognitive Load Break

When `CognitiveLoadHigh` event fires, `<CognitiveLoadBreak>` modal covers the screen with:
- Reassuring copy
- Suggested break length (30 s / 1 min / 2 min)
- Breathing-cue animation (bypassable with `prefers-reduced-motion`)
- "I'm ready" CTA to dismiss

Mirrors mobile `cognitive_load_break.dart`.

## Phase Indicator & Session Flow Arc

Sessions have 3 phases matching mobile's `session_flow_arc.dart`:

- **Warm-up** — first 10–15% of questions, easier items, ambient blue
- **Core** — main learning, ambient gold when in flow
- **Cool-down** — last 10–15%, consolidation, ambient cool

`<PhaseIndicator>` shows current phase as a 3-segment progress bar above the question card.

## Confidence Rating

After the feedback overlay on selected questions, a 1–5 slider appears: "How confident were you?". Used for metacognition + mastery calibration. Matches `confidence_rating.dart`.

## Teach-Back Prompt

On mastery-milestone questions, a `<TeachBackPrompt>` appears post-feedback asking the student to explain the concept in their own words (structured: hypothesis, reasoning, conclusion). Submitted to backend, optionally graded by AI tutor. Matches `teach_back_prompt.dart`.

## Deep Dive Sheet

From the feedback overlay, the student can tap "Deep dive" to open `<DeepDiveSheet>` — a drawer (web) / bottom sheet (mobile) with:
- Extended concept explanation
- Related worked examples
- Link to the concept in the knowledge graph
- Optional tutor conversation starter

## Boss Battle

A high-stakes session type with dedicated UI:
- Health bar that depletes on wrong answers
- Boss portrait (mastery-themed)
- Timer per question (stricter than normal)
- Dramatic feedback animations on victory / defeat
- Result screen with XP multiplier and badge unlock

Matches `boss_battle_screen.dart` + `boss_battle_result.dart`.

## Deep Study Session

Multi-block sessions with recovery breaks. Configurable:
- Block count (2–6)
- Block length (20 / 30 / 45 min)
- Recovery break length (2 / 5 / 10 min)
- Content focus per block (same subject, alternate subjects, spiral)

Between blocks the student sees `<RecoveryBreakScreen>` with:
- Congratulatory message
- Progress toward plan completion
- Suggested stretch / breathing exercise
- "Start next block" CTA with countdown

Matches `deep_study_session.dart` + `deep_study_config.dart`.

## Training Wheels

Per-session difficulty modulator (matches `training_wheels_config.dart`):
- **Hint generosity** — off / low / normal / high
- **Timer strictness** — off / loose / normal / strict
- **Immediate feedback** — on / delayed
- **Confidence prompts** — on / off

Configured in the launcher, enforced by the server.

## Session Summary (`/session/:sessionId/summary`)

Shown when the session ends. Dashboard-style page with:
- Flow time % (from `flow_ambient_indicator.FlowTimeSummary`)
- Accuracy + speed
- Concepts touched (chips linking to knowledge graph)
- XP earned + badges unlocked
- Streak impact
- Mastery deltas per concept
- "Review misses" CTA → starts a review session scoped to the missed questions
- "Save session to favorites" (web-only)
- "Share summary" (web-only, generates a public-safe image)

## Replay (`/session/:sessionId/replay`)

Read-only playback of a completed session. Every question and answer visible, with the option to retry individual questions. Backed by `GET /api/sessions/{id}/replay`.

## Quick Review Button

Floating action button at the bottom-right of the session screen that opens a "quick review" drawer with the last 3 questions, answers, and canonical solutions. Useful for students who want to double-check before moving on. Matches `quick_review_button.dart`.

## Coach Marks (First-Run Tutorial)

On the first session, `<CoachMarkOverlay>` walks the student through:
1. The question card
2. The hint button
3. The flow ambient indicator
4. The progress bar
5. The top-bar controls

Each step is a tooltip with a spotlight. Skippable. Matches `coach_mark_overlay.dart`.

## Web-Specific Enhancements

- **Keyboard shortcuts** (see [14-web-enhancements](14-web-enhancements.md)):
  - `1`–`5` → select option
  - `Enter` → submit / next
  - `H` → request hint
  - `S` → skip
  - `Space` → pause
  - `Esc` → exit confirmation
  - `M` → toggle sound
- **Picture-in-picture diagrams** — drag a diagram out of the question card into a floating window so it stays visible while answering.
- **Side-panel tutor** — press `T` or click "Ask the tutor" to open a 400 px side panel with the AI tutor scoped to the current question.
- **Split-view on `xl`** — on very wide screens, show question on the left and diagram/explanation on the right without overlay.
- **Scratchpad** — press `P` to open a scratch canvas overlay for drawing / notes. Auto-clears on question change; auto-saves to localStorage for that session.
- **Math keyboard** — inline virtual math keyboard for `mathExpression` input type.
- **Draft autosave** — free-text and teach-back answers save to localStorage every 5 s.
- **"Explain like I'm 5" / "Explain like a pro"** — feedback overlay has two alternative explanation toggles.
- **Session replay export** — download a JSON or PDF transcript of the session for portfolio or review.

## Acceptance Criteria

Session launcher:
- [ ] `STU-SES-001` — `/session` launcher page with subject picker, time slider, mode toggle, and preview card.
- [ ] `STU-SES-002` — Deep study config panel available when mode = Deep Study.
- [ ] `STU-SES-003` — Training wheels panel available for all modes except Boss Battle.
- [ ] `STU-SES-004` — Active-session warning blocks new session start unless the existing one is ended.
- [ ] `STU-SES-005` — `POST /api/sessions/start` called on submit; navigates to live session on success.

Live session (state + UI):
- [ ] `STU-SES-010` — `/session/:sessionId` page loads with `blank` layout and full-screen immersive mode.
- [ ] `STU-SES-011` — SignalR connection established with JWT auth and joins `session:{sessionId}` group.
- [ ] `STU-SES-012` — `QuestionDelivered` event renders the question with correct answer-input type.
- [ ] `STU-SES-013` — All 10 answer-input types implemented and keyboard-navigable.
- [ ] `STU-SES-014` — `SubmitAnswer` sent over hub; optimistic UI disabled until `AnswerEvaluated` arrives.
- [ ] `STU-SES-015` — Feedback overlay shows canonical answer, explanation, XP delta, mastery delta.
- [ ] `STU-SES-016` — Hint button issues `RequestHint`, appends returned hint chip, disables when exhausted.
- [ ] `STU-SES-017` — Skip button issues `SkipQuestion` and advances.
- [ ] `STU-SES-018` — `Enter` advances from feedback to next question.
- [ ] `STU-SES-019` — Top bar auto-hides after 4 s of inactivity and reappears on mouse-move.
- [ ] `STU-SES-020` — Progress bar reflects `currentQuestion / totalQuestions` and time remaining.
- [ ] `STU-SES-021` — Phase indicator reflects warm-up / core / cool-down and updates on `PhaseChanged`.
- [ ] `STU-SES-022` — Flow ambient background renders 5 flow states with correct colors and 600 ms cross-fade.
- [ ] `STU-SES-023` — Flow state comes from server events and falls back to client-side calc on disconnect.
- [ ] `STU-SES-024` — Cognitive-load break modal triggered by `CognitiveLoadHigh` event with breathing animation.
- [ ] `STU-SES-025` — Confidence rating slider appears on milestone questions and posts rating to server.
- [ ] `STU-SES-026` — Teach-back prompt appears on mastery milestones; response submitted to server.
- [ ] `STU-SES-027` — Deep dive sheet accessible from feedback overlay and shows extended content.
- [ ] `STU-SES-028` — Quick review button opens drawer with last 3 Q&A.
- [ ] `STU-SES-029` — Coach marks run on first-ever session and are dismissible.
- [ ] `STU-SES-030` — Exit confirmation modal shown on navigation away / `Esc`.
- [ ] `STU-SES-031` — Tab-hidden triggers `PauseSession`; return triggers `ResumeSession`.
- [ ] `STU-SES-032` — Disconnect recovery: in-flight events queued, replayed on reconnect.

Session types:
- [ ] `STU-SES-040` — Standard session flow works end-to-end.
- [ ] `STU-SES-041` — Review session uses SRS-due items and enforces review UI.
- [ ] `STU-SES-042` — Deep study session runs multi-block with recovery break screens.
- [ ] `STU-SES-043` — Boss battle session renders health bar, timer, boss portrait, result screen.
- [ ] `STU-SES-044` — Diagnostic session runs 5–10 items and posts results on completion.
- [ ] `STU-SES-045` — Teacher-assigned session pulls question set and gradebook context.

Summary & replay:
- [ ] `STU-SES-050` — `/session/:sessionId/summary` renders on `SessionEnded` event.
- [ ] `STU-SES-051` — Summary shows flow %, accuracy, concepts, XP, badges, streak impact, mastery deltas.
- [ ] `STU-SES-052` — "Review misses" CTA starts a scoped review session.
- [ ] `STU-SES-053` — Share-summary action generates a public-safe image.
- [ ] `STU-SES-054` — `/session/:sessionId/replay` renders read-only playback with per-question retry.

Web enhancements:
- [ ] `STU-SES-060` — Keyboard shortcuts implemented and documented (1–5, Enter, H, S, Space, Esc, M, T, P).
- [ ] `STU-SES-061` — Picture-in-picture diagram viewer supported.
- [ ] `STU-SES-062` — Side-panel tutor opens with `T` and is scoped to the current question.
- [ ] `STU-SES-063` — Split-view on `xl` breakpoint renders question + diagram side-by-side.
- [ ] `STU-SES-064` — Scratchpad overlay opens with `P`, persists per-session to localStorage.
- [ ] `STU-SES-065` — Math keyboard appears for `mathExpression` input type.
- [ ] `STU-SES-066` — Free-text and teach-back draft autosave every 5 s.
- [ ] `STU-SES-067` — Feedback overlay offers "Explain like I'm 5" and "Explain like a pro" variants.
- [ ] `STU-SES-068` — Session transcript export (JSON, PDF) available from summary page.

## Backend Dependencies

- `/hub/cena` — exists (CenaHub.cs); used as-is
- `POST /api/sessions/start` — new (may exist; verify)
- `GET /api/sessions/active` — exists
- `GET /api/sessions/{id}` — exists
- `GET /api/sessions/{id}/replay` — exists
- `POST /api/sessions/{id}/resume` — exists
- Hub contracts `HubContracts.cs` — exists; additive changes only
