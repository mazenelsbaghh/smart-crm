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
  })  : _apiClient = apiClient,
        _secureStorage = secureStorage;

  Future<AuthSession> login(String email, String password) async {
    final response = await _apiClient.dio.post('/api/auth/login', data: {
      'email': email,
      'password': password,
    });

    final session = AuthSession.fromJson(response.data);
    await _secureStorage.saveTokens(
      accessToken: session.accessToken,
      refreshToken: session.refreshToken,
    );
    await _secureStorage.saveUser(jsonEncode(session.user.toJson()));

    // Fetch projects automatically upon login
    try {
      final projects = await getProjects();
      if (projects.isNotEmpty) {
        final fullProject = await getProject(projects.first.id);
        await _secureStorage.saveActiveProject(jsonEncode(fullProject.toJson()));
      }
    } catch (_) {}

    return session;
  }

  Future<void> register(String email, String password, String fullName) async {
    await _apiClient.dio.post('/api/auth/register', data: {
      'email': email,
      'password': password,
      'fullName': fullName,
    });
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

  Future<void> setActiveProject(Project project) async {
    await _secureStorage.saveActiveProject(jsonEncode(project.toJson()));
  }

  Future<Project?> getActiveProject() async {
    final projectStr = await _secureStorage.getActiveProject();
    if (projectStr == null) return null;
    try {
      return Project.fromJson(jsonDecode(projectStr));
    } catch (_) {
      return null;
    }
  }

  Future<User?> getAuthenticatedUser() async {
    final userStr = await _secureStorage.getUser();
    if (userStr == null) return null;
    try {
      return User.fromJson(jsonDecode(userStr));
    } catch (_) {
      return null;
    }
  }

  Future<void> logout() async {
    try {
      final refreshToken = await _secureStorage.getRefreshToken();
      if (refreshToken != null) {
        await _apiClient.dio.post('/api/auth/logout', data: {
          'refreshToken': refreshToken,
        });
      }
    } catch (_) {} finally {
      await _secureStorage.clearAll();
    }
  }
}
