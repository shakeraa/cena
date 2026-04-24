# WEB-008: Knowledge Graph Canvas Renderer for Web

**Priority:** P2 — visualization of student learning progress
**Blocked by:** WEB-001 (scaffold), WEB-003 (REST API client), WEB-004 (state)
**Estimated effort:** 4 days
**Contract:** `contracts/backend/kg-access-control.md` (role-scoped graph endpoints), `contracts/frontend/state-contracts.ts` (KnowledgeGraphState)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The knowledge graph is a visual DAG (directed acyclic graph) showing concepts as nodes with mastery overlays and prerequisite edges. Students see their own graph; teachers see a student's graph; parents see their child's graph. Nodes are colored by mastery status (gray=not-started, blue=in-progress, green=mastered, orange=decaying). Edges show prerequisite relationships (locked/unlocked). The renderer uses HTML Canvas for performance with large graphs (500+ nodes).

## Subtasks

### WEB-008.1: Graph Data Layer & Layout Algorithm
**Files:**
- `src/web/src/features/knowledge-graph/hooks/useGraphData.ts` — data fetching and transformation
- `src/web/src/features/knowledge-graph/layout/force-directed.ts` — force-directed layout engine
- `src/web/src/features/knowledge-graph/layout/types.ts` — layout types

**Acceptance:**
- [ ] Uses `useStudentGraph(subjectId)` REST hook (`GET /api/student/me/graph/:subjectId`) or teacher variant (`GET /api/teacher/student/:studentId/graph/:subjectId`)
- [ ] Transforms `KnowledgeGraph` -> internal layout format: `LayoutNode` with `x`, `y`, `conceptId`, `masteryLevel`, `status`, `radius` and `LayoutEdge` with `from`, `to`, `unlocked`
- [ ] Force-directed layout with:
  - Repulsion between all nodes (Coulomb's law)
  - Attraction along edges (Hooke's law / spring)
  - Prerequisite direction bias: prerequisites above dependents (top-to-bottom flow)
  - Gravity toward center to prevent drift
  - Node radius proportional to difficulty (1-10 -> 20-50px)
- [ ] Layout stabilization: stop simulation when max velocity < threshold (0.01)
- [ ] Layout caching: store positions in KnowledgeGraphState for instant re-render
- [ ] Incremental update: `applyUpdate` from `KnowledgeGraphUpdatedPayload` moves/re-colors changed nodes without full re-layout
- [ ] `readyToLearn` nodes: pulsing border animation to attract attention
- [ ] `reviewDue` nodes: decay indicator (clock icon or dimming)

**Test:**
```typescript
import { computeLayout } from '@/features/knowledge-graph/layout/force-directed';

test('layout positions all nodes', () => {
  const nodes = [
    { conceptId: 'c1', masteryLevel: 0.0, status: 'not-started', difficulty: 1 },
    { conceptId: 'c2', masteryLevel: 0.5, status: 'in-progress', difficulty: 2 },
    { conceptId: 'c3', masteryLevel: 0.9, status: 'mastered', difficulty: 3 },
  ];
  const edges = [
    { from: 'c1', to: 'c2', unlocked: false },
    { from: 'c2', to: 'c3', unlocked: false },
  ];

  const layout = computeLayout(nodes, edges, { width: 800, height: 600 });

  expect(layout.nodes).toHaveLength(3);
  layout.nodes.forEach(n => {
    expect(n.x).toBeGreaterThan(0);
    expect(n.y).toBeGreaterThan(0);
    expect(n.x).toBeLessThan(800);
    expect(n.y).toBeLessThan(600);
  });
});

test('layout places prerequisites above dependents', () => {
  const nodes = [
    { conceptId: 'prereq', masteryLevel: 0.9, status: 'mastered', difficulty: 1 },
    { conceptId: 'dependent', masteryLevel: 0.0, status: 'not-started', difficulty: 2 },
  ];
  const edges = [{ from: 'prereq', to: 'dependent', unlocked: true }];

  const layout = computeLayout(nodes, edges, { width: 800, height: 600 });
  const prereqNode = layout.nodes.find(n => n.conceptId === 'prereq')!;
  const dependentNode = layout.nodes.find(n => n.conceptId === 'dependent')!;

  expect(prereqNode.y).toBeLessThan(dependentNode.y); // Higher on screen
});

test('layout handles 500 nodes in < 2 seconds', () => {
  const nodes = Array.from({ length: 500 }, (_, i) => ({
    conceptId: `c${i}`, masteryLevel: Math.random(), status: 'in-progress' as const, difficulty: (i % 5) + 1,
  }));
  const edges = Array.from({ length: 499 }, (_, i) => ({
    from: `c${i}`, to: `c${i + 1}`, unlocked: true,
  }));

  const start = performance.now();
  const layout = computeLayout(nodes, edges, { width: 1200, height: 800 });
  const elapsed = performance.now() - start;

  expect(layout.nodes).toHaveLength(500);
  expect(elapsed).toBeLessThan(2000);
});
```

---

### WEB-008.2: Canvas Renderer
**Files:**
- `src/web/src/features/knowledge-graph/KnowledgeGraphCanvas.tsx` — Canvas component
- `src/web/src/features/knowledge-graph/render/node-renderer.ts` — node drawing
- `src/web/src/features/knowledge-graph/render/edge-renderer.ts` — edge drawing
- `src/web/src/features/knowledge-graph/render/colors.ts` — mastery status color map

**Acceptance:**
- [ ] HTML Canvas (`<canvas>`) with `2x` DPI scaling for retina displays
- [ ] Node rendering: circle with `radius` from layout, filled by mastery status color:
  - `not-started`: `#9E9E9E` (gray)
  - `in-progress`: `#2196F3` (blue), opacity proportional to mastery (0.3-1.0)
  - `mastered`: `#4CAF50` (green)
  - `decaying`: `#FF9800` (orange)
- [ ] Edge rendering: bezier curve from `from` to `to` node, color by unlock status:
  - `unlocked`: solid gray line
  - `locked`: dashed red line
- [ ] Node labels: concept name below node, truncated to 15 chars
- [ ] Selected node: highlighted ring, info panel shown
- [ ] Pan: click-and-drag on background
- [ ] Zoom: mouse wheel / pinch, range [0.3x, 3.0x]
- [ ] Hit testing: click on node selects it (distance check on canvas coordinates)
- [ ] `requestAnimationFrame` for smooth 60fps rendering
- [ ] Responsive: canvas resizes with container via ResizeObserver

**Test:**
```typescript
import { render, screen, fireEvent } from '@testing-library/react';
import { KnowledgeGraphCanvas } from '@/features/knowledge-graph/KnowledgeGraphCanvas';

test('renders canvas element', () => {
  render(<KnowledgeGraphCanvas nodes={mockNodes} edges={mockEdges} />);
  expect(screen.getByRole('img', { name: /knowledge graph/i })).toBeInTheDocument();
});

test('clicking node selects it', async () => {
  const onSelect = vi.fn();
  render(<KnowledgeGraphCanvas nodes={mockNodes} edges={mockEdges} onSelectConcept={onSelect} />);

  const canvas = screen.getByRole('img');
  // Simulate click at node position
  fireEvent.click(canvas, { clientX: mockNodes[0].x, clientY: mockNodes[0].y });

  expect(onSelect).toHaveBeenCalledWith('c1');
});

test('zoom changes scale', () => {
  const { container } = render(<KnowledgeGraphCanvas nodes={mockNodes} edges={mockEdges} />);
  const canvas = container.querySelector('canvas')!;

  fireEvent.wheel(canvas, { deltaY: -100 }); // Zoom in
  // Canvas should re-render at higher zoom (verified by internal state)
});

test('mastery status maps to correct color', () => {
  expect(masteryColor('not-started')).toBe('#9E9E9E');
  expect(masteryColor('in-progress')).toBe('#2196F3');
  expect(masteryColor('mastered')).toBe('#4CAF50');
  expect(masteryColor('decaying')).toBe('#FF9800');
});
```

---

### WEB-008.3: Concept Detail Panel & Real-Time Updates
**Files:**
- `src/web/src/features/knowledge-graph/components/ConceptDetailPanel.tsx` — side panel
- `src/web/src/features/knowledge-graph/hooks/useGraphSubscription.ts` — SignalR subscription

**Acceptance:**
- [ ] `ConceptDetailPanel`: shown when a node is selected, displays:
  - Concept name (from `ConceptNode.conceptName`)
  - Mastery level (percentage bar, 0-100%)
  - Predicted recall (percentage, from `predictedRecall`)
  - Status badge (`MasteryStatus`)
  - Attempts count (`attemptsCount`)
  - Last attempt date (`lastAttemptAt`)
  - Active methodology (`activeMethodology`)
  - Half-life hours (`halfLifeHours`)
  - Next review due (`nextReviewDue`)
  - Prerequisites (list from `prerequisiteIds` with lock/unlock icons)
  - Dependents (list from `dependentIds`)
- [ ] "Start Learning" button if concept is in `readyToLearn`
- [ ] "Review Now" button if concept is in `reviewDue`
- [ ] Real-time updates: subscribes to `KnowledgeGraphUpdated` SignalR events
- [ ] On update: node color/size animates to new mastery level
- [ ] `MasteryUpdated` event: node flashes green with celebratory animation
- [ ] Graph auto-zooms to updated node on mastery event

**Test:**
```typescript
import { render, screen } from '@testing-library/react';
import { ConceptDetailPanel } from '@/features/knowledge-graph/components/ConceptDetailPanel';

test('renders concept details', () => {
  const node: ConceptNode = {
    conceptId: 'c1', conceptName: 'Addition', topic: 'arithmetic',
    difficulty: 2, masteryLevel: 0.72, predictedRecall: 0.85,
    status: 'in-progress', attemptsCount: 15,
    lastAttemptAt: '2026-03-25T14:00:00Z', lastMasteredAt: null,
    activeMethodology: 'socratic', halfLifeHours: null,
    nextReviewDue: null, prerequisiteIds: [], dependentIds: ['c2', 'c3'],
  };

  render(<ConceptDetailPanel node={node} />);

  expect(screen.getByText('Addition')).toBeInTheDocument();
  expect(screen.getByText('72%')).toBeInTheDocument(); // Mastery
  expect(screen.getByText('85%')).toBeInTheDocument(); // Recall
  expect(screen.getByText('15 attempts')).toBeInTheDocument();
  expect(screen.getByText('socratic')).toBeInTheDocument();
});

test('shows Start Learning button for ready concepts', () => {
  render(<ConceptDetailPanel node={readyNode} isReadyToLearn={true} />);
  expect(screen.getByRole('button', { name: /start learning/i })).toBeInTheDocument();
});

test('real-time mastery update highlights node', async () => {
  const { rerender } = render(
    <KnowledgeGraphCanvas nodes={mockNodes} edges={mockEdges} />
  );

  // Simulate mastery update
  const updatedNodes = mockNodes.map(n =>
    n.conceptId === 'c1' ? { ...n, masteryLevel: 0.92, status: 'mastered' as const } : n
  );
  rerender(<KnowledgeGraphCanvas nodes={updatedNodes} edges={mockEdges} />);

  // Node c1 should have mastered color after re-render
  // (Visual verification via snapshot or canvas pixel check)
});
```

**Edge cases:**
- Graph with 0 nodes (new student) -> show "Start your first lesson!" empty state
- Graph with disconnected components -> layout handles multiple clusters
- Very long concept name -> truncate in node label, show full name in detail panel
- Zoom out too far -> clamp at 0.3x
- Touch devices -> support touch pan and pinch-zoom

---

## Integration Test

```typescript
test('knowledge graph full interaction', async () => {
  render(<KnowledgeGraphPage subjectId="math" />, { wrapper: createStudentWrapper() });

  // 1. Graph loads
  await screen.findByRole('img', { name: /knowledge graph/i });

  // 2. Click a node
  fireEvent.click(screen.getByRole('img'), { clientX: 400, clientY: 300 });
  await screen.findByText('Addition'); // Detail panel opens

  // 3. Check mastery display
  expect(screen.getByText(/72%/)).toBeInTheDocument();

  // 4. Real-time update
  simulateSignalREvent('KnowledgeGraphUpdated', {
    updatedNodes: [
      { conceptId: 'c1', conceptName: 'Addition', masteryLevel: 0.92,
        predictedRecall: 0.95, status: 'mastered' },
    ],
    updatedEdges: [{ fromConceptId: 'c1', toConceptId: 'c2', unlocked: true }],
  });

  await screen.findByText(/92%/); // Mastery updated
});
```

## Rollback Criteria
- If Canvas performance is poor on low-end devices: switch to SVG with D3.js (simpler but slower at 500+ nodes)
- If force-directed layout is too slow: pre-compute layout on server, send positions with graph data
- If pan/zoom UX is poor: use a library like `react-zoom-pan-pinch`

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] `npm test -- --filter knowledge-graph` -> 0 failures
- [ ] Force-directed layout handles 500 nodes in < 2 seconds
- [ ] Canvas renders at 60fps with smooth pan/zoom
- [ ] Mastery status colors match specification
- [ ] Node selection shows detail panel with all ConceptNode fields
- [ ] Real-time updates animate smoothly
- [ ] Responsive canvas sizing
- [ ] PR reviewed by frontend lead
