enum ConversationStatus { open, pending, resolved, closed }

extension ConversationStatusApi on ConversationStatus {
  String get apiValue => switch (this) {
    ConversationStatus.open => 'Open',
    ConversationStatus.pending => 'Pending',
    ConversationStatus.resolved => 'Resolved',
    ConversationStatus.closed => 'Closed',
  };
}

class CustomerSummary {
  final String id;
  final String name;
  final String phone;
  final String? avatarUrl;
  final String? label;

  CustomerSummary({
    required this.id,
    required this.name,
    required this.phone,
    this.avatarUrl,
    this.label,
  });

  factory CustomerSummary.fromJson(Map<String, dynamic> json) {
    return CustomerSummary(
      id: json['id'] ?? '',
      name: json['name'] ?? '',
      phone: json['phone'] ?? '',
      avatarUrl: json['avatarUrl'],
      label: json['label'],
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'phone': phone,
    'avatarUrl': avatarUrl,
    'label': label,
  };

  CustomerSummary copyWith({
    String? name,
    String? phone,
    String? avatarUrl,
    String? label,
  }) {
    return CustomerSummary(
      id: id,
      name: name ?? this.name,
      phone: phone ?? this.phone,
      avatarUrl: avatarUrl ?? this.avatarUrl,
      label: label ?? this.label,
    );
  }
}

class Conversation {
  final String id;
  final String projectId;
  final CustomerSummary customer;
  final ConversationStatus status;
  final DateTime lastMessageAt;
  final int unreadCount;
  final String? assignedAgentId;
  final String? assignedAgentName;
  final String? whatsAppAccountId;
  final String? whatsAppAccountName;
  final bool isAiTyping;
  final int? aiTypingCountdown;
  final String? aiTypingStage;

  Conversation({
    required this.id,
    required this.projectId,
    required this.customer,
    required this.status,
    required this.lastMessageAt,
    required this.unreadCount,
    this.assignedAgentId,
    this.assignedAgentName,
    this.whatsAppAccountId,
    this.whatsAppAccountName,
    this.isAiTyping = false,
    this.aiTypingCountdown,
    this.aiTypingStage,
  });

  factory Conversation.fromJson(Map<String, dynamic> json) {
    ConversationStatus parseStatus(String? statusStr) {
      switch (statusStr?.trim().toLowerCase()) {
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

    return Conversation(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      customer: CustomerSummary.fromJson(json['customer'] ?? {}),
      status: parseStatus(json['status']),
      lastMessageAt:
          DateTime.tryParse(json['lastMessageAt'] ?? '') ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      unreadCount: json['unreadCount'] ?? 0,
      assignedAgentId: json['assignedAgentId'],
      assignedAgentName: json['assignedAgentName'],
      whatsAppAccountId: json['whatsAppAccountId'],
      whatsAppAccountName: json['whatsAppAccountName'],
      isAiTyping: json['isAiTyping'] ?? false,
      aiTypingCountdown: json['aiTypingCountdown'],
      aiTypingStage: json['aiTypingStage'],
    );
  }

  Conversation copyWith({
    CustomerSummary? customer,
    ConversationStatus? status,
    DateTime? lastMessageAt,
    int? unreadCount,
    bool? isAiTyping,
    int? aiTypingCountdown,
    String? aiTypingStage,
    String? assignedAgentId,
    String? assignedAgentName,
    String? whatsAppAccountId,
    String? whatsAppAccountName,
  }) {
    return Conversation(
      id: id,
      projectId: projectId,
      customer: customer ?? this.customer,
      status: status ?? this.status,
      lastMessageAt: lastMessageAt ?? this.lastMessageAt,
      unreadCount: unreadCount ?? this.unreadCount,
      assignedAgentId: assignedAgentId ?? this.assignedAgentId,
      assignedAgentName: assignedAgentName ?? this.assignedAgentName,
      whatsAppAccountId: whatsAppAccountId ?? this.whatsAppAccountId,
      whatsAppAccountName: whatsAppAccountName ?? this.whatsAppAccountName,
      isAiTyping: isAiTyping ?? this.isAiTyping,
      aiTypingCountdown: aiTypingCountdown ?? this.aiTypingCountdown,
      aiTypingStage: aiTypingStage ?? this.aiTypingStage,
    );
  }
}

enum SenderType { customer, agent, system, ai }

enum MediaType { image, voice, document }

class Message {
  final String id;
  final String conversationId;
  final SenderType senderType;
  final String content;
  final DateTime createdAt;
  final String status;
  final String? mediaUrl;
  final MediaType? mediaType;
  final String? assetId;
  final String? transcription;

  Message({
    required this.id,
    required this.conversationId,
    required this.senderType,
    required this.content,
    required this.createdAt,
    required this.status,
    this.mediaUrl,
    this.mediaType,
    this.assetId,
    this.transcription,
  });

  factory Message.fromJson(Map<String, dynamic> json) {
    SenderType parseSender(String? senderStr) {
      switch (senderStr) {
        case 'Agent':
          return SenderType.agent;
        case 'System':
          return SenderType.system;
        case 'AI':
          return SenderType.ai;
        default:
          return SenderType.customer;
      }
    }

    MediaType? parseMedia(String? mediaStr) {
      if (mediaStr == 'Image') return MediaType.image;
      if (mediaStr == 'Voice') return MediaType.voice;
      if (mediaStr == 'Document') return MediaType.document;
      return null;
    }

    return Message(
      id: json['id'] ?? '',
      conversationId: json['conversationId'] ?? '',
      senderType: parseSender(json['senderType']),
      content: json['content'] ?? '',
      createdAt:
          DateTime.tryParse(json['createdAt'] ?? '') ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      status: json['status'] ?? 'Sent',
      mediaUrl: json['mediaUrl'],
      mediaType: parseMedia(json['mediaType']),
      assetId: json['assetId'],
      transcription: json['transcription'],
    );
  }
}

class AISuggestion {
  final String conversationId;
  final String suggestionText;
  final double confidenceScore;
  final String reasoning;

  AISuggestion({
    required this.conversationId,
    required this.suggestionText,
    required this.confidenceScore,
    required this.reasoning,
  });

  factory AISuggestion.fromJson(Map<String, dynamic> json) {
    return AISuggestion(
      conversationId: json['conversationId'] ?? '',
      suggestionText: json['suggestionText'] ?? '',
      confidenceScore: (json['confidenceScore'] ?? 0.0).toDouble(),
      reasoning: json['reasoning'] ?? '',
    );
  }
}
