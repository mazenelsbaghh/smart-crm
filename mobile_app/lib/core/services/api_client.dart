import 'dart:convert';
import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'secure_storage.dart';

class ApiClient {
  final Dio dio;
  final SecureStorageService _secureStorage;

  // Point to the production server by default.
  static const String defaultBaseUrl = 'https://n8n-mazen.online';

  ApiClient({required SecureStorageService secureStorage, String baseUrl = defaultBaseUrl})
      : _secureStorage = secureStorage,
        dio = Dio(BaseOptions(
          baseUrl: baseUrl,
          connectTimeout: const Duration(seconds: 10),
          receiveTimeout: const Duration(seconds: 10),
          headers: {'Content-Type': 'application/json'},
        )) {
    dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) async {
        final token = await _secureStorage.getAccessToken();
        if (token != null) {
          options.headers['Authorization'] = 'Bearer $token';
        }

        final activeProjectStr = await _secureStorage.getActiveProject();
        if (activeProjectStr != null) {
          try {
            final project = jsonDecode(activeProjectStr);
            if (project['id'] != null) {
              options.headers['X-Project-Id'] = project['id'];
            }
          } catch (_) {}
        }
        return handler.next(options);
      },
      onError: (DioException error, handler) async {
        if (error.response?.statusCode == 401 &&
            !error.requestOptions.path.contains('/api/auth/login') &&
            !error.requestOptions.path.contains('/api/auth/refresh')) {
          
          final refreshToken = await _secureStorage.getRefreshToken();
          if (refreshToken != null) {
            try {
              // Create a standalone Dio instance to avoid circular interceptor calls
              final refreshDio = Dio(BaseOptions(baseUrl: dio.options.baseUrl));
              final response = await refreshDio.post('/api/auth/refresh', data: {
                'refreshToken': refreshToken,
              });

              if (response.statusCode == 200) {
                final newAccessToken = response.data['accessToken'];
                final newRefreshToken = response.data['refreshToken'];

                await _secureStorage.saveTokens(
                  accessToken: newAccessToken,
                  refreshToken: newRefreshToken,
                );

                // Retry original request with new token
                final options = error.requestOptions;
                options.headers['Authorization'] = 'Bearer $newAccessToken';
                
                final retryResponse = await dio.fetch(options);
                return handler.resolve(retryResponse);
              }
            } catch (_) {
              // Refresh failed, clear session
              await _secureStorage.clearAll();
            }
          }
        }
        return handler.next(error);
      },
    ));
  }
}
