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
