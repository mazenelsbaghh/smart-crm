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

  factory User.fromJson(Object? response) {
    if (response is! Map<String, dynamic>) {
      throw const FormatException('Invalid user response');
    }
    return User(
      id: _requiredString(response, 'id'),
      email: _requiredString(response, 'email'),
      fullName: response['fullName'] is String
          ? response['fullName'] as String
          : '',
      role: _requiredString(response, 'role'),
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'email': email,
    'fullName': fullName,
    'role': role,
  };

  bool get canManageProject {
    final normalized = role.trim().toLowerCase();
    return normalized == 'owner' ||
        normalized == 'projectowner' ||
        normalized == 'admin' ||
        normalized == 'superadmin';
  }
}

class AuthSession {
  final String accessToken;
  final String refreshToken;
  final User user;

  AuthSession({
    required this.accessToken,
    required this.refreshToken,
    required this.user,
  });

  factory AuthSession.fromJson(Object? response) {
    if (response is! Map<String, dynamic>) {
      throw const FormatException('Invalid login response');
    }
    return AuthSession(
      accessToken: _requiredString(response, 'accessToken'),
      refreshToken: _requiredString(response, 'refreshToken'),
      user: User.fromJson(response['user']),
    );
  }
}

String _requiredString(Map<String, dynamic> response, String key) {
  final field = response[key];
  if (field is! String || field.trim().isEmpty) {
    throw FormatException('Missing required field: $key');
  }
  return field;
}

class ProjectSettings {
  final bool aiAutoReplyEnabled;
  final String timezone;
  final String geminiApiKey;
  final String geminiModel;
  final String aiTonePreference;
  final String aiTargetAudience;
  final int replyDelay;
  final int maxDailyMessages;
  final bool isGroupAppointmentsEnabled;
  final bool geminiApiKeyConfigured;
  final bool isWhatsAppGroupAutomationEnabled;
  final String groupAutomationManagerPhone;
  final String activeInstructors;
  final bool humanTransferEnabled;
  final String? humanTransferPhone;
  final bool isTalkTipsTrialGateEnabled;
  final bool messengerAiAutoReplyEnabled;
  final int messengerReplyDelay;
  final bool commentsAiAutoReplyEnabled;
  final int commentsReplyDelay;
  final String? systemPrompt;
  final Map<String, dynamic>? aiBehavior;

  ProjectSettings({
    required this.aiAutoReplyEnabled,
    required this.timezone,
    required this.geminiApiKey,
    required this.geminiModel,
    required this.aiTonePreference,
    required this.aiTargetAudience,
    required this.replyDelay,
    required this.maxDailyMessages,
    required this.isGroupAppointmentsEnabled,
    this.geminiApiKeyConfigured = false,
    this.isWhatsAppGroupAutomationEnabled = false,
    this.groupAutomationManagerPhone = '',
    this.activeInstructors = '',
    this.humanTransferEnabled = false,
    this.humanTransferPhone,
    this.isTalkTipsTrialGateEnabled = false,
    this.messengerAiAutoReplyEnabled = false,
    this.messengerReplyDelay = 5,
    this.commentsAiAutoReplyEnabled = false,
    this.commentsReplyDelay = 10,
    this.systemPrompt,
    this.aiBehavior,
  });

  factory ProjectSettings.fromJson(Map<String, dynamic> json) {
    return ProjectSettings(
      aiAutoReplyEnabled: json['aiAutoReplyEnabled'] ?? false,
      timezone: json['timezone'] ?? 'UTC',
      // API secrets must never be retained in app state or persisted locally.
      geminiApiKey: '',
      geminiModel: json['geminiModel'] ?? 'gemini-3.5-flash',
      aiTonePreference:
          json['aiTonePreference'] ?? 'العامية المصرية المهذبة والمحترمة',
      aiTargetAudience: json['aiTargetAudience'] ?? '',
      replyDelay: json['replyDelay'] ?? 3,
      maxDailyMessages: json['maxDailyMessages'] ?? 500,
      isGroupAppointmentsEnabled: json['isGroupAppointmentsEnabled'] ?? false,
      geminiApiKeyConfigured: json['geminiApiKeyConfigured'] ?? false,
      isWhatsAppGroupAutomationEnabled:
          json['isWhatsAppGroupAutomationEnabled'] ?? false,
      groupAutomationManagerPhone: json['groupAutomationManagerPhone'] ?? '',
      activeInstructors: json['activeInstructors'] ?? '',
      humanTransferEnabled: json['humanTransferEnabled'] ?? false,
      humanTransferPhone: json['humanTransferPhone'],
      isTalkTipsTrialGateEnabled: json['isTalkTipsTrialGateEnabled'] ?? false,
      messengerAiAutoReplyEnabled: json['messengerAiAutoReplyEnabled'] ?? false,
      messengerReplyDelay: json['messengerReplyDelay'] ?? 5,
      commentsAiAutoReplyEnabled: json['commentsAiAutoReplyEnabled'] ?? false,
      commentsReplyDelay: json['commentsReplyDelay'] ?? 10,
      systemPrompt: json['systemPrompt'],
      aiBehavior: json['aiBehavior'] is Map
          ? Map<String, dynamic>.from(json['aiBehavior'])
          : null,
    );
  }

  Map<String, dynamic> toUpdateJson() => {
    'aiAutoReplyEnabled': aiAutoReplyEnabled,
    'timezone': timezone,
    'geminiModel': geminiModel,
    'aiTonePreference': aiTonePreference,
    'aiTargetAudience': aiTargetAudience,
    'replyDelay': replyDelay,
    'maxDailyMessages': maxDailyMessages,
    'isGroupAppointmentsEnabled': isGroupAppointmentsEnabled,
    'isWhatsAppGroupAutomationEnabled': isWhatsAppGroupAutomationEnabled,
    'groupAutomationManagerPhone': groupAutomationManagerPhone,
    'activeInstructors': activeInstructors,
    'humanTransferEnabled': humanTransferEnabled,
    'humanTransferPhone': humanTransferPhone,
    'isTalkTipsTrialGateEnabled': isTalkTipsTrialGateEnabled,
    'messengerAiAutoReplyEnabled': messengerAiAutoReplyEnabled,
    'messengerReplyDelay': messengerReplyDelay,
    'commentsAiAutoReplyEnabled': commentsAiAutoReplyEnabled,
    'commentsReplyDelay': commentsReplyDelay,
    'systemPrompt': systemPrompt,
    if (aiBehavior != null) 'aiBehavior': aiBehavior,
  };

  Map<String, dynamic> toJson() => {
    ...toUpdateJson(),
    'geminiApiKey': '',
    'geminiApiKeyConfigured': geminiApiKeyConfigured,
  };
}

class Project {
  final String id;
  final String name;
  final bool whatsappConnected;
  final String? whatsappNumber;
  final ProjectSettings settings;

  Project({
    required this.id,
    required this.name,
    required this.whatsappConnected,
    this.whatsappNumber,
    required this.settings,
  });

  factory Project.fromJson(Map<String, dynamic> json) {
    return Project(
      id: json['id'] ?? '',
      name: json['name'] ?? '',
      whatsappConnected: json['whatsappConnected'] ?? false,
      whatsappNumber: json['whatsappNumber'],
      settings: ProjectSettings.fromJson(json['settings'] ?? {}),
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'whatsappConnected': whatsappConnected,
    'whatsappNumber': whatsappNumber,
    'settings': settings.toJson(),
  };
}
