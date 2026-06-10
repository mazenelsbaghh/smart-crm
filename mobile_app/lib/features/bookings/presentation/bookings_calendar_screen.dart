import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:table_calendar/table_calendar.dart';
import 'package:intl/intl.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../bloc/bookings_bloc.dart';
import '../data/models/appointment_model.dart';
import 'booking_form_dialog.dart';

class BookingsCalendarScreen extends StatefulWidget {
  const BookingsCalendarScreen({Key? key}) : super(key: key);

  @override
  State<BookingsCalendarScreen> createState() => _BookingsCalendarScreenState();
}

class _BookingsCalendarScreenState extends State<BookingsCalendarScreen> {
  CalendarFormat _calendarFormat = CalendarFormat.month;
  DateTime _focusedDay = DateTime.now();
  DateTime? _selectedDay;

  @override
  void initState() {
    super.initState();
    _selectedDay = _focusedDay;
    _fetchAppointments();
  }

  void _fetchAppointments() {
    context.read<BookingsBloc>().add(BookingsFetchRequested());
  }

  List<GroupAppointment> _getEventsForDay(DateTime day, List<GroupAppointment> appointments) {
    return appointments.where((appointment) {
      return isSameDay(appointment.dateTime, day);
    }).toList();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        backgroundColor: AppColors.surface,
        elevation: 0,
        title: Text(
          'حجز المواعيد الجماعية',
          style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
        ),
        centerTitle: true,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh, color: AppColors.primary),
            onPressed: _fetchAppointments,
          ),
        ],
      ),
      body: BlocBuilder<BookingsBloc, BookingsState>(
        builder: (context, state) {
          final dayEvents = _getEventsForDay(_selectedDay ?? _focusedDay, state.appointments);

          return Column(
            children: [
              _buildCalendar(state.appointments),
              const SizedBox(height: 12),
              const Divider(color: AppColors.border, height: 1),
              Expanded(
                child: state.loading
                    ? const Center(
                        child: CircularProgressIndicator(
                          valueColor: AlwaysStoppedAnimation(AppColors.primary),
                        ),
                      )
                    : _buildEventsList(dayEvents),
              ),
            ],
          );
        },
      ),
      floatingActionButton: FloatingActionButton(
        backgroundColor: AppColors.primary,
        onPressed: () {
          showDialog(
            context: context,
            builder: (_) => BookingFormDialog(selectedDate: _selectedDay ?? _focusedDay),
          );
        },
        child: const Icon(Icons.add, color: AppColors.background),
      ),
    );
  }

  Widget _buildCalendar(List<GroupAppointment> appointments) {
    return Container(
      color: AppColors.surface,
      child: TableCalendar(
        locale: 'ar',
        firstDay: DateTime.utc(2025, 1, 1),
        lastDay: DateTime.utc(2030, 12, 31),
        focusedDay: _focusedDay,
        calendarFormat: _calendarFormat,
        selectedDayPredicate: (day) {
          return isSameDay(_selectedDay, day);
        },
        onDaySelected: (selectedDay, focusedDay) {
          setState(() {
            _selectedDay = selectedDay;
            _focusedDay = focusedDay;
          });
        },
        onFormatChanged: (format) {
          if (_calendarFormat != format) {
            setState(() {
              _calendarFormat = format;
            });
          }
        },
        onPageChanged: (focusedDay) {
          _focusedDay = focusedDay;
        },
        eventLoader: (day) {
          return _getEventsForDay(day, appointments);
        },
        calendarStyle: CalendarStyle(
          defaultTextStyle: AppTypography.body,
          weekendTextStyle: AppTypography.body.copyWith(color: AppColors.textMuted),
          todayDecoration: const BoxDecoration(
            color: AppColors.border,
            shape: BoxShape.circle,
          ),
          selectedDecoration: const BoxDecoration(
            color: AppColors.primary,
            shape: BoxShape.circle,
          ),
          selectedTextStyle: AppTypography.body.copyWith(color: AppColors.background, fontWeight: FontWeight.bold),
          markerDecoration: const BoxDecoration(
            color: AppColors.secondary,
            shape: BoxShape.circle,
          ),
        ),
        headerStyle: HeaderStyle(
          titleTextStyle: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
          formatButtonVisible: false,
          leftChevronIcon: const Icon(Icons.chevron_left, color: AppColors.text),
          rightChevronIcon: const Icon(Icons.chevron_right, color: AppColors.text),
        ),
      ),
    );
  }

  Widget _buildEventsList(List<GroupAppointment> events) {
    if (events.isEmpty) {
      return Center(
        child: Text(
          'لا توجد مواعيد مجدولة في هذا اليوم',
          style: AppTypography.bodyMuted,
        ),
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: events.length,
      separatorBuilder: (context, index) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final event = events[index];
        final timeStr = DateFormat('hh:mm a').format(event.dateTime);

        return Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: AppColors.surface,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: AppColors.border),
          ),
          child: Row(
            children: [
              IconButton(
                icon: const Icon(Icons.delete_outline, color: AppColors.error),
                onPressed: () {
                  context.read<BookingsBloc>().add(BookingsDeleteRequested(event.id));
                },
              ),
              const Spacer(),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(
                    event.name,
                    style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
                  ),
                  const SizedBox(height: 6),
                  Row(
                    children: [
                      Text(
                        '${event.bookings.length} / ${event.capacity} مقاعد',
                        style: AppTypography.label.copyWith(color: AppColors.textMuted),
                      ),
                      const SizedBox(width: 8),
                      const Icon(Icons.people_outline, size: 14, color: AppColors.textMuted),
                    ],
                  ),
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      Text(
                        event.mode == 'online' ? 'أونلاين' : 'حضوري',
                        style: AppTypography.label.copyWith(color: AppColors.primary),
                      ),
                      const SizedBox(width: 8),
                      Icon(
                        event.mode == 'online' ? Icons.videocam_outlined : Icons.location_on_outlined,
                        size: 14,
                        color: AppColors.primary,
                      ),
                    ],
                  ),
                ],
              ),
              const SizedBox(width: 16),
              Column(
                children: [
                  Text(
                    timeStr,
                    style: AppTypography.mono.copyWith(
                      color: AppColors.primary,
                      fontWeight: FontWeight.bold,
                      fontSize: 13,
                    ),
                  ),
                ],
              ),
            ],
          ),
        );
      },
    );
  }
}
