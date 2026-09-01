import 'dart:async';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class SecureStorageService {
  final FlutterSecureStorage _storage;
  Future<void> _sessionMutationTail = Future.value();

  SecureStorageService() : _storage = const FlutterSecureStorage();

  static const String _accessTokenKey = 'accessToken';
  static const String _refreshTokenKey = 'refreshToken';
  static const String _userKey = 'user';
  static const String _activeProjectKey = 'activeProject';

  Future<void> saveSession({
    required String accessToken,
    required String refreshToken,
    required String userJson,
  }) async {
    await _withSessionLock(() async {
      await _storage.delete(key: _activeProjectKey);
      await _writeTokens(accessToken, refreshToken);
      await _storage.write(key: _userKey, value: userJson);
    });
  }

  Future<bool> replaceTokensIfCurrent({
    required String expectedRefreshToken,
    required String accessToken,
    required String refreshToken,
  }) {
    return _withSessionLock(() async {
      final currentRefreshToken = await _storage.read(key: _refreshTokenKey);
      if (currentRefreshToken != expectedRefreshToken) return false;
      await _writeTokens(accessToken, refreshToken);
      return true;
    });
  }

  Future<void> clearSessionIfRefreshTokenMatches(String refreshToken) async {
    await _withSessionLock(() async {
      final currentRefreshToken = await _storage.read(key: _refreshTokenKey);
      if (currentRefreshToken == refreshToken) await _clearSessionValues();
    });
  }

  Future<String?> getAccessToken() async {
    return await _storage.read(key: _accessTokenKey);
  }

  Future<String?> getRefreshToken() async {
    return await _storage.read(key: _refreshTokenKey);
  }

  Future<String?> getUser() async {
    return await _storage.read(key: _userKey);
  }

  Future<bool> saveActiveProjectIfCurrent({
    required String expectedRefreshToken,
    required String projectJson,
  }) {
    return _withSessionLock(() async {
      final currentRefreshToken = await _storage.read(key: _refreshTokenKey);
      if (currentRefreshToken != expectedRefreshToken) return false;
      await _storage.write(key: _activeProjectKey, value: projectJson);
      return true;
    });
  }

  Future<String?> getActiveProject() async {
    return await _storage.read(key: _activeProjectKey);
  }

  Future<void> clearAll() async {
    await _withSessionLock(() async {
      await _clearSessionValues();
    });
  }

  Future<void> _writeTokens(String accessToken, String refreshToken) async {
    await _storage.write(key: _accessTokenKey, value: accessToken);
    await _storage.write(key: _refreshTokenKey, value: refreshToken);
  }

  Future<void> _clearSessionValues() async {
    await _storage.delete(key: _accessTokenKey);
    await _storage.delete(key: _refreshTokenKey);
    await _storage.delete(key: _userKey);
    await _storage.delete(key: _activeProjectKey);
  }

  Future<T> _withSessionLock<T>(Future<T> Function() mutation) async {
    final previousMutation = _sessionMutationTail;
    final completion = Completer<void>();
    _sessionMutationTail = completion.future;
    await previousMutation;
    try {
      return await mutation();
    } finally {
      completion.complete();
    }
  }
}
