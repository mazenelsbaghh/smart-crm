import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:equatable/equatable.dart';
import '../data/models/chat_models.dart';
import '../data/repositories/chat_repository.dart';

// Events
abstract class InboxEvent extends Equatable {
  const InboxEvent();
  @override
  List<Object?> get props => [];
}

class InboxConversationsFetchRequested extends InboxEvent {
  final String projectId;
  final String? status;
  final String? search;
  const InboxConversationsFetchRequested({required this.projectId, this.status, this.search});
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
  const InboxConversationStatusChanged({required this.conversationId, required this.status});
  @override
  List<Object?> get props => [conversationId, status];
}

class InboxCustomerUpdated extends InboxEvent {
  final dynamic customer;
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
      aiSuggestion: aiSuggestion != null ? aiSuggestion() : this.aiSuggestion,
      aiTypingConversations: aiTypingConversations ?? this.aiTypingConversations,
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

  InboxBloc({required ChatRepository chatRepository})
      : _chatRepository = chatRepository,
        super(const InboxState()) {
    on<InboxConversationsFetchRequested>(_onConversationsFetch);
    on<InboxConversationsLoadMoreRequested>(_onConversationsLoadMore);
    on<InboxActiveConversationSelected>(_onActiveConversationSelected);
    on<InboxMessageSent>(_onMessageSent);
    on<InboxMessageReceived>(_onMessageReceived);
    on<InboxAISuggestionReceived>(_onAISuggestionReceived);
    on<InboxAITypingUpdated>(_onAITypingUpdated);
    on<InboxConversationStatusChanged>(_onConversationStatusChanged);
    on<InboxCustomerUpdated>(_onCustomerUpdated);
  }

  Future<void> _onConversationsFetch(
    InboxConversationsFetchRequested event,
    Emitter<InboxState> emit,
  ) async {
    _currentFilterStatus = event.status;
    _currentSearchQuery = event.search;
    emit(state.copyWith(loadingConvs: true, error: () => null));
    try {
      final list = await _chatRepository.getConversations(
        event.projectId,
        status: event.status,
        search: event.search,
      );
      emit(state.copyWith(
        conversations: list,
        hasMoreConvs: list.length == 20,
        loadingConvs: false,
      ));
    } catch (e) {
      emit(state.copyWith(loadingConvs: false, error: () => e.toString()));
    }
  }

  Future<void> _onConversationsLoadMore(
    InboxConversationsLoadMoreRequested event,
    Emitter<InboxState> emit,
  ) async {
    if (state.loadingConvs || !state.hasMoreConvs || state.conversations.isEmpty) return;
    emit(state.copyWith(loadingConvs: true));
    try {
      final before = state.conversations.last.lastMessageAt.toIso8601String();
      final list = await _chatRepository.getConversations(
        event.projectId,
        status: _currentFilterStatus,
        search: _currentSearchQuery,
        before: before,
      );
      emit(state.copyWith(
        conversations: [...state.conversations, ...list],
        hasMoreConvs: list.length == 20,
        loadingConvs: false,
      ));
    } catch (e) {
      emit(state.copyWith(loadingConvs: false, error: () => e.toString()));
    }
  }

  Future<void> _onActiveConversationSelected(
    InboxActiveConversationSelected event,
    Emitter<InboxState> emit,
  ) async {
    emit(state.copyWith(
      activeConv: () => event.conversation,
      loadingMessages: true,
      messages: [],
      hasMoreMessages: false,
      aiSuggestion: () => null,
    ));
    try {
      final list = await _chatRepository.getMessages(event.conversation.id);
      emit(state.copyWith(
        messages: list,
        hasMoreMessages: list.length == 10,
        loadingMessages: false,
      ));
    } catch (e) {
      emit(state.copyWith(loadingMessages: false, error: () => e.toString()));
    }
  }

  Future<void> _onMessageSent(InboxMessageSent event, Emitter<InboxState> emit) async {
    final active = state.activeConv;
    if (active == null) return;
    try {
      final sentMessage = await _chatRepository.sendMessage(active.id, event.content);
      emit(state.copyWith(
        messages: [...state.messages, sentMessage],
        aiSuggestion: () => null,
      ));
    } catch (e) {
      emit(state.copyWith(error: () => e.toString()));
    }
  }

  void _onMessageReceived(InboxMessageReceived event, Emitter<InboxState> emit) {
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
          unreadCount: active?.id == c.id ? 0 : c.unreadCount + 1,
        );
      }
      return c;
    }).toList();
    emit(state.copyWith(conversations: updatedList));
  }

  void _onAISuggestionReceived(InboxAISuggestionReceived event, Emitter<InboxState> emit) {
    final active = state.activeConv;
    if (active != null && event.suggestion.conversationId == active.id) {
      emit(state.copyWith(aiSuggestion: () => event.suggestion));
    }
  }

  void _onAITypingUpdated(InboxAITypingUpdated event, Emitter<InboxState> emit) {
    final updatedTyping = Map<String, bool>.from(state.aiTypingConversations);
    final updatedStages = Map<String, String>.from(state.aiTypingStages);

    updatedTyping[event.conversationId] = event.isTyping;
    if (event.stage != null) {
      updatedStages[event.conversationId] = event.stage!;
    }

    emit(state.copyWith(
      aiTypingConversations: updatedTyping,
      aiTypingStages: updatedStages,
    ));
  }

  void _onConversationStatusChanged(
    InboxConversationStatusChanged event,
    Emitter<InboxState> emit,
  ) {
    final updatedList = state.conversations.map((c) {
      if (c.id == event.conversationId) {
        return c.copyWith(status: _parseStatus(event.status));
      }
      return c;
    }).toList();

    Conversation? updatedActive = state.activeConv;
    if (updatedActive != null && updatedActive.id == event.conversationId) {
      updatedActive = updatedActive.copyWith(status: _parseStatus(event.status));
    }

    emit(state.copyWith(
      conversations: updatedList,
      activeConv: () => updatedActive,
    ));
  }

  void _onCustomerUpdated(InboxCustomerUpdated event, Emitter<InboxState> emit) {
    // Refresh active customer data when modified
    final cust = event.customer;
    final updatedList = state.conversations.map((c) {
      if (c.customer.id == cust['id']) {
        return Conversation(
          id: c.id,
          projectId: c.projectId,
          status: c.status,
          lastMessageAt: c.lastMessageAt,
          unreadCount: c.unreadCount,
          customer: CustomerSummary(
            id: c.customer.id,
            name: cust['name'] ?? c.customer.name,
            phone: cust['phone'] ?? c.customer.phone,
            label: cust['label'] ?? c.customer.label,
          ),
        );
      }
      return c;
    }).toList();

    Conversation? updatedActive = state.activeConv;
    if (updatedActive != null && updatedActive.customer.id == cust['id']) {
      updatedActive = Conversation(
        id: updatedActive.id,
        projectId: updatedActive.projectId,
        status: updatedActive.status,
        lastMessageAt: updatedActive.lastMessageAt,
        unreadCount: updatedActive.unreadCount,
        customer: CustomerSummary(
          id: updatedActive.customer.id,
          name: cust['name'] ?? updatedActive.customer.name,
          phone: cust['phone'] ?? updatedActive.customer.phone,
          label: cust['label'] ?? updatedActive.customer.label,
        ),
      );
    }

    emit(state.copyWith(
      conversations: updatedList,
      activeConv: () => updatedActive,
    ));
  }

  ConversationStatus _parseStatus(String statusStr) {
    switch (statusStr) {
      case 'Pending':
        return ConversationStatus.Pending;
      case 'Resolved':
        return ConversationStatus.Resolved;
      case 'Closed':
        return ConversationStatus.Closed;
      default:
        return ConversationStatus.Open;
    }
  }
}
