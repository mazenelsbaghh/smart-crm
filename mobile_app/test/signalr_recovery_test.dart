import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/core/services/secure_storage.dart';
import 'package:mobile_app/core/services/signalr_service.dart';

void main() {
  test(
    'initial connection failure recovers and stop cancels future retries',
    () async {
      final reserved = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      final port = reserved.port;
      await reserved.close(force: true);
      final connected = Completer<String>();
      final service = SignalRService(
        secureStorage: SecureStorageService(),
        wsUrl: 'http://127.0.0.1:$port/hubs',
        accessTokenProvider: () async => 'test-token',
      )..onConnected = connected.complete;
      addTearDown(service.stop);
      expect(await service.start(projectId: 'project-a'), isFalse);

      final server = await HttpServer.bind(InternetAddress.loopbackIPv4, port);
      final sockets = <WebSocket>[];
      addTearDown(() async {
        for (final socket in sockets) {
          await socket.close();
        }
        await server.close(force: true);
      });
      server.listen((request) async {
        if (request.uri.path.endsWith('/negotiate')) {
          request.response.headers.contentType = ContentType.json;
          request.response.write(
            jsonEncode({
              'negotiateVersion': 1,
              'connectionId': 'test-connection',
              'connectionToken': 'test-connection-token',
              'availableTransports': [
                {
                  'transport': 'WebSockets',
                  'transferFormats': ['Text', 'Binary'],
                },
              ],
            }),
          );
          await request.response.close();
        } else if (WebSocketTransformer.isUpgradeRequest(request)) {
          final socket = await WebSocketTransformer.upgrade(request);
          sockets.add(socket);
          socket.listen((message) {
            final text = message is String
                ? message
                : utf8.decode(message as List<int>);
            for (final frame
                in text.split('\u001e').where((frame) => frame.isNotEmpty)) {
              final payload = jsonDecode(frame) as Map<String, dynamic>;
              if (payload.containsKey('protocol')) {
                socket.add('{}\u001e');
              } else if (payload['type'] == 1) {
                expect(payload['target'], 'JoinProjectGroup');
                expect(payload['arguments'], ['project-a']);
                socket.add(
                  '${jsonEncode({'type': 3, 'invocationId': payload['invocationId']})}\u001e',
                );
              }
            }
          });
        } else {
          request.response.statusCode = 404;
          await request.response.close();
        }
      });

      expect(
        await connected.future.timeout(const Duration(seconds: 15)),
        'project-a',
      );
      await service.stop();
      final stoppedConnections = sockets.length;
      await Future<void>.delayed(const Duration(seconds: 6));
      expect(sockets.length, stoppedConnections);
    },
    timeout: const Timeout(Duration(seconds: 30)),
  );
}
