import 'dart:async';
import 'dart:io';

import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';

import '../widgets/notification_banner.dart';
import 'api_client.dart';

@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage _) async {}

class PushNotificationService {
  static final ValueNotifier<String> statusNotifier = ValueNotifier<String>(
    'غير مسجل',
  );
  static final ValueNotifier<String?> tokenNotifier = ValueNotifier<String?>(
    null,
  );

  final ApiClient _apiClient;
  final String _projectId;
  final GlobalKey<NavigatorState> _navigatorKey;
  final Function(String route) _onNavigate;
  StreamSubscription<String>? _tokenRefreshSubscription;
  StreamSubscription<RemoteMessage>? _foregroundSubscription;
  StreamSubscription<RemoteMessage>? _openedSubscription;
  bool _disposed = false;
  bool _handlersConfigured = false;

  PushNotificationService({
    required ApiClient apiClient,
    required String projectId,
    required GlobalKey<NavigatorState> navigatorKey,
    required Function(String route) onNavigate,
  }) : _apiClient = apiClient,
       _projectId = projectId,
       _navigatorKey = navigatorKey,
       _onNavigate = onNavigate;

  Future<void> initialize() async {
    try {
      final messaging = FirebaseMessaging.instance;

      // On iOS, Firebase will fire this automatically once APNS is ready,
      // even if our manual getToken() call below fails.
      _tokenRefreshSubscription = messaging.onTokenRefresh.listen((
        newToken,
      ) async {
        if (_disposed) return;
        tokenNotifier.value = newToken;
        statusNotifier.value = 'جاري التسجيل في السيرفر...';
        await _registerTokenWithBackend(newToken);
      });

      statusNotifier.value = 'جاري طلب الصلاحيات...';
      final settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );
      if (_disposed) return;
      if (settings.authorizationStatus == AuthorizationStatus.denied) {
        statusNotifier.value = 'تم رفض صلاحيات الإشعارات';
        _setupMessageHandlers(messaging);
        return;
      }

      if (Platform.isIOS) {
        statusNotifier.value = 'جاري انتظار معرف APNS من آبل...';
        String? apnsToken;
        const maxRetries = 20;

        for (int i = 0; i < maxRetries; i++) {
          if (_disposed) return;
          apnsToken = await messaging.getAPNSToken();
          if (apnsToken != null) {
            break;
          }
          await Future.delayed(const Duration(seconds: 1));
        }

        if (apnsToken == null) {
          // Calling getToken before APNS is ready can fail; the listener recovers later.
          statusNotifier.value =
              'في انتظار تسجيل APNS... (سيتم تلقائياً عند الجاهزية)';
          _setupMessageHandlers(messaging);
          return;
        }

        // Grace period — let Firebase internals sync with the APNS token
        await Future.delayed(const Duration(seconds: 2));
      }

      if (_disposed) return;
      statusNotifier.value = 'جاري جلب رمز FCM...';
      String? token;
      const tokenRetries = 3;

      for (int i = 0; i < tokenRetries; i++) {
        if (_disposed) return;
        try {
          token = await messaging.getToken();
          if (token != null) break;
        } catch (_) {
          if (i < tokenRetries - 1) {
            await Future.delayed(const Duration(seconds: 2));
          }
        }
      }

      if (token != null) {
        if (_disposed) return;
        tokenNotifier.value = token;
        statusNotifier.value = 'جاري التسجيل في السيرفر...';
        await _registerTokenWithBackend(token);
      } else {
        statusNotifier.value =
            'في انتظار رمز FCM... (سيتم تلقائياً عند الجاهزية)';
      }

      _setupMessageHandlers(messaging);
    } catch (_) {
      if (!_disposed) {
        statusNotifier.value =
            'تعذر تفعيل الإشعارات الآن. تحقق من الاتصال وحاول مجددًا.';
      }
    }
  }

  /// Sets up foreground, background, and notification-click handlers.
  /// Called regardless of whether token registration succeeded, so that
  /// notifications still work if the token arrives later via onTokenRefresh.
  void _setupMessageHandlers(FirebaseMessaging messaging) {
    if (_disposed || _handlersConfigured) return;
    _handlersConfigured = true;
    FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);

    _foregroundSubscription = FirebaseMessaging.onMessage.listen((
      RemoteMessage message,
    ) {
      if (_disposed) return;
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

    _openedSubscription = FirebaseMessaging.onMessageOpenedApp.listen((
      RemoteMessage message,
    ) {
      if (_disposed) return;
      _handleNotificationClick(message);
    });

    messaging.getInitialMessage().then((message) {
      if (!_disposed && message != null) {
        _handleNotificationClick(message);
      }
    });
  }

  Future<void> _registerTokenWithBackend(String token) async {
    if (_disposed) return;
    try {
      await _apiClient.dio.post(
        '/api/projects/$_projectId/fcm-tokens',
        data: {'token': token},
      );
      if (!_disposed) statusNotifier.value = 'مسجل ونشط';
    } catch (_) {
      if (!_disposed) {
        statusNotifier.value =
            'تعذر تسجيل الإشعارات بالخادم. تحقق من الاتصال وحاول مجددًا.';
      }
    }
  }

  void _handleNotificationClick(RemoteMessage message) {
    final type = message.data['type']?.toString() ?? 'General';
    if (type == 'Booking') {
      _onNavigate('/bookings');
    }
  }

  void dispose() {
    _disposed = true;
    unawaited(_tokenRefreshSubscription?.cancel());
    unawaited(_foregroundSubscription?.cancel());
    unawaited(_openedSubscription?.cancel());
    _tokenRefreshSubscription = null;
    _foregroundSubscription = null;
    _openedSubscription = null;
    tokenNotifier.value = null;
    statusNotifier.value = 'غير مسجل';
  }
}
