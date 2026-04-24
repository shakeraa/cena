// =============================================================================
// Cena Adaptive Learning Platform — Deep Study Session
// Extended session format with multiple flow-arc blocks separated by
// recovery breaks. Designed for sustained focus periods of 45-90 minutes.
// =============================================================================

// ---------------------------------------------------------------------------
// Deep Study Block Types
// ---------------------------------------------------------------------------

/// The type of content focus for a [DeepStudyBlock].
enum DeepStudyBlockType {
  /// Review previously seen material + introduce new concepts.
  review,

  /// Push deeper into challenging material at higher Bloom levels.
  deep,

  /// Synthesize across concepts, connect ideas, apply to new contexts.
  synthesis,
}

// ---------------------------------------------------------------------------
// Deep Study Block
// ---------------------------------------------------------------------------

/// A single block within a [DeepStudySession].
///
/// Each block is essentially a mini flow-arc (warm-up / core / cool-down)
/// with a specific content focus. Blocks are separated by recovery breaks.
class DeepStudyBlock {
  const DeepStudyBlock({
    required this.blockNumber,
    required this.durationMinutes,
    required this.type,
  });

  /// 1-based block number within the session.
  final int blockNumber;

  /// Duration of this block in minutes (excluding break time).
  final int durationMinutes;

  /// Content focus type for this block.
  final DeepStudyBlockType type;

  /// Estimated number of questions in this block.
  ///
  /// Assumes ~1.5 minutes per question on average (accounting for
  /// reading, thinking, answering, and reviewing feedback).
  int get estimatedQuestions => (durationMinutes / 1.5).floor().clamp(5, 40);

  @override
  String toString() =>
      'DeepStudyBlock(#$blockNumber, ${durationMinutes}min, $type)';

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is DeepStudyBlock &&
          blockNumber == other.blockNumber &&
          durationMinutes == other.durationMinutes &&
          type == other.type;

  @override
  int get hashCode => Object.hash(blockNumber, durationMinutes, type);
}

// ---------------------------------------------------------------------------
// Allowed Durations
// ---------------------------------------------------------------------------

/// The set of allowed deep study session durations.
abstract class DeepStudyDurations {
  static const List<int> allowed = [45, 60, 75, 90];
  static const int defaultDuration = 60;

  /// Validates that [minutes] is an allowed duration.
  static bool isValid(int minutes) => allowed.contains(minutes);
}

// ---------------------------------------------------------------------------
// Deep Study Session
// ---------------------------------------------------------------------------

/// A deep study session divides an extended time period into 2-3 flow-arc
/// blocks with 5-minute recovery breaks between them.
///
/// Block structure by duration:
///   - **45 min**: Block 1 (review 20min) -> Break -> Block 2 (deep 20min)
///   - **60 min**: Block 1 (review 20min) -> Break -> Block 2 (deep 25min)
///                 -> Break -> Block 3 (synthesis 5min)
///   - **75 min**: Block 1 (review 25min) -> Break -> Block 2 (deep 25min)
///                 -> Break -> Block 3 (synthesis 10min)
///   - **90 min**: Block 1 (review 25min) -> Break -> Block 2 (deep 30min)
///                 -> Break -> Block 3 (synthesis 20min)
class DeepStudySession {
  DeepStudySession({
    required this.totalDurationMinutes,
  }) : assert(
          DeepStudyDurations.isValid(totalDurationMinutes),
          'Duration must be one of ${DeepStudyDurations.allowed}',
        ) {
    _blocks = _buildBlocks();
  }

  /// Total session duration in minutes (45, 60, 75, or 90).
  final int totalDurationMinutes;

  /// The computed blocks for this session.
  late final List<DeepStudyBlock> _blocks;

  /// Recovery break duration in minutes.
  static const int breakDurationMinutes = 5;

  /// The ordered list of study blocks.
  List<DeepStudyBlock> get blocks => List.unmodifiable(_blocks);

  /// Number of breaks between blocks.
  int get breakCount => (_blocks.length - 1).clamp(0, 10);

  /// Total break time in minutes.
  int get totalBreakMinutes => breakCount * breakDurationMinutes;

  /// Total active study time (excluding breaks).
  int get totalStudyMinutes => totalDurationMinutes - totalBreakMinutes;

  /// Total number of blocks.
  int get blockCount => _blocks.length;

  /// Returns the block at [index] (0-based).
  DeepStudyBlock blockAt(int index) {
    assert(index >= 0 && index < _blocks.length);
    return _blocks[index];
  }

  /// Whether the session has a synthesis block (3-block sessions).
  bool get hasSynthesisBlock =>
      _blocks.any((b) => b.type == DeepStudyBlockType.synthesis);

  /// Builds the block layout based on total duration.
  ///
  /// The algorithm:
  ///   1. Subtract break time from total duration.
  ///   2. Allocate study minutes across blocks based on the session length.
  ///   3. Shorter sessions (45min) get 2 blocks; longer ones get 3.
  List<DeepStudyBlock> _buildBlocks() {
    switch (totalDurationMinutes) {
      case 45:
        // 2 blocks: 20 + 5(break) + 20 = 45
        return const [
          DeepStudyBlock(
            blockNumber: 1,
            durationMinutes: 20,
            type: DeepStudyBlockType.review,
          ),
          DeepStudyBlock(
            blockNumber: 2,
            durationMinutes: 20,
            type: DeepStudyBlockType.deep,
          ),
        ];
      case 60:
        // 3 blocks: 20 + 5(break) + 25 + 5(break) + 5 = 60
        return const [
          DeepStudyBlock(
            blockNumber: 1,
            durationMinutes: 20,
            type: DeepStudyBlockType.review,
          ),
          DeepStudyBlock(
            blockNumber: 2,
            durationMinutes: 25,
            type: DeepStudyBlockType.deep,
          ),
          DeepStudyBlock(
            blockNumber: 3,
            durationMinutes: 5,
            type: DeepStudyBlockType.synthesis,
          ),
        ];
      case 75:
        // 3 blocks: 25 + 5(break) + 25 + 5(break) + 10 = 70 + 5 = 75
        // Actually: 25 + 25 + 15 study = 65min + 10min breaks = 75
        return const [
          DeepStudyBlock(
            blockNumber: 1,
            durationMinutes: 25,
            type: DeepStudyBlockType.review,
          ),
          DeepStudyBlock(
            blockNumber: 2,
            durationMinutes: 25,
            type: DeepStudyBlockType.deep,
          ),
          DeepStudyBlock(
            blockNumber: 3,
            durationMinutes: 15,
            type: DeepStudyBlockType.synthesis,
          ),
        ];
      case 90:
        // 3 blocks: 25 + 30 + 20 study = 75min + 15min breaks
        // Wait — 3 blocks means 2 breaks = 10min. 90-10=80 study.
        // 25 + 30 + 25 = 80
        return const [
          DeepStudyBlock(
            blockNumber: 1,
            durationMinutes: 25,
            type: DeepStudyBlockType.review,
          ),
          DeepStudyBlock(
            blockNumber: 2,
            durationMinutes: 30,
            type: DeepStudyBlockType.deep,
          ),
          DeepStudyBlock(
            blockNumber: 3,
            durationMinutes: 25,
            type: DeepStudyBlockType.synthesis,
          ),
        ];
      default:
        // Fallback — should not happen due to assertion.
        return const [
          DeepStudyBlock(
            blockNumber: 1,
            durationMinutes: 25,
            type: DeepStudyBlockType.review,
          ),
          DeepStudyBlock(
            blockNumber: 2,
            durationMinutes: 25,
            type: DeepStudyBlockType.deep,
          ),
        ];
    }
  }

  @override
  String toString() =>
      'DeepStudySession(${totalDurationMinutes}min, '
      '${_blocks.length} blocks, $breakCount breaks)';
}
