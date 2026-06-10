import 'dart:convert';

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
  });

  factory ProjectSettings.fromJson(Map<String, dynamic> json) {
    return ProjectSettings(
      aiAutoReplyEnabled: json['aiAutoReplyEnabled'] ?? false,
      timezone: json['timezone'] ?? 'Africa/Cairo',
      geminiApiKey: json['geminiApiKey'] ?? '',
      geminiModel: json['geminiModel'] ?? 'gemini-3.5-flash',
      aiTonePreference: json['aiTonePreference'] ?? 'العامية المصرية الروشة والصايعة',
      aiTargetAudience: json['aiTargetAudience'] ?? 'طلاب كورس كول سنتر يبحثون عن عمل',
      replyDelay: json['replyDelay'] ?? 3,
      maxDailyMessages: json['maxDailyMessages'] ?? 500,
      isGroupAppointmentsEnabled: json['isGroupAppointmentsEnabled'] ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
        'aiAutoReplyEnabled': aiAutoReplyEnabled,
        'timezone': timezone,
        'geminiApiKey': geminiApiKey,
        'geminiModel': geminiModel,
        'aiTonePreference': aiTonePreference,
        'aiTargetAudience': aiTargetAudience,
        'replyDelay': replyDelay,
        'maxDailyMessages': maxDailyMessages,
        'isGroupAppointmentsEnabled': isGroupAppointmentsEnabled,
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
