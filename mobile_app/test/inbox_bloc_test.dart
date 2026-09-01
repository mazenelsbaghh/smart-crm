import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/core/services/api_client.dart';
import 'package:mobile_app/features/inbox/bloc/inbox_bloc.dart';
import 'package:mobile_app/features/inbox/data/models/chat_models.dart';
import 'package:mobile_app/features/inbox/data/repositories/chat_repository.dart';

class FakeChatRepository implements ChatRepository {
  List<Message> initialMessages = [];
  List<Message> olderMessages = [];
  Completer<void>? statusGate;
  bool failStatusUpdate = false;
  String? persistedStatus;
  Object? sendError;

  @override
  ApiClient get apiClient => throw UnimplementedError();

  @override
  Future<List<Conversation>> getConversations(
    String projectId, {
    String? status,
    String? search,
    int limit = 20,
    String? before,
  }) async => [];

  @override
  Future<List<Message>> getMessages(
    String conversationId, {
    String? before,
    int limit = 30,
  }) async => before == null ? initialMessages : olderMessages;

  @override
  Future<Message> sendMessage(
    String conversationId,
    String content,
    String idempotencyKey,
  ) async {
    if (sendError case final error?) throw error;
    return _message('sent', 100, content: content, sender: SenderType.agent);
  }

  @override
  Future<void> updateConversationStatus(
    String conversationId,
    String status,
  ) async {
    await statusGate?.future;
    if (failStatusUpdate) throw StateError('network unavailable');
    persistedStatus = status;
  }

  @override
  Future<void> updateCustomerProfile(
    String customerId,
    Map<String, dynamic> data,
  ) async {}
}

final _conversation = Conversation(
  id: 'conversation-1',
  projectId: 'project-1',
  customer: CustomerSummary(
    id: 'customer-1',
    name: 'عميل الاختبار',
    phone: '01000000000',
  ),
  status: ConversationStatus.open,
  lastMessageAt: DateTime.utc(2026, 8, 25, 12),
  unreadCount: 0,
);

Message _message(
  String id,
  int minute, {
  String content = 'رسالة',
  SenderType sender = SenderType.customer,
}) {
  return Message(
    id: id,
    conversationId: _conversation.id,
    senderType: sender,
    content: content,
    createdAt: DateTime.utc(2026, 8, 25, 10).add(Duration(minutes: minute)),
    status: 'Sent',
  );
}

Future<void> _selectConversation(InboxBloc bloc) async {
  final ready = bloc.stream.firstWhere(
    (state) =>
        state.activeConv?.id == _conversation.id && !state.loadingMessages,
  );
  bloc.add(InboxActiveConversationSelected(_conversation));
  await ready;
}

void main() {
  late FakeChatRepository repository;
  late InboxBloc bloc;

  setUp(() {
    repository = FakeChatRepository();
    bloc = InboxBloc(chatRepository: repository);
  });

  tearDown(() => bloc.close());

  test('status change stays pending until persistence is confirmed', () async {
    await _selectConversation(bloc);
    repository.statusGate = Completer<void>();

    final optimistic = bloc.stream.firstWhere(
      (state) =>
          state.activeConv?.status == ConversationStatus.resolved &&
          state.statusUpdatesInProgress.contains(_conversation.id),
    );
    bloc.add(
      const InboxConversationStatusUpdateRequested(
        conversationId: 'conversation-1',
        status: 'Resolved',
      ),
    );

    await optimistic;
    expect(repository.persistedStatus, isNull);

    final confirmed = bloc.stream.firstWhere(
      (state) =>
          state.activeConv?.status == ConversationStatus.resolved &&
          !state.statusUpdatesInProgress.contains(_conversation.id),
    );
    repository.statusGate!.complete();
    await confirmed;

    expect(repository.persistedStatus, 'Resolved');
    expect(bloc.state.statusUpdateError, isNull);
  });

  test('failed status persistence rolls the optimistic value back', () async {
    await _selectConversation(bloc);
    repository
      ..statusGate = Completer<void>()
      ..failStatusUpdate = true;

    final optimistic = bloc.stream.firstWhere(
      (state) => state.activeConv?.status == ConversationStatus.closed,
    );
    bloc.add(
      const InboxConversationStatusUpdateRequested(
        conversationId: 'conversation-1',
        status: 'Closed',
      ),
    );
    await optimistic;

    final rolledBack = bloc.stream.firstWhere(
      (state) =>
          state.activeConv?.status == ConversationStatus.open &&
          state.statusUpdateError != null,
    );
    repository.statusGate!.complete();
    final state = await rolledBack;

    expect(state.statusUpdatesInProgress, isEmpty);
    expect(state.statusUpdateError, isNot(contains('network unavailable')));
    expect(repository.persistedStatus, isNull);
  });

  test(
    'loading older messages deduplicates and preserves chronology',
    () async {
      repository.initialMessages = [
        for (var index = 30; index < 60; index++) _message('m$index', index),
      ];
      repository.olderMessages = [
        for (var index = 0; index < 29; index++) _message('m$index', index),
        _message('m30', 30),
      ];
      await _selectConversation(bloc);
      expect(bloc.state.hasMoreMessages, isTrue);

      final loaded = bloc.stream.firstWhere(
        (state) =>
            !state.loadingMoreMessages &&
            state.messages.firstOrNull?.id == 'm0' &&
            state.messages.length == 59,
      );
      bloc.add(const InboxMessagesLoadMoreRequested());
      final state = await loaded;

      expect(
        state.messages.map((message) => message.id).toSet(),
        hasLength(59),
      );
      final sortedTimes = [
        ...state.messages.map((message) => message.createdAt),
      ]..sort();
      expect(
        state.messages.map((message) => message.createdAt),
        orderedEquals(sortedTimes),
      );
    },
  );

  test('failed send never publishes a success signal or message', () async {
    repository.sendError = StateError('network unavailable');
    await _selectConversation(bloc);
    final failed = bloc.stream.firstWhere(
      (state) => !state.sendingMessage && state.messageSendError != null,
    );
    bloc.add(const InboxMessageSent('نص مهم غير محفوظ'));
    final state = await failed;

    expect(state.messages, isEmpty);
    expect(state.messageSendRevision, 0);
    expect(state.lastSentContent, isNull);
    expect(state.messageSendError, isNot(contains('network unavailable')));
  });
}
