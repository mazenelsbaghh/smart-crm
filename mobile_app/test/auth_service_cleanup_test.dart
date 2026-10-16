import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/core/services/api_client.dart';
import 'package:mobile_app/core/services/push_notification_service.dart';
import 'package:mobile_app/features/auth/bloc/auth_bloc.dart';
import 'package:mobile_app/features/auth/presentation/login_screen.dart';
import 'package:mobile_app/main.dart';

void main() {
  testWidgets(
    'late logout cleanup preserves the next login push subscription',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      final oldTokenDeletion = Completer<void>();
      final deletedTokens = <String>[];
      await tester.pumpWidget(const MyApp());
      await tester.pumpAndSettle();
      final context = tester.element(find.byType(LoginScreen));
      final auth = context.read<AuthBloc>();
      context.read<ApiClient>().dio.interceptors.add(
        InterceptorsWrapper(
          onRequest: (request, handler) async {
            Object? response = <String, dynamic>{};
            if (request.path == '/api/auth/login') {
              response = {
                'accessToken': 'test-access-token',
                'refreshToken': 'test-refresh-token',
                'user': {
                  'id': 'user-1',
                  'email': 'agent@example.com',
                  'role': 'Agent',
                },
              };
            } else if (request.path == '/api/projects') {
              response = [
                {'id': 'project-1', 'name': 'Project', 'settings': {}},
              ];
            } else if (request.path == '/api/projects/project-1') {
              response = {'id': 'project-1', 'name': 'Project', 'settings': {}};
            } else if (request.path.endsWith('/fcm-tokens') &&
                request.method == 'DELETE') {
              final token = request.data['token'] as String;
              deletedTokens.add(token);
              if (token == 'old-device-token') await oldTokenDeletion.future;
            } else if (request.method == 'GET') {
              response = [];
            }
            handler.resolve(
              Response<Object?>(
                requestOptions: request,
                statusCode: 200,
                data: jsonDecode(jsonEncode(response)),
              ),
            );
          },
        ),
      );

      Future<void> flushServices() async {
        for (var turn = 0; turn < 20; turn++) {
          await tester.pump(const Duration(milliseconds: 50));
        }
      }

      auth.add(
        const AuthLoginRequested(email: 'agent@example.com', password: 'test'),
      );
      await flushServices();
      expect(auth.state, isA<AuthAuthenticated>());
      // Seed the device token returned by the optional native Firebase service.
      PushNotificationService.tokenNotifier.value = 'old-device-token';
      auth.add(AuthLogoutRequested());
      await flushServices();
      expect(deletedTokens, ['old-device-token']);

      auth.add(
        const AuthLoginRequested(email: 'agent@example.com', password: 'test'),
      );
      await flushServices();
      expect(auth.state, isA<AuthAuthenticated>());
      oldTokenDeletion.complete();
      await flushServices();

      PushNotificationService.tokenNotifier.value = 'new-device-token';
      auth.add(AuthLogoutRequested());
      await flushServices();
      expect(deletedTokens, ['old-device-token', 'new-device-token']);
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pump();
    },
  );
}
