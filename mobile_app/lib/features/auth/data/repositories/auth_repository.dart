import 'dart:convert';

import 'package:dio/dio.dart';

import '../../../../core/services/api_client.dart';
import '../../../../core/services/secure_storage.dart';
import '../models/user_model.dart';

class AuthRepository {
  final ApiClient _apiClient;
  final SecureStorageService _secureStorage;

  AuthRepository({
    required ApiClient apiClient,
    required SecureStorageService secureStorage,
  }) : _apiClient = apiClient,
       _secureStorage = secureStorage;

  Future<AuthSession> login(String email, String password) async {
    final response = await _apiClient.dio.post(
      '/api/auth/login',
      data: {'email': email, 'password': password},
    );
    return AuthSession.fromJson(response.data);
  }

  Future<void> saveSession(AuthSession session) async {
    await _secureStorage.saveSession(
      accessToken: session.accessToken,
      refreshToken: session.refreshToken,
      userJson: jsonEncode(session.user.toJson()),
    );
  }

  Future<String?> getStoredRefreshToken() async {
    final refreshToken = await _secureStorage.getRefreshToken();
    return refreshToken == null || refreshToken.trim().isEmpty
        ? null
        : refreshToken;
  }

  Future<void> invalidateSession(String refreshToken) async {
    await _secureStorage.clearSessionIfRefreshTokenMatches(refreshToken);
    await _revokeRefreshToken(refreshToken);
  }

  Future<List<Project>> getProjects() async {
    final response = await _apiClient.dio.get('/api/projects');
    final List list = response.data ?? [];
    return list.map((item) => Project.fromJson(item)).toList();
  }

  Future<Project> getProject(String id) async {
    final response = await _apiClient.dio.get('/api/projects/$id');
    return Project.fromJson(response.data);
  }

  Future<bool> setActiveProject(Project project, String expectedRefreshToken) {
    return _secureStorage.saveActiveProjectIfCurrent(
      expectedRefreshToken: expectedRefreshToken,
      projectJson: jsonEncode(project.toJson()),
    );
  }

  Future<Project?> getActiveProject() async {
    final projectStr = await _secureStorage.getActiveProject();
    if (projectStr == null) return null;
    try {
      final cachedProject = jsonDecode(projectStr);
      if (cachedProject is! Map<String, dynamic>) return null;
      return Project.fromJson(cachedProject);
    } on FormatException {
      return null;
    }
  }

  Future<User?> getAuthenticatedUser() async {
    final userStr = await _secureStorage.getUser();
    if (userStr == null) return null;
    try {
      final cachedUser = jsonDecode(userStr);
      if (cachedUser is! Map<String, dynamic>) return null;
      return User.fromJson(cachedUser);
    } on FormatException {
      return null;
    }
  }

  Future<void> logout() async {
    final refreshToken = await getStoredRefreshToken();
    await _secureStorage.clearAll();
    if (refreshToken != null) await _revokeRefreshToken(refreshToken);
  }

  Future<void> _revokeRefreshToken(String refreshToken) async {
    try {
      await _apiClient.dio.post(
        '/api/auth/logout',
        data: {'refreshToken': refreshToken},
      );
    } on DioException {
      // The local session is already gone; revocation can expire server-side.
    }
  }
}
