# TASK-STU-W-15: Web Enhancements (Keyboard, Command Palette, Multi-Pane, PWA, Polish)

**Priority**: MEDIUM — polish that makes the web app feel native, not a mobile port
**Effort**: 6-10 days (parallelizable into sub-deliveries)
**Phase**: 4
**Depends on**: Phase 2 complete + most of Phase 3
**Backend tasks**: none
**Status**: Not Started

---

## Goal

Add the "only on web" layer: keyboard shortcuts everywhere, command palette, multi-pane layouts, picture-in-picture window manager, scratchpad, math keyboard, draft autosave, PWA shell, global search, rich export formats. Make the web client feel intentionally designed for larger screens and keyboards instead of a mobile port.

## Spec

Full specification in [docs/student/14-web-enhancements.md](../../docs/student/14-web-enhancements.md). All 17 `STU-WEB-*` acceptance criteria form this task's checklist.

## Scope

Split into six sub-deliveries; they're independent and can land in separate PRs as time allows.

### Sub-delivery A — Keyboard Shortcuts + Cheatsheet (1-2 days)

- Global shortcut registration via a `useShortcut()` composable backed by a single keydown handler (avoids N listeners)
- Shortcut registry as a single source of truth so the cheatsheet modal reads from it automatically
- Global shortcuts: `?`, `/`, `g h`, `g s`, `g p`, `g t`, `g k`, `g l`, `g n`, `Cmd/Ctrl+K`, `Cmd/Ctrl+,`, `Esc`
- Session shortcuts (wire into STU-W-06): `1`-`5`, `Enter`, `H`, `S`, `Space`, `M`, `T`, `P`, `F`
- Tutor shortcuts: `Cmd/Ctrl+N` new thread, `Cmd/Ctrl+↑/↓` prev/next thread
- Graph shortcuts: `F`, `L`, `+`/`-` (wire into STU-W-10)
- `?` opens `<KeyboardShortcutCheatsheet>` modal grouped by area
- Shortcuts disabled in input fields except the global ones

### Sub-delivery B — Command Palette (1-2 days)

- `<CommandPalette>` opened with `Cmd/Ctrl+K`, built on a lightweight fuzzy matcher (Fuse.js or kbar)
- Sources: routes, recent sessions, recent concepts, recent tutor threads, direct actions ("Start session", "End session", "Toggle dark mode", "New tutor thread")
- Actions are declarative — a single `commandRegistry.ts` with `{ id, title, shortcut, category, run }`
- Keyboard navigation + Enter to execute
- Muscle memory: last action pinned at top for one minute

### Sub-delivery C — Multi-Pane + PiP Window Manager (2-3 days)

- Multi-pane layouts enabled at ≥ `xl` breakpoint and toggleable in settings:
  - Session + Tutor side-by-side
  - Progress list + detail
  - Knowledge graph + concept detail
  - Notifications + item detail
- `<PiPWindowManager>` singleton managing multiple floating windows (tutor, diagram, whiteboard, video explainer)
- Windows draggable, resizable, snappable (snap to corners + edges)
- Window state persisted to localStorage keyed by student ID
- Focus management: clicking a window brings it to front; `Alt+Tab`-style cycling with `Cmd/Ctrl+\``

### Sub-delivery D — Scratchpad + Math Keyboard (1-2 days)

- `<SessionScratchpad>` transparent overlay canvas opened with `P` during a session
- Tools: pen, eraser, highlighter, color picker, undo/redo
- Auto-clears on question change (configurable)
- Auto-saves per question to localStorage; restores on question return
- Export strokes to PNG
- `<MathKeyboard>` virtual keyboard powered by MathLive
- Subject-specific shortcut rows (algebra, calculus, chemistry)
- Integrated into `mathExpression` answer type in STU-W-06 and tutor composer in STU-W-08
- Greek letters, operators, fractions, roots, integrals, matrices

### Sub-delivery E — Draft Autosave + Rich Export (1-2 days)

- `useDraftAutosave(key, ref)` composable from STU-W-03 wired into every multi-line input:
  - Teach-back responses (session)
  - Tutor composer
  - Profile bio (STU-W-14)
  - Private concept annotations (STU-W-10)
- "Saved N seconds ago" indicator on each input
- PDF export: progress report (STU-W-09), tutor thread (STU-W-08), session summary (STU-W-06)
- JSON data export (GDPR) wired to existing backend endpoint
- PNG / SVG diagram export wired to STU-W-13
- iCal export for session schedule (STU-W-09)
- Shareable badge cards (STU-W-07) and session summary cards (STU-W-06)
- Parent/tutor share tokens UI (STU-W-14)

### Sub-delivery F — PWA + Global Search + LMS Embed (1 day)

- Service worker registration via `vite-plugin-pwa`
- Caches app shell + static assets + offline fallback page
- Update prompt with "Reload to update" toast
- Installable on desktop + tablet home screens
- Web Push API for notifications (opt-in, mirrors mobile FCM)
- `<GlobalSearch>` in the top app bar (`/` focuses)
- Sources: concepts, questions (student's own history), sessions, tutor threads, badges, peer solutions, help articles
- Client-side FlexSearch index for student's own data; server-side for concepts
- `?embed=1` query param mode: minimal chrome (no sidebar, no app bar), enforces CSP `frame-ancestors`
- LTI 1.3 contract slot reserved (no implementation yet — just a placeholder route)

## Out of Scope

- Electron / Tauri desktop wrapper
- Live video tutoring
- Handwriting / stylus math
- Full offline sync for live sessions (offline is reload-only in v1)
- Embedded code editor for programming challenges
- Real-time collaborative diagrams
- VR / AR

## Definition of Done

- [ ] All 17 `STU-WEB-*` acceptance criteria in [14-web-enhancements.md](../../docs/student/14-web-enhancements.md) pass
- [ ] Cheatsheet modal lists every registered shortcut automatically
- [ ] Command palette opens from any page and supports keyboard-only navigation
- [ ] Multi-pane layouts tested at 1920 and 2560 width
- [ ] PiP window manager supports at least 3 simultaneous windows without jank
- [ ] Scratchpad strokes persist per question and export to PNG
- [ ] MathLive keyboard renders valid LaTeX to the input ref
- [ ] Draft autosave survives a crash-and-reload (verified via Playwright)
- [ ] All rich export formats produce valid files
- [ ] PWA installable on Chrome, Edge, Safari
- [ ] Service worker update prompt fires when a new version ships
- [ ] Global search returns results within 50 ms for student-local data
- [ ] `?embed=1` mode renders without sidebar/app bar and enforces CSP
- [ ] Cross-cutting concerns from the bundle README apply
- [ ] Bundle budgets still respected (MathLive + command palette under 250 KB combined, lazy-loaded)

## Risks

- **Shortcut conflicts** with browser defaults — `Cmd/Ctrl+K` collides with Firefox search. Document browser-specific workarounds.
- **PiP window z-index soup** — rigorous z-index policy (constants, not magic numbers) or windows will fight modals.
- **Service worker staleness** — a bad SW can trap users on an old version. Ship with `skipWaiting: true` and a forced reload on update.
- **FlexSearch memory** — full student history can blow the index into tens of MB. Partition by time (last 90 days by default, older on demand).
- **MathLive bundle** — ~300 KB. Lazy-load only when a math input is focused.
- **LMS embed CSP** — each embedding LMS needs a per-tenant frame-ancestors allowlist. Stub the mechanism now even if there are no tenants yet.
- **Feature creep** — this task has six sub-deliveries and could easily become three months. Ship A-C first, D-F as a fast-follow if time is tight.
