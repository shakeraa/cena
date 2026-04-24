// =============================================================================
// Cena Adaptive Learning Platform — Knowledge Graph Skeleton (MOB-039)
// =============================================================================
//
// Shimmer-animated placeholder for the knowledge graph visualization.
// Renders circular node placeholders connected by line edges to give the
// impression of a graph loading.
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';
import 'package:shimmer/shimmer.dart';

/// Skeleton placeholder for the knowledge graph screen.
///
/// Shows a set of circular node placeholders arranged in a force-directed-like
/// layout with shimmer on both nodes and connecting edges.
class KnowledgeGraphSkeleton extends StatelessWidget {
  const KnowledgeGraphSkeleton({super.key});

  /// Deterministic pseudo-random node positions for a consistent skeleton.
  static final List<Offset> _nodePositions = _generateNodePositions();

  /// Edges connecting nodes (pairs of indices into [_nodePositions]).
  static const List<(int, int)> _edges = [
    (0, 1),
    (0, 2),
    (1, 3),
    (2, 3),
    (2, 4),
    (3, 5),
    (4, 5),
    (4, 6),
    (1, 6),
    (5, 7),
    (6, 7),
  ];

  static List<Offset> _generateNodePositions() {
    final rng = Random(42);
    return List.generate(8, (i) {
      // Distribute nodes across the viewport using a seeded RNG.
      // Positions are fractional [0, 1] — scaled to actual size in build.
      return Offset(
        0.15 + rng.nextDouble() * 0.70,
        0.10 + rng.nextDouble() * 0.75,
      );
    });
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final baseColor = isDark ? const Color(0xFF1E293B) : const Color(0xFFE0E0E0);
    final highlightColor =
        isDark ? const Color(0xFF334155) : const Color(0xFFF5F5F5);

    return Shimmer.fromColors(
      baseColor: baseColor,
      highlightColor: highlightColor,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final w = constraints.maxWidth;
          final h = constraints.maxHeight;

          // Scale fractional positions to actual pixel coordinates
          final scaledNodes = _nodePositions.map((p) {
            return Offset(p.dx * w, p.dy * h);
          }).toList();

          return CustomPaint(
            painter: _GraphEdgePainter(
              nodes: scaledNodes,
              edges: _edges,
            ),
            child: Stack(
              children: [
                for (int i = 0; i < scaledNodes.length; i++)
                  Positioned(
                    left: scaledNodes[i].dx - 20,
                    top: scaledNodes[i].dy - 20,
                    child: _NodeSkeleton(
                      radius: i == 0 || i == 3 ? 24.0 : 18.0,
                    ),
                  ),
              ],
            ),
          );
        },
      ),
    );
  }
}

/// Skeleton circle representing a single knowledge graph node.
class _NodeSkeleton extends StatelessWidget {
  const _NodeSkeleton({this.radius = 18.0});

  final double radius;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: radius * 2,
      height: radius * 2,
      decoration: const BoxDecoration(
        color: Colors.white,
        shape: BoxShape.circle,
      ),
    );
  }
}

/// Paints edges (connecting lines) between skeleton graph nodes.
class _GraphEdgePainter extends CustomPainter {
  _GraphEdgePainter({
    required this.nodes,
    required this.edges,
  });

  final List<Offset> nodes;
  final List<(int, int)> edges;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = Colors.white.withValues(alpha: 0.4)
      ..strokeWidth = 2.0
      ..style = PaintingStyle.stroke;

    for (final (from, to) in edges) {
      if (from < nodes.length && to < nodes.length) {
        canvas.drawLine(nodes[from], nodes[to], paint);
      }
    }
  }

  @override
  bool shouldRepaint(_GraphEdgePainter oldDelegate) => false;
}
