import 'package:dio/dio.dart';

import '../../../../core/services/api_client.dart';
import '../models/whatsapp_account.dart';

class WhatsAppAccountsRepository {
  final Dio _dio;

  WhatsAppAccountsRepository({required ApiClient apiClient})
    : _dio = apiClient.dio;

  WhatsAppAccountsRepository.withDio(this._dio);

  Future<List<WhatsAppAccount>> listAccounts({
    required String projectId,
    CancelToken? cancelToken,
  }) async {
    final response = await _dio.get<Object?>(
      '/api/whatsapp/accounts',
      queryParameters: {'projectId': projectId},
      cancelToken: cancelToken,
    );
    final accountsPayload = response.data;
    if (accountsPayload is! List) {
      throw const FormatException('Invalid WhatsApp accounts response');
    }

    final accounts = accountsPayload
        .map(WhatsAppAccount.fromJson)
        .toList(growable: false);
    final ids = <String>{};
    for (final account in accounts) {
      if (account.projectId != projectId || !ids.add(account.id)) {
        throw const FormatException('Mismatched WhatsApp account response');
      }
    }
    return accounts;
  }

  Future<WhatsAppAccount> createAccount({
    required String projectId,
    required String name,
    CancelToken? cancelToken,
  }) async {
    final response = await _dio.post<Object?>(
      '/api/whatsapp/accounts',
      data: {'projectId': projectId, 'name': name},
      cancelToken: cancelToken,
    );
    final account = WhatsAppAccount.fromJson(response.data);
    _validateAccount(account, projectId: projectId);
    return account;
  }

  Future<WhatsAppAccount> setDefaultAccount({
    required String projectId,
    required WhatsAppAccount account,
    CancelToken? cancelToken,
  }) async {
    final response = await _dio.put<Object?>(
      '/api/whatsapp/accounts/${account.id}',
      data: {'projectId': projectId, 'name': account.name, 'isDefault': true},
      cancelToken: cancelToken,
    );
    final updatedAccount = WhatsAppAccount.fromJson(response.data);
    _validateAccount(
      updatedAccount,
      projectId: projectId,
      whatsappAccountId: account.id,
    );
    if (!updatedAccount.isDefault) {
      throw const FormatException('Default WhatsApp account was not updated');
    }
    return updatedAccount;
  }

  Future<WhatsAppSessionSnapshot> getStatus({
    required String projectId,
    required String whatsappAccountId,
    CancelToken? cancelToken,
  }) async {
    final response = await _dio.get<Object?>(
      '/api/whatsapp/session/status',
      queryParameters: {
        'projectId': projectId,
        'whatsappAccountId': whatsappAccountId,
      },
      cancelToken: cancelToken,
    );
    final snapshot = WhatsAppSessionSnapshot.fromJson(response.data);
    _validateResponseScope(
      responseProjectId: snapshot.projectId,
      responseAccountId: snapshot.whatsappAccountId,
      projectId: projectId,
      whatsappAccountId: whatsappAccountId,
    );
    return snapshot;
  }

  Future<WhatsAppQrPayload> getQr({
    required String projectId,
    required String whatsappAccountId,
    CancelToken? cancelToken,
  }) async {
    final response = await _dio.get<Object?>(
      '/api/whatsapp/session/qr',
      queryParameters: {
        'projectId': projectId,
        'whatsappAccountId': whatsappAccountId,
      },
      options: Options(
        validateStatus: (status) => status == 200 || status == 404,
      ),
      cancelToken: cancelToken,
    );
    final payload = WhatsAppQrPayload.fromJson(response.data);
    _validateResponseScope(
      responseProjectId: payload.projectId ?? projectId,
      responseAccountId: payload.whatsappAccountId,
      projectId: projectId,
      whatsappAccountId: whatsappAccountId,
    );
    return payload;
  }

  Future<void> startSession({
    required String projectId,
    required String whatsappAccountId,
    CancelToken? cancelToken,
  }) async {
    await _dio.post<void>(
      '/api/whatsapp/session/start',
      data: {'projectId': projectId, 'whatsappAccountId': whatsappAccountId},
      cancelToken: cancelToken,
    );
  }

  Future<void> disconnectSession({
    required String projectId,
    required String whatsappAccountId,
    CancelToken? cancelToken,
  }) async {
    await _dio.post<void>(
      '/api/whatsapp/session/disconnect',
      data: {'projectId': projectId, 'whatsappAccountId': whatsappAccountId},
      cancelToken: cancelToken,
    );
  }

  static void _validateAccount(
    WhatsAppAccount account, {
    required String projectId,
    String? whatsappAccountId,
  }) {
    if (account.projectId != projectId ||
        (whatsappAccountId != null && account.id != whatsappAccountId)) {
      throw const FormatException('Mismatched WhatsApp account response');
    }
  }

  static void _validateResponseScope({
    required String? responseProjectId,
    required String? responseAccountId,
    required String projectId,
    required String whatsappAccountId,
  }) {
    if (responseProjectId != projectId ||
        (responseAccountId != null && responseAccountId != whatsappAccountId)) {
      throw const FormatException('Mismatched WhatsApp session response');
    }
  }
}
