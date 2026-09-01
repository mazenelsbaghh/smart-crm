import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../bloc/bookings_bloc.dart';

class BookingFormDialog extends StatefulWidget {
  final DateTime? initialDate;

  const BookingFormDialog({super.key, this.initialDate});

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
  late int _initialCreateRevision;
  bool _submitted = false;

  @override
  void initState() {
    super.initState();
    final today = DateUtils.dateOnly(DateTime.now());
    final requestedDate = DateUtils.dateOnly(widget.initialDate ?? today);
    _selectedDate = requestedDate.isBefore(today) ? today : requestedDate;
    _initialCreateRevision = context.read<BookingsBloc>().state.createRevision;
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
    return PopScope<void>(
      canPop: !_submitted,
      child: BlocConsumer<BookingsBloc, BookingsState>(
        listenWhen: (previous, current) =>
            previous.createRevision != current.createRevision ||
            previous.createError != current.createError,
        listener: (context, state) {
          if (_submitted && state.createRevision > _initialCreateRevision) {
            Navigator.of(context).pop(true);
          } else if (_submitted && state.createError != null) {
            setState(() => _submitted = false);
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(
                content: Text(state.createError!),
                backgroundColor: AppColors.error,
              ),
            );
          }
        },
        builder: (context, state) => AbsorbPointer(
          absorbing: _submitted,
          child: AlertDialog(
            backgroundColor: AppColors.surface,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(16),
              side: const BorderSide(color: AppColors.border),
            ),
            title: Text(
              'جدولة موعد جديد',
              style: AppTypography.title.copyWith(
                fontWeight: FontWeight.bold,
                fontSize: 18,
              ),
              textAlign: TextAlign.center,
            ),
            content: SingleChildScrollView(
              child: Form(
                key: _formKey,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    _buildTextField(
                      'عنوان الموعد / اسم الجلسة',
                      _nameController,
                      hintText: 'مثال: في السنتر (Offline)',
                    ),
                    const SizedBox(height: 12),
                    _buildTextField(
                      'أيام الانعقاد',
                      _daysController,
                      hintText: 'مثال: سبت، إثنين، أربعاء',
                    ),
                    const SizedBox(height: 12),
                    _buildTextField(
                      'السعة الاستيعابية (عدد المقاعد)',
                      _capacityController,
                      keyboardType: TextInputType.number,
                    ),
                    const SizedBox(height: 12),
                    Text(
                      'تاريخ البدء',
                      style: AppTypography.label.copyWith(
                        color: AppColors.text,
                      ),
                      textAlign: TextAlign.right,
                    ),
                    const SizedBox(height: 6),
                    Semantics(
                      button: true,
                      label:
                          'اختيار تاريخ البدء، ${DateFormat.yMMMd('ar').format(_selectedDate)}',
                      child: InkWell(
                        onTap: () async {
                          final date = await showDatePicker(
                            context: context,
                            initialDate: _selectedDate,
                            firstDate: DateUtils.dateOnly(DateTime.now()),
                            lastDate: DateTime.now().add(
                              const Duration(days: 365),
                            ),
                          );
                          if (date != null) {
                            setState(() {
                              _selectedDate = date;
                            });
                          }
                        },
                        child: Container(
                          constraints: const BoxConstraints(minHeight: 48),
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 10,
                          ),
                          decoration: BoxDecoration(
                            color: AppColors.background,
                            borderRadius: BorderRadius.circular(8),
                            border: Border.all(color: AppColors.border),
                          ),
                          child: Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              const Icon(
                                Icons.calendar_today,
                                color: AppColors.primary,
                                size: 18,
                              ),
                              Text(
                                DateFormat.yMMMd('ar').format(_selectedDate),
                                style: AppTypography.body,
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      'وقت البدء',
                      style: AppTypography.label.copyWith(
                        color: AppColors.text,
                      ),
                      textAlign: TextAlign.right,
                    ),
                    const SizedBox(height: 6),
                    Semantics(
                      button: true,
                      label:
                          'اختيار وقت البدء، ${_selectedTime.format(context)}',
                      child: InkWell(
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
                          constraints: const BoxConstraints(minHeight: 48),
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 10,
                          ),
                          decoration: BoxDecoration(
                            color: AppColors.background,
                            borderRadius: BorderRadius.circular(8),
                            border: Border.all(color: AppColors.border),
                          ),
                          child: Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              const Icon(
                                Icons.access_time,
                                color: AppColors.primary,
                                size: 18,
                              ),
                              Text(
                                _selectedTime.format(context),
                                style: AppTypography.body,
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      'نوع اللقاء',
                      style: AppTypography.label.copyWith(
                        color: AppColors.text,
                      ),
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
                onPressed: _submitted
                    ? null
                    : () => Navigator.of(context).pop(),
                child: Text(
                  'إلغاء',
                  style: AppTypography.body.copyWith(
                    color: AppColors.textMuted,
                  ),
                ),
              ),
              ElevatedButton(
                onPressed: state.creating || _submitted
                    ? null
                    : () {
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
                            'capacity': int.parse(
                              _capacityController.text.trim(),
                            ),
                            'mode': _selectedMode,
                            'days': _daysController.text.trim(),
                            'isActive': true,
                          };

                          setState(() => _submitted = true);
                          context.read<BookingsBloc>().add(
                            BookingsCreateRequested(data),
                          );
                        }
                      },
                style: ElevatedButton.styleFrom(
                  backgroundColor: AppColors.primary,
                  foregroundColor: AppColors.background,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                ),
                child: state.creating
                    ? const SizedBox.square(
                        dimension: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('جدولة'),
              ),
            ],
          ),
        ),
      ),
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
            labelText: label,
            hintText: hintText,
            hintStyle: AppTypography.bodyMuted.copyWith(fontSize: 12),
            filled: true,
            fillColor: AppColors.background,
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 16,
              vertical: 10,
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
            if (value == null || value.isEmpty) {
              return 'الحقل مطلوب';
            }
            if (keyboardType == TextInputType.number) {
              final capacity = int.tryParse(value);
              if (capacity == null || capacity <= 0 || capacity > 10000) {
                return 'أدخل سعة بين 1 و‎10,000';
              }
            }
            return null;
          },
        ),
      ],
    );
  }

  Widget _buildModeButton(String mode, String label) {
    final isActive = _selectedMode == mode;
    return Semantics(
      button: true,
      selected: isActive,
      label: label,
      child: InkWell(
        onTap: () => setState(() => _selectedMode = mode),
        borderRadius: BorderRadius.circular(8),
        child: Container(
          constraints: const BoxConstraints(minHeight: 48),
          padding: const EdgeInsets.symmetric(vertical: 10),
          decoration: BoxDecoration(
            color: isActive
                ? AppColors.primary.withValues(alpha: 0.08)
                : AppColors.background,
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
      ),
    );
  }
}
