import 'dart:async';
import 'dart:io';

import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';

import '../widgets/notification_banner.dart';
import 'api_client.dart';

@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  // Handles background and terminated state notification events
  print('[FCM] Background message received: ${message.messageId}');
}

class PushNotificationService {
  static final ValueNotifier<String> statusNotifier = ValueNotifier<String>('غير مسجل (Not Registered)');
  static final ValueNotifier<String?> tokenNotifier = ValueNotifier<String?>(null);

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
      final messaging = FirebaseMessaging.instance;

      // ── 1. Set up onTokenRefresh EARLY as a reliable fallback ──
      // On iOS, Firebase will fire this automatically once APNS is ready,
      // even if our manual getToken() call below fails.
      messaging.onTokenRefresh.listen((newToken) async {
        print('[FCM] onTokenRefresh fired with token: $newToken');
        tokenNotifier.value = newToken;
        statusNotifier.value = 'جاري التسجيل في السيرفر...';
        await _registerTokenWithBackend(newToken);
      });

      // ── 2. Request notification permission ──
      statusNotifier.value = 'جاري طلب الصلاحيات...';
      final settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );
      print('[FCM] Permission status: ${settings.authorizationStatus}');

      if (settings.authorizationStatus == AuthorizationStatus.denied) {
        statusNotifier.value = 'تم رفض صلاحيات الإشعارات';
        _setupMessageHandlers(messaging);
        return;
      }

      // ── 3. On iOS, wait for APNS token from Apple ──
      if (Platform.isIOS) {
        statusNotifier.value = 'جاري انتظار معرف APNS من آبل...';
        String? apnsToken;
        const maxRetries = 20; // Wait up to 20 seconds

        for (int i = 0; i < maxRetries; i++) {
          apnsToken = await messaging.getAPNSToken();
          if (apnsToken != null) {
            print('[FCM] APNS Token retrieved on attempt ${i + 1}: $apnsToken');
            break;
          }
          print('[FCM] APNS token not ready yet (${i + 1}/$maxRetries)...');
          await Future.delayed(const Duration(seconds: 1));
        }

        if (apnsToken == null) {
          // APNS token never arrived — don't try getToken(), it will crash.
          // The onTokenRefresh listener above will pick it up later if it arrives.
          print('[FCM] APNS token not available after $maxRetries seconds. '
              'Relying on onTokenRefresh fallback.');
          statusNotifier.value =
              'في انتظار تسجيل APNS... (سيتم تلقائياً عند الجاهزية)';
          _setupMessageHandlers(messaging);
          return;
        }

        // Grace period — let Firebase internals sync with the APNS token
        await Future.delayed(const Duration(seconds: 2));
      }

      // ── 4. Fetch FCM token (with retry) ──
      statusNotifier.value = 'جاري جلب رمز FCM...';
      String? token;
      const tokenRetries = 3;

      for (int i = 0; i < tokenRetries; i++) {
        try {
          token = await messaging.getToken();
          if (token != null) break;
        } catch (e) {
          print('[FCM] getToken() attempt ${i + 1} failed: $e');
          if (i < tokenRetries - 1) {
            await Future.delayed(const Duration(seconds: 2));
          }
        }
      }

      if (token != null) {
        print('[FCM] FCM token obtained: $token');
        tokenNotifier.value = token;
        statusNotifier.value = 'جاري التسجيل في السيرفر...';
        await _registerTokenWithBackend(token);
      } else {
        print('[FCM] Could not get FCM token after retries. '
            'Relying on onTokenRefresh fallback.');
        statusNotifier.value =
            'في انتظار رمز FCM... (سيتم تلقائياً عند الجاهزية)';
      }

      // ── 5. Set up all message handlers ──
      _setupMessageHandlers(messaging);

    } catch (e) {
      statusNotifier.value = 'فشل التهيئة: $e';
      print('[FCM] Push Notification Service initialization failed: $e');
    }
  }

  /// Sets up foreground, background, and notification-click handlers.
  /// Called regardless of whether token registration succeeded, so that
  /// notifications still work if the token arrives later via onTokenRefresh.
  void _setupMessageHandlers(FirebaseMessaging messaging) {
    // Background message handler
    FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);

    // Foreground notifications → show premium banner
    FirebaseMessaging.onMessage.listen((RemoteMessage message) {
      print('[FCM] Foreground message: ${message.messageId}');

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

    // Notification click from background
    FirebaseMessaging.onMessageOpenedApp.listen((RemoteMessage message) {
      print('[FCM] Notification click (background): ${message.messageId}');
      _handleNotificationClick(message);
    });

    // App launched from terminated state via notification
    messaging.getInitialMessage().then((message) {
      if (message != null) {
        print('[FCM] Launched from notification (terminated): ${message.messageId}');
        _handleNotificationClick(message);
      }
    });
  }

  Future<void> _registerTokenWithBackend(String token) async {
    try {
      print('[FCM] Registering token on backend: $token');
      await _apiClient.dio.post(
        '/api/projects/$_projectId/fcm-tokens',
        data: {'token': token},
      );
      statusNotifier.value = 'مسجل ونشط (Registered & Active)';
      print('[FCM] Token registered successfully on backend.');
    } catch (e) {
      statusNotifier.value = 'فشل التسجيل في السيرفر: $e';
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
