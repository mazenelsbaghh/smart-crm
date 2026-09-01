import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/core/theme/app_theme.dart';
import 'package:mobile_app/features/settings/data/repositories/whatsapp_accounts_repository.dart';
import 'package:mobile_app/features/settings/presentation/widgets/whatsapp_accounts_section.dart';
import 'package:qr_flutter/qr_flutter.dart';

const _projectId = '11111111-1111-1111-1111-111111111111';
const _mainAccountId = '22222222-2222-2222-2222-222222222222';
const _branchAccountId = '33333333-3333-3333-3333-333333333333';
final _uuidPattern = RegExp(
  r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$',
  caseSensitive: false,
);

void main() {
  testWidgets('a late status response cannot overwrite newer account state', (
    tester,
  ) async {
    final olderMainStatus = Completer<Map<String, dynamic>>();
    final apiStub = _WhatsAppHttpStub();
    apiStub.statusResponse = (accountId, callCount) {
      if (accountId == _mainAccountId && callCount == 1) {
        return olderMainStatus.future;
      }
      if (accountId == _mainAccountId) {
        return Future.value(
          _statusJson(accountId, 'Connected', phoneNumber: '201000000009'),
        );
      }
      return Future.value(_statusJson(accountId, 'Disconnected'));
    };

    await tester.pumpWidget(_testApp(apiStub.repository));
    await _pumpHttpResponses(tester);

    final mainRow = find.byKey(
      const ValueKey('whatsapp-account-$_mainAccountId'),
    );
    final branchRow = find.byKey(
      const ValueKey('whatsapp-account-$_branchAccountId'),
    );
    expect(mainRow, findsOneWidget);
    expect(branchRow, findsOneWidget);
    expect(
      find.descendant(of: branchRow, matching: find.text('غير متصل')),
      findsOneWidget,
    );

    final refreshMain = find.descendant(
      of: mainRow,
      matching: find.widgetWithText(OutlinedButton, 'تحديث الحالة'),
    );
    await tester.tap(refreshMain);
    await _pumpHttpResponses(tester);

    expect(
      find.descendant(of: mainRow, matching: find.text('متصل')),
      findsOneWidget,
    );
    expect(
      find.descendant(of: mainRow, matching: find.text('+201000000009')),
      findsOneWidget,
    );

    olderMainStatus.complete(_statusJson(_mainAccountId, 'Disconnected'));
    await _pumpHttpResponses(tester);

    expect(
      find.descendant(of: mainRow, matching: find.text('متصل')),
      findsOneWidget,
    );
    expect(
      find.descendant(of: mainRow, matching: find.text('غير متصل')),
      findsNothing,
    );
    expect(
      find.descendant(of: branchRow, matching: find.text('غير متصل')),
      findsOneWidget,
    );
  });

  testWidgets(
    'starting one account shows only its scoped QR without exposing data',
    (tester) async {
      const qrPayload = 'temporary-private-qr-payload';
      final apiStub = _WhatsAppHttpStub(branchQrPayload: qrPayload);
      final semantics = tester.ensureSemantics();

      await tester.pumpWidget(_testApp(apiStub.repository));
      await _pumpHttpResponses(tester);
      final branchRow = find.byKey(
        const ValueKey('whatsapp-account-$_branchAccountId'),
      );
      final connectBranch = find.descendant(
        of: branchRow,
        matching: find.widgetWithText(ElevatedButton, 'ربط الرقم'),
      );

      await tester.tap(connectBranch);
      await _pumpHttpResponses(tester);

      expect(
        find.descendant(of: branchRow, matching: find.byType(QrImageView)),
        findsOneWidget,
      );
      expect(find.bySemanticsLabel('كود ربط حساب فرع الجيزة'), findsOneWidget);
      expect(find.bySemanticsLabel(qrPayload), findsNothing);
      expect(
        find.descendant(
          of: find.byKey(const ValueKey('whatsapp-account-$_mainAccountId')),
          matching: find.byType(QrImageView),
        ),
        findsNothing,
      );
      semantics.dispose();
    },
  );
}

Future<void> _pumpHttpResponses(WidgetTester tester) async {
  for (var attempt = 0; attempt < 4; attempt++) {
    await tester.pump(const Duration(milliseconds: 10));
  }
}

Widget _testApp(WhatsAppAccountsRepository repository) {
  return MaterialApp(
    theme: AppTheme.dark,
    home: Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        body: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: WhatsAppAccountsSection(
            projectId: _projectId,
            repository: repository,
          ),
        ),
      ),
    ),
  );
}

Map<String, dynamic> _statusJson(
  String accountId,
  String status, {
  String? phoneNumber,
}) {
  return {
    'projectId': _projectId,
    'whatsappAccountId': accountId,
    'status': status,
    'phoneNumber': phoneNumber,
  };
}

class _WhatsAppHttpStub {
  static const _accounts = [
    {
      'id': _mainAccountId,
      'projectId': _projectId,
      'name': 'المبيعات الرئيسي',
      'isDefault': true,
    },
    {
      'id': _branchAccountId,
      'projectId': _projectId,
      'name': 'فرع الجيزة',
      'isDefault': false,
    },
  ];

  final Dio dio = Dio();
  final String? branchQrPayload;
  final Map<String, int> _statusCalls = {};
  late Future<Map<String, dynamic>> Function(String accountId, int callCount)
  statusResponse;

  _WhatsAppHttpStub({this.branchQrPayload}) {
    statusResponse = (accountId, _) =>
        Future.value(_statusJson(accountId, 'Disconnected'));
    dio.interceptors.add(InterceptorsWrapper(onRequest: _respond));
  }

  WhatsAppAccountsRepository get repository =>
      WhatsAppAccountsRepository.withDio(dio);

  Future<void> _respond(
    RequestOptions request,
    RequestInterceptorHandler handler,
  ) async {
    final requestData = request.data is Map ? request.data as Map : const {};
    _requireUuid(
      request.queryParameters['projectId'] ?? requestData['projectId'],
      'projectId',
    );
    final accountId =
        request.queryParameters['whatsappAccountId'] as String? ??
        requestData['whatsappAccountId'] as String?;
    if (request.path != '/api/whatsapp/accounts') {
      _requireUuid(accountId, 'whatsappAccountId');
    }
    final responseJson = switch ((request.method, request.path)) {
      ('GET', '/api/whatsapp/accounts') => _accounts,
      ('GET', '/api/whatsapp/session/status') => await _nextStatus(accountId!),
      ('POST', '/api/whatsapp/session/start') => <String, dynamic>{},
      ('GET', '/api/whatsapp/session/qr') => _qrJson(accountId!),
      _ => throw StateError(
        'Unexpected request: ${request.method} ${request.path}',
      ),
    };
    handler.resolve(
      Response<Object?>(
        requestOptions: request,
        statusCode: 200,
        data: responseJson,
      ),
    );
  }

  Future<Map<String, dynamic>> _nextStatus(String accountId) {
    final callCount = (_statusCalls[accountId] ?? 0) + 1;
    _statusCalls[accountId] = callCount;
    return statusResponse(accountId, callCount);
  }

  Map<String, dynamic> _qrJson(String accountId) {
    return {
      'projectId': _projectId,
      'whatsappAccountId': accountId,
      if (accountId == _branchAccountId && branchQrPayload != null)
        'qr': branchQrPayload,
      if (accountId != _branchAccountId || branchQrPayload == null)
        'error': 'QR unavailable',
    };
  }
}

void _requireUuid(Object? value, String fieldName) {
  if (value is! String || !_uuidPattern.hasMatch(value)) {
    throw StateError('$fieldName must be a UUID.');
  }
}
