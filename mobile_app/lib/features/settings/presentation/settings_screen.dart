import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/api_client.dart';
import '../../../core/services/push_notification_service.dart';
import '../../../core/services/user_facing_error.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../../dashboard/bloc/dashboard_bloc.dart';
import '../data/repositories/whatsapp_accounts_repository.dart';
import 'widgets/whatsapp_accounts_section.dart';

class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key});

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  final _formKey = GlobalKey<FormState>();

  final _nameController = TextEditingController();
  final _timezoneController = TextEditingController();
  final _geminiApiKeyController = TextEditingController();
  final _aiToneController = TextEditingController();
  final _aiTargetAudienceController = TextEditingController();
  final _replyDelayController = TextEditingController();
  final _maxDailyMessagesController = TextEditingController();

  bool _aiAutoReplyEnabled = false;
  bool _isGroupAppointmentsEnabled = false;
  bool _obscureApiKey = true;
  bool _geminiApiKeyConfigured = false;
  String _selectedGeminiModel = 'gemini-3.5-flash';
  String? _legacyGeminiModel;
  bool _saving = false;
  bool _testingNotification = false;
  bool _initialAiAutoReplyEnabled = false;
  bool _initialGroupAppointmentsEnabled = false;
  bool _allowPop = false;
  Map<String, Object?> _initialValues = const {};

  final List<String> _geminiModels = [
    'gemini-flash-latest',
    'gemini-flash-lite-latest',
    'gemini-3.6-flash',
    'gemini-3.5-flash-lite',
    'gemini-2.5-flash-lite',
    'gemini-3.1-flash-lite',
    'gemini-3.5-flash',
  ];

  @override
  void initState() {
    super.initState();
    _loadSettings();
    for (final controller in [
      _nameController,
      _timezoneController,
      _geminiApiKeyController,
      _aiToneController,
      _aiTargetAudienceController,
      _replyDelayController,
      _maxDailyMessagesController,
    ]) {
      controller.addListener(_onFieldChanged);
    }
  }

  @override
  void dispose() {
    _nameController.dispose();
    _timezoneController.dispose();
    _geminiApiKeyController.dispose();
    _aiToneController.dispose();
    _aiTargetAudienceController.dispose();
    _replyDelayController.dispose();
    _maxDailyMessagesController.dispose();
    super.dispose();
  }

  void _loadSettings() {
    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      final project = authState.activeProject;
      _nameController.text = project.name;
      _timezoneController.text = project.settings.timezone;
      _geminiApiKeyController.clear();
      _geminiApiKeyConfigured = project.settings.geminiApiKeyConfigured;
      _aiToneController.text = project.settings.aiTonePreference;
      _aiTargetAudienceController.text = project.settings.aiTargetAudience;
      _replyDelayController.text = project.settings.replyDelay.toString();
      _maxDailyMessagesController.text = project.settings.maxDailyMessages
          .toString();
      _aiAutoReplyEnabled = project.settings.aiAutoReplyEnabled;
      _isGroupAppointmentsEnabled = project.settings.isGroupAppointmentsEnabled;
      _initialAiAutoReplyEnabled = _aiAutoReplyEnabled;
      _initialGroupAppointmentsEnabled = _isGroupAppointmentsEnabled;

      final model = project.settings.geminiModel;
      _legacyGeminiModel = null;
      if (_geminiModels.contains(model)) {
        _selectedGeminiModel = model;
      } else {
        _geminiModels.add(model);
        _selectedGeminiModel = model;
        _legacyGeminiModel = model;
      }
      _captureInitialValues();
    }
  }

  void _onFieldChanged() {
    if (mounted) setState(() {});
  }

  Map<String, Object?> get _currentValues => {
    'name': _nameController.text,
    'timezone': _timezoneController.text,
    'model': _selectedGeminiModel,
    'tone': _aiToneController.text,
    'audience': _aiTargetAudienceController.text,
    'delay': _replyDelayController.text,
    'maxMessages': _maxDailyMessagesController.text,
    'aiEnabled': _aiAutoReplyEnabled,
    'groupEnabled': _isGroupAppointmentsEnabled,
  };

  bool get _isDirty {
    if (_geminiApiKeyController.text.trim().isNotEmpty) return true;
    final current = _currentValues;
    return current.entries.any(
      (entry) => _initialValues[entry.key] != entry.value,
    );
  }

  void _captureInitialValues() {
    _initialValues = Map.unmodifiable(_currentValues);
  }

  Future<void> _handleBlockedPop() async {
    if (_saving) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('انتظر حتى يكتمل حفظ الإعدادات.')),
      );
      return;
    }
    final discard = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('تجاهل التغييرات؟'),
        content: const Text('لديك تغييرات غير محفوظة وستفقدها عند الرجوع.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('متابعة التعديل'),
          ),
          ElevatedButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('تجاهل التغييرات'),
          ),
        ],
      ),
    );
    if (discard == true && mounted) {
      setState(() => _allowPop = true);
      Navigator.of(context).pop();
    }
  }

  Future<void> _triggerTestNotification() async {
    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      setState(() {
        _testingNotification = true;
      });

      try {
        final apiClient = context.read<ApiClient>();
        final projectId = authState.activeProject.id;

        await apiClient.dio.post('/api/projects/$projectId/fcm-tokens/test');

        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('تم إرسال إشعار تجريبي بنجاح.'),
            backgroundColor: AppColors.success,
          ),
        );
      } catch (e) {
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(userFacingError(e)),
            backgroundColor: AppColors.error,
          ),
        );
      } finally {
        if (mounted) setState(() => _testingNotification = false);
      }
    }
  }

  Future<void> _saveSettings() async {
    if (_saving || !_formKey.currentState!.validate()) return;

    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      final hasSensitiveChange =
          _aiAutoReplyEnabled != _initialAiAutoReplyEnabled ||
          _isGroupAppointmentsEnabled != _initialGroupAppointmentsEnabled ||
          _geminiApiKeyController.text.trim().isNotEmpty;
      if (hasSensitiveChange) {
        final changes = <String>[
          if (_aiAutoReplyEnabled != _initialAiAutoReplyEnabled)
            _aiAutoReplyEnabled ? 'تشغيل الرد التلقائي' : 'إيقاف الرد التلقائي',
          if (_isGroupAppointmentsEnabled != _initialGroupAppointmentsEnabled)
            _isGroupAppointmentsEnabled
                ? 'تشغيل المواعيد الجماعية'
                : 'إيقاف المواعيد الجماعية',
          if (_geminiApiKeyController.text.trim().isNotEmpty)
            'استبدال مفتاح Gemini الحالي',
        ];
        final confirmed = await showDialog<bool>(
          context: context,
          builder: (dialogContext) => AlertDialog(
            title: const Text('تأكيد تغيير الإعدادات الحساسة'),
            content: Text(
              'سيتم تطبيق التغييرات التالية:\n• ${changes.join('\n• ')}',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(dialogContext, false),
                child: const Text('رجوع'),
              ),
              ElevatedButton(
                onPressed: () => Navigator.pop(dialogContext, true),
                child: const Text('تأكيد الحفظ'),
              ),
            ],
          ),
        );
        if (confirmed != true || !mounted) return;
      }

      setState(() => _saving = true);

      final settings = <String, dynamic>{
        ...authState.activeProject.settings.toUpdateJson(),
        'projectName': _nameController.text.trim(),
        'aiAutoReplyEnabled': _aiAutoReplyEnabled,
        'timezone': _timezoneController.text.trim(),
        if (_geminiApiKeyController.text.trim().isNotEmpty)
          'geminiApiKey': _geminiApiKeyController.text.trim(),
        'geminiModel': _selectedGeminiModel,
        'aiTonePreference': _aiToneController.text.trim(),
        'aiTargetAudience': _aiTargetAudienceController.text.trim(),
        'replyDelay': int.parse(_replyDelayController.text.trim()),
        'maxDailyMessages': int.parse(_maxDailyMessagesController.text.trim()),
        'isGroupAppointmentsEnabled': _isGroupAppointmentsEnabled,
      };
      if (_selectedGeminiModel == _legacyGeminiModel) {
        settings.remove('geminiModel');
      }

      context.read<DashboardBloc>().add(
        DashboardSettingsUpdateRequested(
          projectId: authState.activeProject.id,
          settings: settings,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final activeProjectId = context.select<AuthBloc, String?>((authBloc) {
      final authState = authBloc.state;
      return authState is AuthAuthenticated ? authState.activeProject.id : null;
    });
    return PopScope<void>(
      canPop: _allowPop || (!_isDirty && !_saving),
      onPopInvokedWithResult: (didPop, result) {
        if (!didPop) _handleBlockedPop();
      },
      child: BlocListener<DashboardBloc, DashboardState>(
        listenWhen: (previous, current) =>
            (!previous.settingsUpdateSuccess &&
                current.settingsUpdateSuccess) ||
            (previous.settingsUpdating &&
                !current.settingsUpdating &&
                current.settingsUpdateError != null),
        listener: (context, state) {
          if (state.settingsUpdateSuccess) {
            final replacedApiKey = _geminiApiKeyController.text
                .trim()
                .isNotEmpty;
            _geminiApiKeyController.clear();
            _captureInitialValues();
            setState(() {
              _saving = false;
              if (replacedApiKey) _geminiApiKeyConfigured = true;
              _initialAiAutoReplyEnabled = _aiAutoReplyEnabled;
              _initialGroupAppointmentsEnabled = _isGroupAppointmentsEnabled;
            });
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(
                content: Text('تم حفظ الإعدادات بنجاح.'),
                backgroundColor: AppColors.success,
              ),
            );
            context.read<AuthBloc>().add(AuthCheckStatus());
          } else if (state.settingsUpdateError != null) {
            setState(() {
              _saving = false;
            });
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(
                content: Text('فشل الحفظ: ${state.settingsUpdateError}'),
                backgroundColor: AppColors.error,
              ),
            );
          }
        },
        child: Scaffold(
          backgroundColor: AppColors.background,
          appBar: AppBar(
            backgroundColor: AppColors.surface,
            elevation: 0,
            title: Text(
              'إعدادات المشروع والمساعد الذكي',
              style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
            ),
            centerTitle: true,
          ),
          bottomNavigationBar: SafeArea(
            top: false,
            child: Container(
              padding: const EdgeInsetsDirectional.fromSTEB(20, 12, 20, 12),
              decoration: const BoxDecoration(
                color: AppColors.surface,
                border: Border(top: BorderSide(color: AppColors.border)),
              ),
              child: ElevatedButton(
                onPressed: _saving || !_isDirty ? null : _saveSettings,
                child: _saving
                    ? const SizedBox.square(
                        dimension: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('حفظ الإعدادات'),
              ),
            ),
          ),
          body: AbsorbPointer(
            absorbing: _saving,
            child: SingleChildScrollView(
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
              child: Center(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 720),
                  child: Form(
                    key: _formKey,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (_isDirty) ...[
                          Semantics(
                            liveRegion: true,
                            child: Container(
                              padding: const EdgeInsets.all(12),
                              decoration: BoxDecoration(
                                color: AppColors.warning.withValues(
                                  alpha: 0.12,
                                ),
                                border: Border.all(color: AppColors.warning),
                                borderRadius: BorderRadius.circular(8),
                              ),
                              child: Text(
                                'لديك تغييرات غير محفوظة.',
                                style: AppTypography.body.copyWith(
                                  color: AppColors.warning,
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 16),
                        ],
                        if (activeProjectId != null) ...[
                          WhatsAppAccountsSection(
                            projectId: activeProjectId,
                            repository: WhatsAppAccountsRepository(
                              apiClient: context.read<ApiClient>(),
                            ),
                          ),
                          const SizedBox(height: 24),
                        ],
                        _buildTextField('اسم المشروع', _nameController),
                        const SizedBox(height: 16),
                        _buildTextField('المنطقة الزمنية', _timezoneController),
                        const SizedBox(height: 16),
                        _buildPasswordField(
                          'مفتاح Gemini جديد (اختياري)',
                          _geminiApiKeyController,
                        ),
                        const SizedBox(height: 16),
                        _buildDropdownField(
                          'نموذج الذكاء الاصطناعي (Gemini)',
                          _selectedGeminiModel,
                          _geminiModels,
                          (val) {
                            if (val != null) {
                              setState(() {
                                _selectedGeminiModel = val;
                              });
                            }
                          },
                        ),
                        if (_selectedGeminiModel == _legacyGeminiModel) ...[
                          const SizedBox(height: 8),
                          Text(
                            'النموذج المحفوظ قديم. سنبقيه كما هو ما لم '
                            'تختر نموذجًا مدعومًا.',
                            style: AppTypography.bodyMuted,
                            textAlign: TextAlign.right,
                          ),
                        ],
                        const SizedBox(height: 16),
                        _buildTextField(
                          'أسلوب ونبرة ردود الذكاء الاصطناعي',
                          _aiToneController,
                        ),
                        const SizedBox(height: 16),
                        _buildTextField(
                          'الجمهور المستهدف (الفئة العمرية/الاهتمام)',
                          _aiTargetAudienceController,
                        ),
                        const SizedBox(height: 16),
                        _buildTextField(
                          'تأخير الرد (بالثواني)',
                          _replyDelayController,
                          keyboardType: TextInputType.number,
                        ),
                        const SizedBox(height: 16),
                        _buildTextField(
                          'الحد الأقصى للرسائل اليومية للذكاء الاصطناعي',
                          _maxDailyMessagesController,
                          keyboardType: TextInputType.number,
                        ),
                        const SizedBox(height: 24),

                        _buildSwitchCard(
                          title: 'تفعيل الرد التلقائي بالذكاء الاصطناعي',
                          subtitle:
                              'تشغيل مساعد Gemini للاستجابة السريعة على المحادثات',
                          value: _aiAutoReplyEnabled,
                          onChanged: (val) {
                            setState(() {
                              _aiAutoReplyEnabled = val;
                            });
                          },
                        ),
                        const SizedBox(height: 16),

                        _buildSwitchCard(
                          title: 'تفعيل حجز المواعيد الجماعية',
                          subtitle:
                              'السماح للعملاء بحجز مقاعد في المجموعات والورش المفتوحة',
                          value: _isGroupAppointmentsEnabled,
                          onChanged: (val) {
                            setState(() {
                              _isGroupAppointmentsEnabled = val;
                            });
                          },
                        ),
                        const SizedBox(height: 24),

                        _buildTestNotificationsCard(),
                        const SizedBox(height: 24),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildTextField(
    String label,
    TextEditingController controller, {
    TextInputType keyboardType = TextInputType.text,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          label,
          style: AppTypography.label.copyWith(
            color: AppColors.text,
            fontWeight: FontWeight.bold,
          ),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 8),
        TextFormField(
          controller: controller,
          keyboardType: keyboardType,
          style: AppTypography.body,
          textAlign: TextAlign.right,
          decoration: InputDecoration(
            labelText: label,
            filled: true,
            fillColor: AppColors.surface,
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 16,
              vertical: 12,
            ),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.border),
            ),
            enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.border),
            ),
            focusedBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.primary),
            ),
          ),
          validator: (value) {
            if (value == null || value.trim().isEmpty) {
              return 'هذا الحقل مطلوب';
            }
            if (label == 'المنطقة الزمنية' &&
                !RegExp(
                  r'^[A-Za-z_]+(?:/[A-Za-z0-9_+\-]+)+$',
                ).hasMatch(value.trim())) {
              return 'استخدم اسم IANA مثل Africa/Cairo';
            }
            if (keyboardType == TextInputType.number) {
              final parsed = int.tryParse(value.trim());
              if (parsed == null || parsed < 0) {
                return 'أدخل رقمًا صحيحًا غير سالب';
              }
              if (label.contains('تأخير') && parsed > 3600) {
                return 'الحد الأقصى ساعة واحدة';
              }
              if (label.contains('الحد الأقصى') && parsed > 1000000) {
                return 'القيمة أكبر من الحد المسموح';
              }
            }
            return null;
          },
        ),
      ],
    );
  }

  Widget _buildPasswordField(String label, TextEditingController controller) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          label,
          style: AppTypography.label.copyWith(
            color: AppColors.text,
            fontWeight: FontWeight.bold,
          ),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 8),
        TextFormField(
          controller: controller,
          obscureText: _obscureApiKey,
          autocorrect: false,
          enableSuggestions: false,
          style: AppTypography.body,
          textAlign: TextAlign.right,
          decoration: InputDecoration(
            labelText: label,
            helperText: _geminiApiKeyConfigured
                ? 'يوجد مفتاح محفوظ. اترك الحقل فارغًا للإبقاء عليه.'
                : 'لا يوجد مفتاح محفوظ حاليًا.',
            filled: true,
            fillColor: AppColors.surface,
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 16,
              vertical: 12,
            ),
            prefixIcon: IconButton(
              tooltip: _obscureApiKey ? 'إظهار المفتاح' : 'إخفاء المفتاح',
              icon: Icon(
                _obscureApiKey ? Icons.visibility_off : Icons.visibility,
                color: AppColors.textMuted,
              ),
              onPressed: () {
                setState(() {
                  _obscureApiKey = !_obscureApiKey;
                });
              },
            ),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.border),
            ),
            enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.border),
            ),
            focusedBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.primary),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildDropdownField(
    String label,
    String currentValue,
    List<String> items,
    ValueChanged<String?> onChanged,
  ) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          label,
          style: AppTypography.label.copyWith(
            color: AppColors.text,
            fontWeight: FontWeight.bold,
          ),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 8),
        DropdownButtonFormField<String>(
          initialValue: currentValue,
          items: items.map((val) {
            return DropdownMenuItem<String>(
              value: val,
              child: Align(
                alignment: Alignment.centerRight,
                child: Text(val, style: AppTypography.body),
              ),
            );
          }).toList(),
          onChanged: onChanged,
          alignment: AlignmentDirectional.centerEnd,
          decoration: InputDecoration(
            labelText: label,
            filled: true,
            fillColor: AppColors.surface,
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 16,
              vertical: 12,
            ),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.border),
            ),
            enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.border),
            ),
            focusedBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(8),
              borderSide: const BorderSide(color: AppColors.primary),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildSwitchCard({
    required String title,
    required String subtitle,
    required bool value,
    required ValueChanged<bool> onChanged,
  }) {
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: SwitchListTile.adaptive(
        value: value,
        onChanged: onChanged,
        activeThumbColor: AppColors.primary,
        title: Text(title, style: AppTypography.title),
        subtitle: Text(subtitle, style: AppTypography.bodyMuted),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      ),
    );
  }

  Widget _buildTestNotificationsCard() {
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
          Text(
            'اختبار الإشعارات الفورية',
            style: AppTypography.title.copyWith(
              fontWeight: FontWeight.bold,
              fontSize: 14,
            ),
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 4),
          Text(
            'أرسل تنبيهاً تجريبياً لهاتفك للتأكد من عمل نظام الإشعارات في الخلفية',
            style: AppTypography.bodyMuted,
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 12),
          ValueListenableBuilder<String>(
            valueListenable: PushNotificationService.statusNotifier,
            builder: (context, status, child) {
              return Semantics(
                liveRegion: true,
                label: 'حالة الإشعار: $status',
                child: Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 8,
                  ),
                  margin: const EdgeInsets.only(bottom: 12),
                  decoration: BoxDecoration(
                    color: AppColors.background,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: AppColors.border),
                  ),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Expanded(
                        child: Text(
                          status,
                          style: AppTypography.body.copyWith(
                            fontSize: 12,
                            color: status.contains('فشل')
                                ? AppColors.error
                                : (status.contains('نشط')
                                      ? AppColors.success
                                      : AppColors.text),
                          ),
                          textAlign: TextAlign.left,
                        ),
                      ),
                      const SizedBox(width: 8),
                      Text(
                        'حالة الإشعار:',
                        style: AppTypography.bodyMuted.copyWith(
                          fontSize: 12,
                          fontWeight: FontWeight.bold,
                        ),
                        textAlign: TextAlign.right,
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
          ElevatedButton.icon(
            onPressed: _testingNotification ? null : _triggerTestNotification,
            icon: _testingNotification
                ? const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      valueColor: AlwaysStoppedAnimation(AppColors.primary),
                    ),
                  )
                : const Icon(Icons.send_to_mobile_rounded, size: 16),
            label: const Text('إرسال إشعار تجريبي'),
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.primary.withValues(alpha: 0.1),
              foregroundColor: AppColors.primary,
              elevation: 0,
              padding: const EdgeInsets.symmetric(vertical: 12),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
