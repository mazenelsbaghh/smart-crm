enum WhatsAppSessionStatus {
  disconnected,
  initializing,
  reconnecting,
  connected;

  factory WhatsAppSessionStatus.fromApi(Object? value) {
    return switch (value) {
      'Disconnected' => WhatsAppSessionStatus.disconnected,
      'Initializing' => WhatsAppSessionStatus.initializing,
      'Reconnecting' => WhatsAppSessionStatus.reconnecting,
      'Connected' => WhatsAppSessionStatus.connected,
      _ => throw const FormatException('Invalid WhatsApp session status'),
    };
  }
}

class WhatsAppAccount {
  final String id;
  final String projectId;
  final String name;
  final bool isDefault;

  const WhatsAppAccount({
    required this.id,
    required this.projectId,
    required this.name,
    required this.isDefault,
  });

  factory WhatsAppAccount.fromJson(Object? jsonPayload) {
    final json = _jsonObject(jsonPayload, 'WhatsApp account');
    return WhatsAppAccount(
      id: _requiredString(json, 'id'),
      projectId: _requiredString(json, 'projectId'),
      name: _requiredString(json, 'name'),
      isDefault: _requiredBool(json, 'isDefault'),
    );
  }

  WhatsAppAccount copyWith({bool? isDefault}) {
    return WhatsAppAccount(
      id: id,
      projectId: projectId,
      name: name,
      isDefault: isDefault ?? this.isDefault,
    );
  }
}

class WhatsAppSessionSnapshot {
  final String projectId;
  final String? whatsappAccountId;
  final WhatsAppSessionStatus status;
  final String? phoneNumber;
  final String? error;

  const WhatsAppSessionSnapshot({
    required this.projectId,
    required this.whatsappAccountId,
    required this.status,
    required this.phoneNumber,
    required this.error,
  });

  factory WhatsAppSessionSnapshot.fromJson(Object? jsonPayload) {
    final json = _jsonObject(jsonPayload, 'WhatsApp session');
    return WhatsAppSessionSnapshot(
      projectId: _requiredString(json, 'projectId'),
      whatsappAccountId: _optionalString(json, 'whatsappAccountId'),
      status: WhatsAppSessionStatus.fromApi(json['status']),
      phoneNumber: _optionalString(json, 'phoneNumber'),
      error: _optionalString(json, 'error'),
    );
  }
}

class WhatsAppQrPayload {
  static const int maximumPayloadLength = 2048;

  final String? projectId;
  final String? whatsappAccountId;
  final String? value;
  final String? error;

  const WhatsAppQrPayload({
    required this.projectId,
    required this.whatsappAccountId,
    required this.value,
    required this.error,
  });

  factory WhatsAppQrPayload.fromJson(Object? jsonPayload) {
    final json = _jsonObject(jsonPayload, 'WhatsApp QR');
    final qrValue = _optionalString(json, 'qr');
    if (qrValue != null && qrValue.length > maximumPayloadLength) {
      throw const FormatException('WhatsApp QR payload is too large');
    }

    return WhatsAppQrPayload(
      projectId: _optionalString(json, 'projectId'),
      whatsappAccountId: _optionalString(json, 'whatsappAccountId'),
      value: qrValue,
      error: _optionalString(json, 'error'),
    );
  }
}

Map<String, dynamic> _jsonObject(Object? jsonPayload, String label) {
  if (jsonPayload is Map<String, dynamic>) return jsonPayload;
  if (jsonPayload is Map) return Map<String, dynamic>.from(jsonPayload);
  throw FormatException('Invalid $label response');
}

String _requiredString(Map<String, dynamic> json, String key) {
  final fieldValue = json[key];
  if (fieldValue is String && fieldValue.trim().isNotEmpty) {
    return fieldValue.trim();
  }
  throw FormatException('Invalid $key');
}

String? _optionalString(Map<String, dynamic> json, String key) {
  final fieldValue = json[key];
  if (fieldValue == null) return null;
  if (fieldValue is String) {
    final trimmed = fieldValue.trim();
    return trimmed.isEmpty ? null : trimmed;
  }
  throw FormatException('Invalid $key');
}

bool _requiredBool(Map<String, dynamic> json, String key) {
  final fieldValue = json[key];
  if (fieldValue is bool) return fieldValue;
  throw FormatException('Invalid $key');
}
