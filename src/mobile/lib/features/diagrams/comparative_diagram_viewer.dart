// =============================================================================
// Cena — Comparative Diagram Viewer (MOB-VIS-015)
// =============================================================================
// Side-by-side (landscape/tablet) or stacked (portrait phone) comparison
// of two ConceptDiagrams. Shared zoom/pan via a single InteractiveViewer
// wrapping both diagrams.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../../core/config/app_config.dart';
import '../../core/theme/glass_widgets.dart';
import 'diagram_viewer.dart';
import 'models/diagram_models.dart';

// ─────────────────────────────────────────────────────────────────────
// ComparativeDiagramViewer
// ─────────────────────────────────────────────────────────────────────

/// Displays two [ConceptDiagram]s side-by-side (width > 600) or stacked
/// (width <= 600) for comparison. Both diagrams share a single
/// [InteractiveViewer] so zoom and pan are automatically synchronized.
class ComparativeDiagramViewer extends StatefulWidget {
  const ComparativeDiagramViewer({
    super.key,
    required this.diagramA,
    required this.diagramB,
    this.labelA = 'A',
    this.labelB = 'B',
    this.onHotspotTap,
    this.locale = 'he',
  });

  final ConceptDiagram diagramA;
  final ConceptDiagram diagramB;
  final String labelA;
  final String labelB;
  final void Function(DiagramHotspot)? onHotspotTap;
  final String locale;

  @override
  State<ComparativeDiagramViewer> createState() =>
      _ComparativeDiagramViewerState();
}

class _ComparativeDiagramViewerState extends State<ComparativeDiagramViewer> {
  final _transformController = TransformationController();

  /// Hotspot IDs present only in diagram A (not in B).
  late Set<String> _uniqueToA;

  /// Hotspot IDs present only in diagram B (not in A).
  late Set<String> _uniqueToB;

  @override
  void initState() {
    super.initState();
    _computeUniqueHotspots();
  }

  @override
  void didUpdateWidget(covariant ComparativeDiagramViewer oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.diagramA != widget.diagramA ||
        oldWidget.diagramB != widget.diagramB) {
      _computeUniqueHotspots();
    }
  }

  void _computeUniqueHotspots() {
    final idsA = widget.diagramA.hotspots.map((h) => h.id).toSet();
    final idsB = widget.diagramB.hotspots.map((h) => h.id).toSet();
    _uniqueToA = idsA.difference(idsB);
    _uniqueToB = idsB.difference(idsA);
  }

  void _resetZoom() {
    _transformController.value = Matrix4.identity();
  }

  @override
  void dispose() {
    _transformController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          child: InteractiveViewer(
            transformationController: _transformController,
            minScale: 0.5,
            maxScale: 4.0,
            child: LayoutBuilder(
              builder: (context, constraints) {
                final isSideBySide = constraints.maxWidth > 600;
                final children = [
                  Expanded(
                    child: _DiagramPane(
                      diagram: widget.diagramA,
                      label: widget.labelA,
                      uniqueHotspotIds: _uniqueToA,
                      onHotspotTap: widget.onHotspotTap,
                      locale: widget.locale,
                    ),
                  ),
                  SizedBox(
                    width: isSideBySide ? SpacingTokens.sm : 0,
                    height: isSideBySide ? 0 : SpacingTokens.sm,
                  ),
                  Expanded(
                    child: _DiagramPane(
                      diagram: widget.diagramB,
                      label: widget.labelB,
                      uniqueHotspotIds: _uniqueToB,
                      onHotspotTap: widget.onHotspotTap,
                      locale: widget.locale,
                    ),
                  ),
                ];

                if (isSideBySide) {
                  return Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: children,
                  );
                }
                return Column(children: children);
              },
            ),
          ),
        ),
        // Reset zoom FAB
        Positioned(
          right: SpacingTokens.sm,
          bottom: SpacingTokens.sm,
          child: FloatingActionButton.small(
            heroTag: 'comparative_reset',
            onPressed: _resetZoom,
            tooltip: 'Reset zoom',
            child: const Icon(Icons.fit_screen, size: 18),
          ),
        ),
      ],
    );
  }
}

// ─────────────────────────────────────────────────────────────────────
// _DiagramPane — one side of the comparison
// ─────────────────────────────────────────────────────────────────────

class _DiagramPane extends StatelessWidget {
  const _DiagramPane({
    required this.diagram,
    required this.label,
    required this.uniqueHotspotIds,
    this.onHotspotTap,
    this.locale = 'he',
  });

  final ConceptDiagram diagram;
  final String label;
  final Set<String> uniqueHotspotIds;
  final void Function(DiagramHotspot)? onHotspotTap;
  final String locale;

  SubjectDiagramPalette get _palette =>
      SubjectDiagramPalette.forSubject(diagram.subject);

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, box) {
        final w = box.maxWidth;
        final h = box.maxHeight;
        return Stack(
          children: [
            // SVG diagram
            _buildSvg(context, w, h),

            // Hotspot overlays
            ...diagram.hotspots.map(
              (hs) => _buildHotspot(context, hs, w, h),
            ),

            // Label chip at top-left
            Positioned(
              left: SpacingTokens.sm,
              top: SpacingTokens.sm,
              child: GlassChip(
                label: label,
                icon: Icons.compare_arrows,
                color: Color(_palette.primary),
              ),
            ),
          ],
        );
      },
    );
  }

  Widget _buildSvg(BuildContext context, double w, double h) {
    if (diagram.inlineSvg != null && diagram.inlineSvg!.isNotEmpty) {
      return SvgPicture.string(
        diagram.inlineSvg!,
        width: w,
        height: h,
        fit: BoxFit.contain,
      );
    }
    return SvgPicture.network(
      diagram.assetUrl,
      width: w,
      height: h,
      fit: BoxFit.contain,
      placeholderBuilder: (_) => Container(
        height: h,
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        child: const Center(child: CircularProgressIndicator()),
      ),
    );
  }

  Widget _buildHotspot(
    BuildContext context,
    DiagramHotspot hs,
    double w,
    double h,
  ) {
    final b = hs.bounds;
    final accent = Color(_palette.accent);
    final isUnique = uniqueHotspotIds.contains(hs.id);

    return Positioned(
      left: b.x * w,
      top: b.y * h,
      width: b.width * w,
      height: b.height * h,
      child: GestureDetector(
        onTap: () {
          onHotspotTap?.call(hs);
          showHotspotDetailSheet(
            context: context,
            hotspot: hs,
            locale: locale,
            palette: _palette,
          );
        },
        behavior: HitTestBehavior.opaque,
        child: AnimatedContainer(
          duration: AnimationTokens.fast,
          decoration: BoxDecoration(
            border: Border.all(
              color: accent.withValues(alpha: 0.6),
              width: isUnique ? 2.5 : 1.5,
            ),
            borderRadius: BorderRadius.circular(RadiusTokens.sm),
            boxShadow: isUnique
                ? [
                    BoxShadow(
                      color: accent.withValues(alpha: 0.5),
                      blurRadius: 8,
                      spreadRadius: 1,
                    ),
                  ]
                : null,
          ),
        ),
      ),
    );
  }
}
