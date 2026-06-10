import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../bloc/bookings_bloc.dart';

class BookingFormDialog extends StatefulWidget {
  final DateTime? initialDate;

  const BookingFormDialog({Key? key, this.initialDate}) : super(key: key);

  @override
  State<BookingFormDialog> createState() => _BookingFormDialogState();
}

class _BookingFormDialogState extends State<BookingFormDialog> {
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _capacityController = TextEditingController(text: '10');
  final _daysController = TextEditingController(text: 'أحد، ثلاثاء');
  
  late DateTime _selectedDate;
  TimeOfDay _selectedTime = const TimeOfDay(hour: 18, minute: 0);
  String _selectedMode = 'offline';

  @override
  void initState() {
    super.initState();
    _selectedDate = widget.initialDate ?? DateTime.now();
  }

  @override
  void dispose() {
    _nameController.dispose();
    _capacityController.dispose();
    _daysController.dispose();
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
        style: AppTypography.title.copyWith(fontWeight: FontWeight.bold, fontSize: 18),
        textAlign: TextAlign.center,
      ),
      content: SingleChildScrollView(
        child: Form(
          key: _formKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _buildTextField('عنوان الموعد / اسم الجلسة', _nameController, hintText: 'مثال: في السنتر (Offline)'),
              const SizedBox(height: 12),
              _buildTextField('أيام الانعقاد', _daysController, hintText: 'مثال: سبت، إثنين، أربعاء'),
              const SizedBox(height: 12),
              _buildTextField('السعة الاستيعابية (عدد المقاعد)', _capacityController, keyboardType: TextInputType.number),
              const SizedBox(height: 12),
              Text(
                'تاريخ البدء',
                style: AppTypography.label.copyWith(color: AppColors.text),
                textAlign: TextAlign.right,
              ),
              const SizedBox(height: 6),
              InkWell(
                onTap: () async {
                  final date = await showDatePicker(
                    context: context,
                    initialDate: _selectedDate,
                    firstDate: DateTime.now().subtract(const Duration(days: 365)),
                    lastDate: DateTime.now().add(const Duration(days: 365)),
                  );
                  if (date != null) {
                    setState(() {
                      _selectedDate = date;
                    });
                  }
                },
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                  decoration: BoxDecoration(
                    color: AppColors.background,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: AppColors.border),
                  ),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      const Icon(Icons.calendar_today, color: AppColors.primary, size: 18),
                      Text(
                        DateFormat('yyyy-MM-dd').format(_selectedDate),
                        style: AppTypography.body,
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 12),
              Text(
                'وقت البدء',
                style: AppTypography.label.copyWith(color: AppColors.text),
                textAlign: TextAlign.right,
              ),
              const SizedBox(height: 6),
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
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                  decoration: BoxDecoration(
                    color: AppColors.background,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: AppColors.border),
                  ),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      const Icon(Icons.access_time, color: AppColors.primary, size: 18),
                      Text(
                        _selectedTime.format(context),
                        style: AppTypography.body,
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 12),
              Text(
                'نوع اللقاء',
                style: AppTypography.label.copyWith(color: AppColors.text),
                textAlign: TextAlign.right,
              ),
              const SizedBox(height: 6),
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
              final startDateTime = DateTime(
                _selectedDate.year,
                _selectedDate.month,
                _selectedDate.day,
                _selectedTime.hour,
                _selectedTime.minute,
              );

              final data = {
                'name': _nameController.text.trim(),
                'dateTime': startDateTime.toUtc().toIso8601String(),
                'capacity': int.tryParse(_capacityController.text) ?? 10,
                'mode': _selectedMode,
                'days': _daysController.text.trim(),
                'isActive': true,
              };

              context.read<BookingsBloc>().add(BookingsCreateRequested(data));
              Navigator.of(context).pop();
            }
          },
          style: ElevatedButton.styleFrom(
            backgroundColor: AppColors.primary,
            foregroundColor: AppColors.surface,
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
    String? hintText,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          label,
          style: AppTypography.label.copyWith(color: AppColors.text),
          textAlign: TextAlign.right,
        ),
        const SizedBox(height: 6),
        TextFormField(
          controller: controller,
          keyboardType: keyboardType,
          style: AppTypography.body,
          textAlign: TextAlign.right,
          decoration: InputDecoration(
            hintText: hintText,
            hintStyle: AppTypography.bodyMuted.copyWith(fontSize: 12),
            filled: true,
            fillColor: AppColors.background,
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
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
        padding: const EdgeInsets.symmetric(vertical: 10),
        decoration: BoxDecoration(
          color: isActive ? AppColors.primary.withOpacity(0.08) : AppColors.background,
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
              fontSize: 12,
            ),
          ),
        ),
      ),
    );
  }
}
