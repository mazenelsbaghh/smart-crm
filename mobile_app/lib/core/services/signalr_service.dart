import 'dart:async';
import 'package:signalr_netcore/signalr_client.dart';
import 'secure_storage.dart';

class SignalRService {
  final SecureStorageService _secureStorage;
  final String _wsUrl;
  HubConnection? _connection;

  // Listeners callbacks
  void Function(dynamic message)? onMessageReceived;
  void Function(String convId, String status)? onConversationStatusChanged;
  void Function(dynamic suggestion)? onAISuggestionGenerated;
  void Function(Map<String, dynamic> data)? onAITyping;
  void Function(Map<String, dynamic> data)? onAITypingError;
  void Function(String title, String body, String type)? onNotificationReceived;
  void Function(String agentId, String status)? onAgentPresenceUpdated;
  void Function(dynamic customer)? onCustomerUpdated;

  SignalRService({
    required SecureStorageService secureStorage,
    String? wsUrl,
  })  : _secureStorage = secureStorage,
        _wsUrl = wsUrl ?? 'https://n8n-mazen.online/hubs';

  Future<void> start({required String projectId}) async {
    if (_connection != null && _connection!.state != HubConnectionState.Disconnected) return;

    final token = await _secureStorage.getAccessToken() ?? '';
    final hubUrl = '$_wsUrl/notifications?projectId=$projectId';

    _connection = HubConnectionBuilder()
        .withUrl(hubUrl, options: HttpConnectionOptions(
          accessTokenFactory: () async => token,
        ))
        .build();

    // Listen to backend hub events
    _connection!.on('ReceiveMessage', (arguments) {
      if (arguments != null && arguments.isNotEmpty && onMessageReceived != null) {
        onMessageReceived!(arguments[0]);
      }
    });

    _connection!.on('ConversationStatusChanged', (arguments) {
      if (arguments != null && arguments.length >= 2 && onConversationStatusChanged != null) {
        onConversationStatusChanged!(arguments[0].toString(), arguments[1].toString());
      }
    });

    _connection!.on('AISuggestionGenerated', (arguments) {
      if (arguments != null && arguments.isNotEmpty && onAISuggestionGenerated != null) {
        onAISuggestionGenerated!(arguments[0]);
      }
    });

    _connection!.on('AITyping', (arguments) {
      if (arguments != null && arguments.isNotEmpty && onAITyping != null) {
        final map = arguments[0] as Map<String, dynamic>;
        onAITyping!(map);
      }
    });

    _connection!.on('AITypingError', (arguments) {
      if (arguments != null && arguments.isNotEmpty && onAITypingError != null) {
        final map = arguments[0] as Map<String, dynamic>;
        onAITypingError!(map);
      }
    });

    _connection!.on('ReceiveNotification', (arguments) {
      if (arguments != null && arguments.isNotEmpty && onNotificationReceived != null) {
        try {
          final data = arguments[0];
          if (data is Map) {
            final type = data['type']?.toString() ?? 'General';
            final message = data['message']?.toString() ?? '';
            String title = 'تنبيه جديد';
            if (type == 'Booking') {
              title = 'حجز جديد 📅';
            } else if (type == 'Complaint') {
              title = 'شكوى جديدة ⚠️';
            } else if (type == 'VIP') {
              title = 'عميل VIP 🌟';
            }
            onNotificationReceived!(title, message, type);
          }
        } catch (e) {
          print('[SignalR] Error parsing ReceiveNotification: $e');
        }
      }
    });

    _connection!.on('AgentPresenceUpdated', (arguments) {
      if (arguments != null && arguments.length >= 2 && onAgentPresenceUpdated != null) {
        onAgentPresenceUpdated!(arguments[0].toString(), arguments[1].toString());
      }
    });

    _connection!.on('CustomerUpdated', (arguments) {
      if (arguments != null && arguments.isNotEmpty && onCustomerUpdated != null) {
        onCustomerUpdated!(arguments[0]);
      }
    });

    try {
      await _connection!.start();
      print('[SignalR] Connected successfully.');
      await _connection!.invoke('JoinProjectGroup', args: [projectId]);
    } catch (e) {
      print('[SignalR] Connection error: $e');
      _connection = null;
    }
  }

  Future<void> stop() async {
    if (_connection == null) return;
    try {
      await _connection!.stop();
      print('[SignalR] Disconnected.');
    } catch (e) {
      print('[SignalR] Disconnect error: $e');
    } finally {
      _connection = null;
    }
  }

  Future<void> updatePresence(String status) async {
    if (_connection?.state != HubConnectionState.Connected) return;
    try {
      await _connection!.invoke('UpdatePresence', args: [status]);
    } catch (e) {
      print('[SignalR] Error updating presence: $e');
    }
  }
}
