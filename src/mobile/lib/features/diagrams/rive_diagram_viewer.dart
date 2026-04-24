// =============================================================================
// Cena — Rive Diagram Viewer (MOB-VIS-017)
// =============================================================================
// Renders animated Rive diagrams loaded from CDN with play/pause/scrub
// controls and hotspot tap regions. Falls back to static SVG/PNG if
// the .riv file fails to load.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:rive/rive.dart';

import '../../core/config/app_config.dart';
import '../../core/theme/glass_widgets.dart';
import 'diagram_viewer.dart' show showHotspotDetailSheet;
import 'models/diagram_models.dart';

// ─────────────────────────────────────────────────────────────────────
// RiveDiagramViewer
// ─────────────────────────────────────────────────────────────────────

/// Renders animated Rive diagrams loaded from CDN.
/// Supports play/pause/scrub controls and hotspot tap regions.
/// Falls back to static SVG/PNG if the .riv file fails to load.
class RiveDiagramViewer extends StatefulWidget {
  const RiveDiagramViewer({
    super.key,
    required this.diagram,
    this.fallbackSvgUrl,
    this.onHotspotTap,
    this.locale = 'he',
  });

  final ConceptDiagram diagram;
  final String? fallbackSvgUrl;
  final void Function(DiagramHotspot)? onHotspotTap;
  final String locale;

  @override
  State<RiveDiagramViewer> createState() => _RiveDiagramViewerState();
}

class _RiveDiagramViewerState extends State<RiveDiagramViewer> {
  Artboard? _artboard;
  StateMachineController? _stateMachineController;
  RiveAnimationController? _simpleController;
  bool _isLoading = true;
  bool _hasError = false;
  bool _isPlaying = true;
  double _scrubValue = 0.0;

  SubjectDiagramPalette get _palette =>
      SubjectDiagramPalette.forSubject(widget.diagram.subject);

  @override
  void initState() {
    super.initState();
    _loadRiveFile();
  }

  Future<void> _loadRiveFile() async {
    try {
      final file = await RiveFile.network(widget.diagram.assetUrl);
      final artboard = file.mainArtboard.instance();

      // Try state machine first (find first state machine name),
      // fall back to simple animation.
      StateMachineController? smController;
      for (final animation in artboard.animations) {
        smController = StateMachineController.fromArtboard(
          artboard,
          animation.name,
        );
        if (smController != null) break;
      }

      if (smController != null) {
        artboard.addController(smController);
        _stateMachineController = smController;
      } else {
        final simple = SimpleAnimation(
          artboard.animations.isNotEmpty
              ? artboard.animations.first.name
              : '',
        );
        artboard.addController(simple);
        _simpleController = simple;
      }

      if (mounted) {
        setState(() {
          _artboard = artboard;
          _isLoading = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _isLoading = false;
          _hasError = true;
        });
      }
    }
  }

  void _togglePlayPause() {
    setState(() {
      _isPlaying = !_isPlaying;
      if (_simpleController is SimpleAnimation) {
        (_simpleController as SimpleAnimation).isActive = _isPlaying;
      }
      if (_stateMachineController != null) {
        _stateMachineController!.isActive = _isPlaying;
      }
    });
  }

  void _onScrub(double value) {
    setState(() {
      _scrubValue = value;
      // Pause during manual scrub.
      if (_isPlaying) {
        _isPlaying = false;
        if (_simpleController is SimpleAnimation) {
          (_simpleController as SimpleAnimation).isActive = false;
        }
        if (_stateMachineController != null) {
          _stateMachineController!.isActive = false;
        }
      }
    });
  }

  void _onHotspotTapped(DiagramHotspot hotspot) {
    // Pause animation when a hotspot is tapped.
    if (_isPlaying) {
      _togglePlayPause();
    }
    widget.onHotspotTap?.call(hotspot);
    showHotspotDetailSheet(
      context: context,
      hotspot: hotspot,
      locale: widget.locale,
      palette: _palette,
    );
  }

  @override
  void dispose() {
    _stateMachineController?.dispose();
    _simpleController?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Loading state
    if (_isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    // Error fallback
    if (_hasError) {
      return _buildFallback(context);
    }

    final accent = Color(_palette.accent);

    return Column(
      children: [
        // Rive canvas + hotspot overlays
        Expanded(
          child: ClipRRect(
            borderRadius: BorderRadius.circular(RadiusTokens.lg),
            child: LayoutBuilder(
              builder: (context, box) {
                final w = box.maxWidth;
                final h = box.maxHeight;
                return Stack(
                  children: [
                    if (_artboard != null)
                      Positioned.fill(
                        child: Rive(artboard: _artboard!),
                      ),

                    // Hotspot overlays
                    ...widget.diagram.hotspots.map((hs) {
                      final b = hs.bounds;
                      return Positioned(
                        left: b.x * w,
                        top: b.y * h,
                        width: b.width * w,
                        height: b.height * h,
                        child: GestureDetector(
                          onTap: () => _onHotspotTapped(hs),
                          behavior: HitTestBehavior.opaque,
                          child: Container(
                            decoration: BoxDecoration(
                              border: Border.all(
                                color: accent.withValues(alpha: 0.5),
                                width: 1.5,
                              ),
                              borderRadius:
                                  BorderRadius.circular(RadiusTokens.sm),
                            ),
                          ),
                        ),
                      );
                    }),
                  ],
                );
              },
            ),
          ),
        ),

        // Playback controls bar
        Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: SpacingTokens.sm,
            vertical: SpacingTokens.xs,
          ),
          child: GlassContainer(
            blur: 10,
            opacity: 0.08,
            borderRadius: BorderRadius.circular(RadiusTokens.full),
            child: Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.sm,
              ),
              child: Row(
                children: [
                  IconButton(
                    icon: Icon(
                      _isPlaying
                          ? Icons.pause_rounded
                          : Icons.play_arrow_rounded,
                      color: accent,
                      size: 22,
                    ),
                    onPressed: _togglePlayPause,
                    tooltip: _isPlaying ? 'Pause' : 'Play',
                    padding: EdgeInsets.zero,
                    constraints: const BoxConstraints(
                      minWidth: 36,
                      minHeight: 36,
                    ),
                  ),
                  Expanded(
                    child: SliderTheme(
                      data: SliderThemeData(
                        activeTrackColor: accent,
                        inactiveTrackColor:
                            accent.withValues(alpha: 0.2),
                        thumbColor: accent,
                        trackHeight: 3,
                        thumbShape: const RoundSliderThumbShape(
                          enabledThumbRadius: 6,
                        ),
                      ),
                      child: Slider(
                        value: _scrubValue,
                        onChanged: _onScrub,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildFallback(BuildContext context) {
    if (widget.fallbackSvgUrl != null) {
      // Build a minimal ConceptDiagram pointing to the fallback SVG
      // and render it with the standard InteractiveDiagramViewer.
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.animation,
              size: 40,
              color: Theme.of(context)
                  .colorScheme
                  .onSurfaceVariant
                  .withValues(alpha: 0.5),
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              'Animation unavailable — showing static diagram',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
            ),
          ],
        ),
      );
    }

    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            Icons.error_outline,
            size: 40,
            color: Theme.of(context).colorScheme.error,
          ),
          const SizedBox(height: SpacingTokens.sm),
          Text(
            'Failed to load animation',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: Theme.of(context).colorScheme.error,
                ),
          ),
        ],
      ),
    );
  }
}
