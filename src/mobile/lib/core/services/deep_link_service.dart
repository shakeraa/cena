// =============================================================================
// Cena Adaptive Learning Platform — Deep Link Service
// Parses incoming deep links and maps them to GoRouter routes.
// =============================================================================

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

import '../router.dart';

final _logger = Logger(
  printer: PrettyPrinter(
    methodCount: 0,
    dateTimeFormat: DateTimeFormat.onlyTimeAndSinceStart,
  ),
);

// ---------------------------------------------------------------------------
// Deep Link Parsing
// ---------------------------------------------------------------------------

/// Parsed representation of an incoming deep link.
@immutable
class DeepLinkResult {
  const DeepLinkResult({
    required this.routePath,
    this.pathParameters = const {},
    this.queryParameters = const {},
  });

  /// GoRouter path to navigate to, e.g. "/session/abc-123".
  final String routePath;

  /// Named path parameters extracted from the URL.
  final Map<String, String> pathParameters;

  /// Query parameters from the URL.
  final Map<String, String> queryParameters;

  @override
  String toString() =>
      'DeepLinkResult(route=$routePath, params=$pathParameters, query=$queryParameters)';
}

// ---------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------

/// Stateless service that parses deep link URIs into routable paths.
///
/// Supports two schemes:
///   - `https://app.cena.education/<path>`  (Universal Links / App Links)
///   - `cena://<path>`                      (Custom scheme)
///
/// Recognized paths:
///   - `/session/:id`  -> session detail
///   - `/graph`        -> knowledge graph
///   - `/profile`      -> student profile
///   - `/onboarding`   -> onboarding flow
///   - `/home`         -> home screen
///   - `/login`        -> authentication screen
class DeepLinkService {
  const DeepLinkService();

  /// Known host for universal links.
  static const String _universalHost = 'app.cena.education';

  /// Custom URL scheme.
  static const String _customScheme = 'cena';

  /// Attempts to parse [uri] into a [DeepLinkResult].
  ///
  /// Returns `null` if the URI is not a recognized Cena deep link.
  DeepLinkResult? parse(Uri uri) {
    if (!_isRecognizedUri(uri)) {
      _logger.w('Unrecognized deep link URI: $uri');
      return null;
    }

    final path = uri.path.isEmpty ? '/' : uri.path;
    final queryParams = uri.queryParameters;

    _logger.i('Parsing deep link: scheme=${uri.scheme}, path=$path');

    return _matchRoute(path, queryParams);
  }

  /// Parses a raw string URI. Returns `null` on parse failure or unrecognized
  /// link.
  DeepLinkResult? parseString(String uriString) {
    final uri = Uri.tryParse(uriString);
    if (uri == null) {
      _logger.w('Failed to parse deep link string: $uriString');
      return null;
    }
    return parse(uri);
  }

  /// Maps a deferred deep link path (stored before app was initialized) to a
  /// [DeepLinkResult]. Use this when the app starts cold from a deep link and
  /// needs to navigate after initialization completes.
  DeepLinkResult? resolveDeferredPath(String path) {
    _logger.i('Resolving deferred deep link path: $path');
    return _matchRoute(path, const {});
  }

  // ---- Private helpers ----

  bool _isRecognizedUri(Uri uri) {
    // Custom scheme: cena://...
    if (uri.scheme == _customScheme) return true;

    // Universal link: https://app.cena.education/...
    if ((uri.scheme == 'https' || uri.scheme == 'http') &&
        uri.host == _universalHost) {
      return true;
    }

    return false;
  }

  DeepLinkResult? _matchRoute(
    String path,
    Map<String, String> queryParams,
  ) {
    // Normalize: strip trailing slash, ensure leading slash.
    final normalized = _normalizePath(path);

    // /session/:id
    final sessionMatch = RegExp(r'^/session/([^/]+)$').firstMatch(normalized);
    if (sessionMatch != null) {
      final sessionId = sessionMatch.group(1)!;
      return DeepLinkResult(
        routePath: '/session/$sessionId',
        pathParameters: {'id': sessionId},
        queryParameters: queryParams,
      );
    }

    // Exact matches for known routes.
    const knownRoutes = {
      '/session': CenaRoutes.session,
      '/graph': CenaRoutes.graph,
      '/profile': CenaRoutes.profile,
      '/onboarding': CenaRoutes.onboarding,
      '/home': CenaRoutes.home,
      '/login': CenaRoutes.login,
    };

    final matched = knownRoutes[normalized];
    if (matched != null) {
      return DeepLinkResult(
        routePath: matched,
        queryParameters: queryParams,
      );
    }

    _logger.w('No route match for deep link path: $normalized');
    return null;
  }

  String _normalizePath(String path) {
    var normalized = path;
    if (!normalized.startsWith('/')) normalized = '/$normalized';
    if (normalized.length > 1 && normalized.endsWith('/')) {
      normalized = normalized.substring(0, normalized.length - 1);
    }
    return normalized;
  }
}

// ---------------------------------------------------------------------------
// Deferred Deep Link Storage
// ---------------------------------------------------------------------------

/// Holds a deep link that arrived before the app finished initialization.
///
/// The router redirect checks this and navigates after auth/onboarding gates
/// are resolved. Once consumed, the value is cleared.
class DeferredDeepLink {
  DeferredDeepLink._();

  static final DeferredDeepLink instance = DeferredDeepLink._();

  String? _pendingPath;

  /// Store a path to navigate to after initialization.
  void store(String path) {
    _logger.i('Storing deferred deep link: $path');
    _pendingPath = path;
  }

  /// Consume and clear the pending path. Returns `null` if nothing pending.
  String? consume() {
    final path = _pendingPath;
    _pendingPath = null;
    if (path != null) {
      _logger.i('Consumed deferred deep link: $path');
    }
    return path;
  }

  /// Whether a deferred link is waiting.
  bool get hasPending => _pendingPath != null;
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

/// Riverpod provider for the deep link service — stateless singleton.
final deepLinkServiceProvider = Provider<DeepLinkService>((ref) {
  return const DeepLinkService();
});
