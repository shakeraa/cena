// =============================================================================
// Cena Adaptive Learning Platform — Age-Tiered Social Safety Matrix (MOB-045)
// =============================================================================
// Enforces COPPA compliance and age-appropriate social feature access.
// Under-13 students require parental consent before ANY social feature.
// =============================================================================

// ---------------------------------------------------------------------------
// Age tiers
// ---------------------------------------------------------------------------

/// Age tier classification for safety and compliance gating.
enum AgeTier {
  /// Ages 6-9: No social features at all.
  tier1_6to9,

  /// Ages 10-12: Class aggregate stats only, pre-set reactions, no peer comparison.
  /// COPPA parental consent required before any social feature.
  tier2_10to12,

  /// Ages 13-15: Optional lateral comparison, study groups, moderated text.
  tier3_13to15,

  /// Ages 16+: Full social suite.
  tier4_16plus,
}

// ---------------------------------------------------------------------------
// Social features
// ---------------------------------------------------------------------------

/// Enumeration of all social features that can be gated by age tier.
enum SocialFeature {
  classFeed,
  reactions,
  textResponses,
  leaderboards,
  peerSolutions,
  accountabilityPartners,
}

// ---------------------------------------------------------------------------
// Feature access result
// ---------------------------------------------------------------------------

/// Result of a feature access check.
class FeatureAccess {
  const FeatureAccess({
    required this.allowed,
    required this.reason,
  });

  /// Whether the feature is allowed for the given tier.
  final bool allowed;

  /// Human-readable reason for the decision.
  final String reason;

  static const FeatureAccess granted = FeatureAccess(
    allowed: true,
    reason: 'Feature is available for your age group.',
  );
}

// ---------------------------------------------------------------------------
// Consent
// ---------------------------------------------------------------------------

/// Status of parental consent for social features (COPPA compliance).
enum ConsentStatus {
  /// Consent has not been requested yet.
  pending,

  /// Parent or guardian has granted consent.
  parentalConsentGranted,

  /// Consent granted and student has activated social features.
  active,

  /// Consent has been explicitly revoked by parent or guardian.
  revoked,
}

/// Tracks consent state for a student's social feature access.
class ConsentState {
  const ConsentState({
    required this.studentId,
    required this.ageTier,
    required this.consentStatus,
    this.grantedFeatures = const [],
    this.revokedAt,
  });

  final String studentId;
  final AgeTier ageTier;
  final ConsentStatus consentStatus;

  /// List of features explicitly granted by parental consent.
  final List<SocialFeature> grantedFeatures;

  /// When consent was revoked, if applicable.
  final DateTime? revokedAt;

  /// Whether any social features are currently active.
  bool get hasSocialAccess =>
      consentStatus == ConsentStatus.active ||
      consentStatus == ConsentStatus.parentalConsentGranted;

  factory ConsentState.fromJson(Map<String, dynamic> json) {
    return ConsentState(
      studentId: json['studentId'] as String? ?? '',
      ageTier: _parseAgeTier(json['ageTier'] as String?),
      consentStatus: _parseConsentStatus(json['consentStatus'] as String?),
      grantedFeatures: (json['grantedFeatures'] as List<dynamic>?)
              ?.map((e) => _parseSocialFeature(e as String))
              .toList() ??
          const [],
      revokedAt: json['revokedAt'] != null
          ? DateTime.parse(json['revokedAt'] as String)
          : null,
    );
  }

  Map<String, dynamic> toJson() => {
        'studentId': studentId,
        'ageTier': ageTier.name,
        'consentStatus': consentStatus.name,
        'grantedFeatures': grantedFeatures.map((f) => f.name).toList(),
        if (revokedAt != null) 'revokedAt': revokedAt!.toIso8601String(),
      };
}

// ---------------------------------------------------------------------------
// Rate limiting
// ---------------------------------------------------------------------------

/// Rate limit configuration per age tier.
class _RateLimitConfig {
  const _RateLimitConfig({
    required this.maxSocialActionsPerDay,
  });

  final int maxSocialActionsPerDay;
}

// ---------------------------------------------------------------------------
// Age Safety Service
// ---------------------------------------------------------------------------

/// Enforces age-tiered access to social features with COPPA compliance.
///
/// Feature access matrix:
/// - Tier 1 (6-9):  No social features.
/// - Tier 2 (10-12): Class aggregate stats only, pre-set reactions, no peer comparison.
///                    COPPA parental consent required.
/// - Tier 3 (13-15): Optional lateral comparison, study groups, moderated text.
/// - Tier 4 (16+):   Full social suite.
class AgeSafetyService {
  AgeSafetyService({ConsentState? consentState})
      : _consentState = consentState;

  ConsentState? _consentState;

  /// Track daily action counts per student for rate limiting.
  final Map<String, int> _dailyActionCounts = {};

  /// The date for which action counts are tracked.
  DateTime? _actionCountDate;

  // ---- Age tier resolution ----

  /// Determine the age tier for a given age.
  AgeTier getAgeTier(int age) {
    if (age < 6) return AgeTier.tier1_6to9;
    if (age <= 9) return AgeTier.tier1_6to9;
    if (age <= 12) return AgeTier.tier2_10to12;
    if (age <= 15) return AgeTier.tier3_13to15;
    return AgeTier.tier4_16plus;
  }

  // ---- Feature access checks ----

  /// Check whether a feature is accessible for the given age tier.
  /// Takes into account the tier's allowed features and consent status.
  FeatureAccess canAccessFeature(AgeTier tier, SocialFeature feature) {
    // Tier 1: no social features at all.
    if (tier == AgeTier.tier1_6to9) {
      return const FeatureAccess(
        allowed: false,
        reason: 'Social features are not available for ages 6-9.',
      );
    }

    // Tier 2: restricted features only, COPPA consent required.
    if (tier == AgeTier.tier2_10to12) {
      return _checkTier2Access(feature);
    }

    // Tier 3: most features with moderation.
    if (tier == AgeTier.tier3_13to15) {
      return _checkTier3Access(feature);
    }

    // Tier 4: full access.
    return FeatureAccess.granted;
  }

  FeatureAccess _checkTier2Access(SocialFeature feature) {
    // COPPA: Under-13 requires parental consent for ANY social feature.
    if (_consentState == null || !_consentState!.hasSocialAccess) {
      return const FeatureAccess(
        allowed: false,
        reason: 'Parental consent is required for social features (COPPA).',
      );
    }

    switch (feature) {
      case SocialFeature.classFeed:
        // Aggregate stats only — no individual names.
        return FeatureAccess.granted;
      case SocialFeature.reactions:
        // Pre-set reactions only (thumbs up, star, clap).
        return FeatureAccess.granted;
      case SocialFeature.textResponses:
        return const FeatureAccess(
          allowed: false,
          reason: 'Free text responses are not available for ages 10-12.',
        );
      case SocialFeature.leaderboards:
        return const FeatureAccess(
          allowed: false,
          reason: 'Peer comparison is not available for ages 10-12.',
        );
      case SocialFeature.peerSolutions:
        return const FeatureAccess(
          allowed: false,
          reason: 'Peer solutions are not available for ages 10-12.',
        );
      case SocialFeature.accountabilityPartners:
        return const FeatureAccess(
          allowed: false,
          reason: 'Accountability partners are not available for ages 10-12.',
        );
    }
  }

  FeatureAccess _checkTier3Access(SocialFeature feature) {
    switch (feature) {
      case SocialFeature.classFeed:
        return FeatureAccess.granted;
      case SocialFeature.reactions:
        return FeatureAccess.granted;
      case SocialFeature.textResponses:
        // Moderated text responses.
        return const FeatureAccess(
          allowed: true,
          reason: 'Text responses are available with moderation for ages 13-15.',
        );
      case SocialFeature.leaderboards:
        // Optional lateral comparison.
        return const FeatureAccess(
          allowed: true,
          reason: 'Opt-in leaderboards available for ages 13-15.',
        );
      case SocialFeature.peerSolutions:
        return FeatureAccess.granted;
      case SocialFeature.accountabilityPartners:
        return FeatureAccess.granted;
    }
  }

  // ---- Consent management ----

  /// Update the consent state for the current student.
  void updateConsentState(ConsentState consentState) {
    _consentState = consentState;
  }

  /// Get the current consent state.
  ConsentState? get consentState => _consentState;

  /// Whether the student requires consent before social feature access.
  bool requiresConsent(AgeTier tier) {
    // COPPA applies to under-13 (tiers 1 and 2).
    return tier == AgeTier.tier1_6to9 || tier == AgeTier.tier2_10to12;
  }

  // ---- Rate limiting ----

  /// Get the rate limit configuration for an age tier.
  _RateLimitConfig _rateLimitFor(AgeTier tier) {
    switch (tier) {
      case AgeTier.tier1_6to9:
        return const _RateLimitConfig(maxSocialActionsPerDay: 0);
      case AgeTier.tier2_10to12:
        return const _RateLimitConfig(maxSocialActionsPerDay: 5);
      case AgeTier.tier3_13to15:
        return const _RateLimitConfig(maxSocialActionsPerDay: 15);
      case AgeTier.tier4_16plus:
        return const _RateLimitConfig(maxSocialActionsPerDay: 30);
    }
  }

  /// Check if a social action is allowed under rate limits.
  /// Returns true if the action is permitted, false if rate limited.
  bool canPerformSocialAction(String studentId, AgeTier tier) {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);

    // Reset counters on new day.
    if (_actionCountDate == null || _actionCountDate != today) {
      _dailyActionCounts.clear();
      _actionCountDate = today;
    }

    final limit = _rateLimitFor(tier);
    final currentCount = _dailyActionCounts[studentId] ?? 0;
    return currentCount < limit.maxSocialActionsPerDay;
  }

  /// Record a social action for rate limiting.
  void recordSocialAction(String studentId) {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);

    if (_actionCountDate == null || _actionCountDate != today) {
      _dailyActionCounts.clear();
      _actionCountDate = today;
    }

    _dailyActionCounts[studentId] =
        (_dailyActionCounts[studentId] ?? 0) + 1;
  }

  /// Get remaining social actions for today.
  int remainingActions(String studentId, AgeTier tier) {
    final limit = _rateLimitFor(tier);
    final currentCount = _dailyActionCounts[studentId] ?? 0;
    return (limit.maxSocialActionsPerDay - currentCount).clamp(0, 999);
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

AgeTier _parseAgeTier(String? value) {
  switch (value) {
    case 'tier1_6to9':
      return AgeTier.tier1_6to9;
    case 'tier2_10to12':
      return AgeTier.tier2_10to12;
    case 'tier3_13to15':
      return AgeTier.tier3_13to15;
    case 'tier4_16plus':
      return AgeTier.tier4_16plus;
    default:
      return AgeTier.tier1_6to9;
  }
}

ConsentStatus _parseConsentStatus(String? value) {
  switch (value) {
    case 'pending':
      return ConsentStatus.pending;
    case 'parentalConsentGranted':
      return ConsentStatus.parentalConsentGranted;
    case 'active':
      return ConsentStatus.active;
    case 'revoked':
      return ConsentStatus.revoked;
    default:
      return ConsentStatus.pending;
  }
}

SocialFeature _parseSocialFeature(String value) {
  switch (value) {
    case 'classFeed':
      return SocialFeature.classFeed;
    case 'reactions':
      return SocialFeature.reactions;
    case 'textResponses':
      return SocialFeature.textResponses;
    case 'leaderboards':
      return SocialFeature.leaderboards;
    case 'peerSolutions':
      return SocialFeature.peerSolutions;
    case 'accountabilityPartners':
      return SocialFeature.accountabilityPartners;
    default:
      return SocialFeature.classFeed;
  }
}
