import 'dart:async';

import 'package:signalr_netcore/signalr_client.dart';

import 'secure_storage.dart';

class SignalRService {
  final SecureStorageService _secureStorage;
  final String _wsUrl;
  HubConnection? _connection;
  int _connectionGeneration = 0;

  void Function(Map<String, dynamic> message)? onMessageReceived;
  void Function(String convId, String status)? onConversationStatusChanged;
  void Function(Map<String, dynamic> suggestion)? onAISuggestionGenerated;
  void Function(Map<String, dynamic> data)? onAITyping;
  void Function(String title, String body, String type)? onNotificationReceived;
  void Function(Map<String, dynamic> customer)? onCustomerUpdated;

  SignalRService({required SecureStorageService secureStorage, String? wsUrl})
    : _secureStorage = secureStorage,
      _wsUrl = wsUrl ?? 'https://n8n-mazen.online/hubs';

  Future<bool> start({required String projectId}) async {
    if (_connection != null &&
        _connection!.state != HubConnectionState.Disconnected) {
      return true;
    }

    final connectionGeneration = ++_connectionGeneration;
    final token = await _secureStorage.getAccessToken() ?? '';
    if (connectionGeneration != _connectionGeneration) return false;
    final hubUrl = '$_wsUrl/notifications?projectId=$projectId';

    final connection = HubConnectionBuilder()
        .withUrl(
          hubUrl,
          options: HttpConnectionOptions(accessTokenFactory: () async => token),
        )
        .build();
    _connection = connection;

    connection.on('ReceiveMessage', (arguments) {
      final payload = _firstMap(arguments);
      if (payload != null) onMessageReceived?.call(payload);
    });

    connection.on('ConversationStatusChanged', (arguments) {
      if (arguments == null || arguments.length < 2) return;
      final conversationId = arguments[0];
      final status = arguments[1];
      if (conversationId is String &&
          conversationId.isNotEmpty &&
          status is String &&
          status.isNotEmpty) {
        onConversationStatusChanged?.call(conversationId, status);
      }
    });

    connection.on('AISuggestionGenerated', (arguments) {
      final payload = _firstMap(arguments);
      if (payload != null) onAISuggestionGenerated?.call(payload);
    });

    connection.on('AITyping', (arguments) {
      final payload = _firstMap(arguments);
      if (payload != null) onAITyping?.call(payload);
    });

    connection.on('ReceiveNotification', (arguments) {
      final data = _firstMap(arguments);
      if (data == null) return;
      final rawType = data['type'];
      final rawMessage = data['message'];
      final type = rawType is String ? rawType : 'General';
      final message = rawMessage is String ? rawMessage : '';
      final title = switch (type) {
        'Booking' => 'حجز جديد',
        'Complaint' => 'شكوى جديدة',
        'VIP' => 'عميل مميز',
        _ => 'تنبيه جديد',
      };
      onNotificationReceived?.call(title, message, type);
    });

    connection.on('CustomerUpdated', (arguments) {
      final payload = _firstMap(arguments);
      if (payload != null) onCustomerUpdated?.call(payload);
    });

    try {
      await connection.start();
      if (!_isCurrent(connection, connectionGeneration)) {
        await _stopIgnoringErrors(connection);
        return false;
      }
      await connection.invoke('JoinProjectGroup', args: [projectId]);
      if (!_isCurrent(connection, connectionGeneration)) {
        await _stopIgnoringErrors(connection);
        return false;
      }
      return true;
    } catch (_) {
      await _stopIgnoringErrors(connection);
      if (_isCurrent(connection, connectionGeneration)) _connection = null;
      return false;
    }
  }

  Future<void> stop() async {
    final connection = _connection;
    _connectionGeneration++;
    _connection = null;
    if (connection == null) return;
    await _stopIgnoringErrors(connection);
  }

  bool _isCurrent(HubConnection connection, int generation) {
    return identical(_connection, connection) &&
        generation == _connectionGeneration;
  }

  Future<void> _stopIgnoringErrors(HubConnection connection) async {
    try {
      await connection.stop();
    } catch (_) {
      // A closed or failed transport needs no further recovery.
    }
  }

  Map<String, dynamic>? _firstMap(List<Object?>? arguments) {
    if (arguments == null || arguments.isEmpty) return null;
    final value = arguments.first;
    if (value is Map<String, dynamic>) return value;
    if (value is! Map) return null;

    final result = <String, dynamic>{};
    for (final entry in value.entries) {
      final key = entry.key;
      if (key is! String) return null;
      result[key] = entry.value;
    }
    return result;
  }
}
