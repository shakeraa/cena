# 14 — Web Enhancements (Features Not on Mobile)

## Overview

This doc is the canonical list of everything the web app does that the mobile app does **not**, and why each is worth doing. Treat each item as a candidate task bundle.

## Principles

- **Pull in, don't bolt on.** Every enhancement should feel native to the Vuexy theme and the overall product tone.
- **Respect mobile parity.** If a web-only feature becomes essential, it should eventually land on mobile too; document the gap.
- **Don't break the session flow.** Immersive mode is sacred — web enhancements should enrich other surfaces first.
- **Keyboard-first.** Every enhancement must be usable without a mouse.

---

## Keyboard Shortcuts

Global:
- `?` — open shortcut cheatsheet
- `/` — focus search
- `g h` — go home
- `g s` — go session launcher
- `g p` — go progress
- `g t` — go tutor
- `g k` — go knowledge graph
- `g l` — go leaderboard
- `g n` — go notifications
- `Cmd/Ctrl + k` — command palette
- `Cmd/Ctrl + ,` — open settings
- `Esc` — close modal / drawer / menu

Session (see [05-learning-session](05-learning-session.md)):
- `1`–`5` — select multiple-choice option
- `Enter` — submit / next
- `H` — hint
- `S` — skip
- `Space` — pause
- `M` — mute
- `T` — tutor side panel
- `P` — scratchpad
- `F` — fullscreen

Tutor:
- `Enter` — send
- `Shift + Enter` — newline
- `Cmd/Ctrl + N` — new thread
- `Cmd/Ctrl + ↑/↓` — previous / next thread

Knowledge graph:
- `F` — fit view
- `L` — cycle layouts
- `+` / `-` — zoom
- `Esc` — exit fullscreen

---

## Command Palette

Press `Cmd/Ctrl + K` anywhere → a fuzzy-searchable palette with:
- All routes
- Recent sessions
- Recent concepts
- Recent tutor threads
- Direct actions ("Start session", "End session", "New tutor thread", "Toggle dark mode")

Component: `<CommandPalette>`.

---

## Multi-Pane Layouts (`xl` Breakpoint and Above)

At ≥ 1904 px:

- **Session + Tutor side-by-side** — question on the left (70%), tutor chat on the right (30%).
- **Progress + Detail** — list on the left, detail panel on the right.
- **Knowledge Graph + Concept Detail** — graph on the left, concept card on the right.
- **Notifications + Item Detail** — inbox on the left, item on the right.

Layout is responsive and can be toggled off by preference.

---

## Picture-in-Picture Windows

Long-lived floating windows that persist across navigation:

- **Tutor window** — continue a conversation while browsing.
- **Diagram window** — keep a reference diagram visible.
- **Video explainer window** — for concept video walk-throughs (if available).
- **Whiteboard window** — persistent scratch canvas.

Implementation: `<PiPWindowManager>` with draggable, resizable, snappable positioning. State in `localStorage`.

---

## Scratchpad

Press `P` during a session to open a transparent overlay canvas for quick notes or math scribbles.

- Pen / eraser / highlighter tools
- Color picker
- Undo / redo
- Auto-clears on question change (configurable)
- Auto-saves per question to localStorage
- Export strokes to PNG

Component: `<SessionScratchpad>`.

---

## Math Keyboard

For `mathExpression` answer type and math inputs in tutor: a floating virtual math keyboard with:
- Common operators, exponents, roots, fractions
- Greek letters
- Trig / log / calculus
- Symbols tab
- Custom shortcut row per subject

Library: `mathlive` or `mathquill` (MIT licensed).

Component: `<MathKeyboard>`.

---

## Draft Autosave

Every multi-line or long-running text input (teach-back, tutor composer, bio) auto-saves to `localStorage` every 5 s and restores on page reload or tab crash. Indicated by a subtle "saved" timestamp next to the field.

Composable: `useDraftAutosave(key, ref)`.

---

## Export & Share

- **PDF progress report** — generated from `/progress` data.
- **JSON data export** — GDPR-compliant data download.
- **PNG / SVG diagram export** — with annotations baked in.
- **Shareable badge cards** — Open Graph images for social sharing.
- **Shareable session summary** — image card.
- **Parent / tutor share tokens** — time-bound view-only links.
- **iCal export** — session schedule as `.ics`.

---

## LMS Embedding

- `?embed=1` query param enables a minimal layout (no sidebar, no app bar).
- CSP `frame-ancestors` enforced per-tenant.
- LTI 1.3 integration reserved (contract slot for future release).
- Student launched from an LMS lands directly on the relevant session / challenge.

---

## PWA (Progressive Web App)

- Installable on desktop and tablet home screens.
- Service worker caches shell + static assets for fast reload.
- Offline fallback page for session replay (not live sessions).
- Push notification support via Web Push API (alternative to FCM for mobile).
- Update notification with "Reload to update" toast.

Stretch: full offline sync is out of scope for v1.

---

## Accessibility Beyond AA

- Full keyboard operability (tested with keyboard-only QA pass).
- Screen-reader pass on all major flows (NVDA + VoiceOver).
- Color-blind safe mastery palette.
- Font size override up to 200%.
- Focus ring strengthened to 2 px.
- Skip links to main content.
- Aria live regions for XP, streak, timer, feedback.

---

## Rich Search

Top-bar global search (`/`):
- Concepts
- Questions (from the student's history)
- Sessions (with filters)
- Tutor threads
- Badges
- Peer solutions
- Help articles

Powered by client-side index (FlexSearch) for the student's own data + server-side search for concepts.

---

## Command Line Mode (Power Users)

A hidden `/cli` route exposes a keyboard-driven command-line interface for power users, developers, and teachers. Commands like:
- `start session math 30`
- `review due`
- `mastery algebra`
- `tutor ask "what is a derivative"`
- `export report week`

Out-of-scope for v1 but designed so a Pinia action layer exists for every capability, making CLI trivial to add later.

---

## Classroom-Only Surfaces

Available when the student is enrolled in a class:
- **Teacher announcements** — pinned in class feed
- **Teacher-assigned sessions** — appear in home "assigned" slot
- **Class-specific leaderboards**
- **Co-op study rooms**
- **Classroom challenges** — synced with other students

---

## Stretch Goals (Deferred)

Listed here so they are not lost; explicitly not part of v1:

- Electron / Tauri desktop wrapper
- Live video tutoring rooms
- Handwriting / stylus math input
- Full offline sync
- Embedded code editor for programming subjects
- Real-time collaborative diagrams
- VR / AR lessons

---

## Acceptance Criteria (Summary)

- [ ] `STU-WEB-001` — All global keyboard shortcuts implemented and tested.
- [ ] `STU-WEB-002` — Command palette opens with `Cmd/Ctrl+K` and supports fuzzy search across all listed categories.
- [ ] `STU-WEB-003` — Shortcut cheatsheet modal with `?` key.
- [ ] `STU-WEB-004` — Multi-pane layouts enabled at ≥ 1904 px and toggleable in preferences.
- [ ] `STU-WEB-005` — PiP window manager supports tutor, diagram, video, whiteboard.
- [ ] `STU-WEB-006` — Scratchpad overlay with pen tools, undo, auto-save, export.
- [ ] `STU-WEB-007` — Math keyboard available on math inputs.
- [ ] `STU-WEB-008` — Draft autosave composable wired into all multi-line inputs.
- [ ] `STU-WEB-009` — PDF / JSON / PNG export functions implemented.
- [ ] `STU-WEB-010` — LMS embed mode (`?embed=1`) renders minimal layout.
- [ ] `STU-WEB-011` — PWA installable, service worker caches shell, update prompt works.
- [ ] `STU-WEB-012` — Web push notifications opt-in.
- [ ] `STU-WEB-013` — Accessibility audit passes AA and keyboard-only pass.
- [ ] `STU-WEB-014` — Rich global search with client + server-side index.
- [ ] `STU-WEB-015` — Parent/tutor share tokens scope read access correctly.
- [ ] `STU-WEB-016` — Calendar export (`.ics`) for session schedule.
- [ ] `STU-WEB-017` — Classroom-only surfaces gated by enrollment.
