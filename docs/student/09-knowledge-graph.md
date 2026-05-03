# 09 — Knowledge Graph & Skill Tree

## Overview

The knowledge graph is the map of **what there is to learn** and where the student currently stands within it. It complements mastery (which is the "how well do I know X") with a topological view (which is "how does X connect to everything else").

This is the single biggest win for web over mobile — a large screen is essential for a graph browsing experience.

## Mobile Parity

- [knowledge_graph_renderer.dart](../../src/mobile/lib/features/knowledge_graph/knowledge_graph_renderer.dart)
- [knowledge_graph_screen.dart](../../src/mobile/lib/features/knowledge_graph/knowledge_graph_screen.dart)
- [skill_tree_widget.dart](../../src/mobile/lib/features/knowledge_graph/skill_tree_widget.dart)

Mobile renders a small, touch-constrained graph. Web takes it further: full-viewport, mouse + keyboard, zoom, pan, search, filter, path-finding.

## Pages

### `/knowledge-graph`

Full-bleed graph viewer.

```
┌─────────────────────────────────────────────────────────────────┐
│  [Filters] [Search]                          [Layout] [Export]  │ ← toolbar
├───────────────┬─────────────────────────────────────────────────┤
│               │                                                 │
│  Sidebar      │                                                 │
│  ───────      │                                                 │
│  Subjects     │               [GRAPH CANVAS]                    │
│  ─ Math       │                                                 │
│  ─ Science    │         ● ── ●                                  │
│  ─ Language   │        /      \                                 │
│               │       ●        ● ── ●                           │
│  Legend       │        \      /                                 │
│  ─ Mastered   │         ● ── ●                                  │
│  ─ Proficient │                                                 │
│  ─ Learning   │                                                 │
│  ─ Locked     │                                                 │
│               │                                                 │
└───────────────┴─────────────────────────────────────────────────┘
```

### `/knowledge-graph/concept/:id`

Concept detail page with:
- Name, description, aliases
- Prerequisites (with mastery indicators)
- Successors (concepts that depend on this)
- Related concepts (not prerequisites, but similar)
- Mastery chart for this concept
- Example questions (3, with "try this" CTA)
- Linked diagrams
- Discussion from peers (see social)
- "Ask the tutor about this" CTA
- "Start a session on this concept" CTA

## Rendering

- **Library**: Cytoscape.js (MIT license, handles 5k+ nodes)
- **Layouts**: 
  - `dagre` — hierarchical top-down (default)
  - `cose-bilkent` — force-directed
  - `concentric` — concept-of-interest at center
  - `breadthfirst` — BFS from root
- **Node styles** per mastery level (match Mastery color tokens).
- **Edge types**: prerequisite (solid arrow), related (dashed), path (highlighted blue on route).

## Interaction

- Click node → open concept detail in right drawer.
- Double-click → zoom to node.
- Shift-click two nodes → compute shortest prerequisite path and highlight.
- Right-click → context menu (Start session · Pin · Mark unlocked · Hide · Ask tutor).
- Scroll → zoom.
- Space + drag → pan.
- `F` → fit view.
- `L` → cycle layouts.
- `/` → focus search box.

## Filters

Left sidebar:
- Subject / topic
- Mastery level range
- Difficulty range
- "Only unlocked" (hide prerequisites not yet met)
- "Show path to goal" — pick a target concept, highlight the learning path

## Search

Full-text search with fuzzy matching across concept name, description, and aliases. Results list + auto-pan to top match.

## Skill Tree View

Alternative visualization for the same data — tree layout (like a video game skill tree) instead of a graph. Better for novice students who find a graph overwhelming. Toggle between graph / tree modes.

Skill tree features:
- Concepts grouped by subject lane
- Prerequisites enforce vertical ordering
- Locked concepts shown grayed with a lock icon
- Unlock animation when a prerequisite is completed

## Graph Data Model

```ts
interface ConceptNode {
  id: string
  subject: string
  topic: string
  name: string
  aliases: string[]
  description: string
  difficulty: number          // 0–1
  mastery: number | null      // 0–1, null if unattempted
  unlocked: boolean
  prerequisites: string[]     // ids
  successors: string[]        // ids
  related: string[]           // ids
}
```

Fetched from `GET /api/content/concepts?subject=&depth=`. Cached in Pinia.

## Web-Specific Enhancements

- **Shortest path to goal** — pick a target concept, server computes optimal learning path weighted by mastery deficits, client highlights it and offers "Start path".
- **Skill tree view toggle** — graph vs tree mode.
- **Minimap** — small overview in the corner for navigation on large graphs.
- **Node pinning** — pin important concepts so they stay in view.
- **Annotations** — add private sticky-note annotations on concepts.
- **Diff view** — "what did I unlock this week" — shows recently-unlocked nodes with a pulse animation.
- **Share link** — highlight a specific node / path and share via URL.
- **Export SVG / PNG** — export the current view as an image.
- **Fullscreen mode** — `F11`-style fullscreen for immersive exploration.
- **Compare** — overlay two students' graphs (classroom use-case, teacher-only by default).

## Acceptance Criteria

- [ ] `STU-KG-001` — `/knowledge-graph` renders with Cytoscape.js full-viewport.
- [ ] `STU-KG-002` — Graph data loaded from `/api/content/concepts` with mastery from `/api/analytics/mastery`.
- [ ] `STU-KG-003` — Four layout algorithms available and switchable.
- [ ] `STU-KG-004` — Node colors match mastery tokens; legend rendered in sidebar.
- [ ] `STU-KG-005` — Click opens concept detail drawer; double-click zooms.
- [ ] `STU-KG-006` — Shift-click two nodes highlights shortest prerequisite path.
- [ ] `STU-KG-007` — Right-click context menu with Start session, Pin, Mark unlocked, Hide, Ask tutor.
- [ ] `STU-KG-008` — Search box with fuzzy matching and auto-pan to top match.
- [ ] `STU-KG-009` — Filter sidebar: subject, topic, mastery range, difficulty, unlocked-only.
- [ ] `STU-KG-010` — "Show path to goal" picks a target and highlights the learning path.
- [ ] `STU-KG-011` — Concept detail page `/knowledge-graph/concept/:id` exists with all listed sections.
- [ ] `STU-KG-012` — Skill tree alternative view implemented and toggleable.
- [ ] `STU-KG-013` — Skill tree shows locked concepts with lock icon and unlock animation on reinforcement.
- [ ] `STU-KG-014` — Minimap overlay for navigation on graphs with > 100 nodes.
- [ ] `STU-KG-015` — Node pinning persists to user preferences.
- [ ] `STU-KG-016` — Private annotations on concepts, synced to backend.
- [ ] `STU-KG-017` — Diff view highlights concepts unlocked this week.
- [ ] `STU-KG-018` — Share link encodes current view (node, zoom, layout).
- [ ] `STU-KG-019` — Export current view as SVG or PNG.
- [ ] `STU-KG-020` — Fullscreen mode supported.
- [ ] `STU-KG-021` — Graph renders a 500-node sample in < 500 ms.
- [ ] `STU-KG-022` — All interactions are keyboard-reachable.
- [ ] `STU-KG-023` — Empty-state shown for students with no mastery yet.

## Backend Dependencies

- `GET /api/content/concepts?subject=&depth=` — new or extend existing
- `GET /api/analytics/mastery` — exists
- `GET /api/content/concepts/{id}` — new
- `POST /api/me/concept-annotations` — new
- `GET /api/me/concept-annotations?conceptId=` — new
- `POST /api/knowledge/path?from=&to=` — new (shortest learning path)
