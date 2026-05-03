// =============================================================================
// Cena — Interactive Diagram System (MOB-DIAG-001)
// =============================================================================
// Full interactive diagram viewer with hotspots, drag-label challenges,
// function-plot crosshair insight, and formula chip bar.
// Renders pre-generated SVG diagrams from CDN with rich interactivity.
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../../core/config/app_config.dart';
import '../../l10n/app_localizations.dart';
import '../../core/theme/glass_widgets.dart';
import '../../core/theme/micro_interactions.dart';
import '../session/widgets/math_text.dart';
import 'models/diagram_models.dart';

// ─────────────────────────────────────────────────────────────────────
// 1. InteractiveDiagramViewer — main entry widget
// ─────────────────────────────────────────────────────────────────────

/// Renders a [ConceptDiagram] with pinch-to-zoom, pan, hotspot overlays,
/// formula chip bar, and a reset-zoom FAB.
class InteractiveDiagramViewer extends StatefulWidget {
  const InteractiveDiagramViewer({
    super.key,
    required this.diagram,
    this.onHotspotTap,
    this.onConceptLink,
    this.locale = 'he',
  });

  final ConceptDiagram diagram;
  final void Function(DiagramHotspot hotspot)? onHotspotTap;
  final void Function(String conceptId)? onConceptLink;
  final String locale;

  @override
  State<InteractiveDiagramViewer> createState() =>
      _InteractiveDiagramViewerState();
}

class _InteractiveDiagramViewerState extends State<InteractiveDiagramViewer> {
  final _transformController = TransformationController();
  bool _showHidden = false;

  SubjectDiagramPalette get _palette =>
      SubjectDiagramPalette.forSubject(widget.diagram.subject);

  void _resetZoom() {
    _transformController.value = Matrix4.identity();
  }

  String _title() => _localized(
        widget.locale,
        he: widget.diagram.titleHe,
        ar: widget.diagram.titleAr,
        en: widget.diagram.titleEn,
      );

  @override
  void dispose() {
    _transformController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final diagram = widget.diagram;
    final hasHidden =
        diagram.hotspots.any((h) => h.style == HotspotStyle.hidden);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // ── Title row ──
        Padding(
          padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  _title(),
                  style: theme.textTheme.titleSmall?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              if (hasHidden)
                IconButton(
                  icon: Icon(
                    _showHidden ? Icons.visibility : Icons.visibility_off,
                    size: 20,
                  ),
                  tooltip: 'Reveal labels',
                  onPressed: () =>
                      setState(() => _showHidden = !_showHidden),
                ),
            ],
          ),
        ),

        // ── Formula chips ──
        if (diagram.formulas.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
            child: _FormulaChipBar(
              formulas: diagram.formulas,
              palette: _palette,
              onFormulaTap: (i) {
                if (i < diagram.hotspots.length) {
                  _openDetail(context, diagram.hotspots[i]);
                }
              },
            ),
          ),

        // ── Diagram + hotspots ──
        Expanded(
          child: Stack(
            children: [
              ClipRRect(
                borderRadius: BorderRadius.circular(RadiusTokens.lg),
                child: InteractiveViewer(
                  transformationController: _transformController,
                  minScale: 0.5,
                  maxScale: 4.0,
                  child: LayoutBuilder(
                    builder: (context, box) {
                      final w = box.maxWidth;
                      final h = box.maxHeight;
                      return Stack(
                        children: [
                          _DiagramSvg(diagram: diagram, width: w),
                          ...diagram.hotspots.map((hs) {
                            if (hs.style == HotspotStyle.hidden &&
                                !_showHidden) {
                              return const SizedBox.shrink();
                            }
                            return _HotspotOverlay(
                              hotspot: hs,
                              width: w,
                              height: h,
                              palette: _palette,
                              onTap: () {
                                widget.onHotspotTap?.call(hs);
                                _openDetail(context, hs);
                              },
                            );
                          }),
                        ],
                      );
                    },
                  ),
                ),
              ),
              // Reset FAB
              Positioned(
                right: SpacingTokens.sm,
                bottom: SpacingTokens.sm,
                child: FloatingActionButton.small(
                  heroTag: 'diagram_reset_${diagram.id}',
                  onPressed: _resetZoom,
                  tooltip: 'Reset zoom',
                  child: const Icon(Icons.fit_screen, size: 18),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }

  void _openDetail(BuildContext ctx, DiagramHotspot hs) {
    showHotspotDetailSheet(
      context: ctx,
      hotspot: hs,
      locale: widget.locale,
      palette: _palette,
      onConceptLink: widget.onConceptLink,
    );
  }
}

// ─────────────────────────────────────────────────────────────────────
// 2. _HotspotOverlay — tappable hotspot region
// ─────────────────────────────────────────────────────────────────────

class _HotspotOverlay extends StatelessWidget {
  const _HotspotOverlay({
    required this.hotspot,
    required this.width,
    required this.height,
    required this.palette,
    required this.onTap,
  });

  final DiagramHotspot hotspot;
  final double width;
  final double height;
  final SubjectDiagramPalette palette;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final b = hotspot.bounds;
    final color = Color(palette.accent);

    return Positioned(
      left: b.x * width,
      top: b.y * height,
      width: b.width * width,
      height: b.height * height,
      child: GestureDetector(
        onTap: onTap,
        behavior: HitTestBehavior.opaque,
        child: _visual(color),
      ),
    );
  }

  Widget _visual(Color color) {
    switch (hotspot.style) {
      case HotspotStyle.outline:
        return PulseGlow(
          color: color,
          glowRadius: 6,
          child: Container(
            decoration: BoxDecoration(
              border:
                  Border.all(color: color.withValues(alpha: 0.6), width: 2),
              borderRadius: BorderRadius.circular(RadiusTokens.sm),
            ),
          ),
        );
      case HotspotStyle.highlight:
        return Container(
          decoration: BoxDecoration(
            color: color.withValues(alpha: 0.2),
            borderRadius: BorderRadius.circular(RadiusTokens.sm),
          ),
        );
      case HotspotStyle.numbered:
        final n =
            int.tryParse(hotspot.id.replaceAll(RegExp(r'[^0-9]'), '')) ?? 0;
        return Align(
          alignment: Alignment.topLeft,
          child: Container(
            width: 22,
            height: 22,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
            alignment: Alignment.center,
            child: Text(
              '${n + 1}',
              style: const TextStyle(
                color: Colors.white,
                fontSize: 11,
                fontWeight: FontWeight.bold,
              ),
            ),
          ),
        );
      case HotspotStyle.hidden:
        return Container(
          decoration: BoxDecoration(
            border: Border.all(color: color.withValues(alpha: 0.3)),
            borderRadius: BorderRadius.circular(RadiusTokens.sm),
          ),
        );
    }
  }
}

// ─────────────────────────────────────────────────────────────────────
// 3. DragLabelDiagram — drag-and-drop challenge
// ─────────────────────────────────────────────────────────────────────

/// Students drag label chips onto diagram hotspot positions.
/// Used for [ChallengeAnswerType.dragLabel].
class DragLabelDiagram extends StatefulWidget {
  const DragLabelDiagram({
    super.key,
    required this.diagram,
    this.labels,
    this.onComplete,
    this.onAllPlaced,
    this.locale = 'he',
  });

  final ConceptDiagram diagram;

  /// Label texts mapped to hotspot IDs: `{hotspotId: labelText}`.
  /// When null, labels are derived from `diagram.hotspots[*].labelHe`.
  final Map<String, String>? labels;

  /// Called when every label is correctly placed.
  final void Function(bool allCorrect)? onComplete;

  /// Legacy callback alias for [onComplete].
  final void Function(bool allCorrect)? onAllPlaced;

  final String locale;

  @override
  State<DragLabelDiagram> createState() => _DragLabelDiagramState();
}

class _DragLabelDiagramState extends State<DragLabelDiagram> {
  final Map<String, bool> _placed = {};
  late List<MapEntry<String, String>> _pool;
  final Map<String, GlobalKey<ShakeWidgetState>> _shakeKeys = {};

  late Map<String, String> _labelMap;

  SubjectDiagramPalette get _palette =>
      SubjectDiagramPalette.forSubject(widget.diagram.subject);

  @override
  void initState() {
    super.initState();
    _labelMap = widget.labels ??
        {for (final h in widget.diagram.hotspots) h.id: h.labelHe};
    _pool = _labelMap.entries.toList()..shuffle(Random());
    for (final hs in widget.diagram.hotspots) {
      if (_labelMap.containsKey(hs.id)) {
        _shakeKeys[hs.id] = GlobalKey<ShakeWidgetState>();
      }
    }
  }

  bool get _allPlaced =>
      _placed.length == _labelMap.length &&
      _placed.values.every((v) => v);

  void _handleDrop(String hotspotId, String droppedId) {
    if (hotspotId == droppedId) {
      HapticFeedback.mediumImpact();
      setState(() {
        _placed[hotspotId] = true;
        _pool.removeWhere((e) => e.key == droppedId);
      });
      if (_allPlaced) {
        (widget.onComplete ?? widget.onAllPlaced)?.call(true);
      }
    } else {
      HapticFeedback.heavyImpact();
      _shakeKeys[hotspotId]?.currentState?.shake();
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final accent = Color(_palette.accent);

    return Column(
      children: [
        // Diagram with drop targets
        Expanded(
          child: ClipRRect(
            borderRadius: BorderRadius.circular(RadiusTokens.lg),
            child: LayoutBuilder(
              builder: (context, box) {
                final w = box.maxWidth;
                final h = box.maxHeight;
                return Stack(
                  children: [
                    _DiagramSvg(diagram: widget.diagram, width: w),
                    ...widget.diagram.hotspots
                        .where((hs) => _labelMap.containsKey(hs.id))
                        .map((hs) => _buildDropTarget(hs, w, h, accent, theme)),
                  ],
                );
              },
            ),
          ),
        ),
        const SizedBox(height: SpacingTokens.md),

        // Draggable label pool
        if (!_allPlaced)
          SizedBox(
            height: 48,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(
                  horizontal: SpacingTokens.sm),
              itemCount: _pool.length,
              separatorBuilder: (_, __) =>
                  const SizedBox(width: SpacingTokens.sm),
              itemBuilder: (_, i) {
                final entry = _pool[i];
                return Draggable<String>(
                  data: entry.key,
                  feedback: Material(
                    color: Colors.transparent,
                    child:
                        _LabelChip(text: entry.value, color: accent, dragging: true),
                  ),
                  childWhenDragging: Opacity(
                    opacity: 0.3,
                    child: _LabelChip(text: entry.value, color: accent),
                  ),
                  child: _LabelChip(text: entry.value, color: accent),
                );
              },
            ),
          ),

        if (_allPlaced)
          const Padding(
            padding: EdgeInsets.all(SpacingTokens.sm),
            child: GlassChip(
              label: 'Complete',
              icon: Icons.check_circle,
              color: Colors.green,
            ),
          ),
      ],
    );
  }

  Widget _buildDropTarget(
    DiagramHotspot hs,
    double w,
    double h,
    Color accent,
    ThemeData theme,
  ) {
    final b = hs.bounds;
    final done = _placed[hs.id] == true;

    return Positioned(
      left: b.x * w,
      top: b.y * h,
      width: b.width * w,
      height: b.height * h,
      child: ShakeWidget(
        key: _shakeKeys[hs.id],
        child: DragTarget<String>(
          onWillAcceptWithDetails: (_) => !done,
          onAcceptWithDetails: (d) => _handleDrop(hs.id, d.data),
          builder: (_, candidates, __) {
            final hover = candidates.isNotEmpty;
            return AnimatedContainer(
              duration: AnimationTokens.fast,
              decoration: BoxDecoration(
                color: done
                    ? Colors.green.withValues(alpha: 0.2)
                    : hover
                        ? accent.withValues(alpha: 0.15)
                        : accent.withValues(alpha: 0.06),
                border: Border.all(
                  color: done ? Colors.green : accent.withValues(alpha: 0.5),
                  width: hover ? 2.5 : 1.5,
                ),
                borderRadius: BorderRadius.circular(RadiusTokens.sm),
              ),
              alignment: Alignment.center,
              child: done
                  ? Text(
                      _labelMap[hs.id]!,
                      style: theme.textTheme.labelSmall?.copyWith(
                        fontWeight: FontWeight.bold,
                        color: Colors.green.shade800,
                      ),
                      textAlign: TextAlign.center,
                    )
                  : Icon(Icons.add, size: 16,
                      color: accent.withValues(alpha: 0.4)),
            );
          },
        ),
      ),
    );
  }
}

class _LabelChip extends StatelessWidget {
  const _LabelChip({
    required this.text,
    required this.color,
    this.dragging = false,
  });

  final String text;
  final Color color;
  final bool dragging;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm + 2,
        vertical: SpacingTokens.xs + 2,
      ),
      decoration: BoxDecoration(
        color: color.withValues(alpha: dragging ? 0.25 : 0.1),
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: Border.all(color: color.withValues(alpha: 0.5)),
        boxShadow: dragging
            ? [BoxShadow(color: color.withValues(alpha: 0.3), blurRadius: 8)]
            : null,
      ),
      child: Text(
        text,
        style: TextStyle(
          fontSize: TypographyTokens.labelMedium,
          fontWeight: FontWeight.w600,
          color: color,
        ),
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────
// 4. GraphInsightViewer — crosshair for function_plot diagrams
// ─────────────────────────────────────────────────────────────────────

/// Overlays a vertical crosshair + floating tooltip on a function plot.
/// Snaps to nearby hotspots (intercepts, maxima, minima).
class GraphInsightViewer extends StatefulWidget {
  const GraphInsightViewer({
    super.key,
    required this.diagram,
    this.xMin = 0,
    this.xMax = 10,
    this.yMin = 0,
    this.yMax = 10,
    this.locale = 'he',
    this.onHotspotTap,
  });

  final ConceptDiagram diagram;
  final double xMin;
  final double xMax;
  final double yMin;
  final double yMax;
  final String locale;
  final void Function(DiagramHotspot)? onHotspotTap;

  @override
  State<GraphInsightViewer> createState() => _GraphInsightViewerState();
}

class _GraphInsightViewerState extends State<GraphInsightViewer> {
  double? _touchNorm; // 0-1 normalized x
  int? _snappedIdx;

  SubjectDiagramPalette get _palette =>
      SubjectDiagramPalette.forSubject(widget.diagram.subject);

  void _update(double localX, double totalW) {
    final norm = (localX / totalW).clamp(0.0, 1.0);
    int? snap;
    double best = 0.04;
    for (int i = 0; i < widget.diagram.hotspots.length; i++) {
      final cx = widget.diagram.hotspots[i].bounds.x +
          widget.diagram.hotspots[i].bounds.width / 2;
      final d = (norm - cx).abs();
      if (d < best) {
        best = d;
        snap = i;
      }
    }
    setState(() {
      _touchNorm = snap != null
          ? widget.diagram.hotspots[snap].bounds.x +
              widget.diagram.hotspots[snap].bounds.width / 2
          : norm;
      _snappedIdx = snap;
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final accent = Color(_palette.accent);
    final diagram = widget.diagram;

    return LayoutBuilder(
      builder: (context, box) {
        final w = box.maxWidth;
        final h = box.maxHeight;
        return GestureDetector(
          onHorizontalDragUpdate: (d) => _update(d.localPosition.dx, w),
          onTapDown: (d) => _update(d.localPosition.dx, w),
          onHorizontalDragEnd: (_) => setState(() {
            _touchNorm = null;
            _snappedIdx = null;
          }),
          child: Stack(
            children: [
              ClipRRect(
                borderRadius: BorderRadius.circular(RadiusTokens.lg),
                child: _DiagramSvg(
                    diagram: diagram, width: w, height: h),
              ),

              // Key-point dots
              ...diagram.hotspots.asMap().entries.map((e) {
                final hs = e.value;
                final cx = hs.bounds.x * w + hs.bounds.width * w / 2;
                final cy = hs.bounds.y * h + hs.bounds.height * h / 2;
                final snapped = _snappedIdx == e.key;
                final r = snapped ? 8.0 : 5.0;
                return Positioned(
                  left: cx - r,
                  top: cy - r,
                  child: AnimatedContainer(
                    duration: AnimationTokens.fast,
                    width: r * 2,
                    height: r * 2,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: snapped
                          ? accent
                          : accent.withValues(alpha: 0.5),
                      border: Border.all(
                        color: Colors.white,
                        width: snapped ? 2 : 1,
                      ),
                    ),
                  ),
                );
              }),

              // Crosshair line
              if (_touchNorm != null)
                Positioned(
                  left: _touchNorm! * w,
                  top: 0,
                  bottom: 0,
                  child: Container(
                    width: 1.5,
                    color: accent.withValues(alpha: 0.7),
                  ),
                ),

              // Tooltip card
              if (_touchNorm != null)
                Positioned(
                  left: (_touchNorm! * w + 12).clamp(0, w - 140).toDouble(),
                  top: SpacingTokens.md,
                  child: GlassContainer(
                    blur: 12,
                    opacity: 0.15,
                    borderRadius: BorderRadius.circular(RadiusTokens.md),
                    child: Padding(
                      padding: const EdgeInsets.all(SpacingTokens.sm),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            'x = ${(widget.xMin + _touchNorm! * (widget.xMax - widget.xMin)).toStringAsFixed(2)}',
                            style: theme.textTheme.labelSmall?.copyWith(
                              fontFamily: TypographyTokens.monoFontFamily,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          if (_snappedIdx != null) ...[
                            const SizedBox(height: SpacingTokens.xs),
                            Text(
                              _localized(
                                widget.locale,
                                he: diagram.hotspots[_snappedIdx!].labelHe,
                                ar: diagram.hotspots[_snappedIdx!].labelAr,
                                en: diagram.hotspots[_snappedIdx!].labelEn,
                              ),
                              style: theme.textTheme.labelSmall?.copyWith(
                                fontWeight: FontWeight.bold,
                                color: Color(_palette.primary),
                              ),
                            ),
                          ],
                          if (diagram.formulas.isNotEmpty) ...[
                            const SizedBox(height: SpacingTokens.xs),
                            SizedBox(
                              width: 120,
                              child: MathText(
                                content: diagram.formulas.first,
                                textStyle: theme.textTheme.labelSmall,
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                ),
            ],
          ),
        );
      },
    );
  }
}

// ─────────────────────────────────────────────────────────────────────
// 5. _FormulaChipBar — scrollable formula chips
// ─────────────────────────────────────────────────────────────────────

class _FormulaChipBar extends StatelessWidget {
  const _FormulaChipBar({
    required this.formulas,
    required this.palette,
    this.onFormulaTap,
  });

  final List<String> formulas;
  final SubjectDiagramPalette palette;
  final void Function(int index)? onFormulaTap;

  @override
  Widget build(BuildContext context) {
    if (formulas.isEmpty) return const SizedBox.shrink();
    final accentColor = Color(palette.accent);

    // Hero mode: first formula displayed large
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        // Hero formula (always the first one)
        GestureDetector(
          onTap: () => onFormulaTap?.call(0),
          onLongPress: () {
            Clipboard.setData(ClipboardData(text: formulas.first));
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(
                content: Text('Formula copied'),
                duration: Duration(seconds: 1),
              ),
            );
          },
          child: GlassContainer(
            blur: 12,
            opacity: 0.08,
            borderRadius: BorderRadius.circular(RadiusTokens.xl),
            child: Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.lg,
                vertical: SpacingTokens.md,
              ),
              child: MathText(
                content: formulas.first,
                textStyle: TextStyle(
                  fontSize: TypographyTokens.displayMedium,
                  fontWeight: FontWeight.w700,
                  color: accentColor,
                ),
                mathColor: accentColor,
                mathBackground: accentColor.withValues(alpha: 0.08),
              ),
            ),
          ),
        ),

        // Remaining formulas as small scrollable chips
        if (formulas.length > 1) ...[
          const SizedBox(height: SpacingTokens.sm),
          SizedBox(
            height: 36,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              padding:
                  const EdgeInsets.symmetric(horizontal: SpacingTokens.xs),
              itemCount: formulas.length - 1,
              separatorBuilder: (_, __) =>
                  const SizedBox(width: SpacingTokens.sm),
              itemBuilder: (_, i) {
                final idx = i + 1;
                return GestureDetector(
                  onTap: () => onFormulaTap?.call(idx),
                  child: GlassContainer(
                    blur: 8,
                    opacity: 0.06,
                    borderRadius: BorderRadius.circular(RadiusTokens.full),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: SpacingTokens.sm + 2,
                        vertical: SpacingTokens.xs,
                      ),
                      child: MathText(
                        content: formulas[idx],
                        textStyle: TextStyle(
                          fontSize: TypographyTokens.labelSmall,
                          color: accentColor,
                        ),
                        mathColor: accentColor,
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ],
    );
  }
}

// ─────────────────────────────────────────────────────────────────────
// 6. HotspotDetailSheet — reusable bottom sheet
// ─────────────────────────────────────────────────────────────────────

/// Shows a modal bottom sheet with hotspot label, explanation (LaTeX),
/// and optional "View Concept" link chip.
void showHotspotDetailSheet({
  required BuildContext context,
  required DiagramHotspot hotspot,
  required String locale,
  required SubjectDiagramPalette palette,
  void Function(String conceptId)? onConceptLink,
}) {
  final label = _localized(
    locale,
    he: hotspot.labelHe,
    ar: hotspot.labelAr,
    en: hotspot.labelEn,
  );
  final explanation = _localized(
    locale,
    he: hotspot.explanationHe,
    ar: hotspot.explanationAr,
    en: hotspot.explanationEn,
  );

  showModalBottomSheet(
    context: context,
    isScrollControlled: true,
    backgroundColor: Colors.transparent,
    builder: (ctx) {
      final theme = Theme.of(ctx);
      return Container(
        constraints: BoxConstraints(
          maxHeight: MediaQuery.of(ctx).size.height * 0.45,
        ),
        decoration: BoxDecoration(
          color: theme.colorScheme.surface,
          borderRadius: const BorderRadius.vertical(
            top: Radius.circular(RadiusTokens.xl),
          ),
        ),
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Drag handle
            Center(
              child: Container(
                width: 36,
                height: 4,
                decoration: BoxDecoration(
                  color: theme.colorScheme.outlineVariant,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: SpacingTokens.md),

            // Label
            Text(
              label,
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.bold,
                color: Color(palette.primary),
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),

            // Explanation with LaTeX
            Flexible(
              child: SingleChildScrollView(
                child: MathText(
                  content: explanation,
                  mathColor: Color(palette.accent),
                ),
              ),
            ),

            // Concept link chip
            if (hotspot.linkedConceptId != null) ...[
              const SizedBox(height: SpacingTokens.md),
              TapScaleButton(
                onTap: () {
                  Navigator.of(ctx).pop();
                  onConceptLink?.call(hotspot.linkedConceptId!);
                },
                child: GlassChip(
                  label: 'View concept',
                  icon: Icons.arrow_forward_rounded,
                  color: Color(palette.primary),
                ),
              ),
            ],
          ],
        ),
      );
    },
  );
}

// ─────────────────────────────────────────────────────────────────────
// Shared helpers
// ─────────────────────────────────────────────────────────────────────

/// Renders SVG from inline string or network URL.
class _DiagramSvg extends StatelessWidget {
  const _DiagramSvg({
    required this.diagram,
    required this.width,
    this.height,
  });

  final ConceptDiagram diagram;
  final double width;
  final double? height;

  @override
  Widget build(BuildContext context) {
    if (diagram.inlineSvg != null && diagram.inlineSvg!.isNotEmpty) {
      return SvgPicture.string(
        diagram.inlineSvg!,
        width: width,
        height: height,
        fit: BoxFit.contain,
      );
    }
    return SvgPicture.network(
      diagram.assetUrl,
      width: width,
      height: height,
      fit: BoxFit.contain,
      placeholderBuilder: (_) => Container(
        height: height ?? 200,
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        child: const Center(child: CircularProgressIndicator()),
      ),
    );
  }
}

/// Pick a localized string (he/ar/en) based on the current locale code.
String _localized(
  String locale, {
  required String he,
  String? ar,
  String? en,
}) {
  switch (locale) {
    case 'ar':
      return ar ?? he;
    case 'en':
      return en ?? he;
    default:
      return he;
  }
}

// ─────────────────────────────────────────────────────────────────────
// 8. FillInLabelsDiagram — interactive label quiz (MOB-VIS-018)
// ─────────────────────────────────────────────────────────────────────

/// Interactive "fill in the blank" mode where hotspot labels are hidden.
/// Students tap a numbered hotspot and type what they think the label is.
/// Validation is case-insensitive natural language matching (not LaTeX).
class FillInLabelsDiagram extends StatefulWidget {
  const FillInLabelsDiagram({
    super.key,
    required this.diagram,
    this.onComplete,
  });

  final ConceptDiagram diagram;
  final void Function(int correctCount, int totalCount)? onComplete;

  @override
  State<FillInLabelsDiagram> createState() => _FillInLabelsDiagramState();
}

class _FillInLabelsDiagramState extends State<FillInLabelsDiagram> {
  final Map<String, String?> _studentAnswers = {};
  final Map<String, bool> _results = {};
  final _answerController = TextEditingController();

  List<DiagramHotspot> get _hotspots => widget.diagram.hotspots;
  int get _totalCount => _hotspots.length;
  int get _correctCount => _results.values.where((v) => v).length;
  int get _attemptedCount => _results.length;
  bool get _isComplete => _attemptedCount == _totalCount;

  String _getLabel(DiagramHotspot h) {
    final locale = Localizations.localeOf(context).languageCode;
    return _localized(locale, he: h.labelHe, ar: h.labelAr, en: h.labelEn);
  }

  void _onHotspotTap(DiagramHotspot hotspot) {
    if (_results.containsKey(hotspot.id)) return; // already answered
    _answerController.clear();

    showDialog<void>(
      context: context,
      builder: (ctx) {
        final l = AppLocalizations.of(ctx);
        return AlertDialog(
          title: Text('Label #${_hotspots.indexOf(hotspot) + 1}'),
          content: TextField(
            controller: _answerController,
            autofocus: true,
            decoration: InputDecoration(hintText: l.writeAnswerHere),
            textInputAction: TextInputAction.done,
            onSubmitted: (_) => _checkAnswer(ctx, hotspot),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: Text(l.cancel),
            ),
            FilledButton(
              onPressed: () => _checkAnswer(ctx, hotspot),
              child: Text(l.submitAnswer),
            ),
          ],
        );
      },
    );
  }

  void _checkAnswer(BuildContext dialogCtx, DiagramHotspot hotspot) {
    final answer = _answerController.text.trim().toLowerCase();
    final correctLabel = _getLabel(hotspot).trim().toLowerCase();
    final isCorrect = answer == correctLabel;

    setState(() {
      _studentAnswers[hotspot.id] = _answerController.text.trim();
      _results[hotspot.id] = isCorrect;
    });

    Navigator.pop(dialogCtx);

    if (isCorrect) {
      HapticFeedback.mediumImpact();
    } else {
      HapticFeedback.heavyImpact();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('${AppLocalizations.of(context).tryAgain}: ${_getLabel(hotspot)}')),
      );
    }

    if (_isComplete) {
      widget.onComplete?.call(_correctCount, _totalCount);
    }
  }

  @override
  void dispose() {
    _answerController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final palette = SubjectDiagramPalette.forSubject(widget.diagram.subject);

    return Column(
      children: [
        // Diagram with numbered hotspots
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) {
              return InteractiveViewer(
                minScale: 0.5,
                maxScale: 4.0,
                child: Stack(
                  children: [
                    _DiagramSvg(
                      diagram: widget.diagram,
                      width: constraints.maxWidth,
                      height: constraints.maxHeight,
                    ),
                    ..._hotspots.asMap().entries.map((entry) {
                      final i = entry.key;
                      final h = entry.value;
                      final isCorrect = _results[h.id] == true;
                      final isWrong = _results[h.id] == false;
                      final color = isCorrect
                          ? Colors.green
                          : isWrong
                              ? Colors.red
                              : Color(palette.primary);

                      return Positioned(
                        left: h.bounds.x * constraints.maxWidth,
                        top: h.bounds.y * constraints.maxHeight,
                        width: h.bounds.width * constraints.maxWidth,
                        height: h.bounds.height * constraints.maxHeight,
                        child: GestureDetector(
                          onTap: () => _onHotspotTap(h),
                          child: Container(
                            decoration: BoxDecoration(
                              color: color.withValues(alpha: 0.15),
                              border: Border.all(color: color, width: 2),
                              borderRadius:
                                  BorderRadius.circular(RadiusTokens.sm),
                            ),
                            child: Center(
                              child: isCorrect
                                  ? Icon(Icons.check_rounded,
                                      color: Colors.green, size: 18)
                                  : isWrong
                                      ? Icon(Icons.close_rounded,
                                          color: Colors.red, size: 18)
                                      : CircleAvatar(
                                          radius: 10,
                                          backgroundColor: color,
                                          child: Text(
                                            '${i + 1}',
                                            style: const TextStyle(
                                              color: Colors.white,
                                              fontSize: 10,
                                              fontWeight: FontWeight.w700,
                                            ),
                                          ),
                                        ),
                            ),
                          ),
                        ),
                      );
                    }),
                  ],
                ),
              );
            },
          ),
        ),

        // Progress bar
        Padding(
          padding: const EdgeInsets.all(SpacingTokens.md),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(
                _isComplete ? Icons.emoji_events_rounded : Icons.edit_rounded,
                size: 16,
                color: _isComplete ? Colors.amber : Colors.grey,
              ),
              const SizedBox(width: SpacingTokens.sm),
              Text(
                '$_correctCount / $_totalCount',
                style: Theme.of(context).textTheme.labelLarge?.copyWith(
                      fontWeight: FontWeight.w700,
                    ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
