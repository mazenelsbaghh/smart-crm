import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../../dashboard/bloc/dashboard_bloc.dart';
import '../../../core/services/api_client.dart';

class SettingsScreen extends StatefulWidget {
  const SettingsScreen({Key? key}) : super(key: key);

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
  String _selectedGeminiModel = 'gemini-3.5-flash';
  bool _saving = false;
  bool _testingNotification = false;

  final List<String> _geminiModels = [
    'gemini-1.5-flash',
    'gemini-1.5-pro',
    'gemini-2.0-flash',
    'gemini-2.0-pro',
    'gemini-2.5-flash',
    'gemini-3.5-flash',
    'gemini-3.5-pro',
  ];

  @override
  void initState() {
    super.initState();
    _loadSettings();
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
      _geminiApiKeyController.text = project.settings.geminiApiKey;
      _aiToneController.text = project.settings.aiTonePreference;
      _aiTargetAudienceController.text = project.settings.aiTargetAudience;
      _replyDelayController.text = project.settings.replyDelay.toString();
      _maxDailyMessagesController.text = project.settings.maxDailyMessages.toString();
      _aiAutoReplyEnabled = project.settings.aiAutoReplyEnabled;
      _isGroupAppointmentsEnabled = project.settings.isGroupAppointmentsEnabled;
      
      // Ensure current model is in list, or add it
      final model = project.settings.geminiModel;
      if (_geminiModels.contains(model)) {
        _selectedGeminiModel = model;
      } else {
        _selectedGeminiModel = _geminiModels.first;
      }
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
        
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('تم إرسال إشعار تجريبي بنجاح! 🧪'),
            backgroundColor: AppColors.success,
          ),
        );
      } catch (e) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('فشل إرسال الإشعار التجريبي: $e'),
            backgroundColor: AppColors.error,
          ),
        );
      } finally {
        setState(() {
          _testingNotification = false;
        });
      }
    }
  }

  Future<void> _saveSettings() async {
    if (!_formKey.currentState!.validate()) return;
    
    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      setState(() {
        _saving = true;
      });

      final settings = {
        'projectName': _nameController.text.trim(),
        'aiAutoReplyEnabled': _aiAutoReplyEnabled,
        'timezone': _timezoneController.text.trim(),
        'geminiApiKey': _geminiApiKeyController.text.trim(),
        'geminiModel': _selectedGeminiModel,
        'aiTonePreference': _aiToneController.text.trim(),
        'aiTargetAudience': _aiTargetAudienceController.text.trim(),
        'replyDelay': int.tryParse(_replyDelayController.text) ?? 3,
        'maxDailyMessages': int.tryParse(_maxDailyMessagesController.text) ?? 500,
        'isGroupAppointmentsEnabled': _isGroupAppointmentsEnabled,
      };

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
    return BlocListener<DashboardBloc, DashboardState>(
      listener: (context, state) {
        if (state.settingsUpdateSuccess) {
          setState(() {
            _saving = false;
          });
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text('تم حفظ الإعدادات بنجاح! ✨'),
              backgroundColor: AppColors.success,
            ),
          );
          // Reload active status & sync active project settings
          context.read<AuthBloc>().add(AuthCheckStatus());
        } else if (state.error != null) {
          setState(() {
            _saving = false;
          });
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('فشل الحفظ: ${state.error}'),
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
        body: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
          child: Form(
            key: _formKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _buildTextField('اسم المشروع', _nameController),
                const SizedBox(height: 16),
                _buildTextField('المنطقة الزمنية', _timezoneController),
                const SizedBox(height: 16),
                _buildPasswordField('مفتاح Gemini API Key', _geminiApiKeyController),
                const SizedBox(height: 16),
                _buildDropdownField('طراز الذكاء الاصطناعي (Gemini Model)', _selectedGeminiModel, _geminiModels, (val) {
                  if (val != null) {
                    setState(() {
                      _selectedGeminiModel = val;
                    });
                  }
                }),
                const SizedBox(height: 16),
                _buildTextField('أسلوب ونبرة ردود الذكاء الاصطناعي', _aiToneController),
                const SizedBox(height: 16),
                _buildTextField('الجمهور المستهدف (الفئة العمرية/الاهتمام)', _aiTargetAudienceController),
                const SizedBox(height: 16),
                _buildTextField('تأخير الرد (بالثواني)', _replyDelayController, keyboardType: TextInputType.number),
                const SizedBox(height: 16),
                _buildTextField('الحد الأقصى للرسائل اليومية للذكاء الاصطناعي', _maxDailyMessagesController, keyboardType: TextInputType.number),
                const SizedBox(height: 24),
                
                // Toggle AI Auto Reply
                _buildSwitchCard(
                  title: 'تفعيل الرد التلقائي بالذكاء الاصطناعي',
                  subtitle: 'تشغيل مساعد Gemini للاستجابة السريعة على المحادثات',
                  value: _aiAutoReplyEnabled,
                  onChanged: (val) {
                    setState(() {
                      _aiAutoReplyEnabled = val;
                    });
                  },
                ),
                const SizedBox(height: 16),
                
                // Toggle Group Appointments
                _buildSwitchCard(
                  title: 'تفعيل حجز المواعيد الجماعية',
                  subtitle: 'السماح للعملاء بحجز مقاعد في المجموعات والورش المفتوحة',
                  value: _isGroupAppointmentsEnabled,
                  onChanged: (val) {
                    setState(() {
                      _isGroupAppointmentsEnabled = val;
                    });
                  },
                ),
                const SizedBox(height: 24),
                
                // Test Push Notifications Card
                _buildTestNotificationsCard(),
                
                const SizedBox(height: 40),
                ElevatedButton(
                  onPressed: _saving ? null : _saveSettings,
                  style: ElevatedButton.styleFrom(
                    backgroundColor: AppColors.primary,
                    foregroundColor: AppColors.surface,
                    padding: const EdgeInsets.symmetric(vertical: 16),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(8),
                    ),
                  ),
                  child: _saving
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(
                            strokeWidth: 2,
                            valueColor: AlwaysStoppedAnimation(AppColors.surface),
                          ),
                        )
                      : Text(
                          'حفظ الإعدادات',
                          style: AppTypography.title.copyWith(
                            color: AppColors.surface,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                ),
              ],
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
          style: AppTypography.label.copyWith(color: AppColors.text, fontWeight: FontWeight.bold),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 8),
        TextFormField(
          controller: controller,
          keyboardType: keyboardType,
          style: AppTypography.body,
          textAlign: TextAlign.right,
          decoration: InputDecoration(
            filled: true,
            fillColor: AppColors.surface,
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
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
          style: AppTypography.label.copyWith(color: AppColors.text, fontWeight: FontWeight.bold),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 8),
        TextFormField(
          controller: controller,
          obscureText: _obscureApiKey,
          style: AppTypography.body,
          textAlign: TextAlign.right,
          decoration: InputDecoration(
            filled: true,
            fillColor: AppColors.surface,
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            prefixIcon: IconButton(
              icon: Icon(_obscureApiKey ? Icons.visibility_off : Icons.visibility, color: AppColors.textMuted),
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
          style: AppTypography.label.copyWith(color: AppColors.text, fontWeight: FontWeight.bold),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 8),
        DropdownButtonFormField<String>(
          value: currentValue,
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
            filled: true,
            fillColor: AppColors.surface,
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
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
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Switch(
            value: value,
            onChanged: onChanged,
            activeColor: AppColors.primary,
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(
                  title,
                  style: AppTypography.title.copyWith(fontWeight: FontWeight.bold, fontSize: 14),
                  textAlign: TextAlign.right,
                ),
                const SizedBox(height: 4),
                Text(
                  subtitle,
                  style: AppTypography.bodyMuted.copyWith(fontSize: 11),
                  textAlign: TextAlign.right,
                ),
              ],
            ),
          ),
        ],
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
            style: AppTypography.title.copyWith(fontWeight: FontWeight.bold, fontSize: 14),
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 4),
          Text(
            'أرسل تنبيهاً تجريبياً لهاتفك للتأكد من عمل نظام الإشعارات في الخلفية',
            style: AppTypography.bodyMuted.copyWith(fontSize: 11),
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 12),
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
            label: const Text('إرسال إشعار تجريبي 🧪'),
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.primary.withOpacity(0.1),
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
