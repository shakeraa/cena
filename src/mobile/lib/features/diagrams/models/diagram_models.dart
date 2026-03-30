// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Interactive Diagram Models & Caching
// Layer: Mobile (Flutter) | Feature: Pre-generated STEM Diagrams
//
// Diagrams are generated batch (overnight) by Kimi K2.5 → SVG,
// cached on S3/CDN, and served instantly during sessions.
// Students interact with labeled hotspots, not static images.
// ═══════════════════════════════════════════════════════════════════════

import 'package:freezed_annotation/freezed_annotation.dart';

part 'diagram_models.freezed.dart';
part 'diagram_models.g.dart';

// ─────────────────────────────────────────────────────────────────────
// 1. DIAGRAM TYPES (matches Kimi generation pipeline)
// ─────────────────────────────────────────────────────────────────────

/// Diagram categories — each has a different generation prompt and rendering style.
enum DiagramType {
  /// Function plots, graphs, coordinate geometry (Math)
  @JsonValue('function_plot')
  functionPlot,

  /// Circuit diagrams — series, parallel, logic gates (Physics)
  @JsonValue('circuit')
  circuit,

  /// Geometric constructions, proofs, transformations (Math)
  @JsonValue('geometry')
  geometry,

  /// Molecular structures, chemical bonds, reactions (Chemistry)
  @JsonValue('molecular')
  molecular,

  /// Cell biology, organ systems, ecological diagrams (Biology)
  @JsonValue('biological')
  biological,

  /// Flowcharts for algorithms, data structures (CS)
  @JsonValue('flowchart')
  flowchart,

  /// Force diagrams, motion vectors, wave patterns (Physics)
  @JsonValue('physics_vector')
  physicsVector,

  /// Step-by-step worked example with progressive reveal (all subjects)
  @JsonValue('worked_example')
  workedExample,

  /// Interactive challenge card — SmartyMe style (all subjects)
  @JsonValue('challenge_card')
  challengeCard,
}

/// Diagram render format
enum DiagramFormat {
  /// Inline SVG — interactive, zoomable, tappable hotspots
  @JsonValue('svg')
  svg,

  /// Pre-rendered PNG — fallback for complex diagrams
  @JsonValue('png')
  png,

  /// Rive animation — animated diagrams (circuit flow, wave motion)
  @JsonValue('rive')
  rive,

  /// Remotion video — step-by-step explanation video
  @JsonValue('remotion_video')
  remotionVideo,
}

// ─────────────────────────────────────────────────────────────────────
// 2. DIAGRAM MODEL (cached artifact from generation pipeline)
// ─────────────────────────────────────────────────────────────────────

/// A pre-generated diagram for a specific concept at a specific difficulty.
/// Generated batch by Kimi K2.5, cached on S3/CDN, served instantly.
@freezed
class ConceptDiagram with _$ConceptDiagram {
  const factory ConceptDiagram({
    /// Unique diagram ID (UUIDv7)
    required String id,

    /// The concept this diagram illustrates
    required String conceptId,

    /// Subject (determines color palette)
    required String subject,

    /// Diagram type (determines rendering strategy)
    required DiagramType type,

    /// Render format
    required DiagramFormat format,

    /// Bloom's level this diagram targets
    /// recall = basic label diagram, analysis = interactive challenge
    required String bloomLevel,

    /// CDN URL for the diagram asset (SVG/PNG/Rive)
    required String assetUrl,

    /// Thumbnail URL (for knowledge graph node previews)
    required String thumbnailUrl,

    /// Inline SVG content (if format=svg and size < 50KB, embedded for offline)
    String? inlineSvg,

    /// Interactive hotspots — tap targets within the diagram
    @Default([]) List<DiagramHotspot> hotspots,

    /// Localized title displayed above diagram (Hebrew primary, Arabic, English fallback).
    required String titleHe,
    String? titleAr,
    String? titleEn,

    /// Localized description / challenge prompt.
    required String descriptionHe,
    String? descriptionAr,
    String? descriptionEn,

    /// Formula(s) shown on the diagram (LaTeX)
    @Default([]) List<String> formulas,

    /// Generation metadata
    required DiagramGenerationMeta generationMeta,

    /// Cache control
    required DiagramCacheMeta cacheMeta,
  }) = _ConceptDiagram;

  factory ConceptDiagram.fromJson(Map<String, dynamic> json) =>
      _$ConceptDiagramFromJson(json);
}

// ─────────────────────────────────────────────────────────────────────
// 3. HOTSPOTS (interactive tap targets within diagrams)
// ─────────────────────────────────────────────────────────────────────

/// A tappable region within a diagram that reveals an explanation.
/// Coordinates are relative to SVG viewBox (0-1 normalized).
@freezed
class DiagramHotspot with _$DiagramHotspot {
  const factory DiagramHotspot({
    /// Hotspot ID within the diagram
    required String id,

    /// SVG element ID to highlight on tap (for SVG format)
    String? svgElementId,

    /// Normalized bounding box (0.0-1.0 relative to diagram dimensions)
    required HotspotBounds bounds,

    /// Localized label shown on hover/tap (Hebrew primary, Arabic, English).
    required String labelHe,
    String? labelAr,
    String? labelEn,

    /// Localized explanation revealed on tap (supports markdown + LaTeX).
    required String explanationHe,
    String? explanationAr,
    String? explanationEn,

    /// Optional: link to prerequisite concept (tap to navigate in KG)
    String? linkedConceptId,

    /// Visual style
    @Default(HotspotStyle.outline) HotspotStyle style,
  }) = _DiagramHotspot;

  factory DiagramHotspot.fromJson(Map<String, dynamic> json) =>
      _$DiagramHotspotFromJson(json);
}

@freezed
class HotspotBounds with _$HotspotBounds {
  const factory HotspotBounds({
    required double x, // 0.0-1.0 from left
    required double y, // 0.0-1.0 from top
    required double width, // 0.0-1.0 fraction of diagram width
    required double height, // 0.0-1.0 fraction of diagram height
  }) = _HotspotBounds;

  factory HotspotBounds.fromJson(Map<String, dynamic> json) =>
      _$HotspotBoundsFromJson(json);
}

enum HotspotStyle {
  /// Dashed outline that pulses
  @JsonValue('outline')
  outline,

  /// Semi-transparent colored overlay
  @JsonValue('highlight')
  highlight,

  /// Small numbered circle (for sequential explanations)
  @JsonValue('numbered')
  numbered,

  /// Hidden until student taps "reveal all labels"
  @JsonValue('hidden')
  hidden,
}

// ─────────────────────────────────────────────────────────────────────
// 4. CHALLENGE CARD (SmartyMe-style interactive card)
// ─────────────────────────────────────────────────────────────────────

/// A game-like challenge card that combines a diagram with a question.
/// Inspired by SmartyMe's physics circuit cards.
@freezed
class ChallengeCard with _$ChallengeCard {
  const factory ChallengeCard({
    /// Card ID
    required String id,

    /// The underlying concept diagram
    required ConceptDiagram diagram,

    /// Challenge difficulty tier (visual: border glow color)
    required ChallengeTier tier,

    /// The challenge question (Hebrew)
    required String questionHe,

    /// Expected answer type
    required ChallengeAnswerType answerType,

    /// For MCQ: the options
    @Default([]) List<ChallengeOption> options,

    /// For numeric: expected value with tolerance
    double? expectedValue,
    double? tolerance,

    /// For expression: LaTeX pattern to match
    String? expectedExpression,

    /// Hint available on long-press
    String? hintHe,

    /// Points awarded for correct answer
    @Default(10) int xpReward,

    /// Unlocks next card in sequence? (game progression)
    String? nextCardId,
  }) = _ChallengeCard;

  factory ChallengeCard.fromJson(Map<String, dynamic> json) =>
      _$ChallengeCardFromJson(json);
}

enum ChallengeTier {
  /// Green glow — recall level
  @JsonValue('beginner')
  beginner,

  /// Blue glow — comprehension
  @JsonValue('intermediate')
  intermediate,

  /// Orange glow — application
  @JsonValue('advanced')
  advanced,

  /// Red glow — analysis/synthesis
  @JsonValue('expert')
  expert,
}

enum ChallengeAnswerType {
  @JsonValue('multiple_choice')
  multipleChoice,
  @JsonValue('numeric')
  numeric,
  @JsonValue('expression')
  expression,
  @JsonValue('drag_label')
  dragLabel,
  @JsonValue('tap_hotspot')
  tapHotspot,
}

@freezed
class ChallengeOption with _$ChallengeOption {
  const factory ChallengeOption({
    required String id,
    required String textHe,
    required bool isCorrect,

    /// Shown if student selects this wrong answer
    String? feedbackHe,
  }) = _ChallengeOption;

  factory ChallengeOption.fromJson(Map<String, dynamic> json) =>
      _$ChallengeOptionFromJson(json);
}

// ─────────────────────────────────────────────────────────────────────
// 5. GENERATION METADATA (from Kimi batch pipeline)
// ─────────────────────────────────────────────────────────────────────

@freezed
class DiagramGenerationMeta with _$DiagramGenerationMeta {
  const factory DiagramGenerationMeta({
    /// Model used (e.g., "kimi-k2.5")
    required String model,

    /// Generation timestamp
    required DateTime generatedAt,

    /// Curriculum version this diagram was generated against
    required String curriculumVersion,

    /// Expert review status
    required DiagramReviewStatus reviewStatus,

    /// If reviewed, who approved it
    String? reviewedBy,

    /// Generation cost (tokens)
    int? inputTokens,
    int? outputTokens,
  }) = _DiagramGenerationMeta;

  factory DiagramGenerationMeta.fromJson(Map<String, dynamic> json) =>
      _$DiagramGenerationMetaFromJson(json);
}

enum DiagramReviewStatus {
  @JsonValue('pending')
  pending,
  @JsonValue('approved')
  approved,
  @JsonValue('rejected')
  rejected,
  @JsonValue('auto_approved')
  autoApproved,
}

// ─────────────────────────────────────────────────────────────────────
// 6. CACHE METADATA (CDN + local cache management)
// ─────────────────────────────────────────────────────────────────────

@freezed
class DiagramCacheMeta with _$DiagramCacheMeta {
  const factory DiagramCacheMeta({
    /// S3 key (e.g., "diagrams/math/v1.2.0/derivatives-chain-rule-001.svg")
    required String s3Key,

    /// CDN URL with cache-busting hash
    required String cdnUrl,

    /// Content hash (SHA-256) for integrity verification
    required String contentHash,

    /// File size in bytes (for download budget management)
    required int sizeBytes,

    /// When this version was published to CDN
    required DateTime publishedAt,

    /// TTL: how long the client should cache this locally (hours)
    @Default(168) int clientCacheTtlHours,

    /// If true, this diagram should be pre-fetched for offline use
    @Default(false) bool prefetchForOffline,
  }) = _DiagramCacheMeta;

  factory DiagramCacheMeta.fromJson(Map<String, dynamic> json) =>
      _$DiagramCacheMetaFromJson(json);
}

// ─────────────────────────────────────────────────────────────────────
// 7. SUBJECT DESIGN TOKENS (consistent visual identity per STEM domain)
// ─────────────────────────────────────────────────────────────────────

/// Color palette per subject — used for diagram borders, hotspot highlights,
/// knowledge graph nodes, and challenge card glows.
class SubjectDiagramPalette {
  final int primary; // Main color (node fill, card border)
  final int primaryDim; // Background tint
  final int accent; // Hotspot highlight, formula color
  final int text; // Label text on diagrams

  const SubjectDiagramPalette({
    required this.primary,
    required this.primaryDim,
    required this.accent,
    required this.text,
  });

  // Defined palettes (matching system-overview.md design tokens)

  static const math = SubjectDiagramPalette(
    primary: 0xFF0891B2, // Teal
    primaryDim: 0x1A0891B2,
    accent: 0xFF06B6D4, // Cyan
    text: 0xFF134E4A,
  );

  static const physics = SubjectDiagramPalette(
    primary: 0xFFD97706, // Amber
    primaryDim: 0x1AD97706,
    accent: 0xFFF59E0B, // Yellow
    text: 0xFF78350F,
  );

  static const chemistry = SubjectDiagramPalette(
    primary: 0xFF059669, // Emerald
    primaryDim: 0x1A059669,
    accent: 0xFF10B981, // Green
    text: 0xFF064E3B,
  );

  static const biology = SubjectDiagramPalette(
    primary: 0xFF7C3AED, // Violet
    primaryDim: 0x1A7C3AED,
    accent: 0xFFA78BFA, // Purple
    text: 0xFF4C1D95,
  );

  static const cs = SubjectDiagramPalette(
    primary: 0xFF475569, // Slate
    primaryDim: 0x1A475569,
    accent: 0xFF64748B, // Gray
    text: 0xFF1E293B,
  );

  static SubjectDiagramPalette forSubject(String subject) {
    switch (subject.toLowerCase()) {
      case 'math':
      case 'mathematics':
        return math;
      case 'physics':
        return physics;
      case 'chemistry':
        return chemistry;
      case 'biology':
        return biology;
      case 'cs':
      case 'computer_science':
        return cs;
      default:
        return math;
    }
  }
}
