// =============================================================================
// Cena Adaptive Learning Platform — Moderation Pipeline (MOB-046)
// =============================================================================
// Client-side pre-filter for user-generated content before backend evaluation.
// Checks for profanity, PII, and length limits. Tracks reports for auto-hide.
// =============================================================================

import 'age_safety_service.dart';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

/// Overall moderation verdict.
enum ModerationResult {
  /// Content passes all checks.
  safe,

  /// Content has potential issues, flagged for human review.
  reviewNeeded,

  /// Content is blocked and must not be displayed.
  blocked,
}

/// Categories of flagged content.
enum ModerationCategory {
  profanity,
  pii,
  lengthExceeded,
  spam,
  harassment,
}

// ---------------------------------------------------------------------------
// Decision model
// ---------------------------------------------------------------------------

/// Result of evaluating a piece of content.
class ModerationDecision {
  const ModerationDecision({
    required this.result,
    required this.confidence,
    this.flaggedCategories = const [],
  });

  final ModerationResult result;

  /// Confidence score [0.0, 1.0] for the decision.
  final double confidence;

  /// Categories that triggered the decision.
  final List<ModerationCategory> flaggedCategories;

  /// Convenience for checking if content is safe to display.
  bool get isSafe => result == ModerationResult.safe;
}

// ---------------------------------------------------------------------------
// Content report
// ---------------------------------------------------------------------------

/// A user-submitted report against a piece of content.
class ContentReport {
  const ContentReport({
    required this.contentId,
    required this.reporterId,
    required this.timestamp,
    required this.reason,
  });

  final String contentId;
  final String reporterId;
  final DateTime timestamp;
  final String reason;

  factory ContentReport.fromJson(Map<String, dynamic> json) {
    return ContentReport(
      contentId: json['contentId'] as String? ?? '',
      reporterId: json['reporterId'] as String? ?? '',
      timestamp: json['timestamp'] != null
          ? DateTime.parse(json['timestamp'] as String)
          : DateTime.now(),
      reason: json['reason'] as String? ?? '',
    );
  }

  Map<String, dynamic> toJson() => {
        'contentId': contentId,
        'reporterId': reporterId,
        'timestamp': timestamp.toIso8601String(),
        'reason': reason,
      };
}

// ---------------------------------------------------------------------------
// Moderation Service
// ---------------------------------------------------------------------------

/// Client-side content moderation pre-filter.
///
/// Performs lightweight checks before sending content to the backend:
/// - Basic profanity detection (English, Hebrew, Arabic patterns)
/// - PII detection (email, phone number patterns)
/// - Length limits (max 280 characters)
/// - Report tracking with auto-hide at 3 unique reporters
///
/// This is a first-pass filter. The backend performs deeper AI-based
/// analysis for anything that passes client-side checks.
class ModerationService {
  ModerationService({AgeSafetyService? ageSafetyService})
      : _ageSafetyService = ageSafetyService;

  final AgeSafetyService? _ageSafetyService;

  /// Content ID -> set of reporter IDs who have reported it.
  final Map<String, Set<String>> _reports = {};

  /// Content IDs that have been auto-hidden due to reports.
  final Set<String> _autoHiddenContent = {};

  /// Number of unique reports required to auto-hide content.
  static const int autoHideThreshold = 3;

  /// Maximum character length for posts.
  static const int maxPostLength = 280;

  // ---- Evaluation ----

  /// Evaluate a piece of text content for moderation issues.
  /// Returns a [ModerationDecision] with the verdict and flagged categories.
  ModerationDecision evaluate(String text) {
    final categories = <ModerationCategory>[];
    double worstConfidence = 1.0;

    // Length check.
    if (text.length > maxPostLength) {
      categories.add(ModerationCategory.lengthExceeded);
      worstConfidence = 0.99;
    }

    // PII detection.
    if (_containsPii(text)) {
      categories.add(ModerationCategory.pii);
      worstConfidence = 0.95;
    }

    // Profanity check.
    final profanityResult = _checkProfanity(text);
    if (profanityResult != null) {
      categories.add(profanityResult);
      worstConfidence = 0.90;
    }

    if (categories.isEmpty) {
      return const ModerationDecision(
        result: ModerationResult.safe,
        confidence: 1.0,
      );
    }

    // PII and profanity are hard blocks; length is a review.
    final hasCritical = categories.contains(ModerationCategory.pii) ||
        categories.contains(ModerationCategory.profanity);

    return ModerationDecision(
      result: hasCritical ? ModerationResult.blocked : ModerationResult.reviewNeeded,
      confidence: worstConfidence,
      flaggedCategories: categories,
    );
  }

  // ---- PII detection ----

  /// Detect personally identifiable information in text.
  bool _containsPii(String text) {
    // Email pattern.
    final emailPattern = RegExp(
      r'[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}',
      caseSensitive: false,
    );
    if (emailPattern.hasMatch(text)) return true;

    // Phone number patterns (international and local).
    // Israeli: 05X-XXXXXXX, 0X-XXXXXXX, +972-XX-XXXXXXX
    // General: sequences of 7+ digits with optional separators.
    final phonePattern = RegExp(
      r'(?:\+?\d{1,3}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}',
    );
    if (phonePattern.hasMatch(text)) return true;

    // Israeli ID number pattern (9 digits).
    final israeliIdPattern = RegExp(r'\b\d{9}\b');
    if (israeliIdPattern.hasMatch(text)) return true;

    return false;
  }

  // ---- Profanity check ----

  /// Check for profanity in English, Hebrew, and Arabic.
  /// Returns [ModerationCategory.profanity] if found, null otherwise.
  ModerationCategory? _checkProfanity(String text) {
    final normalized = text.toLowerCase().trim();

    // English slur/profanity patterns.
    // Using pattern matching rather than a word list for broader coverage.
    for (final pattern in _englishProfanityPatterns) {
      if (pattern.hasMatch(normalized)) {
        return ModerationCategory.profanity;
      }
    }

    // Hebrew profanity patterns (common slurs and insults).
    for (final pattern in _hebrewProfanityPatterns) {
      if (pattern.hasMatch(normalized)) {
        return ModerationCategory.profanity;
      }
    }

    // Arabic profanity patterns (common slurs and insults).
    for (final pattern in _arabicProfanityPatterns) {
      if (pattern.hasMatch(normalized)) {
        return ModerationCategory.profanity;
      }
    }

    return null;
  }

  // Common English profanity patterns (asterisk-censored for readability,
  // actual patterns match character sequences).
  static final List<RegExp> _englishProfanityPatterns = [
    RegExp(r'\bf+u+c+k+\b', caseSensitive: false),
    RegExp(r'\bs+h+i+t+\b', caseSensitive: false),
    RegExp(r'\ba+s+s+h+o+l+e+\b', caseSensitive: false),
    RegExp(r'\bb+i+t+c+h+\b', caseSensitive: false),
    RegExp(r'\bd+a+m+n+\b', caseSensitive: false),
    RegExp(r'\bh+e+l+l+\b', caseSensitive: false),
    RegExp(r'\bn+i+g+g+', caseSensitive: false),
    RegExp(r'\bf+a+g+\b', caseSensitive: false),
    RegExp(r'\bc+u+n+t+\b', caseSensitive: false),
    RegExp(r'\bd+i+c+k+\b', caseSensitive: false),
    RegExp(r'\bw+h+o+r+e+\b', caseSensitive: false),
    RegExp(r'\bs+l+u+t+\b', caseSensitive: false),
    RegExp(r'\br+e+t+a+r+d+', caseSensitive: false),
    RegExp(r'\bk+i+l+l\s+y+o+u+r+s+e+l+f+\b', caseSensitive: false),
  ];

  // Hebrew profanity patterns.
  static final List<RegExp> _hebrewProfanityPatterns = [
    RegExp(r'זונה'),
    RegExp(r'כוס\s*אמ'),
    RegExp(r'בן\s*זונה'),
    RegExp(r'מניאק'),
    RegExp(r'חרא'),
    RegExp(r'זיין'),
    RegExp(r'תמות'),
    RegExp(r'מטומטם'),
  ];

  // Arabic profanity patterns.
  static final List<RegExp> _arabicProfanityPatterns = [
    RegExp(r'كلب'),
    RegExp(r'حمار'),
    RegExp(r'شرموط'),
    RegExp(r'عاهر'),
    RegExp(r'كس\s*ام'),
    RegExp(r'ابن\s*الكلب'),
    RegExp(r'زبال'),
  ];

  // ---- Reporting ----

  /// Report a piece of content. Tracks unique reporters per content ID.
  void reportContent(String contentId, String reporterId) {
    _reports.putIfAbsent(contentId, () => {});
    _reports[contentId]?.add(reporterId);

    // Auto-hide when threshold is reached.
    if (_reports[contentId]!.length >= autoHideThreshold) {
      _autoHiddenContent.add(contentId);
    }
  }

  /// Whether a content item should be auto-hidden based on report count.
  /// Returns true when 3 or more unique users have reported it.
  bool shouldAutoHide(String contentId) {
    return _autoHiddenContent.contains(contentId);
  }

  /// Get the number of unique reporters for a content item.
  int reportCount(String contentId) {
    return _reports[contentId]?.length ?? 0;
  }

  // ---- Rate limit enforcement ----

  /// Check if the student can perform a social action based on age tier limits.
  bool canPost(String studentId, AgeTier tier) {
    if (_ageSafetyService == null) return true;
    return _ageSafetyService.canPerformSocialAction(studentId, tier);
  }

  /// Record that a social action was performed (for rate limiting).
  void recordAction(String studentId) {
    _ageSafetyService?.recordSocialAction(studentId);
  }
}
