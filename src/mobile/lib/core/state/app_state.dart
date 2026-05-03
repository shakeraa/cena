// =============================================================================
// Cena Adaptive Learning Platform — Core Application State
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/app_config.dart';
import '../models/domain_models.dart';
import '../services/api_client.dart';

/// Provider for the API client, configured with the current environment.
final apiClientProvider = Provider<ApiClient>((ref) {
  final config = ref.watch(appConfigProvider);
  return ApiClient(config: config);
});

/// Provider for the app configuration. Overridden at startup.
final appConfigProvider = Provider<AppConfig>((ref) {
  return AppConfig.forEnvironment(Environment.dev);
});

/// Provider for the current sync status of the offline queue.
final syncStatusProvider = StateProvider<SyncStatus>((ref) {
  return SyncStatus.idle;
});

/// Provider for the current student profile.
/// Null when not authenticated.
final currentStudentProvider = StateProvider<Student?>((ref) {
  return null;
});

/// Provider tracking whether Firebase was initialized successfully.
/// Used to gate Firebase-dependent features.
final firebaseAvailableProvider = StateProvider<bool>((ref) {
  return false;
});
