# 12 — Diagrams & Interactives

## Overview

Diagrams are not illustrations — they are **interactive learning surfaces**. A student can probe, manipulate, and reveal layers of a diagram to build intuition. The web version unlocks larger canvases and richer interactions than mobile.

## Mobile Parity

- [diagram_viewer.dart](../../src/mobile/lib/features/diagrams/diagram_viewer.dart)
- [rive_diagram_viewer.dart](../../src/mobile/lib/features/diagrams/rive_diagram_viewer.dart)
- [comparative_diagram_viewer.dart](../../src/mobile/lib/features/diagrams/comparative_diagram_viewer.dart)
- [challenge_card_widget.dart](../../src/mobile/lib/features/diagrams/challenge_card_widget.dart)
- [models/diagram_models.dart](../../src/mobile/lib/features/diagrams/models/diagram_models.dart)

## Diagram Types

| Type | Renderer | Example |
|------|----------|---------|
| `static-svg` | Plain SVG | Labelled diagrams |
| `interactive-svg` | SVG + JS overlay | Click-to-reveal regions |
| `rive` | @rive-app/canvas | Animated explanations |
| `simulation` | HTML5 Canvas / three.js | Physics, chemistry sims |
| `graph` | Function grapher (Desmos-like) | Math |
| `molecule` | 3Dmol.js | Chemistry 3D |
| `circuit` | Custom SVG editor | Electronics |
| `photo` | Image viewer | Real-world reference |
| `comparative` | Side-by-side viewer | Before/after, abstraction levels |

## Pages

### `/diagrams/:diagramId`

Full-screen standalone viewer. Deep-linkable. Used when the student wants to focus on a diagram outside of a session.

Features:
- Zoom / pan
- Layer toggle (show/hide labels, annotations, construction lines)
- Step-through for multi-step animations
- Reset
- Fullscreen
- Share link with current state encoded in URL
- Download as SVG / PNG / PDF

### Embedded in Session

Diagrams inside a question card use the same renderer but are constrained to a smaller viewport. The student can tap "expand" to open a floating picture-in-picture window (web-only) that persists across questions if the same diagram is referenced.

## Components

| Component | Purpose |
|-----------|---------|
| `<DiagramViewer>` | Router that picks the right renderer based on `diagram.type` |
| `<StaticSvgRenderer>` | Inline SVG with optional aria labels |
| `<InteractiveSvgRenderer>` | SVG + click regions with reveal animations |
| `<RiveRenderer>` | Rive animation with play/pause/scrub |
| `<SimulationRenderer>` | Canvas / three.js simulation wrapper |
| `<GraphRenderer>` | Function grapher component |
| `<MoleculeRenderer>` | 3Dmol.js wrapper for 3D molecular view |
| `<CircuitRenderer>` | Circuit diagram with interactive components |
| `<ComparativeViewer>` | Side-by-side or swipe-compare mode |
| `<DiagramAnnotationLayer>` | Private annotation overlay (web-only) |
| `<PictureInPictureWindow>` | Floating window container (web-only) |

## Interactions

- Keyboard: arrow keys to step through; `+` / `-` to zoom; `0` to reset; `Esc` to exit fullscreen.
- Mouse: scroll to zoom, drag to pan, click to reveal, hover for tooltips.
- Touch (tablet): pinch, two-finger pan, long-press for context menu.

## Accessibility

- All meaningful regions have `aria-label`.
- Alternative text description available as a toggle (replaces visual diagram with structured prose).
- Keyboard navigation between interactive regions.
- `prefers-reduced-motion` disables Rive animation autoplay.
- High-contrast mode strengthens borders and disables ambient effects.

## Web-Specific Enhancements

- **Picture-in-picture mode** — detach a diagram into a floating, draggable window.
- **Private annotations** — draw on the diagram (pen, arrow, text) and save per-student.
- **Comparison table** — select up to 4 diagrams and show them in a grid for cross-reference.
- **Export formats** — SVG, PNG, PDF, with optional student annotations baked in.
- **Embed in tutor conversation** — paste a diagram link into the AI tutor and the tutor can reference specific regions.
- **Teacher markup** — teachers can push annotation layers ("pay attention here") that students see on top of the diagram.
- **Full 3D manipulation** — rotate, zoom, slice 3D molecules and solids with orbit controls.
- **Measurement tools** — ruler / protractor overlay for geometry diagrams.

## Acceptance Criteria

- [ ] `STU-DIA-001` — `<DiagramViewer>` correctly routes to the right renderer based on `diagram.type`.
- [ ] `STU-DIA-002` — Static SVG renders with aria labels and alt text.
- [ ] `STU-DIA-003` — Interactive SVG supports click-to-reveal with smooth animation.
- [ ] `STU-DIA-004` — Rive renderer supports play, pause, scrub, reset.
- [ ] `STU-DIA-005` — Simulation renderer hosts three.js / canvas with proper cleanup.
- [ ] `STU-DIA-006` — Graph renderer plots functions with zoom, pan, trace.
- [ ] `STU-DIA-007` — Molecule renderer shows 3D model with orbit controls.
- [ ] `STU-DIA-008` — Circuit renderer shows interactive components with live values.
- [ ] `STU-DIA-009` — Comparative viewer supports side-by-side and swipe modes.
- [ ] `STU-DIA-010` — `/diagrams/:diagramId` deep-linkable and loads the correct diagram.
- [ ] `STU-DIA-011` — Fullscreen mode works across all renderers.
- [ ] `STU-DIA-012` — Export to SVG, PNG, PDF works and includes annotations when present.
- [ ] `STU-DIA-013` — Picture-in-picture window can be detached, moved, resized, and persists across questions.
- [ ] `STU-DIA-014` — Private annotations save per-student via backend.
- [ ] `STU-DIA-015` — Teacher annotation layers are rendered when pushed from the admin side.
- [ ] `STU-DIA-016` — Measurement tools (ruler, protractor) available on geometry diagrams.
- [ ] `STU-DIA-017` — All interactions are keyboard-reachable.
- [ ] `STU-DIA-018` — `prefers-reduced-motion` disables Rive autoplay.
- [ ] `STU-DIA-019` — High-contrast mode strengthens borders.
- [ ] `STU-DIA-020` — Diagram viewer passes accessibility audit.

## Backend Dependencies

- `GET /api/content/diagrams/{id}` — exists (ContentEndpoints.cs)
- `POST /api/me/diagram-annotations` — new
- `GET /api/me/diagram-annotations?diagramId=` — new
- `GET /api/content/diagrams/{id}/teacher-layer` — new (optional)
- `POST /api/content/diagrams/{id}/export` — new (server-side PDF rendering)
