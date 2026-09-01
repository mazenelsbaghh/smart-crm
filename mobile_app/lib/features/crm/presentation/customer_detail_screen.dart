import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/user_facing_error.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../bloc/crm_bloc.dart';
import '../data/repositories/crm_repository.dart';

class CustomerDetailScreen extends StatefulWidget {
  final String customerId;

  const CustomerDetailScreen({super.key, required this.customerId});

  @override
  State<CustomerDetailScreen> createState() => _CustomerDetailScreenState();
}

class _CustomerDetailScreenState extends State<CustomerDetailScreen> {
  final _nameController = TextEditingController();
  final _cityController = TextEditingController();
  final _budgetController = TextEditingController();
  final _notesController = TextEditingController();
  final _tagController = TextEditingController();

  int _leadScore = 0;
  bool _isBlacklisted = false;
  List<String> _tags = [];
  bool _loading = true;
  bool _saving = false;
  String? _loadError;
  late int _initialSaveRevision;
  Map<String, Object?> _initialValues = const {};
  bool _allowPop = false;

  @override
  void initState() {
    super.initState();
    _initialSaveRevision = context.read<CrmBloc>().state.customerSaveRevision;
    _loadCustomer();
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

  Future<void> _loadCustomer() async {
    setState(() {
      _loading = true;
      _loadError = null;
    });
    try {
      final repository = context.read<CrmRepository>();
      final customer = await repository.getCustomer(widget.customerId);
      if (!mounted) return;
      setState(() {
        _nameController.text = customer.name;
        _cityController.text = customer.city;
        _budgetController.text = customer.budget != null
            ? customer.budget.toString()
            : '';
        _notesController.text = customer.notes;
        _leadScore = customer.leadScore;
        _isBlacklisted = customer.isBlacklisted;
        _tags = List<String>.from(customer.tags);
        _captureInitialValues();
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _loadError = userFacingError(error);
      });
    }
  }

  Future<void> _saveCustomer() async {
    if (_saving) return;
    final crmBloc = context.read<CrmBloc>();
    if (crmBloc.state.customerSaving) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('يوجد حفظ آخر قيد التنفيذ. حاول بعد لحظات.'),
        ),
      );
      return;
    }
    if (_nameController.text.trim().isEmpty) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('اسم العميل مطلوب.')));
      return;
    }
    final budget = double.tryParse(_budgetController.text.trim());
    if (_budgetController.text.trim().isNotEmpty &&
        (budget == null || budget < 0)) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('أدخل ميزانية صحيحة.')));
      return;
    }
    setState(() => _saving = true);
    _initialSaveRevision = crmBloc.state.customerSaveRevision;
    final data = {
      'name': _nameController.text.trim(),
      'city': _cityController.text.trim(),
      'budget': _budgetController.text.isNotEmpty ? budget : null,
      'leadScore': _leadScore,
      'notes': _notesController.text.trim(),
      'tags': _tags,
      'isBlacklisted': _isBlacklisted,
    };

    crmBloc.add(
      CrmCustomerUpdateRequested(customerId: widget.customerId, data: data),
    );
  }

  Map<String, Object?> get _currentValues => {
    'name': _nameController.text,
    'city': _cityController.text,
    'budget': _budgetController.text,
    'notes': _notesController.text,
    'blacklisted': _isBlacklisted,
    'tags': _tags.join('\u0000'),
  };

  bool get _isDirty => _currentValues.entries.any(
    (entry) => _initialValues[entry.key] != entry.value,
  );

  void _captureInitialValues() {
    _initialValues = Map.unmodifiable(_currentValues);
  }

  Future<void> _handleBlockedPop() async {
    if (_saving) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('انتظر حتى يكتمل حفظ بيانات العميل.')),
      );
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
      setState(() => _allowPop = true);
      Navigator.of(context).pop();
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

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: AppLoadingSkeleton(rows: 7));
    }

    if (_loadError != null) {
      return Scaffold(
        appBar: AppBar(title: const Text('تعديل ملف العميل')),
        body: AppStateView(
          icon: Icons.cloud_off_outlined,
          title: 'تعذر تحميل بيانات العميل',
          message: _loadError!,
          actionLabel: 'إعادة المحاولة',
          onAction: _loadCustomer,
        ),
      );
    }

    return PopScope<void>(
      canPop: _allowPop || (!_isDirty && !_saving),
      onPopInvokedWithResult: (didPop, result) {
        if (!didPop) _handleBlockedPop();
      },
      child: BlocListener<CrmBloc, CrmState>(
        listenWhen: (previous, current) =>
            previous.customerSaveRevision != current.customerSaveRevision ||
            previous.customerSaveError != current.customerSaveError,
        listener: (context, state) {
          if (_saving && state.customerSaveRevision > _initialSaveRevision) {
            _initialSaveRevision = state.customerSaveRevision;
            setState(() {
              _saving = false;
              _allowPop = true;
            });
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(
                content: Text('تم حفظ بيانات العميل.'),
                backgroundColor: AppColors.success,
              ),
            );
            Navigator.of(context).pop();
          } else if (_saving && state.customerSaveError != null) {
            setState(() => _saving = false);
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(
                content: Text(state.customerSaveError!),
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
            leading: IconButton(
              tooltip: 'رجوع',
              icon: const Icon(Icons.arrow_forward, color: AppColors.text),
              onPressed: () => Navigator.of(context).maybePop(),
            ),
            title: Text(
              'تعديل ملف العميل',
              style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
            ),
            centerTitle: true,
          ),
          body: AbsorbPointer(
            absorbing: _saving,
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(20),
              child: Center(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 720),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
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
                      _buildTextField('ملاحظات', _notesController, maxLines: 3),
                      const SizedBox(height: 20),
                      SwitchListTile.adaptive(
                        contentPadding: EdgeInsets.zero,
                        value: _isBlacklisted,
                        onChanged: (value) =>
                            setState(() => _isBlacklisted = value),
                        activeThumbColor: AppColors.error,
                        title: const Text(
                          'حظر الرد التلقائي بالذكاء الاصطناعي',
                        ),
                        subtitle: const Text('توقيف ردود المساعد لهذا العميل.'),
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
                        children: _tags
                            .map((tag) => _buildTagChip(tag))
                            .toList(),
                      ),
                      const SizedBox(height: 40),
                      ElevatedButton(
                        onPressed: _saving ? null : _saveCustomer,
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
          onChanged: (value) => setState(() {}),
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
