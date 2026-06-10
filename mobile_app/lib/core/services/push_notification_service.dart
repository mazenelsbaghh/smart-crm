import 'dart:async';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'api_client.dart';
import '../widgets/notification_banner.dart';

@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  // Handles background and terminated state notification events
  print('[FCM] Background message received: ${message.messageId}');
}

class PushNotificationService {
  final ApiClient _apiClient;
  final String _projectId;
  final GlobalKey<NavigatorState> _navigatorKey;
  final Function(String route) _onNavigate;

  PushNotificationService({
    required ApiClient apiClient,
    required String projectId,
    required GlobalKey<NavigatorState> navigatorKey,
    required Function(String route) onNavigate,
  })  : _apiClient = apiClient,
        _projectId = projectId,
        _navigatorKey = navigatorKey,
        _onNavigate = onNavigate;

  Future<void> initialize() async {
    try {
      // 1. Request iOS permission
      final messaging = FirebaseMessaging.instance;
      final settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );

      print('[FCM] User notification permission status: ${settings.authorizationStatus}');

      // 2. Fetch and register token
      final token = await messaging.getToken();
      if (token != null) {
        await _registerTokenWithBackend(token);
      }

      // 3. Listen for token refresh
      messaging.onTokenRefresh.listen((newToken) async {
        await _registerTokenWithBackend(newToken);
      });

      // 4. Configure background message handler
      FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);

      // 5. Handle foreground notifications (show premium banner)
      FirebaseMessaging.onMessage.listen((RemoteMessage message) {
        print('[FCM] Foreground message received: ${message.messageId}');
        
        final notification = message.notification;
        if (notification != null && _navigatorKey.currentState != null) {
          final type = message.data['type']?.toString() ?? 'General';
          
          NotificationBanner.show(
            navigatorState: _navigatorKey.currentState!,
            title: notification.title ?? 'تنبيه جديد',
            message: notification.body ?? '',
            type: type,
            onTap: () {
              if (type == 'Booking') {
                _onNavigate('/bookings');
              }
            },
          );
        }
      });

      // 6. Handle notification click events (when app is opened from background)
      FirebaseMessaging.onMessageOpenedApp.listen((RemoteMessage message) {
        print('[FCM] Notification clicked and app opened from background: ${message.messageId}');
        _handleNotificationClick(message);
      });

      // 7. Check if app was opened from completely terminated state via notification
      final initialMessage = await messaging.getInitialMessage();
      if (initialMessage != null) {
        print('[FCM] Terminated state app opened via notification: ${initialMessage.messageId}');
        _handleNotificationClick(initialMessage);
      }

    } catch (e) {
      print('[FCM] Push Notification Service initialization failed: $e');
    }
  }

  Future<void> _registerTokenWithBackend(String token) async {
    try {
      print('[FCM] Registering token on backend: $token');
      await _apiClient.dio.post(
        '/api/projects/$_projectId/fcm-tokens',
        data: {'token': token},
      );
      print('[FCM] Token registered successfully on backend.');
    } catch (e) {
      print('[FCM] Failed to register token on backend: $e');
    }
  }

  void _handleNotificationClick(RemoteMessage message) {
    final type = message.data['type']?.toString() ?? 'General';
    if (type == 'Booking') {
      _onNavigate('/bookings');
    }
  }
}
