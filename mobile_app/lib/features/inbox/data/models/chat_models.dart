enum ConversationStatus { Open, Pending, Resolved, Closed }

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
    this.isAiTyping = false,
    this.aiTypingCountdown,
    this.aiTypingStage,
  });

  factory Conversation.fromJson(Map<String, dynamic> json) {
    ConversationStatus parseStatus(String? statusStr) {
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

    return Conversation(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      customer: CustomerSummary.fromJson(json['customer'] ?? {}),
      status: parseStatus(json['status']),
      lastMessageAt: DateTime.tryParse(json['lastMessageAt'] ?? '') ?? DateTime.now(),
      unreadCount: json['unreadCount'] ?? 0,
      assignedAgentId: json['assignedAgentId'],
      assignedAgentName: json['assignedAgentName'],
      isAiTyping: json['isAiTyping'] ?? false,
      aiTypingCountdown: json['aiTypingCountdown'],
      aiTypingStage: json['aiTypingStage'],
    );
  }

  Conversation copyWith({
    ConversationStatus? status,
    int? unreadCount,
    bool? isAiTyping,
    int? aiTypingCountdown,
    String? aiTypingStage,
    String? assignedAgentId,
    String? assignedAgentName,
  }) {
    return Conversation(
      id: id,
      projectId: projectId,
      customer: customer,
      status: status ?? this.status,
      lastMessageAt: lastMessageAt,
      unreadCount: unreadCount ?? this.unreadCount,
      assignedAgentId: assignedAgentId ?? this.assignedAgentId,
      assignedAgentName: assignedAgentName ?? this.assignedAgentName,
      isAiTyping: isAiTyping ?? this.isAiTyping,
      aiTypingCountdown: aiTypingCountdown ?? this.aiTypingCountdown,
      aiTypingStage: aiTypingStage ?? this.aiTypingStage,
    );
  }
}

enum SenderType { Customer, Agent, System, AI }
enum MediaType { Image, Voice, Document }

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
          return SenderType.Agent;
        case 'System':
          return SenderType.System;
        case 'AI':
          return SenderType.AI;
        default:
          return SenderType.Customer;
      }
    }

    MediaType? parseMedia(String? mediaStr) {
      if (mediaStr == 'Image') return MediaType.Image;
      if (mediaStr == 'Voice') return MediaType.Voice;
      if (mediaStr == 'Document') return MediaType.Document;
      return null;
    }

    return Message(
      id: json['id'] ?? '',
      conversationId: json['conversationId'] ?? '',
      senderType: parseSender(json['senderType']),
      content: json['content'] ?? '',
      createdAt: DateTime.tryParse(json['createdAt'] ?? '') ?? DateTime.now(),
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
