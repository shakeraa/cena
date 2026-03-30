// =============================================================================
// Cena Adaptive Learning Platform — Application Entry Point
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:logger/logger.dart';

import 'app.dart';
import 'core/config/app_config.dart';

final _logger = Logger(
  printer: PrettyPrinter(methodCount: 0, printTime: true),
);

void main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Resolve environment from --dart-define=ENV=dev|staging|prod
  const envString = String.fromEnvironment('ENV', defaultValue: 'dev');
  final environment = AppConfig.resolveEnvironment(envString);
  final config = AppConfig.forEnvironment(environment);

  _logger.i('Starting Cena in ${config.environment.name} mode');

  // Initialize Firebase — gated behind try/catch for dev environments
  // where google-services.json may not be present.
  try {
    await Firebase.initializeApp();
    _logger.i('Firebase initialized successfully');
  } catch (e) {
    _logger.w(
      'Firebase initialization failed. '
      'Auth and push notifications will be unavailable. '
      'Ensure google-services.json / GoogleService-Info.plist is present.',
      error: e,
    );
  }

  runApp(
    ProviderScope(
      child: CenaApp(config: config),
    ),
  );
}
