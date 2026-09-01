import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/settings/data/repositories/whatsapp_accounts_repository.dart';

const _projectId = '11111111-1111-1111-1111-111111111111';
const _accountId = '22222222-2222-2222-2222-222222222222';
const _createdAccountId = '33333333-3333-3333-3333-333333333333';
const _otherAccountId = '44444444-4444-4444-4444-444444444444';
final _uuidPattern = RegExp(
  r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$',
  caseSensitive: false,
);

void main() {
  test(
    'account-scoped API calls always carry project and account identity',
    () async {
      final requests = <RequestOptions>[];
      final dio = Dio();
      dio.interceptors.add(
        InterceptorsWrapper(
          onRequest: (options, handler) {
            _validateRequestScope(options);
            requests.add(options);
            final responseData = switch ((options.method, options.path)) {
              ('GET', '/api/whatsapp/accounts') => [
                {
                  'id': _accountId,
                  'projectId': _projectId,
                  'name': 'المبيعات',
                  'isDefault': true,
                },
              ],
              ('POST', '/api/whatsapp/accounts') => {
                'id': _createdAccountId,
                'projectId': _projectId,
                'name': 'فرع الجيزة',
                'isDefault': false,
              },
              ('PUT', '/api/whatsapp/accounts/$_accountId') => {
                'id': _accountId,
                'projectId': _projectId,
                'name': 'المبيعات',
                'isDefault': true,
              },
              ('GET', '/api/whatsapp/session/status') => {
                'projectId': _projectId,
                'whatsappAccountId': _accountId,
                'status': 'Connected',
                'phoneNumber': '201000000001',
              },
              ('GET', '/api/whatsapp/session/qr') => {
                'projectId': _projectId,
                'whatsappAccountId': _accountId,
                'qr': 'temporary-qr-payload',
              },
              ('POST', '/api/whatsapp/session/start') ||
              (
                'POST',
                '/api/whatsapp/session/disconnect',
              ) => <String, dynamic>{},
              _ => throw StateError(
                'Unexpected request: ${options.method} ${options.path}',
              ),
            };
            handler.resolve(
              Response<Object?>(
                requestOptions: options,
                statusCode: 200,
                data: responseData,
              ),
            );
          },
        ),
      );
      final repository = WhatsAppAccountsRepository.withDio(dio);

      final accounts = await repository.listAccounts(projectId: _projectId);
      final account = accounts.single;
      await repository.createAccount(projectId: _projectId, name: 'فرع الجيزة');
      await repository.setDefaultAccount(
        projectId: _projectId,
        account: account,
      );
      await repository.getStatus(
        projectId: _projectId,
        whatsappAccountId: _accountId,
      );
      await repository.getQr(
        projectId: _projectId,
        whatsappAccountId: _accountId,
      );
      await repository.startSession(
        projectId: _projectId,
        whatsappAccountId: _accountId,
      );
      await repository.disconnectSession(
        projectId: _projectId,
        whatsappAccountId: _accountId,
      );

      expect(requests.first.queryParameters, {'projectId': _projectId});
      expect(requests[1].data, {'projectId': _projectId, 'name': 'فرع الجيزة'});
      expect(requests[2].data, {
        'projectId': _projectId,
        'name': 'المبيعات',
        'isDefault': true,
      });
      for (final request in requests.skip(3).take(2)) {
        expect(request.queryParameters, {
          'projectId': _projectId,
          'whatsappAccountId': _accountId,
        });
      }
      for (final request in requests.skip(5)) {
        expect(request.data, {
          'projectId': _projectId,
          'whatsappAccountId': _accountId,
        });
      }
    },
  );

  test('rejects a status response scoped to another account', () async {
    final dio = Dio();
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          _validateRequestScope(options);
          handler.resolve(
            Response<Object?>(
              requestOptions: options,
              statusCode: 200,
              data: {
                'projectId': _projectId,
                'whatsappAccountId': _otherAccountId,
                'status': 'Connected',
              },
            ),
          );
        },
      ),
    );
    final repository = WhatsAppAccountsRepository.withDio(dio);

    await expectLater(
      repository.getStatus(
        projectId: _projectId,
        whatsappAccountId: _accountId,
      ),
      throwsFormatException,
    );
  });
}

void _validateRequestScope(RequestOptions request) {
  final body = request.data is Map ? request.data as Map : const {};
  _requireUuid(
    request.queryParameters['projectId'] ?? body['projectId'],
    'projectId',
  );

  final accountId =
      request.queryParameters['whatsappAccountId'] ?? body['whatsappAccountId'];
  if (accountId != null) {
    _requireUuid(accountId, 'whatsappAccountId');
  }
  if (request.method == 'PUT' &&
      request.path.startsWith('/api/whatsapp/accounts/')) {
    _requireUuid(request.path.split('/').last, 'accountId');
  }
}

void _requireUuid(Object? value, String fieldName) {
  if (value is! String || !_uuidPattern.hasMatch(value)) {
    throw StateError('$fieldName must be a UUID.');
  }
}
