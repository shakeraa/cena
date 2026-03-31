// =============================================================================
// Cena — Interactive Diagram Viewer (MOB-DIAG-001)
// =============================================================================
// Renders pre-generated SVG diagrams from CDN with interactive hotspots.
// Diagrams are generated batch by Kimi K2.5, cached on S3/CDN.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../../core/config/app_config.dart';

/// Renders a STEM diagram with zoom/pan and tappable hotspot overlays.
class DiagramViewer extends StatelessWidget {
  const DiagramViewer({
    super.key,
    required this.diagram,
    this.onHotspotTap,
  });

  final Diagram diagram;
  final void Function(Hotspot hotspot)? onHotspotTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // Diagram title
        if (diagram.title.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
            child: Text(
              diagram.title,
              style: theme.textTheme.titleSmall?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
          ),

        // SVG with hotspot overlays
        ClipRRect(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          child: InteractiveViewer(
            minScale: 0.5,
            maxScale: 4.0,
            child: Stack(
              children: [
                // SVG from CDN URL
                SvgPicture.network(
                  diagram.svgUrl,
                  width: double.infinity,
                  placeholderBuilder: (context) => Container(
                    height: 200,
                    color: colorScheme.surfaceContainerHighest,
                    child: const Center(child: CircularProgressIndicator()),
                  ),
                ),

                // Hotspot overlays
                ...diagram.hotspots.map((hotspot) {
                  return Positioned(
                    left: hotspot.x,
                    top: hotspot.y,
                    child: GestureDetector(
                      onTap: () => onHotspotTap?.call(hotspot),
                      child: Container(
                        width: hotspot.width,
                        height: hotspot.height,
                        decoration: BoxDecoration(
                          color: colorScheme.primary.withValues(alpha: 0.1),
                          border: Border.all(
                            color: colorScheme.primary.withValues(alpha: 0.4),
                            width: 1.5,
                          ),
                          borderRadius:
                              BorderRadius.circular(RadiusTokens.sm),
                        ),
                        child: Tooltip(
                          message: hotspot.label,
                          child: const SizedBox.expand(),
                        ),
                      ),
                    ),
                  );
                }),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// Simple data class for hotspot rendering (subset of DiagramHotspot).
class Hotspot {
  const Hotspot({
    required this.label,
    required this.x,
    required this.y,
    required this.width,
    required this.height,
    this.conceptId,
  });

  final String label;
  final double x;
  final double y;
  final double width;
  final double height;
  final String? conceptId;
}

/// Lightweight diagram reference for the viewer.
class Diagram {
  const Diagram({
    required this.svgUrl,
    required this.title,
    this.hotspots = const [],
  });

  final String svgUrl;
  final String title;
  final List<Hotspot> hotspots;
}
