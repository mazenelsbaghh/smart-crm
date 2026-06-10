import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../bloc/inbox_bloc.dart';
import '../data/repositories/chat_repository.dart';

class ConversationDetailSheet extends StatefulWidget {
  const ConversationDetailSheet({Key? key}) : super(key: key);

  @override
  State<ConversationDetailSheet> createState() => _ConversationDetailSheetState();
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
    if (conv == null) return;

    setState(() {
      _nameController.text = conv.customer.name;
    });

    try {
      final repository = context.read<ChatRepository>();
      final response = await repository.apiClient.dio.get('/api/customers/${conv.customer.id}');
      final customer = response.data;
      if (customer != null) {
        setState(() {
          _cityController.text = customer['city'] ?? '';
          _budgetController.text = customer['budget'] != null ? customer['budget'].toString() : '';
          _notesController.text = customer['notes'] ?? '';
          _leadScore = customer['leadScore'] ?? 0;
          _isBlacklisted = customer['isBlacklisted'] ?? false;
          _tags = List<String>.from(customer['tags'] ?? []);
        });
      }
    } catch (_) {}
  }

  Future<void> _saveCustomerDetails() async {
    final state = context.read<InboxBloc>().state;
    final conv = state.activeConv;
    if (conv == null) return;

    setState(() {
      _saving = true;
    });

    try {
      final repository = context.read<ChatRepository>();
      final data = {
        'name': _nameController.text.trim(),
        'city': _cityController.text.trim(),
        'budget': _budgetController.text.isNotEmpty ? double.tryParse(_budgetController.text) : null,
        'leadScore': _leadScore,
        'notes': _notesController.text.trim(),
        'tags': _tags,
        'isBlacklisted': _isBlacklisted,
      };

      await repository.updateCustomerProfile(conv.customer.id, data);
      
      // Notify BLoC
      context.read<InboxBloc>().add(InboxCustomerUpdated({
        'id': conv.customer.id,
        'name': _nameController.text.trim(),
        'phone': conv.customer.phone,
        'label': conv.customer.label,
        'city': _cityController.text.trim(),
        'budget': _budgetController.text.isNotEmpty ? double.tryParse(_budgetController.text) : null,
        'leadScore': _leadScore,
        'notes': _notesController.text.trim(),
        'tags': _tags,
      }));

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('تم حفظ الملف الشخصي للعميل بنجاح! ✨'),
          backgroundColor: AppColors.success,
        ),
      );
      Navigator.of(context).pop();
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('فشل حفظ البيانات: $e'),
          backgroundColor: AppColors.error,
        ),
      );
    } finally {
      setState(() {
        _saving = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: const BoxDecoration(
        color: AppColors.background,
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      padding: EdgeInsets.only(
        left: 20,
        right: 20,
        top: 20,
        bottom: MediaQuery.of(context).viewInsets.bottom + 20,
      ),
      child: SingleChildScrollView(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Center(
              child: Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: AppColors.border,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              'بيانات العميل',
              style: AppTypography.headline,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 24),
            _buildTextField('الاسم الكامل', _nameController),
            const SizedBox(height: 16),
            _buildTextField('المدينة', _cityController),
            const SizedBox(height: 16),
            _buildTextField('الميزانية (الجنيه)', _budgetController, keyboardType: TextInputType.number),
            const SizedBox(height: 16),
            _buildTextField('ملاحظات العميل', _notesController, maxLines: 3),
            const SizedBox(height: 20),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Switch(
                  value: _isBlacklisted,
                  onChanged: (val) {
                    setState(() {
                      _isBlacklisted = val;
                    });
                  },
                  activeColor: AppColors.error,
                ),
                Text(
                  'حظر الرد التلقائي بالذكاء الاصطناعي',
                  style: AppTypography.body,
                ),
              ],
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
                        valueColor: AlwaysStoppedAnimation(AppColors.background),
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

  Widget _buildTagField() {
    return Row(
      children: [
        ElevatedButton(
          onPressed: () {
            final text = _tagController.text.trim();
            if (text.isNotEmpty && !_tags.contains(text)) {
              setState(() {
                _tags.add(text);
                _tagController.clear();
              });
            }
          },
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
              hintText: 'أضف وسم جديد...',
              hintStyle: AppTypography.bodyMuted,
              filled: true,
              fillColor: AppColors.surface,
              contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
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
      backgroundColor: AppColors.primary.withOpacity(0.12),
      side: const BorderSide(color: AppColors.primary, width: 0.5),
      deleteIcon: const Icon(Icons.close, size: 14, color: AppColors.primary),
      onDeleted: () {
        setState(() {
          _tags.remove(label);
        });
      },
    );
  }
}
