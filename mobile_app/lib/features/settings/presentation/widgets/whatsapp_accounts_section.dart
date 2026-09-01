import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../../../core/services/user_facing_error.dart';
import '../../../../core/theme/colors.dart';
import '../../../../core/theme/typography.dart';
import '../../data/models/whatsapp_account.dart';
import '../../data/repositories/whatsapp_accounts_repository.dart';

class WhatsAppAccountsSection extends StatefulWidget {
  final String projectId;
  final WhatsAppAccountsRepository repository;

  const WhatsAppAccountsSection({
    required this.projectId,
    required this.repository,
    super.key,
  });

  @override
  State<WhatsAppAccountsSection> createState() =>
      _WhatsAppAccountsSectionState();
}

class _WhatsAppAccountsSectionState extends State<WhatsAppAccountsSection>
    with WidgetsBindingObserver {
  final TextEditingController _newAccountNameController =
      TextEditingController();
  final Map<String, _AccountRuntime> _runtimeByAccount = {};
  final Map<String, int> _statusGenerationByAccount = {};
  final Map<String, int> _qrGenerationByAccount = {};

  List<WhatsAppAccount> _accounts = const [];
  Timer? _pollTimer;
  late CancelToken _projectCancelToken;
  int _projectEpoch = 1;
  int _listGeneration = 0;
  bool _appIsActive = true;
  bool _loading = true;
  bool _adding = false;
  bool _defaultMutationBusy = false;
  String? _loadError;
  String? _addError;
  String? _notice;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _projectCancelToken = CancelToken();
    unawaited(_loadAccounts());
  }

  @override
  void didUpdateWidget(covariant WhatsAppAccountsSection oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.projectId != widget.projectId) _switchProject();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    _appIsActive = state == AppLifecycleState.resumed;
    if (_appIsActive) {
      _restartPolling();
      for (final account in _accounts) {
        final runtime = _runtimeByAccount[account.id];
        if (_isPending(runtime?.status)) unawaited(_fetchStatus(account.id));
      }
    } else {
      _pollTimer?.cancel();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _projectEpoch++;
    _pollTimer?.cancel();
    _projectCancelToken.cancel('WhatsApp accounts section disposed');
    for (final runtime in _runtimeByAccount.values) {
      runtime.qrValue = null;
    }
    _runtimeByAccount.clear();
    _newAccountNameController.dispose();
    super.dispose();
  }

  void _switchProject() {
    _projectEpoch++;
    _listGeneration++;
    _pollTimer?.cancel();
    _projectCancelToken.cancel('Active project changed');
    _projectCancelToken = CancelToken();
    _statusGenerationByAccount.clear();
    _qrGenerationByAccount.clear();
    for (final runtime in _runtimeByAccount.values) {
      runtime.qrValue = null;
    }
    setState(() {
      _accounts = const [];
      _runtimeByAccount.clear();
      _loading = true;
      _adding = false;
      _defaultMutationBusy = false;
      _loadError = null;
      _addError = null;
      _notice = null;
      _newAccountNameController.clear();
    });
    unawaited(_loadAccounts());
  }

  Future<void> _loadAccounts() async {
    const failureMessage = 'تعذر تحميل حسابات واتساب. حاول مرة أخرى.';
    final projectEpoch = _projectEpoch;
    final listGeneration = ++_listGeneration;
    setState(() {
      _loading = true;
      _loadError = null;
    });

    try {
      final accounts = await widget.repository.listAccounts(
        projectId: widget.projectId,
        cancelToken: _projectCancelToken,
      );
      if (!_isCurrentList(projectEpoch, listGeneration)) return;

      final sortedAccounts = accounts.toList(growable: false)
        ..sort((left, right) {
          if (left.isDefault == right.isDefault) return 0;
          return left.isDefault ? -1 : 1;
        });
      setState(() {
        _accounts = sortedAccounts;
        _runtimeByAccount
          ..clear()
          ..addEntries(
            sortedAccounts.map(
              (account) => MapEntry(account.id, _AccountRuntime()),
            ),
          );
        _loading = false;
      });

      for (final account in sortedAccounts) {
        unawaited(_fetchStatus(account.id));
      }
    } on DioException catch (error) {
      if (CancelToken.isCancel(error) ||
          !_isCurrentList(projectEpoch, listGeneration)) {
        return;
      }
      setState(() {
        _loading = false;
        _loadError = userFacingError(error);
      });
    } on FormatException {
      if (!_isCurrentList(projectEpoch, listGeneration)) return;
      setState(() {
        _loading = false;
        _loadError = failureMessage;
      });
    }
  }

  Future<void> _fetchStatus(String accountId) async {
    const failureMessage = 'تعذر تحديث الحالة. قد تكون الحالة المعروضة قديمة.';
    final runtime = _runtimeByAccount[accountId];
    if (runtime == null || runtime.action != null) return;

    final projectEpoch = _projectEpoch;
    final generation = _nextGeneration(_statusGenerationByAccount, accountId);
    setState(() {
      runtime.statusBusy = true;
      runtime.error = null;
    });

    try {
      final snapshot = await widget.repository.getStatus(
        projectId: widget.projectId,
        whatsappAccountId: accountId,
        cancelToken: _projectCancelToken,
      );
      if (!_isCurrentAccountRequest(
        projectEpoch,
        accountId,
        generation,
        _statusGenerationByAccount[accountId],
      )) {
        return;
      }

      setState(() {
        runtime.status = snapshot.status;
        runtime.phoneNumber = snapshot.phoneNumber;
        runtime.error = snapshot.error;
        if (snapshot.status != WhatsAppSessionStatus.initializing) {
          _nextGeneration(_qrGenerationByAccount, accountId);
          runtime.qrValue = null;
          runtime.qrError = null;
          runtime.qrBusy = false;
        }
      });
      if (snapshot.status == WhatsAppSessionStatus.initializing &&
          !runtime.qrBusy) {
        unawaited(_fetchQr(accountId));
      }
      _restartPolling();
    } on DioException catch (error) {
      if (CancelToken.isCancel(error) ||
          !_isCurrentAccountRequest(
            projectEpoch,
            accountId,
            generation,
            _statusGenerationByAccount[accountId],
          )) {
        return;
      }
      setState(() => runtime.error = userFacingError(error));
    } on FormatException {
      if (!_isCurrentAccountRequest(
        projectEpoch,
        accountId,
        generation,
        _statusGenerationByAccount[accountId],
      )) {
        return;
      }
      setState(() => runtime.error = failureMessage);
    } finally {
      if (_isCurrentAccountRequest(
        projectEpoch,
        accountId,
        generation,
        _statusGenerationByAccount[accountId],
      )) {
        setState(() => runtime.statusBusy = false);
      }
    }
  }

  Future<void> _fetchQr(String accountId) async {
    const failureMessage = 'تعذر تحميل كود الربط.';
    final runtime = _runtimeByAccount[accountId];
    if (runtime == null ||
        runtime.action != null ||
        runtime.qrBusy ||
        runtime.status != WhatsAppSessionStatus.initializing) {
      return;
    }

    final projectEpoch = _projectEpoch;
    final generation = _nextGeneration(_qrGenerationByAccount, accountId);
    setState(() {
      runtime.qrBusy = true;
      runtime.qrError = null;
    });

    try {
      final payload = await widget.repository.getQr(
        projectId: widget.projectId,
        whatsappAccountId: accountId,
        cancelToken: _projectCancelToken,
      );
      if (!_isCurrentAccountRequest(
        projectEpoch,
        accountId,
        generation,
        _qrGenerationByAccount[accountId],
      )) {
        return;
      }

      setState(() {
        runtime.qrValue = payload.value;
        runtime.qrError = payload.value == null
            ? payload.error ?? 'كود الربط غير جاهز بعد.'
            : null;
      });
    } on DioException catch (error) {
      if (CancelToken.isCancel(error) ||
          !_isCurrentAccountRequest(
            projectEpoch,
            accountId,
            generation,
            _qrGenerationByAccount[accountId],
          )) {
        return;
      }
      setState(() {
        runtime.qrValue = null;
        runtime.qrError = userFacingError(error);
      });
    } on FormatException {
      if (!_isCurrentAccountRequest(
        projectEpoch,
        accountId,
        generation,
        _qrGenerationByAccount[accountId],
      )) {
        return;
      }
      setState(() {
        runtime.qrValue = null;
        runtime.qrError = failureMessage;
      });
    } finally {
      if (_isCurrentAccountRequest(
        projectEpoch,
        accountId,
        generation,
        _qrGenerationByAccount[accountId],
      )) {
        setState(() => runtime.qrBusy = false);
      }
    }
  }

  Future<void> _addAccount() async {
    const failureMessage = 'تعذر إضافة حساب واتساب.';
    if (_adding) return;
    final name = _newAccountNameController.text.trim();
    if (name.isEmpty) {
      setState(() => _addError = 'اكتب اسمًا يميّز الحساب.');
      return;
    }
    if (name.length > 100) {
      setState(() => _addError = 'اسم الحساب يجب ألا يتجاوز 100 حرف.');
      return;
    }

    final projectEpoch = _projectEpoch;
    setState(() {
      _adding = true;
      _addError = null;
      _notice = null;
    });
    try {
      final account = await widget.repository.createAccount(
        projectId: widget.projectId,
        name: name,
        cancelToken: _projectCancelToken,
      );
      if (!_isCurrentProject(projectEpoch)) return;

      setState(() {
        _accounts = account.isDefault
            ? [
                account,
                ..._accounts.map(
                  (existing) => existing.copyWith(isDefault: false),
                ),
              ]
            : [..._accounts, account];
        _runtimeByAccount[account.id] = _AccountRuntime();
        _newAccountNameController.clear();
        _adding = false;
        _notice = 'تمت إضافة حساب «${account.name}». اربط رقمه الآن.';
      });
      unawaited(_fetchStatus(account.id));
    } on DioException catch (error) {
      if (CancelToken.isCancel(error) || !_isCurrentProject(projectEpoch)) {
        return;
      }
      setState(() {
        _adding = false;
        _addError = userFacingError(error);
      });
    } on FormatException {
      if (!_isCurrentProject(projectEpoch)) return;
      setState(() {
        _adding = false;
        _addError = failureMessage;
      });
    }
  }

  Future<void> _startSession(WhatsAppAccount account) async {
    final runtime = _runtimeByAccount[account.id];
    if (runtime == null || runtime.action != null) return;
    final projectEpoch = _projectEpoch;
    _nextGeneration(_statusGenerationByAccount, account.id);
    _nextGeneration(_qrGenerationByAccount, account.id);
    setState(() {
      runtime.action = _AccountAction.start;
      runtime.error = null;
      runtime.qrError = null;
      _notice = null;
    });

    try {
      await widget.repository.startSession(
        projectId: widget.projectId,
        whatsappAccountId: account.id,
        cancelToken: _projectCancelToken,
      );
      if (!_isCurrentProject(projectEpoch) ||
          !_runtimeByAccount.containsKey(account.id)) {
        return;
      }

      setState(() {
        runtime.action = null;
        runtime.status = WhatsAppSessionStatus.initializing;
        runtime.phoneNumber = null;
        runtime.qrValue = null;
      });
      _restartPolling();
      unawaited(_fetchQr(account.id));
    } on DioException catch (error) {
      if (CancelToken.isCancel(error) || !_isCurrentProject(projectEpoch)) {
        return;
      }
      setState(() {
        runtime.action = null;
        runtime.error = userFacingError(error);
      });
    }
  }

  Future<void> _confirmDisconnect(WhatsAppAccount account) async {
    final projectEpoch = _projectEpoch;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('فصل حساب «${account.name}»؟'),
        content: const Text(
          'سيتوقف الإرسال والاستقبال من هذا الرقم مع الاحتفاظ '
          'بالمحادثات السابقة. ستحتاج إلى مسح كود جديد لإعادة الربط.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('إلغاء'),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.error,
              foregroundColor: AppColors.background,
            ),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('تأكيد الفصل'),
          ),
        ],
      ),
    );
    if (confirmed == true &&
        _isCurrentProject(projectEpoch) &&
        _runtimeByAccount.containsKey(account.id)) {
      await _disconnectSession(account);
    }
  }

  Future<void> _disconnectSession(WhatsAppAccount account) async {
    final runtime = _runtimeByAccount[account.id];
    if (runtime == null || runtime.action != null) return;
    final projectEpoch = _projectEpoch;
    _nextGeneration(_statusGenerationByAccount, account.id);
    _nextGeneration(_qrGenerationByAccount, account.id);
    setState(() {
      runtime.action = _AccountAction.disconnect;
      runtime.error = null;
      _notice = null;
    });

    try {
      await widget.repository.disconnectSession(
        projectId: widget.projectId,
        whatsappAccountId: account.id,
        cancelToken: _projectCancelToken,
      );
      if (!_isCurrentProject(projectEpoch) ||
          !_runtimeByAccount.containsKey(account.id)) {
        return;
      }

      setState(() {
        runtime.action = null;
        runtime.status = WhatsAppSessionStatus.disconnected;
        runtime.phoneNumber = null;
        runtime.qrValue = null;
        runtime.qrError = null;
        runtime.qrBusy = false;
        _notice =
            'تم فصل حساب «${account.name}» مع الاحتفاظ بالمحادثات السابقة.';
      });
      _restartPolling();
    } on DioException catch (error) {
      if (CancelToken.isCancel(error) || !_isCurrentProject(projectEpoch)) {
        return;
      }
      setState(() {
        runtime.action = null;
        runtime.error = userFacingError(error);
      });
    }
  }

  Future<void> _setDefault(WhatsAppAccount account) async {
    const failureMessage = 'تعذر تغيير الحساب الافتراضي.';
    final runtime = _runtimeByAccount[account.id];
    if (runtime == null ||
        runtime.action != null ||
        account.isDefault ||
        _defaultMutationBusy) {
      return;
    }
    final projectEpoch = _projectEpoch;
    setState(() {
      _defaultMutationBusy = true;
      runtime.action = _AccountAction.setDefault;
      runtime.error = null;
      _notice = null;
    });

    try {
      final updated = await widget.repository.setDefaultAccount(
        projectId: widget.projectId,
        account: account,
        cancelToken: _projectCancelToken,
      );
      if (!_isCurrentProject(projectEpoch) ||
          !_runtimeByAccount.containsKey(account.id)) {
        return;
      }

      setState(() {
        _accounts = _accounts
            .map(
              (candidate) => candidate.id == updated.id
                  ? updated
                  : candidate.copyWith(isDefault: false),
            )
            .toList(growable: false);
        runtime.action = null;
        _defaultMutationBusy = false;
        _notice = 'أصبح حساب «${account.name}» هو الحساب الافتراضي.';
      });
    } on DioException catch (error) {
      if (CancelToken.isCancel(error) || !_isCurrentProject(projectEpoch)) {
        return;
      }
      setState(() {
        runtime.action = null;
        runtime.error = userFacingError(error);
        _defaultMutationBusy = false;
      });
    } on FormatException {
      if (!_isCurrentProject(projectEpoch)) return;
      setState(() {
        runtime.action = null;
        runtime.error = failureMessage;
        _defaultMutationBusy = false;
      });
    }
  }

  void _restartPolling() {
    _pollTimer?.cancel();
    if (!_appIsActive ||
        !_accounts.any(
          (account) => _isPending(_runtimeByAccount[account.id]?.status),
        )) {
      return;
    }

    _pollTimer = Timer.periodic(const Duration(seconds: 5), (_) {
      for (final account in _accounts) {
        final runtime = _runtimeByAccount[account.id];
        if (_isPending(runtime?.status) &&
            runtime?.action == null &&
            runtime?.statusBusy == false) {
          unawaited(_fetchStatus(account.id));
        }
      }
    });
  }

  bool _isCurrentList(int projectEpoch, int listGeneration) {
    return _isCurrentProject(projectEpoch) && _listGeneration == listGeneration;
  }

  bool _isCurrentProject(int projectEpoch) {
    return mounted && _projectEpoch == projectEpoch;
  }

  bool _isCurrentAccountRequest(
    int projectEpoch,
    String accountId,
    int generation,
    int? currentGeneration,
  ) {
    return _isCurrentProject(projectEpoch) &&
        _runtimeByAccount.containsKey(accountId) &&
        currentGeneration == generation;
  }

  static int _nextGeneration(
    Map<String, int> generationByAccount,
    String accountId,
  ) {
    final next = (generationByAccount[accountId] ?? 0) + 1;
    generationByAccount[accountId] = next;
    return next;
  }

  static bool _isPending(WhatsAppSessionStatus? status) {
    return status == WhatsAppSessionStatus.initializing ||
        status == WhatsAppSessionStatus.reconnecting;
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _buildHeader(),
          const SizedBox(height: 16),
          if (_loadError != null) ...[
            _InlineMessage(
              message: _loadError!,
              color: AppColors.error,
              icon: Icons.error_outline,
              liveRegion: true,
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: _loading ? null : _loadAccounts,
              icon: const Icon(Icons.refresh),
              label: const Text('إعادة المحاولة'),
            ),
          ],
          if (_notice != null) ...[
            _InlineMessage(
              message: _notice!,
              color: AppColors.success,
              icon: Icons.check_circle_outline,
              liveRegion: true,
            ),
            const SizedBox(height: 12),
          ],
          if (_loading)
            Semantics(
              label: 'جاري تحميل حسابات واتساب',
              child: const LinearProgressIndicator(minHeight: 3),
            )
          else if (_accounts.isEmpty && _loadError == null)
            _buildEmptyState()
          else
            _buildAccountList(),
          const SizedBox(height: 20),
          _buildAddAccount(),
        ],
      ),
    );
  }

  Widget _buildHeader() {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const ExcludeSemantics(
          child: Icon(Icons.phone_android, color: AppColors.primary),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                header: true,
                child: Text(
                  'حسابات واتساب',
                  style: AppTypography.title.copyWith(
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ),
              const SizedBox(height: 4),
              Text(
                'اربط أكثر من رقم وتابع كل حساب بشكل مستقل.',
                style: AppTypography.bodyMuted,
              ),
            ],
          ),
        ),
        if (!_loading)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
            decoration: BoxDecoration(
              color: AppColors.surfaceRaised,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: AppColors.border),
            ),
            child: Text(
              '${_accounts.length} حساب',
              style: AppTypography.label.copyWith(color: AppColors.text),
            ),
          ),
      ],
    );
  }

  Widget _buildEmptyState() {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 20),
      decoration: BoxDecoration(
        color: AppColors.background,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        children: [
          const ExcludeSemantics(
            child: Icon(Icons.add_to_home_screen, color: AppColors.textMuted),
          ),
          const SizedBox(height: 8),
          Text(
            'لا توجد حسابات بعد. أضف أول حساب ثم اربط رقمه بكود QR.',
            style: AppTypography.bodyMuted,
            textAlign: TextAlign.center,
          ),
        ],
      ),
    );
  }

  Widget _buildAccountList() {
    return Container(
      decoration: BoxDecoration(
        color: AppColors.background,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        children: [
          for (var index = 0; index < _accounts.length; index++) ...[
            if (index > 0) const Divider(height: 1),
            _buildAccountRow(_accounts[index]),
          ],
        ],
      ),
    );
  }

  Widget _buildAccountRow(WhatsAppAccount account) {
    final runtime = _runtimeByAccount[account.id]!;
    final statusLabel = _statusLabel(runtime.status, runtime.statusBusy);
    final actionBusy = runtime.action != null;
    final canDisconnect =
        _isPending(runtime.status) ||
        runtime.status == WhatsAppSessionStatus.connected;

    return Semantics(
      key: ValueKey('whatsapp-account-${account.id}'),
      container: true,
      explicitChildNodes: true,
      label: 'حساب ${account.name}',
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Wrap(
                    spacing: 8,
                    runSpacing: 6,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    children: [
                      Text(
                        account.name,
                        style: AppTypography.title.copyWith(
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      if (account.isDefault)
                        Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 8,
                            vertical: 3,
                          ),
                          decoration: BoxDecoration(
                            color: AppColors.primaryContainer,
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: Text(
                            'افتراضي',
                            style: AppTypography.label.copyWith(
                              color: AppColors.primary,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
                if (runtime.statusBusy)
                  const Padding(
                    padding: EdgeInsetsDirectional.only(start: 8, top: 3),
                    child: SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 9),
            Semantics(
              liveRegion: true,
              label: 'حالة حساب ${account.name}: $statusLabel',
              child: Wrap(
                spacing: 8,
                runSpacing: 6,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  ExcludeSemantics(
                    child: Container(
                      width: 9,
                      height: 9,
                      decoration: BoxDecoration(
                        color: _statusColor(runtime.status),
                        shape: BoxShape.circle,
                      ),
                    ),
                  ),
                  Text(statusLabel, style: AppTypography.body),
                  if (runtime.phoneNumber != null)
                    Directionality(
                      textDirection: TextDirection.ltr,
                      child: Text(
                        _formatPhone(runtime.phoneNumber!),
                        style: AppTypography.mono.copyWith(
                          color: AppColors.text,
                        ),
                      ),
                    ),
                ],
              ),
            ),
            if (runtime.error != null) ...[
              const SizedBox(height: 10),
              _InlineMessage(
                message: runtime.error!,
                color: AppColors.error,
                icon: Icons.error_outline,
                liveRegion: true,
              ),
            ],
            if (runtime.status == WhatsAppSessionStatus.initializing) ...[
              const SizedBox(height: 14),
              _buildQrRegion(account, runtime),
            ],
            const SizedBox(height: 14),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                OutlinedButton.icon(
                  onPressed: actionBusy
                      ? null
                      : () => unawaited(_fetchStatus(account.id)),
                  icon: const Icon(Icons.refresh, size: 18),
                  label: const Text('تحديث الحالة'),
                ),
                if (!account.isDefault)
                  OutlinedButton.icon(
                    onPressed: actionBusy || _defaultMutationBusy
                        ? null
                        : () => unawaited(_setDefault(account)),
                    icon: runtime.action == _AccountAction.setDefault
                        ? const SizedBox.square(
                            dimension: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.star_outline, size: 18),
                    label: Text(
                      runtime.action == _AccountAction.setDefault
                          ? 'جاري التعيين'
                          : 'تعيين كافتراضي',
                    ),
                  ),
                if (runtime.status == WhatsAppSessionStatus.disconnected)
                  ElevatedButton.icon(
                    onPressed: actionBusy
                        ? null
                        : () => unawaited(_startSession(account)),
                    icon: runtime.action == _AccountAction.start
                        ? const SizedBox.square(
                            dimension: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.qr_code_2, size: 18),
                    label: Text(
                      runtime.action == _AccountAction.start
                          ? 'جاري التجهيز'
                          : 'ربط الرقم',
                    ),
                  )
                else if (canDisconnect)
                  OutlinedButton.icon(
                    style: OutlinedButton.styleFrom(
                      foregroundColor: AppColors.error,
                      side: const BorderSide(color: AppColors.error),
                    ),
                    onPressed: actionBusy
                        ? null
                        : () => unawaited(_confirmDisconnect(account)),
                    icon: runtime.action == _AccountAction.disconnect
                        ? const SizedBox.square(
                            dimension: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.link_off, size: 18),
                    label: Text(
                      runtime.action == _AccountAction.disconnect
                          ? 'جاري الفصل'
                          : 'فصل',
                    ),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildQrRegion(WhatsAppAccount account, _AccountRuntime runtime) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.surfaceRaised,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        children: [
          if (runtime.qrValue != null)
            Semantics(
              label: 'كود ربط حساب ${account.name}',
              image: true,
              child: ExcludeSemantics(
                child: Container(
                  width: 220,
                  height: 220,
                  padding: const EdgeInsets.all(8),
                  color: const Color(0xFFF1F5F9),
                  child: QrImageView(
                    data: runtime.qrValue!,
                    version: QrVersions.auto,
                    gapless: true,
                    backgroundColor: const Color(0xFFF1F5F9),
                    errorStateBuilder: (context, error) => Center(
                      child: Text(
                        'تعذر عرض كود الربط. حدّث الكود وحاول مرة أخرى.',
                        style: AppTypography.body.copyWith(
                          color: AppColors.error,
                        ),
                        textAlign: TextAlign.center,
                      ),
                    ),
                    eyeStyle: const QrEyeStyle(
                      eyeShape: QrEyeShape.square,
                      color: Color(0xFF0A0E17),
                    ),
                    dataModuleStyle: const QrDataModuleStyle(
                      dataModuleShape: QrDataModuleShape.square,
                      color: Color(0xFF0A0E17),
                    ),
                  ),
                ),
              ),
            )
          else
            Semantics(
              liveRegion: true,
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 18),
                child: runtime.qrBusy
                    ? const Column(
                        children: [
                          CircularProgressIndicator(),
                          SizedBox(height: 10),
                          Text('جاري تجهيز كود الربط'),
                        ],
                      )
                    : Text(
                        runtime.qrError ?? 'كود الربط لم يجهز بعد.',
                        style: AppTypography.bodyMuted,
                        textAlign: TextAlign.center,
                      ),
              ),
            ),
          const SizedBox(height: 12),
          Text(
            'من واتساب على الموبايل افتح «الأجهزة المرتبطة»، ثم امسح الكود.',
            style: AppTypography.bodyMuted,
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 10),
          OutlinedButton.icon(
            onPressed: runtime.qrBusy || runtime.action != null
                ? null
                : () => unawaited(_fetchQr(account.id)),
            icon: const Icon(Icons.refresh, size: 18),
            label: const Text('تحديث الكود'),
          ),
        ],
      ),
    );
  }

  Widget _buildAddAccount() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'إضافة حساب واتساب',
          style: AppTypography.label.copyWith(
            color: AppColors.text,
            fontWeight: FontWeight.bold,
          ),
        ),
        const SizedBox(height: 8),
        TextField(
          key: const ValueKey('new-whatsapp-account-name'),
          controller: _newAccountNameController,
          enabled: !_adding,
          maxLength: 100,
          textInputAction: TextInputAction.done,
          textAlign: TextAlign.right,
          decoration: InputDecoration(
            labelText: 'اسم الحساب الجديد',
            hintText: 'مثال: فرع الجيزة',
            errorText: _addError,
            counterText: '',
          ),
          onChanged: (_) {
            if (_addError != null) setState(() => _addError = null);
          },
          onSubmitted: (_) => unawaited(_addAccount()),
        ),
        const SizedBox(height: 10),
        ElevatedButton.icon(
          key: const ValueKey('add-whatsapp-account'),
          onPressed: _adding ? null : () => unawaited(_addAccount()),
          icon: _adding
              ? const SizedBox.square(
                  dimension: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Icon(Icons.add, size: 20),
          label: Text(_adding ? 'جاري الإضافة' : 'إضافة حساب'),
        ),
      ],
    );
  }

  static String _statusLabel(WhatsAppSessionStatus? status, bool statusBusy) {
    if (status == null && statusBusy) return 'جاري تحديث الحالة';
    return switch (status) {
      WhatsAppSessionStatus.connected => 'متصل',
      WhatsAppSessionStatus.initializing => 'جاري التجهيز',
      WhatsAppSessionStatus.reconnecting => 'جاري استعادة الاتصال',
      WhatsAppSessionStatus.disconnected => 'غير متصل',
      null => 'الحالة غير متاحة',
    };
  }

  static Color _statusColor(WhatsAppSessionStatus? status) {
    return switch (status) {
      WhatsAppSessionStatus.connected => AppColors.success,
      WhatsAppSessionStatus.initializing ||
      WhatsAppSessionStatus.reconnecting => AppColors.warning,
      WhatsAppSessionStatus.disconnected => AppColors.error,
      null => AppColors.textMuted,
    };
  }

  static String _formatPhone(String phoneNumber) {
    return phoneNumber.startsWith('+') ? phoneNumber : '+$phoneNumber';
  }
}

enum _AccountAction { start, disconnect, setDefault }

class _AccountRuntime {
  WhatsAppSessionStatus? status;
  String? phoneNumber;
  String? error;
  String? qrValue;
  String? qrError;
  bool statusBusy = false;
  bool qrBusy = false;
  _AccountAction? action;
}

class _InlineMessage extends StatelessWidget {
  final String message;
  final Color color;
  final IconData icon;
  final bool liveRegion;

  const _InlineMessage({
    required this.message,
    required this.color,
    required this.icon,
    required this.liveRegion,
  });

  @override
  Widget build(BuildContext context) {
    return Semantics(
      liveRegion: liveRegion,
      child: Container(
        padding: const EdgeInsets.all(10),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.1),
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: color.withValues(alpha: 0.65)),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            ExcludeSemantics(child: Icon(icon, color: color, size: 19)),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                message,
                style: AppTypography.body.copyWith(color: color),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
