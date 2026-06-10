import '../../../../core/services/api_client.dart';
import '../models/chat_models.dart';

class ChatRepository {
  final ApiClient apiClient;

  ChatRepository({required this.apiClient});

  Future<List<Conversation>> getConversations(
    String projectId, {
    String? status,
    String? search,
    int limit = 20,
    String? before,
  }) async {
    final response = await apiClient.dio.get(
      '/api/projects/$projectId/conversations',
      queryParameters: {
        if (status != null && status != 'All') 'status': status,
        if (search != null && search.isNotEmpty) 'search': search,
        'limit': limit,
        if (before != null) 'before': before,
      },
    );
    final List list = response.data ?? [];
    return list.map((item) => Conversation.fromJson(item)).toList();
  }

  Future<List<Message>> getMessages(String conversationId, {String? before, int limit = 10}) async {
    final response = await apiClient.dio.get(
      '/api/conversations/$conversationId/messages',
      queryParameters: {
        'limit': limit,
        if (before != null) 'before': before,
      },
    );
    final List list = response.data ?? [];
    return list.map((item) => Message.fromJson(item)).toList();
  }

  Future<Message> sendMessage(String conversationId, String content) async {
    final response = await apiClient.dio.post(
      '/api/conversations/$conversationId/messages',
      data: {'content': content},
    );
    return Message.fromJson(response.data);
  }

  Future<void> updateConversationStatus(String conversationId, String status) async {
    await apiClient.dio.put(
      '/api/conversations/$conversationId/status',
      data: {'status': status},
    );
  }

  Future<void> updateCustomerProfile(String customerId, Map<String, dynamic> data) async {
    await apiClient.dio.put('/api/customers/$customerId', data: data);
  }

  Future<Map<String, dynamic>?> getCustomerMemory(String customerId) async {
    try {
      final response = await apiClient.dio.get('/api/customers/$customerId/memory');
      return response.data;
    } catch (_) {
      return null;
    }
  }

  Future<void> updateCustomerMemory(String customerId, Map<String, dynamic> data) async {
    await apiClient.dio.put('/api/customers/$customerId/memory', data: data);
  }

  Future<void> generateCustomerMemory(String projectId, String customerId) async {
    await apiClient.dio.post('/api/projects/$projectId/customers/$customerId/memory/generate');
  }
}
