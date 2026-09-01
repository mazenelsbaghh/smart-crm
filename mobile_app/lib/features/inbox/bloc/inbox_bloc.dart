import 'dart:math';

import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/user_facing_error.dart';
import '../data/models/chat_models.dart';
import '../data/repositories/chat_repository.dart';

// Events
abstract class InboxEvent extends Equatable {
  const InboxEvent();
  @override
  List<Object?> get props => [];
}

class InboxSessionCleared extends InboxEvent {
  const InboxSessionCleared();
}

class InboxConversationsFetchRequested extends InboxEvent {
  final String projectId;
  final String? status;
  final String? search;
  const InboxConversationsFetchRequested({
    required this.projectId,
    this.status,
    this.search,
  });
  @override
  List<Object?> get props => [projectId, status, search];
}

class InboxConversationsLoadMoreRequested extends InboxEvent {
  final String projectId;
  const InboxConversationsLoadMoreRequested({required this.projectId});
  @override
  List<Object?> get props => [projectId];
}

class InboxActiveConversationSelected extends InboxEvent {
  final Conversation conversation;
  const InboxActiveConversationSelected(this.conversation);
  @override
  List<Object?> get props => [conversation];
}

class InboxMessageSent extends InboxEvent {
  final String content;
  const InboxMessageSent(this.content);
  @override
  List<Object?> get props => [content];
}

class InboxMessagesLoadMoreRequested extends InboxEvent {
  const InboxMessagesLoadMoreRequested();
}

class InboxMessageReceived extends InboxEvent {
  final Message message;
  const InboxMessageReceived(this.message);
  @override
  List<Object?> get props => [message];
}

class InboxAISuggestionReceived extends InboxEvent {
  final AISuggestion suggestion;
  const InboxAISuggestionReceived(this.suggestion);
  @override
  List<Object?> get props => [suggestion];
}

class InboxAITypingUpdated extends InboxEvent {
  final String conversationId;
  final bool isTyping;
  final int? countdown;
  final String? stage;
  const InboxAITypingUpdated({
    required this.conversationId,
    required this.isTyping,
    this.countdown,
    this.stage,
  });
  @override
  List<Object?> get props => [conversationId, isTyping, countdown, stage];
}

class InboxConversationStatusChanged extends InboxEvent {
  final String conversationId;
  final String status;
  const InboxConversationStatusChanged({
    required this.conversationId,
    required this.status,
  });
  @override
  List<Object?> get props => [conversationId, status];
}

class InboxConversationStatusUpdateRequested extends InboxEvent {
  final String conversationId;
  final String status;

  const InboxConversationStatusUpdateRequested({
    required this.conversationId,
    required this.status,
  });

  @override
  List<Object?> get props => [conversationId, status];
}

class InboxCustomerUpdated extends InboxEvent {
  final Map<String, dynamic> customer;
  const InboxCustomerUpdated(this.customer);
  @override
  List<Object?> get props => [customer];
}

// State
class InboxState extends Equatable {
  final List<Conversation> conversations;
  final bool hasMoreConvs;
  final bool loadingConvs;
  final Conversation? activeConv;
  final List<Message> messages;
  final bool loadingMessages;
  final bool hasMoreMessages;
  final bool loadingMoreMessages;
  final bool sendingMessage;
  final int messageSendRevision;
  final String? lastSentContent;
  final String? messageSendError;
  final Set<String> statusUpdatesInProgress;
  final String? statusUpdateError;
  final AISuggestion? aiSuggestion;
  final Map<String, bool> aiTypingConversations;
  final Map<String, String> aiTypingStages;
  final String? error;

  const InboxState({
    this.conversations = const [],
    this.hasMoreConvs = false,
    this.loadingConvs = false,
    this.activeConv,
    this.messages = const [],
    this.loadingMessages = false,
    this.hasMoreMessages = false,
    this.loadingMoreMessages = false,
    this.sendingMessage = false,
    this.messageSendRevision = 0,
    this.lastSentContent,
    this.messageSendError,
    this.statusUpdatesInProgress = const {},
    this.statusUpdateError,
    this.aiSuggestion,
    this.aiTypingConversations = const {},
    this.aiTypingStages = const {},
    this.error,
  });

  InboxState copyWith({
    List<Conversation>? conversations,
    bool? hasMoreConvs,
    bool? loadingConvs,
    Conversation? Function()? activeConv,
    List<Message>? messages,
    bool? loadingMessages,
    bool? hasMoreMessages,
    bool? loadingMoreMessages,
    bool? sendingMessage,
    int? messageSendRevision,
    String? Function()? lastSentContent,
    String? Function()? messageSendError,
    Set<String>? statusUpdatesInProgress,
    String? Function()? statusUpdateError,
    AISuggestion? Function()? aiSuggestion,
    Map<String, bool>? aiTypingConversations,
    Map<String, String>? aiTypingStages,
    String? Function()? error,
  }) {
    return InboxState(
      conversations: conversations ?? this.conversations,
      hasMoreConvs: hasMoreConvs ?? this.hasMoreConvs,
      loadingConvs: loadingConvs ?? this.loadingConvs,
      activeConv: activeConv != null ? activeConv() : this.activeConv,
      messages: messages ?? this.messages,
      loadingMessages: loadingMessages ?? this.loadingMessages,
      hasMoreMessages: hasMoreMessages ?? this.hasMoreMessages,
      loadingMoreMessages: loadingMoreMessages ?? this.loadingMoreMessages,
      sendingMessage: sendingMessage ?? this.sendingMessage,
      messageSendRevision: messageSendRevision ?? this.messageSendRevision,
      lastSentContent: lastSentContent != null
          ? lastSentContent()
          : this.lastSentContent,
      messageSendError: messageSendError != null
          ? messageSendError()
          : this.messageSendError,
      statusUpdatesInProgress:
          statusUpdatesInProgress ?? this.statusUpdatesInProgress,
      statusUpdateError: statusUpdateError != null
          ? statusUpdateError()
          : this.statusUpdateError,
      aiSuggestion: aiSuggestion != null ? aiSuggestion() : this.aiSuggestion,
      aiTypingConversations:
          aiTypingConversations ?? this.aiTypingConversations,
      aiTypingStages: aiTypingStages ?? this.aiTypingStages,
      error: error != null ? error() : this.error,
    );
  }

  @override
  List<Object?> get props => [
    conversations,
    hasMoreConvs,
    loadingConvs,
    activeConv,
    messages,
    loadingMessages,
    hasMoreMessages,
    loadingMoreMessages,
    sendingMessage,
    messageSendRevision,
    lastSentContent,
    messageSendError,
    statusUpdatesInProgress,
    statusUpdateError,
    aiSuggestion,
    aiTypingConversations,
    aiTypingStages,
    error,
  ];
}

// BLoC
class InboxBloc extends Bloc<InboxEvent, InboxState> {
  final ChatRepository _chatRepository;
  String? _currentFilterStatus;
  String? _currentSearchQuery;
  int _conversationRequestGeneration = 0;
  int _messageRequestGeneration = 0;
  int _sessionGeneration = 0;
  final Random _secureRandom = Random.secure();
  String? _pendingSendConversationId;
  String? _pendingSendContent;
  String? _pendingSendIdempotencyKey;

  InboxBloc({required ChatRepository chatRepository})
    : _chatRepository = chatRepository,
      super(const InboxState()) {
    on<InboxSessionCleared>(_onSessionCleared);
    on<InboxConversationsFetchRequested>(_onConversationsFetch);
    on<InboxConversationsLoadMoreRequested>(_onConversationsLoadMore);
    on<InboxActiveConversationSelected>(_onActiveConversationSelected);
    on<InboxMessageSent>(_onMessageSent);
    on<InboxMessagesLoadMoreRequested>(_onMessagesLoadMore);
    on<InboxMessageReceived>(_onMessageReceived);
    on<InboxAISuggestionReceived>(_onAISuggestionReceived);
    on<InboxAITypingUpdated>(_onAITypingUpdated);
    on<InboxConversationStatusChanged>(_onConversationStatusChanged);
    on<InboxConversationStatusUpdateRequested>(
      _onConversationStatusUpdateRequested,
    );
    on<InboxCustomerUpdated>(_onCustomerUpdated);
  }

  void _onSessionCleared(InboxSessionCleared event, Emitter<InboxState> emit) {
    _sessionGeneration++;
    _conversationRequestGeneration++;
    _messageRequestGeneration++;
    _currentFilterStatus = null;
    _currentSearchQuery = null;
    emit(const InboxState());
  }

  Future<void> _onConversationsFetch(
    InboxConversationsFetchRequested event,
    Emitter<InboxState> emit,
  ) async {
    _currentFilterStatus = event.status;
    _currentSearchQuery = event.search;
    final sessionGeneration = _sessionGeneration;
    final requestGeneration = ++_conversationRequestGeneration;
    emit(state.copyWith(loadingConvs: true, error: () => null));
    try {
      final list = await _chatRepository.getConversations(
        event.projectId,
        status: event.status,
        search: event.search,
      );
      if (sessionGeneration != _sessionGeneration ||
          requestGeneration != _conversationRequestGeneration) {
        return;
      }
      emit(
        state.copyWith(
          conversations: list,
          hasMoreConvs: list.length == 20,
          loadingConvs: false,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration ||
          requestGeneration != _conversationRequestGeneration) {
        return;
      }
      emit(
        state.copyWith(loadingConvs: false, error: () => userFacingError(e)),
      );
    }
  }

  Future<void> _onConversationsLoadMore(
    InboxConversationsLoadMoreRequested event,
    Emitter<InboxState> emit,
  ) async {
    if (state.loadingConvs ||
        !state.hasMoreConvs ||
        state.conversations.isEmpty) {
      return;
    }
    final sessionGeneration = _sessionGeneration;
    final requestGeneration = _conversationRequestGeneration;
    emit(state.copyWith(loadingConvs: true));
    try {
      final before = state.conversations.last.lastMessageAt.toIso8601String();
      final list = await _chatRepository.getConversations(
        event.projectId,
        status: _currentFilterStatus,
        search: _currentSearchQuery,
        before: before,
      );
      if (sessionGeneration != _sessionGeneration ||
          requestGeneration != _conversationRequestGeneration) {
        return;
      }
      emit(
        state.copyWith(
          conversations: _mergeConversations(state.conversations, list),
          hasMoreConvs: list.length == 20,
          loadingConvs: false,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration ||
          requestGeneration != _conversationRequestGeneration) {
        return;
      }
      emit(
        state.copyWith(loadingConvs: false, error: () => userFacingError(e)),
      );
    }
  }

  Future<void> _onActiveConversationSelected(
    InboxActiveConversationSelected event,
    Emitter<InboxState> emit,
  ) async {
    final sessionGeneration = _sessionGeneration;
    final messageRequestGeneration = ++_messageRequestGeneration;
    emit(
      state.copyWith(
        activeConv: () => event.conversation,
        loadingMessages: true,
        loadingMoreMessages: false,
        messages: [],
        hasMoreMessages: false,
        aiSuggestion: () => null,
        error: () => null,
        messageSendError: () => null,
        statusUpdateError: () => null,
      ),
    );
    try {
      final list = await _chatRepository.getMessages(event.conversation.id);
      if (sessionGeneration != _sessionGeneration ||
          messageRequestGeneration != _messageRequestGeneration ||
          state.activeConv?.id != event.conversation.id) {
        return;
      }
      final sorted = [...list]
        ..sort((a, b) => a.createdAt.compareTo(b.createdAt));
      final messagesById = <String, Message>{
        for (final message in [...sorted, ...state.messages])
          message.id: message,
      };
      final merged = messagesById.values.toList()
        ..sort((a, b) => a.createdAt.compareTo(b.createdAt));
      emit(
        state.copyWith(
          messages: merged,
          hasMoreMessages: list.length == 30,
          loadingMessages: false,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration ||
          messageRequestGeneration != _messageRequestGeneration ||
          state.activeConv?.id != event.conversation.id) {
        return;
      }
      emit(
        state.copyWith(loadingMessages: false, error: () => userFacingError(e)),
      );
    }
  }

  Future<void> _onMessageSent(
    InboxMessageSent event,
    Emitter<InboxState> emit,
  ) async {
    final active = state.activeConv;
    final content = event.content.trim();
    if (active == null || content.isEmpty || state.sendingMessage) return;
    final sessionGeneration = _sessionGeneration;
    final reusePendingCommand = _pendingSendConversationId == active.id
        && _pendingSendContent == content
        && _pendingSendIdempotencyKey != null;
    final idempotencyKey = reusePendingCommand
        ? _pendingSendIdempotencyKey!
        : _newIdempotencyKey();
    _pendingSendConversationId = active.id;
    _pendingSendContent = content;
    _pendingSendIdempotencyKey = idempotencyKey;

    emit(state.copyWith(sendingMessage: true, messageSendError: () => null));
    try {
      final sentMessage = await _chatRepository.sendMessage(
        active.id,
        content,
        idempotencyKey,
      );
      _clearPendingSendCommand();
      if (sessionGeneration != _sessionGeneration) return;
      if (state.activeConv?.id != active.id) {
        emit(state.copyWith(sendingMessage: false));
        return;
      }
      emit(
        state.copyWith(
          messages: [...state.messages, sentMessage],
          aiSuggestion: () => null,
          sendingMessage: false,
          messageSendRevision: state.messageSendRevision + 1,
          lastSentContent: () => content,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          sendingMessage: false,
          messageSendError: () => userFacingError(e),
        ),
      );
    }
  }

  String _newIdempotencyKey() => [
    DateTime.now().microsecondsSinceEpoch.toRadixString(16),
    _secureRandom.nextInt(0x7fffffff).toRadixString(16),
    _secureRandom.nextInt(0x7fffffff).toRadixString(16),
  ].join('-');

  void _clearPendingSendCommand() {
    _pendingSendConversationId = null;
    _pendingSendContent = null;
    _pendingSendIdempotencyKey = null;
  }

  Future<void> _onMessagesLoadMore(
    InboxMessagesLoadMoreRequested event,
    Emitter<InboxState> emit,
  ) async {
    final active = state.activeConv;
    if (active == null ||
        state.loadingMessages ||
        state.loadingMoreMessages ||
        !state.hasMoreMessages ||
        state.messages.isEmpty) {
      return;
    }

    final before = state.messages.first.createdAt.toUtc().toIso8601String();
    final sessionGeneration = _sessionGeneration;
    final messageRequestGeneration = _messageRequestGeneration;
    emit(state.copyWith(loadingMoreMessages: true, error: () => null));
    try {
      final older = await _chatRepository.getMessages(
        active.id,
        before: before,
      );
      if (sessionGeneration != _sessionGeneration ||
          messageRequestGeneration != _messageRequestGeneration ||
          state.activeConv?.id != active.id) {
        return;
      }
      final byId = <String, Message>{
        for (final message in [...older, ...state.messages])
          message.id: message,
      };
      final merged = byId.values.toList()
        ..sort((a, b) => a.createdAt.compareTo(b.createdAt));
      emit(
        state.copyWith(
          messages: merged,
          hasMoreMessages: older.length == 30,
          loadingMoreMessages: false,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration ||
          messageRequestGeneration != _messageRequestGeneration) {
        return;
      }
      emit(
        state.copyWith(
          loadingMoreMessages: false,
          error: () => userFacingError(e),
        ),
      );
    }
  }

  void _onMessageReceived(
    InboxMessageReceived event,
    Emitter<InboxState> emit,
  ) {
    final active = state.activeConv;
    if (active != null && event.message.conversationId == active.id) {
      // Check if message already exists in log
      final exists = state.messages.any((m) => m.id == event.message.id);
      if (!exists) {
        emit(state.copyWith(messages: [...state.messages, event.message]));
      }
    }
    // Update preview in conversations list
    final updatedList = state.conversations.map((c) {
      if (c.id == event.message.conversationId) {
        return c.copyWith(
          lastMessageAt: event.message.createdAt,
          unreadCount: active?.id == c.id ? 0 : c.unreadCount + 1,
        );
      }
      return c;
    }).toList()..sort((a, b) => b.lastMessageAt.compareTo(a.lastMessageAt));
    emit(state.copyWith(conversations: updatedList));
  }

  void _onAISuggestionReceived(
    InboxAISuggestionReceived event,
    Emitter<InboxState> emit,
  ) {
    final active = state.activeConv;
    if (active != null && event.suggestion.conversationId == active.id) {
      emit(state.copyWith(aiSuggestion: () => event.suggestion));
    }
  }

  void _onAITypingUpdated(
    InboxAITypingUpdated event,
    Emitter<InboxState> emit,
  ) {
    final updatedTyping = Map<String, bool>.from(state.aiTypingConversations);
    final updatedStages = Map<String, String>.from(state.aiTypingStages);

    updatedTyping[event.conversationId] = event.isTyping;
    if (event.stage != null) {
      updatedStages[event.conversationId] = event.stage!;
    }

    emit(
      state.copyWith(
        aiTypingConversations: updatedTyping,
        aiTypingStages: updatedStages,
      ),
    );
  }

  void _onConversationStatusChanged(
    InboxConversationStatusChanged event,
    Emitter<InboxState> emit,
  ) {
    final pending = state.statusUpdatesInProgress.contains(
      event.conversationId,
    );
    final authoritativeState = pending
        ? state.copyWith(
            statusUpdatesInProgress: {...state.statusUpdatesInProgress}
              ..remove(event.conversationId),
          )
        : state;
    emit(
      _stateWithStatus(
        authoritativeState,
        conversationId: event.conversationId,
        status: _parseStatus(event.status),
      ),
    );
  }

  Future<void> _onConversationStatusUpdateRequested(
    InboxConversationStatusUpdateRequested event,
    Emitter<InboxState> emit,
  ) async {
    if (state.statusUpdatesInProgress.contains(event.conversationId)) return;

    final original = _conversationById(event.conversationId)?.status;
    if (original == null) return;
    final requested = _parseStatus(event.status);
    if (requested == original) return;
    final sessionGeneration = _sessionGeneration;

    final inProgress = {...state.statusUpdatesInProgress, event.conversationId};
    emit(
      _stateWithStatus(
        state.copyWith(
          statusUpdatesInProgress: inProgress,
          statusUpdateError: () => null,
        ),
        conversationId: event.conversationId,
        status: requested,
      ),
    );

    try {
      await _chatRepository.updateConversationStatus(
        event.conversationId,
        requested.apiValue,
      );
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          statusUpdatesInProgress: {...state.statusUpdatesInProgress}
            ..remove(event.conversationId),
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      if (!state.statusUpdatesInProgress.contains(event.conversationId)) {
        return;
      }
      final rolledBack = _stateWithStatus(
        state,
        conversationId: event.conversationId,
        status: original,
      );
      emit(
        rolledBack.copyWith(
          statusUpdatesInProgress: {...rolledBack.statusUpdatesInProgress}
            ..remove(event.conversationId),
          statusUpdateError: () => userFacingError(e),
        ),
      );
    }
  }

  void _onCustomerUpdated(
    InboxCustomerUpdated event,
    Emitter<InboxState> emit,
  ) {
    final cust = event.customer;
    final customerId = cust['id'];
    if (customerId is! String || customerId.isEmpty) return;
    String? updatedString(String key) {
      final value = cust[key];
      return value is String ? value : null;
    }

    final updatedList = state.conversations.map((c) {
      if (c.customer.id == customerId) {
        return c.copyWith(
          customer: c.customer.copyWith(
            name: updatedString('name'),
            phone: updatedString('phone'),
            avatarUrl: updatedString('avatarUrl'),
            label: updatedString('label'),
          ),
        );
      }
      return c;
    }).toList();

    Conversation? updatedActive = state.activeConv;
    if (updatedActive != null && updatedActive.customer.id == customerId) {
      updatedActive = updatedActive.copyWith(
        customer: updatedActive.customer.copyWith(
          name: updatedString('name'),
          phone: updatedString('phone'),
          avatarUrl: updatedString('avatarUrl'),
          label: updatedString('label'),
        ),
      );
    }

    emit(
      state.copyWith(
        conversations: updatedList,
        activeConv: () => updatedActive,
      ),
    );
  }

  ConversationStatus _parseStatus(String statusStr) {
    switch (statusStr.trim().toLowerCase()) {
      case 'pending':
        return ConversationStatus.pending;
      case 'resolved':
        return ConversationStatus.resolved;
      case 'closed':
        return ConversationStatus.closed;
      default:
        return ConversationStatus.open;
    }
  }

  Conversation? _conversationById(String id) {
    if (state.activeConv?.id == id) return state.activeConv;
    for (final conversation in state.conversations) {
      if (conversation.id == id) return conversation;
    }
    return null;
  }

  List<Conversation> _mergeConversations(
    List<Conversation> current,
    List<Conversation> incoming,
  ) {
    final byId = <String, Conversation>{
      for (final conversation in [...current, ...incoming])
        conversation.id: conversation,
    };
    return byId.values.toList()
      ..sort((a, b) => b.lastMessageAt.compareTo(a.lastMessageAt));
  }

  InboxState _stateWithStatus(
    InboxState source, {
    required String conversationId,
    required ConversationStatus status,
  }) {
    final updatedList = source.conversations
        .map(
          (conversation) => conversation.id == conversationId
              ? conversation.copyWith(status: status)
              : conversation,
        )
        .toList();
    final active = source.activeConv;
    final updatedActive = active != null && active.id == conversationId
        ? active.copyWith(status: status)
        : active;
    return source.copyWith(
      conversations: updatedList,
      activeConv: () => updatedActive,
    );
  }
}
