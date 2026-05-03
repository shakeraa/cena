# TASK-STU-W-10: Knowledge Graph & Skill Tree

**Priority**: HIGH — biggest "wow" moment for web vs mobile
**Effort**: 4-6 days
**Phase**: 3
**Depends on**: [STU-W-04](TASK-STU-W-04-auth-onboarding.md)
**Backend tasks**: [STB-08](../student-backend/TASK-STB-08-knowledge-graph.md)
**Status**: Not Started

---

## Goal

Ship a full-viewport knowledge graph explorer with multi-layout rendering, search, filters, pathfinding, concept detail drawer, and a skill-tree alternative view. Large-screen interactivity is the whole point — this is where the web client earns its seat.

## Spec

Full specification in [docs/student/09-knowledge-graph.md](../../docs/student/09-knowledge-graph.md). All 23 `STU-KG-*` acceptance criteria form this task's checklist.

## Scope

In scope:

- `/knowledge-graph` full-viewport page with Cytoscape.js
- Cytoscape layouts: `dagre`, `cose-bilkent`, `concentric`, `breadthfirst` — switchable via `L` shortcut and a toolbar dropdown
- Node styles tied to mastery color tokens from STU-W-01; edge types for prerequisite (solid) / related (dashed) / path (highlighted)
- Left sidebar: subjects list, legend, filters (subject, topic, mastery range, difficulty, unlocked-only)
- Right drawer: concept detail (opens on node click)
- Toolbar: search (fuzzy), layout, export (SVG / PNG), fit view, fullscreen
- Interaction model:
  - Click node → open detail drawer
  - Double-click → zoom to node
  - Shift-click two nodes → compute shortest prerequisite path via `POST /api/knowledge/path` (STB-08)
  - Right-click → context menu (Start session, Pin, Mark unlocked, Hide, Ask tutor)
  - Scroll → zoom
  - Space+drag → pan
  - `F` → fit
  - `L` → cycle layouts
  - `/` → focus search
- Search with fuzzy matching (FlexSearch) across concept name, description, aliases; auto-pans to top result
- Minimap overlay when graph has > 100 nodes
- Skill tree alternative view (`<SkillTreeWidget>`) — grouped by subject lane, locked concepts grayed, unlock animation on prerequisite completion
- Toggle between graph and skill tree modes
- Node pinning persisted to user preferences via `/api/me/preferences/home-layout`-style endpoint
- Private annotations on concepts (`POST /api/me/concept-annotations`) — sticky-note overlay
- Diff view: "what did I unlock this week" — highlights recently unlocked nodes with a pulse animation
- Share link encodes current view (node, zoom, layout) as URL params
- Export SVG / PNG of current view via Cytoscape's export API
- `/knowledge-graph/concept/:id` concept detail page (standalone) with prerequisites, successors, related, mastery history, example questions, linked diagrams, peer discussion stub, tutor CTA, "practice this concept" CTA

Out of scope:

- Teacher-mode student comparison — out of scope for student app
- Fullscreen WebGL rendering for 5000+ nodes — defer; 500 is the v1 target
- Automatic graph layout optimization for specific subjects — use Cytoscape defaults

## Definition of Done

- [ ] All 23 `STU-KG-*` acceptance criteria in [09-knowledge-graph.md](../../docs/student/09-knowledge-graph.md) pass
- [ ] 500-node sample graph renders in under 500 ms
- [ ] All four layouts produce readable graphs on 500-node data
- [ ] Search returns results within 50 ms against a client-side index
- [ ] Shortest-path computation round-trips in under 1 second
- [ ] Skill tree view renders the same data as a tree and maintains selection state when toggling
- [ ] Node pinning, annotations, and diff view persist correctly
- [ ] Share link reopens the exact view on another device
- [ ] Concept detail page is deep-linkable and loads directly without a graph fetch
- [ ] Every graph interaction is reachable via keyboard (including right-click menu equivalent)
- [ ] Playwright covers: graph load, layout switch, node select → detail, path between two nodes, skill tree toggle, concept detail deep link
- [ ] Cross-cutting concerns from the bundle README apply
- [ ] Page bundle with Cytoscape stays under 350 KB gzipped (use dynamic imports)

## Risks

- **Cytoscape bundle size** — Cytoscape + extensions can be 400 KB. Aggressively tree-shake and lazy-load only the plugins in use.
- **Large graph performance** — at 1000+ nodes some layouts become unusable. Cap the initial fetch at 500 nodes per subject and fetch others on demand.
- **Accessibility** — graph visualizations are hard to make accessible. Provide a parallel list view for screen readers triggered by a "show as list" toggle.
- **RTL** — Cytoscape does not respect `dir="rtl"`. Labels must flip manually where they include Arabic / Hebrew text. Test with a mixed-language graph.
- **Search index size** — a full concept catalog can be 10k items. Lazy-load the search index (~500 KB) only when the user focuses the search box.
- **Path API latency** — if the backend path computation is slow, show a loading state in the highlight; never block the graph.
