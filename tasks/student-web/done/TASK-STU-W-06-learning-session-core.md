# TASK-STU-W-06: Learning Session — Core Experience

**Priority**: HIGHEST — this is the heart of the product
**Effort**: 8-12 days (the single largest task in the bundle; assign your most experienced lead)
**Phase**: 2
**Depends on**: [STU-W-03](TASK-STU-W-03-api-signalr-client.md)
**Backend tasks**: [STB-01](../student-backend/TASK-STB-01-session-start-and-hub.md), [STB-10](../student-backend/TASK-STB-10-hub-contracts-expansion.md)
**Status**: Not Started

---

## Goal

Build the live learning session: launcher → immersive full-screen session → feedback loop → summary → replay. Match every behavior of the Flutter mobile client and add keyboard-first web affordances. This is the task the rest of the app exists to serve.

## Spec

Full specification in [docs/student/05-learning-session.md](../../docs/student/05-learning-session.md). All 68 `STU-SES-*` acceptance criteria in that file form this task's checklist. Read it end-to-end before starting.

## Scope

This task is big enough to split into four logical sub-deliveries; work within a single branch but land in reviewable chunks.

### Sub-delivery A — Launcher (1-2 days)

- `/session` page with `<SessionSubjectPicker>`, `<SessionTimeSlider>`, `<SessionModeToggle>`, `<DeepStudyConfigPanel>`, `<TrainingWheelsPanel>`, `<SessionPreviewCard>`
- Active-session warning when `/api/sessions/active` returns a session
- `POST /api/sessions/start` integration (STB-01); on success navigate to `/session/:sessionId`
- All launcher controls are keyboard-reachable
- Diagnostic / review / boss / deep-study / teacher-assigned modes configure the right launcher chrome

### Sub-delivery B — Live session shell & core loop (3-5 days)

- `/session/:sessionId` page with `blank` layout, immersive mode, top bar auto-hide, ambient progress bar, phase indicator
- SignalR join: `session:{sessionId}` group, handshake wraps tokens via STU-W-03
- Core state machine: `QuestionDelivered` → `Answering` → `SubmitAnswer` → `Evaluating` → `AnswerEvaluated` → `FeedbackShown` → next
- `<QuestionCard>` with KaTeX math, markdown, embedded diagram slot
- `<AnswerInput>` router switching on `question.answerType` — all 10 types:
  1. `multipleChoice`
  2. `multipleSelect`
  3. `numeric` (with unit picker)
  4. `freeText` (single + multi-line with draft autosave)
  5. `match` (drag items column A → B)
  6. `order` (drag to reorder)
  7. `fillBlank` (inline blanks)
  8. `mathExpression` (inline math keyboard from STU-W-15 — stub for now)
  9. `diagramAnnotate` (click-to-mark on SVG)
  10. `teachBack` (structured prompt with draft autosave)
- Hint chips (`<HintChip>`) with tiered reveal
- `<FeedbackOverlay>` with canonical answer, explanation, XP delta, mastery delta, ask-tutor CTA
- `Enter` advances from feedback to next question

### Sub-delivery C — Flow, load, ambience, extras (2-3 days)

- `<FlowAmbientBackground>` from STU-W-01 wired to `FlowScoreUpdated` events
- `<CognitiveLoadBreak>` modal on `CognitiveLoadHigh` with breathing-cue animation (respects `prefers-reduced-motion`)
- `<PhaseIndicator>` reacting to `PhaseChanged`
- `<ConfidenceRating>` on milestone questions, posts via `SubmitConfidenceRating`
- `<TeachBackPrompt>` on mastery milestones, posts via `SubmitTeachBack`
- `<DeepDiveSheet>` drawer from feedback overlay
- `<QuickReviewButton>` FAB with last-3 Q&A drawer
- `<CoachMarkOverlay>` first-session tutorial, dismissible
- Tab-hidden → `PauseSession`, return → `ResumeSession`
- Disconnect recovery: in-flight client-to-server events queued by STU-W-03 are replayed on reconnect

### Sub-delivery D — Summary, replay, session types (2 days)

- `/session/:sessionId/summary` page rendered on `SessionEnded` event
- Summary widgets: flow %, accuracy, concepts, XP, badges, streak impact, mastery deltas, "Review misses" CTA, share-summary action
- `/session/:sessionId/replay` read-only playback with per-question retry
- Boss battle UI: `<BossBattleScreen>`, `<BossBattleResult>`, health bar, timer, portrait
- Deep study recovery break screen between blocks
- Review session scoped to SRS-due items
- Diagnostic session completion posts results

## Out of Scope

- Keyboard shortcuts and side-panel tutor — STU-W-15
- Picture-in-picture diagrams — STU-W-13
- Scratchpad overlay — STU-W-15
- Math keyboard for `mathExpression` — STU-W-15 (stub input for now)
- Actual boss battle content generation — backend (STB-05)
- SRS algorithm — backend

## Definition of Done

- [ ] All 68 `STU-SES-*` acceptance criteria in [05-learning-session.md](../../docs/student/05-learning-session.md) pass
- [ ] Full Playwright suite: launcher → live → summary flow for standard, review, deep-study, boss-battle, and diagnostic modes
- [ ] SignalR disconnect mid-session queues and replays submits without double-sending
- [ ] Tab-hide/tab-show sends pause/resume correctly
- [ ] Question transition under 150 ms from `AnswerEvaluated` event to next question render
- [ ] Hub event to UI update under 50 ms
- [ ] Flow ambient cross-fade is 600 ms, no jank
- [ ] Feedback overlay passes axe-core; all screen-reader live regions announce correctly
- [ ] Coach marks run exactly once per student and the skip button persists the dismissal
- [ ] Exit confirmation modal blocks Esc and browser back button during an active session
- [ ] All 10 answer-input types keyboard-navigable without a mouse
- [ ] Math rendering (KaTeX) works RTL-aware (equations stay LTR inside RTL paragraphs)
- [ ] Cross-cutting concerns from the bundle README apply
- [ ] Performance: session bundle chunk ≤ 450 KB gzipped (one-time concession above the 200 KB feature budget)

## Risks

- **Scope** — this is the most complex single task; daily stand-ups required. If slipping, land sub-deliveries A + B + D first and push C to a fast-follow PR.
- **Mobile parity drift** — behaviors diverge easily. Every sub-delivery must be reviewed against the referenced Flutter file; discrepancies go into the Flutter task tracker, not silently adapted.
- **Answer-input type sprawl** — each of the 10 types can become a rat hole. Implement a shared `<AnswerInputHost>` that handles submit/clear/keyboard, and keep each input small.
- **Realtime state machine bugs** — the session is hard to debug in production. Add a `?debug=1` query param that shows a dev-only side panel with the last 50 hub events, current state, and WS connection health. Hidden unless the feature flag is on.
- **Optimistic UI + server rollback** — if the client displays mastery delta before the server confirms, a rollback on error causes a visible flicker. Prefer pessimistic feedback for mastery; reserve optimistic UI for XP and streaks only.
- **First-contentful-paint** on a cold session — preload the session chunk on home via `<link rel="prefetch">` so the transition from launcher to live is instant.
- **Deep-study block transitions** need reliable timers that survive tab sleep; use `performance.now()` deltas instead of `setInterval` for accuracy.
