import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/user_facing_error.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../bloc/inbox_bloc.dart';
import '../data/repositories/chat_repository.dart';

class ConversationDetailSheet extends StatefulWidget {
  const ConversationDetailSheet({super.key});

  @override
  State<ConversationDetailSheet> createState() =>
      _ConversationDetailSheetState();
}

class _ConversationDetailSheetState extends State<ConversationDetailSheet> {
  final _nameController = TextEditingController();
  final _cityController = TextEditingController();
  final _budgetController = TextEditingController();
  final _notesController = TextEditingController();
  final _tagController = TextEditingController();

  int _leadScore = 0;
  bool _isBlacklisted = false;
  List<String> _tags = [];
  bool _saving = false;
  bool _loading = true;
  String? _loadError;
  Map<String, Object?> _initialValues = const {};
  bool _allowPop = false;

  @override
  void initState() {
    super.initState();
    _loadCustomerDetails();
  }

  @override
  void dispose() {
    _nameController.dispose();
    _cityController.dispose();
    _budgetController.dispose();
    _notesController.dispose();
    _tagController.dispose();
    super.dispose();
  }

  Future<void> _loadCustomerDetails() async {
    final state = context.read<InboxBloc>().state;
    final conv = state.activeConv;
    if (conv == null) {
      setState(() {
        _loading = false;
        _loadError = 'لم يتم اختيار محادثة.';
      });
      return;
    }

    setState(() {
      _loading = true;
      _loadError = null;
      _nameController.text = conv.customer.name;
    });

    try {
      final repository = context.read<ChatRepository>();
      final response = await repository.apiClient.dio.get(
        '/api/customers/${conv.customer.id}',
      );
      final customer = response.data;
      if (customer is! Map) {
        throw const FormatException('Invalid customer response');
      }
      if (!mounted) return;
      final rawLeadScore = customer['leadScore'];
      final rawTags = customer['tags'];
      setState(() {
        _nameController.text =
            customer['name']?.toString() ?? conv.customer.name;
        _cityController.text = customer['city']?.toString() ?? '';
        _budgetController.text = customer['budget'] != null
            ? customer['budget'].toString()
            : '';
        _notesController.text = customer['notes']?.toString() ?? '';
        _leadScore = rawLeadScore is num
            ? rawLeadScore.round().clamp(0, 100)
            : 0;
        _isBlacklisted = customer['isBlacklisted'] == true;
        _tags = rawTags is List
            ? rawTags.map((tag) => tag.toString()).toList()
            : [];
        _captureInitialValues();
      });
    } catch (error) {
      if (mounted) setState(() => _loadError = userFacingError(error));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _saveCustomerDetails() async {
    final state = context.read<InboxBloc>().state;
    final conv = state.activeConv;
    if (conv == null) return;

    if (_nameController.text.trim().isEmpty) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('اسم العميل مطلوب.')));
      return;
    }
    final budgetText = _budgetController.text.trim();
    final budget = double.tryParse(budgetText);
    if (budgetText.isNotEmpty && (budget == null || budget < 0)) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('أدخل ميزانية صحيحة.')));
      return;
    }

    setState(() => _saving = true);

    try {
      final repository = context.read<ChatRepository>();
      final customerUpdate = <String, dynamic>{
        'name': _nameController.text.trim(),
        'city': _cityController.text.trim(),
        'budget': budgetText.isNotEmpty ? budget : null,
        'leadScore': _leadScore,
        'notes': _notesController.text.trim(),
        'tags': _tags,
        'isBlacklisted': _isBlacklisted,
      };

      await repository.updateCustomerProfile(conv.customer.id, customerUpdate);

      if (!mounted) return;
      // Notify BLoC
      context.read<InboxBloc>().add(
        InboxCustomerUpdated({
          'id': conv.customer.id,
          'phone': conv.customer.phone,
          'label': conv.customer.label,
          ...customerUpdate,
        }),
      );

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('تم حفظ بيانات العميل.'),
          backgroundColor: AppColors.success,
        ),
      );
      _allowPop = true;
      Navigator.of(context).pop();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(userFacingError(e)),
          backgroundColor: AppColors.error,
        ),
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  void _addTag() {
    final text = _tagController.text.trim();
    if (text.isEmpty || _tags.contains(text)) return;
    setState(() {
      _tags.add(text);
      _tagController.clear();
    });
  }

  Map<String, Object?> get _currentValues => {
    'name': _nameController.text,
    'city': _cityController.text,
    'budget': _budgetController.text,
    'notes': _notesController.text,
    'blacklisted': _isBlacklisted,
    'tags': _tags.join('\u0000'),
  };

  bool get _isDirty =>
      _initialValues.isNotEmpty &&
      _currentValues.entries.any(
        (entry) => _initialValues[entry.key] != entry.value,
      );

  void _captureInitialValues() {
    _initialValues = Map.unmodifiable(_currentValues);
  }

  Future<void> _requestClose() async {
    if (_saving) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('انتظر حتى يكتمل حفظ بيانات العميل.')),
      );
      return;
    }
    if (!_isDirty) {
      _allowPop = true;
      Navigator.of(context).pop();
      return;
    }

    final discard = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('تجاهل التغييرات؟'),
        content: const Text('لديك تغييرات غير محفوظة في بيانات العميل.'),
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
      _allowPop = true;
      Navigator.of(context).pop();
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return SizedBox(
        height: 560,
        child: Stack(
          children: [
            const AppLoadingSkeleton(rows: 6),
            PositionedDirectional(
              top: 8,
              start: 8,
              child: IconButton(
                tooltip: 'إغلاق',
                onPressed: _requestClose,
                icon: const Icon(Icons.close),
              ),
            ),
          ],
        ),
      );
    }
    if (_loadError != null) {
      return SizedBox(
        height: 420,
        child: Stack(
          children: [
            AppStateView(
              icon: Icons.cloud_off_outlined,
              title: 'تعذر تحميل بيانات العميل',
              message: _loadError!,
              actionLabel: 'إعادة المحاولة',
              onAction: _loadCustomerDetails,
            ),
            PositionedDirectional(
              top: 8,
              start: 8,
              child: IconButton(
                tooltip: 'إغلاق',
                onPressed: _requestClose,
                icon: const Icon(Icons.close),
              ),
            ),
          ],
        ),
      );
    }
    return PopScope<void>(
      canPop: _allowPop || (!_isDirty && !_saving),
      onPopInvokedWithResult: (didPop, result) {
        if (!didPop) _requestClose();
      },
      child: Container(
        decoration: const BoxDecoration(
          color: AppColors.background,
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
        padding: EdgeInsetsDirectional.only(
          start: 20,
          end: 20,
          top: 12,
          bottom: MediaQuery.of(context).viewInsets.bottom + 20,
        ),
        child: AbsorbPointer(
          absorbing: _saving,
          child: SingleChildScrollView(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    IconButton(
                      tooltip: 'إغلاق',
                      onPressed: _requestClose,
                      icon: const Icon(Icons.close),
                    ),
                    Expanded(
                      child: Text(
                        'بيانات العميل',
                        style: AppTypography.headline,
                        textAlign: TextAlign.center,
                      ),
                    ),
                    const SizedBox(width: 48),
                  ],
                ),
                const SizedBox(height: 24),
                _buildTextField('الاسم الكامل', _nameController),
                const SizedBox(height: 16),
                _buildTextField('المدينة', _cityController),
                const SizedBox(height: 16),
                _buildTextField(
                  'الميزانية (الجنيه)',
                  _budgetController,
                  keyboardType: TextInputType.number,
                ),
                const SizedBox(height: 16),
                _buildTextField(
                  'ملاحظات العميل',
                  _notesController,
                  maxLines: 3,
                ),
                const SizedBox(height: 20),
                SwitchListTile.adaptive(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('حظر الرد التلقائي بالذكاء الاصطناعي'),
                  subtitle: const Text('توقيف ردود المساعد لهذا العميل.'),
                  value: _isBlacklisted,
                  onChanged: (value) => setState(() => _isBlacklisted = value),
                  activeThumbColor: AppColors.error,
                ),
                const SizedBox(height: 20),
                Text(
                  'الوسوم (Tags)',
                  style: AppTypography.title,
                  textAlign: TextAlign.right,
                ),
                const SizedBox(height: 8),
                _buildTagField(),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  alignment: WrapAlignment.end,
                  children: _tags.map((tag) => _buildTagChip(tag)).toList(),
                ),
                const SizedBox(height: 32),
                ElevatedButton(
                  onPressed: _saving ? null : _saveCustomerDetails,
                  style: ElevatedButton.styleFrom(
                    backgroundColor: AppColors.primary,
                    foregroundColor: AppColors.background,
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
                            valueColor: AlwaysStoppedAnimation(
                              AppColors.background,
                            ),
                          ),
                        )
                      : Text(
                          'حفظ التغييرات',
                          style: AppTypography.title.copyWith(
                            color: AppColors.background,
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
    int maxLines = 1,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          label,
          style: AppTypography.label.copyWith(color: AppColors.text),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 8),
        TextField(
          controller: controller,
          keyboardType: keyboardType,
          maxLines: maxLines,
          style: AppTypography.body,
          textAlign: TextAlign.right,
          onChanged: (_) => setState(() {}),
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

  Widget _buildTagField() {
    return Row(
      children: [
        ElevatedButton(
          onPressed: _addTag,
          style: ElevatedButton.styleFrom(
            backgroundColor: AppColors.surface,
            foregroundColor: AppColors.primary,
            side: const BorderSide(color: AppColors.border),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(8),
            ),
          ),
          child: const Text('إضافة'),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: TextField(
            controller: _tagController,
            style: AppTypography.body,
            textAlign: TextAlign.right,
            decoration: InputDecoration(
              labelText: 'وسم جديد',
              hintText: 'أضف وسم جديد...',
              hintStyle: AppTypography.bodyMuted,
              filled: true,
              fillColor: AppColors.surface,
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 16,
                vertical: 8,
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
            onSubmitted: (_) => _addTag(),
          ),
        ),
      ],
    );
  }

  Widget _buildTagChip(String label) {
    return Chip(
      label: Text(
        label,
        style: AppTypography.label.copyWith(color: AppColors.primary),
      ),
      backgroundColor: AppColors.primary.withValues(alpha: 0.12),
      side: const BorderSide(color: AppColors.primary, width: 0.5),
      deleteIcon: const Icon(Icons.close, size: 14, color: AppColors.primary),
      deleteButtonTooltipMessage: 'حذف الوسم $label',
      onDeleted: () {
        setState(() {
          _tags.remove(label);
        });
      },
    );
  }
}
