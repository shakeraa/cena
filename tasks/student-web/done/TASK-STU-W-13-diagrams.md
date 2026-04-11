# TASK-STU-W-13: Diagrams & Interactives

**Priority**: HIGH — learning impact multiplier, essential for STEM subjects
**Effort**: 4-6 days
**Phase**: 3
**Depends on**: [STU-W-06](TASK-STU-W-06-learning-session-core.md)
**Backend tasks**: none (reuses existing `GET /api/content/diagrams/{id}`)
**Status**: Not Started

---

## Goal

Ship the nine diagram renderers, a standalone full-screen viewer, picture-in-picture, annotations, measurement tools, and export — all wrapped in a single `<DiagramViewer>` router that the session and tutor embed.

## Spec

Full specification in [docs/student/12-diagrams.md](../../docs/student/12-diagrams.md). All 20 `STU-DIA-*` acceptance criteria form this task's checklist.

## Scope

In scope:

- `<DiagramViewer>` root component that routes on `diagram.type` to the right renderer
- Nine type renderers (each its own component, lazy-loaded):
  1. `<StaticSvgRenderer>` — inline SVG with aria labels and alt text
  2. `<InteractiveSvgRenderer>` — click-to-reveal regions with smooth transitions
  3. `<RiveRenderer>` — `@rive-app/canvas` wrapper with play/pause/scrub/reset
  4. `<SimulationRenderer>` — canvas / three.js wrapper with proper cleanup
  5. `<GraphRenderer>` — function plotter with zoom/pan/trace (lightweight, custom)
  6. `<MoleculeRenderer>` — 3Dmol.js wrapper, orbit controls, slice
  7. `<CircuitRenderer>` — interactive circuit with live values
  8. `<PhotoRenderer>` — image viewer with zoom/pan
  9. `<ComparativeViewer>` — side-by-side + swipe-compare
- Shared `<DiagramToolbar>` with zoom, pan, fit, fullscreen, layer toggle, step-through, reset, share, download
- `<DiagramAnnotationLayer>` — pen / arrow / text, saves to backend via `POST /api/me/diagram-annotations`
- `<PictureInPictureWindow>` — draggable, resizable, snappable floating window that persists across questions
- `/diagrams/:diagramId` full-screen standalone page, deep-linkable
- Embedded mode: constrained renderer inside a question card with an "expand" button that opens PiP
- Export: SVG, PNG, PDF (the latter via backend `POST /api/content/diagrams/{id}/export`)
- Measurement tools: ruler (drag-to-measure) and protractor (for geometry diagrams)
- Keyboard: arrow keys to step, `+/-` zoom, `0` reset, `Esc` exit fullscreen
- Teacher annotation layer overlay when admin pushes one (via the teacher layer endpoint)
- Alt-text-only mode: toggle replaces visual diagram with a structured prose description for screen readers and low-vision users
- High-contrast mode strengthens borders and disables ambient effects
- `prefers-reduced-motion` disables Rive autoplay; shows a play button instead

Out of scope:

- Authoring diagrams — admin tool
- Creating new diagram types — backend + content team
- Collaborative annotation — stretch, defer

## Definition of Done

- [ ] All 20 `STU-DIA-*` acceptance criteria in [12-diagrams.md](../../docs/student/12-diagrams.md) pass
- [ ] `<DiagramViewer>` correctly routes every type to its renderer
- [ ] Each renderer has at least one happy-path Playwright test
- [ ] Rive renderer respects `prefers-reduced-motion`
- [ ] PiP window survives navigation between questions within the same session and closes on session exit
- [ ] Annotations persist across reloads and sync to backend
- [ ] Export to SVG / PNG works entirely client-side; PDF export calls the backend
- [ ] Measurement tools work on geometry diagrams with sensible snap-to-grid behavior
- [ ] Alt-text mode produces valid structured prose that a screen reader can traverse
- [ ] 3D molecule renderer loads a sample molecule in under 1 second
- [ ] Cross-cutting concerns from the bundle README apply
- [ ] Diagrams bundle lazy-loads per renderer; initial page with only `<StaticSvgRenderer>` stays under 100 KB

## Risks

- **Three.js / 3Dmol bundle size** — can exceed 1 MB each. Lazy-load on first `type === 'simulation' | 'molecule'` only; never include in the initial page.
- **Rive runtime compatibility** — confirm license + browser support matrix before committing.
- **PiP window state across routes** — persist window position and size to localStorage; never to backend.
- **Annotation conflicts** — if the backend reorders diagram elements, annotation coordinates become stale. Store annotations in normalized coordinates (0..1) relative to the diagram bounding box, not pixel positions.
- **Export licensing** — exported SVGs may embed fonts; confirm font licenses allow embedding or use text-to-path conversion.
- **Measurement accuracy** — ruler values are only meaningful if the diagram declares a scale. If no scale present, show "units" instead of "mm/cm".
- **Teacher annotation layer** — UI must clearly distinguish student annotations from teacher markup (different color + a lock icon on teacher layers).
