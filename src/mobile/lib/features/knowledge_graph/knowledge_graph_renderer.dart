// =============================================================================
// Cena Adaptive Learning Platform — Knowledge Graph Renderer (MOB-CORE-003)
// =============================================================================
//
// Force-directed graph layout with concept nodes, prerequisite edges,
// mastery color interpolation, and zoom/pan gestures.
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';

import '../../core/models/domain_models.dart';

/// Simple edge record for the graph (fromId → toId).
class GraphEdge {
  const GraphEdge({required this.fromId, required this.toId});
  final String fromId;
  final String toId;
}

// ---------------------------------------------------------------------------
// Force-directed layout engine
// ---------------------------------------------------------------------------

class _LayoutNode {
  _LayoutNode({required this.concept, required this.position});

  final ConceptNode concept;
  Offset position;
  Offset velocity = Offset.zero;
}

/// Simple force-directed layout computed incrementally per frame.
/// Suitable for graphs up to ~100 nodes on mobile.
class ForceDirectedLayout {
  ForceDirectedLayout({
    required this.nodes,
    required this.edges,
    this.repulsion = 5000.0,
    this.attraction = 0.01,
    this.damping = 0.85,
  });

  final List<_LayoutNode> nodes;
  final List<(int, int)> edges; // (fromIdx, toIdx)
  final double repulsion;
  final double attraction;
  final double damping;

  /// Run one step of the simulation. Returns true if still settling.
  bool step() {
    double totalMovement = 0;

    // Repulsion between all node pairs
    for (int i = 0; i < nodes.length; i++) {
      for (int j = i + 1; j < nodes.length; j++) {
        final delta = nodes[i].position - nodes[j].position;
        final dist = max(delta.distance, 1.0);
        final force = delta / dist * (repulsion / (dist * dist));
        nodes[i].velocity += force;
        nodes[j].velocity -= force;
      }
    }

    // Attraction along edges
    for (final (from, to) in edges) {
      final delta = nodes[to].position - nodes[from].position;
      final force = delta * attraction;
      nodes[from].velocity += force;
      nodes[to].velocity -= force;
    }

    // Apply velocity with damping
    for (final node in nodes) {
      node.velocity *= damping;
      node.position += node.velocity;
      totalMovement += node.velocity.distance;
    }

    return totalMovement > 0.5; // Settled when total movement < threshold
  }

  /// Seed initial positions in a circle.
  static List<_LayoutNode> seedCircle(
      List<ConceptNode> concepts, double radius) {
    final count = concepts.length;
    return List.generate(count, (i) {
      final angle = 2 * pi * i / count;
      return _LayoutNode(
        concept: concepts[i],
        position: Offset(cos(angle) * radius, sin(angle) * radius),
      );
    });
  }
}

// ---------------------------------------------------------------------------
// Knowledge Graph Renderer widget
// ---------------------------------------------------------------------------

/// Renders an interactive knowledge graph with zoom, pan, and node selection.
/// Concept mastery is shown via color interpolation (red→yellow→green).
class KnowledgeGraphRenderer extends StatefulWidget {
  const KnowledgeGraphRenderer({
    super.key,
    required this.concepts,
    required this.edges,
    this.onNodeTap,
    this.selectedConceptId,
  });

  final List<ConceptNode> concepts;
  final List<GraphEdge> edges;
  final void Function(String conceptId)? onNodeTap;
  final String? selectedConceptId;

  @override
  State<KnowledgeGraphRenderer> createState() =>
      _KnowledgeGraphRendererState();
}

class _KnowledgeGraphRendererState extends State<KnowledgeGraphRenderer>
    with SingleTickerProviderStateMixin {
  late List<_LayoutNode> _layoutNodes;
  late List<(int, int)> _edgeIndices;
  late ForceDirectedLayout _layout;
  late AnimationController _simController;

  final TransformationController _transformController =
      TransformationController();
  int _simulationSteps = 0;
  static const _maxSteps = 200;

  @override
  void initState() {
    super.initState();
    _initLayout();
    _simController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 10),
    )..addListener(_stepSimulation);
    _simController.repeat();
  }

  void _initLayout() {
    _layoutNodes = ForceDirectedLayout.seedCircle(widget.concepts, 200);

    // Build edge index pairs
    final idToIdx = <String, int>{};
    for (int i = 0; i < widget.concepts.length; i++) {
      idToIdx[widget.concepts[i].conceptId] = i;
    }
    _edgeIndices = <(int, int)>[];
    for (final edge in widget.edges) {
      final from = idToIdx[edge.fromId];
      final to = idToIdx[edge.toId];
      if (from != null && to != null) {
        _edgeIndices.add((from, to));
      }
    }

    _layout = ForceDirectedLayout(
      nodes: _layoutNodes,
      edges: _edgeIndices,
    );
  }

  void _stepSimulation() {
    if (_simulationSteps >= _maxSteps) {
      _simController.stop();
      return;
    }
    final settling = _layout.step();
    _simulationSteps++;
    if (!settling) {
      _simController.stop();
    }
    setState(() {});
  }

  @override
  void dispose() {
    _simController.dispose();
    _transformController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.concepts.isEmpty) {
      return Center(
        child: Text(
          'Complete more sessions to build your knowledge map',
          style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
          textAlign: TextAlign.center,
        ),
      );
    }

    return InteractiveViewer(
      transformationController: _transformController,
      boundaryMargin: const EdgeInsets.all(200),
      minScale: 0.3,
      maxScale: 3.0,
      child: CustomPaint(
        size: const Size(600, 600),
        painter: _GraphPainter(
          nodes: _layoutNodes,
          edges: _edgeIndices,
          selectedId: widget.selectedConceptId,
          colorScheme: Theme.of(context).colorScheme,
        ),
        child: SizedBox(
          width: 600,
          height: 600,
          child: Stack(
            children: _layoutNodes.map((node) {
              final pos = node.position + const Offset(300, 300);
              return Positioned(
                left: pos.dx - 20,
                top: pos.dy - 20,
                child: GestureDetector(
                  onTap: () =>
                      widget.onNodeTap?.call(node.concept.conceptId),
                  child: _ConceptNodeWidget(
                    concept: node.concept,
                    isSelected:
                        node.concept.conceptId == widget.selectedConceptId,
                  ),
                ),
              );
            }).toList(),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Custom painter for edges
// ---------------------------------------------------------------------------

class _GraphPainter extends CustomPainter {
  _GraphPainter({
    required this.nodes,
    required this.edges,
    required this.selectedId,
    required this.colorScheme,
  });

  final List<_LayoutNode> nodes;
  final List<(int, int)> edges;
  final String? selectedId;
  final ColorScheme colorScheme;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final paint = Paint()
      ..strokeWidth = 1.5
      ..style = PaintingStyle.stroke;

    for (final (from, to) in edges) {
      final p1 = nodes[from].position + center;
      final p2 = nodes[to].position + center;
      paint.color = colorScheme.outlineVariant.withValues(alpha: 0.4);
      canvas.drawLine(p1, p2, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _GraphPainter old) => true;
}

// ---------------------------------------------------------------------------
// Concept node widget with mastery color
// ---------------------------------------------------------------------------

class _ConceptNodeWidget extends StatelessWidget {
  const _ConceptNodeWidget({
    required this.concept,
    required this.isSelected,
  });

  final ConceptNode concept;
  final bool isSelected;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final mastery = concept.mastery;
    final nodeColor = _masteryColor(mastery);

    return Container(
      width: 40,
      height: 40,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: nodeColor.withValues(alpha: 0.2),
        border: Border.all(
          color: isSelected ? theme.colorScheme.primary : nodeColor,
          width: isSelected ? 3 : 2,
        ),
        boxShadow: isSelected
            ? [
                BoxShadow(
                  color: theme.colorScheme.primary.withValues(alpha: 0.3),
                  blurRadius: 8,
                ),
              ]
            : null,
      ),
      child: Center(
        child: Text(
          '${(mastery * 100).toInt()}',
          style: theme.textTheme.labelSmall?.copyWith(
            fontWeight: FontWeight.w700,
            color: nodeColor,
            fontSize: 9,
          ),
        ),
      ),
    );
  }

  /// Red (0%) → Yellow (50%) → Green (100%)
  Color _masteryColor(double mastery) {
    if (mastery < 0.5) {
      return Color.lerp(
        const Color(0xFFEF4444),
        const Color(0xFFF59E0B),
        mastery * 2,
      )!;
    }
    return Color.lerp(
      const Color(0xFFF59E0B),
      const Color(0xFF10B981),
      (mastery - 0.5) * 2,
    )!;
  }
}
