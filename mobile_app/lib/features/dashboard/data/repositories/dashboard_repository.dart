import '../../../../core/services/api_client.dart';
import '../../../auth/data/models/user_model.dart';

class DashboardRepository {
  final ApiClient _apiClient;

  DashboardRepository({required ApiClient apiClient}) : _apiClient = apiClient;

  Future<List<Map<String, dynamic>>> getAnalytics(String projectId, String type) async {
    final response = await _apiClient.dio.get('/api/projects/$projectId/analytics/$type');
    final List list = response.data ?? [];
    return list.map((item) => Map<String, dynamic>.from(item)).toList();
  }

  Future<void> recalculateAnalytics(String projectId) async {
    await _apiClient.dio.post('/api/projects/$projectId/analytics/recalculate');
  }

  Future<Project> updateProjectSettings(String projectId, Map<String, dynamic> settings) async {
    final response = await _apiClient.dio.put('/api/projects/$projectId/settings', data: settings);
    return Project.fromJson(response.data);
  }
}
