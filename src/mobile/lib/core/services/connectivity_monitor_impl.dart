// =============================================================================
// Cena Adaptive Learning Platform — ConnectivityMonitorImpl
// Uses connectivity_plus to detect network reachability changes.
// =============================================================================

import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:logger/logger.dart';

import 'offline_sync_service.dart';

/// Wraps [Connectivity] from `connectivity_plus`.
///
/// Connectivity state is not the same as internet reachability, but for the
/// Cena use-case (WebSocket over HTTPS) it is a reliable enough proxy.
/// Full reachability checks are handled by [SyncManagerImpl] before sync.
class ConnectivityMonitorImpl implements ConnectivityMonitor {
  ConnectivityMonitorImpl() {
    _connectivity = Connectivity();
    _init();
  }

  late final Connectivity _connectivity;
  final _controller = StreamController<bool>.broadcast();
  bool _isOnline = true;
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));
  StreamSubscription<List<ConnectivityResult>>? _subscription;

  void _init() {
    // Prime the initial state.
    _connectivity.checkConnectivity().then(_handleResults).catchError((Object e) {
      _logger.w('ConnectivityMonitor: initial check failed — $e');
    });

    // Listen for changes.
    _subscription = _connectivity.onConnectivityChanged.listen(
      _handleResults,
      onError: (Object e) {
        _logger.w('ConnectivityMonitor: stream error — $e');
      },
    );
  }

  void _handleResults(List<ConnectivityResult> results) {
    final online = results.any((r) =>
        r == ConnectivityResult.mobile ||
        r == ConnectivityResult.wifi ||
        r == ConnectivityResult.ethernet);
    if (online != _isOnline) {
      _isOnline = online;
      _logger.d('ConnectivityMonitor: ${online ? "online" : "offline"}');
      _controller.add(online);
    }
  }

  @override
  Stream<bool> get onConnectivityChanged => _controller.stream;

  @override
  bool get isOnline => _isOnline;

  /// Release resources. Call when the app is destroyed.
  void dispose() {
    _subscription?.cancel();
    _controller.close();
  }
}
