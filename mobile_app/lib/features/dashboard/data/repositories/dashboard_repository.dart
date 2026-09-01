import '../../../../core/services/api_client.dart';

class DashboardRepository {
  final ApiClient _apiClient;

  DashboardRepository({required ApiClient apiClient}) : _apiClient = apiClient;

  Future<List<Map<String, dynamic>>> getAnalytics(
    String projectId,
    String type,
  ) async {
    final response = await _apiClient.dio.get(
      '/api/projects/$projectId/analytics/$type',
    );
    final List list = response.data ?? [];
    return list.map((item) => Map<String, dynamic>.from(item)).toList();
  }

  Future<void> updateProjectSettings(
    String projectId,
    Map<String, dynamic> settings,
  ) async {
    await _apiClient.dio.put(
      '/api/projects/$projectId/settings',
      data: settings,
    );
  }

  Future<bool> getWhatsAppStatus(String projectId) async {
    final accountsResponse = await _apiClient.dio.get(
      '/api/whatsapp/accounts',
      queryParameters: {'projectId': projectId},
    );
    final accounts = accountsResponse.data;
    if (accounts is! List || accounts.isEmpty) {
      throw const FormatException('Invalid WhatsApp accounts response');
    }

    final statuses = await Future.wait(
      accounts.map((account) async {
        if (account is! Map || account['id'] is! String) {
          throw const FormatException('Invalid WhatsApp account response');
        }
        final response = await _apiClient.dio.get(
          '/api/whatsapp/session/status',
          queryParameters: {
            'projectId': projectId,
            'whatsappAccountId': account['id'],
          },
        );
        final status = response.data?['status'];
        if (status is! String || status.isEmpty) {
          throw const FormatException('Invalid WhatsApp status response');
        }
        return status == 'Connected';
      }),
    );
    return statuses.any((connected) => connected);
  }
}
