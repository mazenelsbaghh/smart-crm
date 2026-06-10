# Data Models & Serialization: Flutter App

This document outlines the client-side Dart data models matching the REST API and WebSocket events of Smart Customer Core.

## 1. Authentication & Session Models

### User
```dart
class User {
  final String id;
  final String email;
  final String fullName;
  final String role;

  User({
    required this.id,
    required this.email,
    required this.fullName,
    required this.role,
  });

  factory User.fromJson(Map<String, dynamic> json) {
    return User(
      id: json['id'] ?? '',
      email: json['email'] ?? '',
      fullName: json['fullName'] ?? '',
      role: json['role'] ?? 'Agent',
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'email': email,
    'fullName': fullName,
    'role': role,
  };
}
```

### AuthSession
```dart
class AuthSession {
  final String accessToken;
  final String refreshToken;
  final User user;

  AuthSession({
    required this.accessToken,
    required this.refreshToken,
    required this.user,
  });

  factory AuthSession.fromJson(Map<String, dynamic> json) {
    return AuthSession(
      accessToken: json['accessToken'] ?? '',
      refreshToken: json['refreshToken'] ?? '',
      user: User.fromJson(json['user'] ?? {}),
    );
  }
}
```

---

## 2. Project Models

```dart
class ProjectSettings {
  final bool whatsappConnected;
  final String? whatsappNumber;
  final bool aiAutoReplyEnabled;
  final double leadScoreThreshold;

  ProjectSettings({
    required this.whatsappConnected,
    this.whatsappNumber,
    required this.aiAutoReplyEnabled,
    required this.leadScoreThreshold,
  });

  factory ProjectSettings.fromJson(Map<String, dynamic> json) {
    return ProjectSettings(
      whatsappConnected: json['whatsappConnected'] ?? false,
      whatsappNumber: json['whatsappNumber'],
      aiAutoReplyEnabled: json['aiAutoReplyEnabled'] ?? false,
      leadScoreThreshold: (json['leadScoreThreshold'] ?? 0.0).toDouble(),
    );
  }

  Map<String, dynamic> toJson() => {
    'whatsappConnected': whatsappConnected,
    'whatsappNumber': whatsappNumber,
    'aiAutoReplyEnabled': aiAutoReplyEnabled,
    'leadScoreThreshold': leadScoreThreshold,
  };
}

class Project {
  final String id;
  final String name;
  final ProjectSettings settings;

  Project({
    required this.id,
    required this.name,
    required this.settings,
  });

  factory Project.fromJson(Map<String, dynamic> json) {
    return Project(
      id: json['id'] ?? '',
      name: json['name'] ?? '',
      settings: ProjectSettings.fromJson(json['settings'] ?? {}),
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'settings': settings.toJson(),
  };
}
```

---

## 3. Chat & Message Models

```dart
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
  
  // UI-only reactive fields
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
        case 'Pending': return ConversationStatus.Pending;
        case 'Resolved': return ConversationStatus.Resolved;
        case 'Closed': return ConversationStatus.Closed;
        default: return ConversationStatus.Open;
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
  final String status; // Sent, Delivered, Read
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
        case 'Agent': return SenderType.Agent;
        case 'System': return SenderType.System;
        case 'AI': return SenderType.AI;
        default: return SenderType.Customer;
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
```

---

## 4. CRM Models

```dart
class Customer {
  final String id;
  final String projectId;
  final String phoneNumber;
  final String name;
  final String city;
  final int leadScore;
  final List<String> tags;
  final String notes;
  final double? budget;
  final List<String> interests;
  final String? pipelineStage;
  final String? label;
  final bool isBlacklisted;

  Customer({
    required this.id,
    required this.projectId,
    required this.phoneNumber,
    required this.name,
    required this.city,
    required this.leadScore,
    required this.tags,
    required this.notes,
    this.budget,
    required this.interests,
    this.pipelineStage,
    this.label,
    required this.isBlacklisted,
  });

  factory Customer.fromJson(Map<String, dynamic> json) {
    return Customer(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      phoneNumber: json['phoneNumber'] ?? '',
      name: json['name'] ?? '',
      city: json['city'] ?? '',
      leadScore: json['leadScore'] ?? 0,
      tags: List<String>.from(json['tags'] ?? []),
      notes: json['notes'] ?? '',
      budget: json['budget'] != null ? (json['budget'] as num).toDouble() : null,
      interests: List<String>.from(json['interests'] ?? []),
      pipelineStage: json['pipelineStage'],
      label: json['label'],
      isBlacklisted: json['isBlacklisted'] ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
    'name': name,
    'city': city,
    'leadScore': leadScore,
    'tags': tags,
    'notes': notes,
    'budget': budget,
    'interests': interests,
    'pipelineStage': pipelineStage,
    'label': label,
    'isBlacklisted': isBlacklisted,
  };
}

class PipelineStage {
  final String id;
  final String projectId;
  final String name;
  final int order;

  PipelineStage({
    required this.id,
    required this.projectId,
    required this.name,
    required this.order,
  });

  factory PipelineStage.fromJson(Map<String, dynamic> json) {
    return PipelineStage(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      name: json['name'] ?? '',
      order: json['order'] ?? 0,
    );
  }
}

class Deal {
  final String id;
  final String projectId;
  final String customerId;
  final String title;
  final double amount;
  final String pipelineStageId;
  final int status; // 0 = Open, 1 = Won, 2 = Lost
  final DateTime? closedAt;

  Deal({
    required this.id,
    required this.projectId,
    required this.customerId,
    required this.title,
    required this.amount,
    required this.pipelineStageId,
    required this.status,
    this.closedAt,
  });

  factory Deal.fromJson(Map<String, dynamic> json) {
    return Deal(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      customerId: json['customerId'] ?? '',
      title: json['title'] ?? '',
      amount: (json['amount'] ?? 0.0).toDouble(),
      pipelineStageId: json['pipelineStageId'] ?? '',
      status: json['status'] ?? 0,
      closedAt: DateTime.tryParse(json['closedAt'] ?? ''),
    );
  }

  Map<String, dynamic> toJson() => {
    'customerId': customerId,
    'title': title,
    'amount': amount,
    'pipelineStageId': pipelineStageId,
    'status': status,
  };
}
```
