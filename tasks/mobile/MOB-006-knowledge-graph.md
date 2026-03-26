# MOB-006: Knowledge Graph Widget (Hero Feature)

**Priority:** P1 — this IS the product
**Blocked by:** MOB-005 (Riverpod state), MOB-002 (domain models)
**Estimated effort:** 8 days (most complex Flutter widget)
**Contract:** `contracts/mobile/lib/features/knowledge_graph/knowledge_graph_widget.dart`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The knowledge graph is Cena's differentiator — students SEE their mastery grow. It must render 2000 nodes at 60fps on a mid-range Android device (Snapdragon 6xx, 4GB RAM). The initial contracts used a Stack of widgets — the architect review flagged this as CRITICAL. We must use a single CustomPainter with viewport culling.

## Architecture Decision
**NOT this:** 2000 `ConceptNodeWidget` instances in a `Stack` (review said: kills 60fps)
**THIS:** Single `CustomPainter` + quadtree spatial index. Only the selected node becomes a real widget.

## Subtasks

### MOB-006.1: Graph Data Model + Layout Engine Interface
**Files:**
- `lib/features/knowledge_graph/models/graph_layout.dart`
- `lib/features/knowledge_graph/models/spatial_index.dart`

**Acceptance:**
- [ ] `GraphLayout` class: takes `List<ConceptNode>` + `List<PrerequisiteEdge>` → produces `Map<String, Offset>` (node positions)
- [ ] `SpatialIndex` (quadtree): insert nodes by position, query visible rect → returns only nodes in viewport
- [ ] Layout algorithm: force-directed (Fruchterman-Reingold) with prerequisite edges as springs
- [ ] Layout runs in isolate (not UI thread): `compute()` returns `Future<Map<String, Offset>>`
- [ ] Layout caches: only recompute when graph data changes (not on pan/zoom)
- [ ] Support 2000 nodes: layout completes in < 2 seconds on isolate

**Test:**
```dart
test('quadtree returns only visible nodes', () {
  final index = SpatialIndex();
  for (int i = 0; i < 2000; i++) {
    index.insert('node-$i', Offset(i * 10.0, i * 5.0));
  }
  // Query a small viewport
  final visible = index.query(Rect.fromLTWH(100, 50, 200, 100));
  expect(visible.length, lessThan(100)); // Much less than 2000
  expect(visible.length, greaterThan(0));
});

test('force-directed layout completes for 2000 nodes', () async {
  final nodes = generateTestNodes(2000);
  final edges = generateTestEdges(nodes, density: 0.02);
  final layout = ForceDirectedLayout();

  final positions = await layout.compute(nodes, edges);
  expect(positions.length, equals(2000));
  // No overlapping positions (minimum distance)
  // ... (verify spread)
});
```

---

### MOB-006.2: GraphPainter (CustomPainter)
**Files:**
- `lib/features/knowledge_graph/painters/graph_painter.dart`
- `lib/features/knowledge_graph/painters/edge_painter.dart`

**Acceptance:**
- [ ] `GraphPainter extends CustomPainter`: paints ALL visible nodes + edges on a single canvas
- [ ] Viewport culling: only paints nodes inside the current `InteractiveViewer` transform rect
- [ ] Node rendering: filled circle, radius based on mastery (mastered=larger), color from `SubjectDiagramPalette`
- [ ] Node label: concept name (Hebrew/Arabic, clipped to node width)
- [ ] Edge rendering: bezier curves between prerequisite nodes, dashed for weak edges (strength < 0.5)
- [ ] Mastery color interpolation: gray (0.0) → yellow (0.3-0.84) → green (0.85+)
- [ ] `shouldRepaint`: returns `true` only when graph data or viewport changes (NOT on animation tick unless animating)
- [ ] RepaintBoundary wraps the painter

**Test:**
```dart
testWidgets('GraphPainter renders at 60fps with 500 visible nodes', (tester) async {
  final graph = generateTestGraph(2000);
  final positions = await computeLayout(graph);

  await tester.pumpWidget(
    RepaintBoundary(
      child: CustomPaint(
        painter: GraphPainter(
          nodes: graph.nodes,
          edges: graph.edges,
          positions: positions,
          visibleRect: Rect.fromLTWH(0, 0, 400, 800),
          masteryOverlay: generateMastery(graph.nodes),
        ),
      ),
    ),
  );

  // Measure frame time during simulated pan
  final stopwatch = Stopwatch()..start();
  for (int i = 0; i < 60; i++) {
    await tester.pump(Duration(milliseconds: 16)); // 60fps target
  }
  stopwatch.stop();
  expect(stopwatch.elapsedMilliseconds, lessThan(1100)); // 60 frames in ~1s
});
```

---

### MOB-006.3: InteractiveViewer Integration (Zoom/Pan)
**Files:**
- `lib/features/knowledge_graph/widgets/interactive_knowledge_graph.dart`

**Acceptance:**
- [ ] `InteractiveViewer` wraps `CustomPaint(painter: GraphPainter(...))`
- [ ] Zoom: min 0.1x, max 5.0x, pinch-to-zoom, scroll-to-zoom (web)
- [ ] Pan: drag to pan, bounded to graph extents + padding
- [ ] Zoom/pan state: LOCAL `TransformationController` (NOT in Riverpod — per architect review)
- [ ] Double-tap to fit all nodes in viewport
- [ ] Smooth animation on zoom transitions (200ms ease-out)

**Test:**
```dart
testWidgets('double-tap fits graph in viewport', (tester) async {
  await tester.pumpWidget(InteractiveKnowledgeGraph(graph: testGraph));

  // Pan far off-center
  await tester.drag(find.byType(InteractiveViewer), Offset(500, 500));
  await tester.pumpAndSettle();

  // Double-tap to fit
  await tester.tap(find.byType(InteractiveViewer));
  await tester.tap(find.byType(InteractiveViewer));
  await tester.pumpAndSettle();

  // Verify: all nodes visible in viewport
  // (check transform controller bounds)
});
```

---

### MOB-006.4: Node Selection + Detail Sheet
**Files:**
- `lib/features/knowledge_graph/widgets/concept_detail_sheet.dart`

**Acceptance:**
- [ ] Tap on node → detect which node was hit (using quadtree spatial query at tap position)
- [ ] Selected node: elevated ring highlight, slightly larger
- [ ] Bottom sheet slides up with: concept name (He/Ar/En), mastery %, prerequisite list, last practiced date
- [ ] Bottom sheet shows "Start Practice" button → navigates to session screen for that concept
- [ ] Tap outside node → deselect
- [ ] Long-press on node → show prerequisite path highlighted on graph

**Test:**
```dart
testWidgets('tap on node opens detail sheet', (tester) async {
  await tester.pumpWidget(InteractiveKnowledgeGraph(graph: testGraph));

  // Tap on a known node position
  await tester.tapAt(nodePositions['algebra-1']!);
  await tester.pumpAndSettle();

  // Bottom sheet visible with concept details
  expect(find.text('משוואות'), findsOneWidget); // Hebrew name
  expect(find.text('Start Practice'), findsOneWidget);
});
```

---

### MOB-006.5: Mastery Animations (Node Pulse on Mastery)
**Files:**
- `lib/features/knowledge_graph/painters/mastery_animation_controller.dart`

**Acceptance:**
- [ ] When a concept is mastered (event received via WebSocket): node pulses green 3 times
- [ ] Pulse: scale 1.0 → 1.3 → 1.0 over 500ms, with glow effect
- [ ] Only the mastered node animates (not the whole graph)
- [ ] After animation: node transitions to mastered color (green)
- [ ] Animation respects `prefers-reduced-motion` (skip pulse, just change color)

**Test:**
```dart
testWidgets('mastery animation plays on event', (tester) async {
  await tester.pumpWidget(InteractiveKnowledgeGraph(graph: testGraph));

  // Simulate mastery event
  simulateWebSocketEvent(MasteryUpdated(conceptId: 'algebra-1', mastery: 0.90));
  await tester.pump(Duration(milliseconds: 100));

  // Node is animating (scale > 1.0)
  // ... (verify painter received animation state)

  await tester.pumpAndSettle(); // Wait for animation to complete
  // Node is now green
});
```

---

### MOB-006.6: Subject Filter Chips
**Files:**
- `lib/features/knowledge_graph/widgets/subject_filter_chips.dart`

**Acceptance:**
- [ ] Horizontal scrollable row of chips: Math, Physics, Chemistry, Biology, CS
- [ ] Each chip: subject name (He/Ar), icon, color from `SubjectDiagramPalette`
- [ ] Tap chip → filter graph to show only that subject's nodes
- [ ] "All" chip selected by default
- [ ] Filter is instant (quadtree re-query, not graph re-layout)

**Test:**
```dart
testWidgets('subject filter hides other subjects', (tester) async {
  await tester.pumpWidget(InteractiveKnowledgeGraph(graph: multiSubjectGraph));

  // Initially: all nodes visible
  expect(painter.visibleNodeCount, equals(500));

  // Tap "Math" filter
  await tester.tap(find.text('מתמטיקה'));
  await tester.pumpAndSettle();

  // Only math nodes visible
  expect(painter.visibleNodeCount, lessThan(250));
});
```

---

### MOB-006.7: Accessibility Overlay
**Files:**
- `lib/features/knowledge_graph/widgets/knowledge_graph_semantics.dart`

**Acceptance:**
- [ ] When `MediaQuery.of(context).accessibleNavigation == true`: show list instead of graph
- [ ] `ListView` of concepts sorted by mastery (lowest first = most needs practice)
- [ ] Each item: `Semantics(label: MasteryAccessibilityLabel.nodeLabel(...))`
- [ ] Hebrew, Arabic, English labels per `MasteryAccessibilityLabel`
- [ ] Tap item → opens same `ConceptDetailSheet` as graph tap
- [ ] Status conveyed by shape AND text (not just color): checkmark=mastered, half-circle=progress, empty=not started

**Test:**
```dart
testWidgets('accessibility mode shows list view', (tester) async {
  // Enable accessibility
  tester.binding.platformDispatcher.accessibilityFeaturesTestValue =
      FakeAccessibilityFeatures(accessibleNavigation: true);

  await tester.pumpWidget(InteractiveKnowledgeGraph(graph: testGraph));

  // List view, not canvas
  expect(find.byType(ListView), findsOneWidget);
  expect(find.byType(CustomPaint), findsNothing);

  // Semantic labels present
  final semantics = tester.getSemantics(find.byType(ListView).first);
  expect(semantics.label, contains('נשלט')); // "Mastered" in Hebrew
});
```

---

## Performance Benchmarks (must pass before merge)

```dart
group('Knowledge Graph Performance', () {
  test('layout 2000 nodes in < 2 seconds', () async {
    final sw = Stopwatch()..start();
    await ForceDirectedLayout().compute(nodes2000, edges2000);
    sw.stop();
    expect(sw.elapsedMilliseconds, lessThan(2000));
  });

  test('quadtree query 2000 nodes in < 1ms', () {
    final index = buildSpatialIndex(nodes2000);
    final sw = Stopwatch()..start();
    for (int i = 0; i < 100; i++) {
      index.query(randomViewport());
    }
    sw.stop();
    expect(sw.elapsedMilliseconds / 100, lessThan(1.0)); // < 1ms per query
  });

  test('paint 500 visible nodes in < 8ms', () {
    final recorder = PictureRecorder();
    final canvas = Canvas(recorder);
    final sw = Stopwatch()..start();
    GraphPainter(...).paint(canvas, Size(400, 800));
    sw.stop();
    expect(sw.elapsedMilliseconds, lessThan(8)); // Half of 16ms frame budget
  });
});
```

## Rollback Criteria
- If CustomPainter approach fails: fall back to `flutter_graph_view` package (less control, but functional)
- If 2000 nodes too slow: cluster nearby nodes into super-nodes at low zoom levels (level-of-detail)
- If force-directed layout too slow: use pre-computed layout from Neo4j `gds.pageRank` + manual positioning

## Definition of Done
- [ ] All 7 subtasks pass their individual tests
- [ ] Performance benchmarks pass
- [ ] Knowledge graph renders 2000 nodes at 60fps on Pixel 6a (mid-range Android)
- [ ] Accessibility: TalkBack can traverse all concepts in list mode
- [ ] Arabic labels render correctly (RTL, Noto Sans Arabic font)
- [ ] PR reviewed by mobile lead
