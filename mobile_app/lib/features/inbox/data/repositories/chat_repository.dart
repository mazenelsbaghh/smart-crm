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
    final queryParameters = <String, dynamic>{'limit': limit};
    if (status != null && status != 'All') {
      queryParameters['status'] = status;
    }
    if (search != null && search.isNotEmpty) {
      queryParameters['search'] = search;
    }
    if (before != null) queryParameters['before'] = before;
    final response = await apiClient.dio.get(
      '/api/projects/$projectId/conversations',
      queryParameters: queryParameters,
    );
    final List list = response.data ?? [];
    return list.map((item) => Conversation.fromJson(item)).toList();
  }

  Future<List<Message>> getMessages(
    String conversationId, {
    String? before,
    int limit = 30,
  }) async {
    final queryParameters = <String, dynamic>{'limit': limit};
    if (before != null) queryParameters['before'] = before;
    final response = await apiClient.dio.get(
      '/api/conversations/$conversationId/messages',
      queryParameters: queryParameters,
    );
    final List list = response.data ?? [];
    return list.map((item) => Message.fromJson(item)).toList();
  }

  Future<Message> sendMessage(
    String conversationId,
    String content,
    String idempotencyKey,
  ) async {
    final response = await apiClient.dio.post(
      '/api/conversations/$conversationId/messages',
      data: {'content': content, 'idempotencyKey': idempotencyKey},
    );
    return Message.fromJson(response.data);
  }

  Future<void> updateConversationStatus(
    String conversationId,
    String status,
  ) async {
    await apiClient.dio.put(
      '/api/conversations/$conversationId/status',
      data: {'status': status},
    );
  }

  Future<void> updateCustomerProfile(
    String customerId,
    Map<String, dynamic> data,
  ) async {
    await apiClient.dio.put('/api/customers/$customerId', data: data);
  }
}
