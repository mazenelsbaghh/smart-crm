import 'dart:convert';
import 'package:dio/dio.dart';
import 'secure_storage.dart';

class ApiClient {
  static const _refreshRetryKey = 'tokenRefreshRetried';

  final Dio dio;
  final SecureStorageService _secureStorage;
  Future<_RefreshedTokens?>? _refreshInFlight;
  String? _refreshTokenInFlight;

  // Point to the production server by default.
  static const String defaultBaseUrl = 'https://n8n-mazen.online';

  ApiClient({
    required SecureStorageService secureStorage,
    String baseUrl = defaultBaseUrl,
  }) : _secureStorage = secureStorage,
       dio = Dio(
         BaseOptions(
           baseUrl: baseUrl,
           connectTimeout: const Duration(seconds: 10),
           receiveTimeout: const Duration(seconds: 10),
           headers: {'Content-Type': 'application/json'},
         ),
       ) {
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          final token = await _secureStorage.getAccessToken();
          if (token != null) {
            options.headers['Authorization'] = 'Bearer $token';
            final activeProjectStr = await _secureStorage.getActiveProject();
            if (activeProjectStr != null) {
              try {
                final project = jsonDecode(activeProjectStr);
                if (project is Map<String, dynamic> && project['id'] != null) {
                  options.headers['X-Project-Id'] = project['id'];
                }
              } on FormatException {
                // A corrupt local project cache is ignored; the JWT remains authoritative.
              }
            }
          }
          return handler.next(options);
        },
        onError: (DioException error, handler) async {
          if (!_canRefresh(error)) return handler.next(error);
          final refreshToken = await _secureStorage.getRefreshToken();
          if (refreshToken == null) return handler.next(error);
          try {
            final refreshedTokens = await _refreshTokens(refreshToken);
            if (refreshedTokens == null ||
                await _secureStorage.getRefreshToken() !=
                    refreshedTokens.refreshToken) {
              return handler.next(error);
            }
            final request = error.requestOptions;
            request
              ..headers['Authorization'] =
                  'Bearer ${refreshedTokens.accessToken}'
              ..extra[_refreshRetryKey] = true;
            return handler.resolve(await dio.fetch(request));
          } on DioException catch (refreshError) {
            if (_isRejectedRefresh(refreshError) &&
                await _secureStorage.getRefreshToken() == refreshToken) {
              await _secureStorage.clearAll();
            }
          } on FormatException {
            // Preserve the session when the refresh service returns a malformed response.
          }
          return handler.next(error);
        },
      ),
    );
  }

  bool _canRefresh(DioException error) {
    final path = error.requestOptions.path;
    return error.response?.statusCode == 401 &&
        error.requestOptions.extra[_refreshRetryKey] != true &&
        !path.contains('/api/auth/login') &&
        !path.contains('/api/auth/logout') &&
        !path.contains('/api/auth/refresh');
  }

  bool _isRejectedRefresh(DioException error) {
    final statusCode = error.response?.statusCode;
    return statusCode == 400 || statusCode == 401 || statusCode == 403;
  }

  Future<_RefreshedTokens?> _refreshTokens(String refreshToken) async {
    final activeRefresh = _refreshInFlight;
    if (activeRefresh != null && _refreshTokenInFlight == refreshToken) {
      return activeRefresh;
    }

    final request = _requestRefreshedTokens(refreshToken);
    _refreshInFlight = request;
    _refreshTokenInFlight = refreshToken;
    try {
      return await request;
    } finally {
      if (identical(_refreshInFlight, request)) {
        _refreshInFlight = null;
        _refreshTokenInFlight = null;
      }
    }
  }

  Future<_RefreshedTokens?> _requestRefreshedTokens(String refreshToken) async {
    final refreshDio = Dio(
      BaseOptions(
        baseUrl: dio.options.baseUrl,
        connectTimeout: dio.options.connectTimeout,
        receiveTimeout: dio.options.receiveTimeout,
        headers: {'Content-Type': 'application/json'},
      ),
    );
    final response = await refreshDio.post(
      '/api/auth/refresh',
      data: {'refreshToken': refreshToken},
    );
    final refreshedTokens = _RefreshedTokens.fromResponse(response.data);
    final replaced = await _secureStorage.replaceTokensIfCurrent(
      expectedRefreshToken: refreshToken,
      accessToken: refreshedTokens.accessToken,
      refreshToken: refreshedTokens.refreshToken,
    );
    return replaced ? refreshedTokens : null;
  }
}

class _RefreshedTokens {
  const _RefreshedTokens({
    required this.accessToken,
    required this.refreshToken,
  });

  factory _RefreshedTokens.fromResponse(dynamic response) {
    if (response is! Map) {
      throw const FormatException('Invalid token refresh response');
    }
    final accessToken = response['accessToken'];
    final refreshToken = response['refreshToken'];
    if (accessToken is! String ||
        accessToken.isEmpty ||
        refreshToken is! String ||
        refreshToken.isEmpty) {
      throw const FormatException('Invalid token refresh response');
    }
    return _RefreshedTokens(
      accessToken: accessToken,
      refreshToken: refreshToken,
    );
  }

  final String accessToken;
  final String refreshToken;
}
