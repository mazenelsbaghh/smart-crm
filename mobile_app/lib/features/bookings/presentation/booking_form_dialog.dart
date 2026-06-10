import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../bloc/bookings_bloc.dart';

class BookingFormDialog extends StatefulWidget {
  final DateTime selectedDate;

  const BookingFormDialog({Key? key, required this.selectedDate}) : super(key: key);

  @override
  State<BookingFormDialog> createState() => _BookingFormDialogState();
}

class _BookingFormDialogState extends State<BookingFormDialog> {
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _capacityController = TextEditingController(text: '10');
  
  TimeOfDay _selectedTime = const TimeOfDay(hour: 10, minute: 0);
  String _selectedMode = 'offline';

  @override
  void dispose() {
    _nameController.dispose();
    _capacityController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      backgroundColor: AppColors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: const BorderSide(color: AppColors.border),
      ),
      title: Text(
        'جدولة موعد جديد',
        style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
        textAlign: TextAlign.center,
      ),
      content: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _buildTextField('عنوان الموعد / اسم الجلسة', _nameController),
            const SizedBox(height: 16),
            _buildTextField('السعة الاستيعابية (عدد المقاعد)', _capacityController, keyboardType: TextInputType.number),
            const SizedBox(height: 16),
            Text(
              'وقت البدء',
              style: AppTypography.label.copyWith(color: AppColors.text),
              textAlign: TextAlign.right,
            ),
            const SizedBox(height: 8),
            InkWell(
              onTap: () async {
                final time = await showTimePicker(
                  context: context,
                  initialTime: _selectedTime,
                );
                if (time != null) {
                  setState(() {
                    _selectedTime = time;
                  });
                }
              },
              child: Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: AppColors.background,
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: AppColors.border),
                ),
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Icon(Icons.access_time, color: AppColors.primary),
                    Text(
                      _selectedTime.format(context),
                      style: AppTypography.body,
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              'نوع اللقاء',
              style: AppTypography.label.copyWith(color: AppColors.text),
              textAlign: TextAlign.right,
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  child: _buildModeButton('online', 'أونلاين (فيديو)'),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _buildModeButton('offline', 'حضوري (بالموقع)'),
                ),
              ],
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(
            'إلغاء',
            style: AppTypography.body.copyWith(color: AppColors.textMuted),
          ),
        ),
        ElevatedButton(
          onPressed: () {
            if (_formKey.currentState!.validate()) {
              final date = widget.selectedDate;
              final startDateTime = DateTime(
                date.year,
                date.month,
                date.day,
                _selectedTime.hour,
                _selectedTime.minute,
              );

              final data = {
                'name': _nameController.text.trim(),
                'dateTime': startDateTime.toUtc().toIso8601String(),
                'capacity': int.tryParse(_capacityController.text) ?? 10,
                'mode': _selectedMode,
                'days': '',
                'isActive': true,
              };

              context.read<BookingsBloc>().add(BookingsCreateRequested(data));
              Navigator.of(context).pop();
            }
          },
          style: ElevatedButton.styleFrom(
            backgroundColor: AppColors.primary,
            foregroundColor: AppColors.background,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(8),
            ),
          ),
          child: const Text('جدولة'),
        ),
      ],
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
          style: AppTypography.label.copyWith(color: AppColors.text),
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
            fillColor: AppColors.background,
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
            if (value == null || value.isEmpty) {
              return 'الحقل مطلوب';
            }
            return null;
          },
        ),
      ],
    );
  }

  Widget _buildModeButton(String mode, String label) {
    final isActive = _selectedMode == mode;
    return InkWell(
      onTap: () {
        setState(() {
          _selectedMode = mode;
        });
      },
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 12),
        decoration: BoxDecoration(
          color: isActive ? AppColors.primary.withOpacity(0.12) : AppColors.background,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(
            color: isActive ? AppColors.primary : AppColors.border,
            width: isActive ? 1.5 : 1,
          ),
        ),
        child: Center(
          child: Text(
            label,
            style: AppTypography.body.copyWith(
              color: isActive ? AppColors.primary : AppColors.text,
              fontWeight: isActive ? FontWeight.bold : FontWeight.normal,
            ),
          ),
        ),
      ),
    );
  }
}
