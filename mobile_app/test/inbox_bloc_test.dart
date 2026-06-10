import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/inbox/bloc/inbox_bloc.dart';
import 'package:mobile_app/features/inbox/data/repositories/chat_repository.dart';
import 'package:mobile_app/features/inbox/data/models/chat_models.dart';
import 'package:mobile_app/core/services/api_client.dart';

class MockChatRepository implements ChatRepository {
  @override
  ApiClient get apiClient => throw UnimplementedError();

  @override
  Future<List<Conversation>> getConversations(
    String projectId, {
    String? status,
    String? search,
    int limit = 20,
    String? before,
  }) async {
    return [];
  }

  @override
  Future<List<Message>> getMessages(String conversationId, {String? before, int limit = 10}) async {
    return [];
  }

  @override
  Future<Message> sendMessage(String conversationId, String content) async {
    throw UnimplementedError();
  }

  @override
  Future<void> updateConversationStatus(String conversationId, String status) async {}

  @override
  Future<void> updateCustomerProfile(String customerId, Map<String, dynamic> data) async {}

  @override
  Future<Map<String, dynamic>?> getCustomerMemory(String customerId) async {
    return null;
  }

  @override
  Future<void> updateCustomerMemory(String customerId, Map<String, dynamic> data) async {}

  @override
  Future<void> generateCustomerMemory(String projectId, String customerId) async {}
}

void main() {
  late MockChatRepository mockChatRepository;
  late InboxBloc inboxBloc;

  setUp(() {
    mockChatRepository = MockChatRepository();
    inboxBloc = InboxBloc(chatRepository: mockChatRepository);
  });

  tearDown(() {
    inboxBloc.close();
  });

  test('initial state has empty lists and no active conversation', () {
    expect(inboxBloc.state, const InboxState());
  });
}
