// =============================================================================
// Cena Adaptive Learning Platform — API Client
// =============================================================================

import 'package:dio/dio.dart';
import 'package:logger/logger.dart';

import '../config/app_config.dart';

/// HTTP client wrapper around Dio for REST API communication.
///
/// Configured per environment via [AppConfig]. Handles:
/// - Base URL configuration
/// - Authentication token injection (via interceptor)
/// - Request/response logging in dev mode
/// - Error mapping to domain exceptions
///
/// WebSocket (SignalR) is handled separately by the websocket_service.
class ApiClient {
  ApiClient({required AppConfig config}) {
    _dio = Dio(
      BaseOptions(
        baseUrl: config.endpoints.restBaseUrl,
        connectTimeout: const Duration(seconds: 10),
        receiveTimeout: const Duration(seconds: 30),
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
        },
      ),
    );

    if (config.isDev) {
      _dio.interceptors.add(
        LogInterceptor(
          requestBody: true,
          responseBody: true,
          logPrint: (object) => _logger.d(object.toString()),
        ),
      );
    }
  }

  late final Dio _dio;
  final _logger = Logger(
    printer: PrettyPrinter(methodCount: 0, printTime: true),
  );

  /// Set the auth token for subsequent requests.
  /// Called after successful Firebase authentication.
  void setAuthToken(String token) {
    _dio.options.headers['Authorization'] = 'Bearer $token';
  }

  /// Clear the auth token on sign-out.
  void clearAuthToken() {
    _dio.options.headers.remove('Authorization');
  }

  /// GET request to the REST API.
  Future<Response<T>> get<T>(
    String path, {
    Map<String, dynamic>? queryParameters,
    CancelToken? cancelToken,
  }) async {
    return _dio.get<T>(
      path,
      queryParameters: queryParameters,
      cancelToken: cancelToken,
    );
  }

  /// POST request to the REST API.
  Future<Response<T>> post<T>(
    String path, {
    Object? data,
    Map<String, dynamic>? queryParameters,
    CancelToken? cancelToken,
  }) async {
    return _dio.post<T>(
      path,
      data: data,
      queryParameters: queryParameters,
      cancelToken: cancelToken,
    );
  }

  /// PUT request to the REST API.
  Future<Response<T>> put<T>(
    String path, {
    Object? data,
    Map<String, dynamic>? queryParameters,
    CancelToken? cancelToken,
  }) async {
    return _dio.put<T>(
      path,
      data: data,
      queryParameters: queryParameters,
      cancelToken: cancelToken,
    );
  }

  /// DELETE request to the REST API.
  Future<Response<T>> delete<T>(
    String path, {
    Object? data,
    Map<String, dynamic>? queryParameters,
    CancelToken? cancelToken,
  }) async {
    return _dio.delete<T>(
      path,
      data: data,
      queryParameters: queryParameters,
      cancelToken: cancelToken,
    );
  }

  /// Access the underlying Dio instance for advanced use cases.
  Dio get dio => _dio;
}
