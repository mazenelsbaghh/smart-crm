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
  @override
  void initState() {
    super.initState();
    _fetchAppointments();
  }

  void _fetchAppointments() {
    context.read<BookingsBloc>().add(BookingsFetchRequested());
  }

  void _showSubscribers(GroupAppointment event) {
    showModalBottomSheet(
      context: context,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (bottomSheetContext) {
        return StatefulBuilder(
          builder: (context, setModalState) {
            return Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'قائمة المشتركين - ${event.name}',
                    style: AppTypography.title.copyWith(fontSize: 18, fontWeight: FontWeight.bold),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 6),
                  Text(
                    '${event.bookings.length} مشترك من أصل ${event.capacity}',
                    style: AppTypography.bodyMuted.copyWith(fontSize: 13),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 16),
                  const Divider(color: AppColors.border),
                  Expanded(
                    child: event.bookings.isEmpty
                        ? Center(
                            child: Text(
                              'لا يوجد مشتركين في هذه المجموعة بعد',
                              style: AppTypography.bodyMuted,
                            ),
                          )
                        : ListView.separated(
                            itemCount: event.bookings.length,
                            separatorBuilder: (context, index) => const Divider(color: AppColors.border),
                            itemBuilder: (context, index) {
                              final booking = event.bookings[index];
                              return ListTile(
                                contentPadding: EdgeInsets.zero,
                                leading: Container(
                                  padding: const EdgeInsets.all(8),
                                  decoration: BoxDecoration(
                                    color: AppColors.primary.withOpacity(0.1),
                                    shape: BoxShape.circle,
                                  ),
                                  child: const Icon(Icons.person, color: AppColors.primary, size: 20),
                                ),
                                title: Text(
                                  booking.customerName,
                                  style: AppTypography.body.copyWith(fontWeight: FontWeight.bold),
                                  textAlign: TextAlign.right,
                                ),
                                subtitle: Text(
                                  booking.customerPhone,
                                  style: AppTypography.mono.copyWith(fontSize: 12),
                                  textAlign: TextAlign.right,
                                ),
                                trailing: IconButton(
                                  icon: const Icon(Icons.cancel_outlined, color: AppColors.error),
                                  onPressed: () {
                                    context.read<BookingsBloc>().add(
                                          BookingsBookingCancelRequested(booking.id),
                                        );
                                    Navigator.pop(context);
                                    ScaffoldMessenger.of(context).showSnackBar(
                                      const SnackBar(
                                        content: Text('تم إلغاء الحجز للمشترك بنجاح'),
                                        backgroundColor: AppColors.success,
                                      ),
                                    );
                                  },
                                ),
                              );
                            },
                          ),
                  ),
                ],
              ),
            );
          },
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        backgroundColor: AppColors.surface,
        elevation: 0,
        title: Text(
          'المجموعات الحالية',
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
          if (state.loading && state.appointments.isEmpty) {
            return const Center(
              child: CircularProgressIndicator(
                valueColor: AlwaysStoppedAnimation(AppColors.primary),
              ),
            );
          }

          // Sort appointments: active & not-full on top, active & full in middle, inactive at bottom,
          // then chronologically by time from lowest to highest.
          final sortedAppointments = List<GroupAppointment>.from(state.appointments);
          int getRank(GroupAppointment g) {
            final isFull = g.bookings.length >= g.capacity;
            if (isFull) return 2;
            if (!g.isActive) return 3;
            return 1;
          }

          sortedAppointments.sort((a, b) {
            final rankA = getRank(a);
            final rankB = getRank(b);
            if (rankA != rankB) {
              return rankA.compareTo(rankB);
            }
            return a.dateTime.compareTo(b.dateTime);
          });

          if (sortedAppointments.isEmpty) {
            return Center(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(Icons.calendar_month_outlined, size: 64, color: AppColors.textMuted.withOpacity(0.5)),
                  const SizedBox(height: 16),
                  Text(
                    'لا توجد مجموعات حجز مجدولة حالياً',
                    style: AppTypography.bodyMuted,
                  ),
                ],
              ),
            );
          }

          return RefreshIndicator(
            onRefresh: () async => _fetchAppointments(),
            color: AppColors.primary,
            child: Column(
              children: [
                _buildSummaryCards(sortedAppointments),
                Expanded(
                  child: ListView.separated(
                    padding: const EdgeInsets.only(left: 16, right: 16, bottom: 16),
                    itemCount: sortedAppointments.length,
                    separatorBuilder: (context, index) => const SizedBox(height: 16),
                    itemBuilder: (context, index) {
                      final event = sortedAppointments[index];
                      final isFull = event.bookings.length >= event.capacity;
                      final timeStr = DateFormat('hh:mm a').format(event.dateTime.toLocal());
                      final fillPercentage = event.capacity > 0 ? (event.bookings.length / event.capacity) : 0.0;

                      return Container(
                        decoration: BoxDecoration(
                          color: AppColors.surface,
                          borderRadius: BorderRadius.circular(16),
                          border: Border.all(color: AppColors.border),
                          boxShadow: [
                            BoxShadow(
                              color: Colors.black.withOpacity(0.02),
                              blurRadius: 10,
                              offset: const Offset(0, 4),
                            ),
                          ],
                        ),
                        child: ClipRRect(
                          borderRadius: BorderRadius.circular(16),
                          child: ExpansionTile(
                            backgroundColor: AppColors.surface,
                            collapsedBackgroundColor: AppColors.surface,
                            title: Padding(
                              padding: const EdgeInsets.only(top: 8),
                              child: Row(
                                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                children: [
                                  // Status Badge
                                  () {
                                    final String statusText;
                                    final Color statusColor;
                                    if (!event.isActive) {
                                      statusText = 'غير نشطة';
                                      statusColor = AppColors.textMuted;
                                    } else if (isFull) {
                                      statusText = 'مكتملة!';
                                      statusColor = AppColors.error;
                                    } else {
                                      statusText = 'نشطة';
                                      statusColor = AppColors.success;
                                    }

                                    return Container(
                                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                                      decoration: BoxDecoration(
                                        color: statusColor.withOpacity(0.1),
                                        borderRadius: BorderRadius.circular(20),
                                      ),
                                      child: Text(
                                        statusText,
                                        style: AppTypography.label.copyWith(
                                          color: statusColor,
                                          fontWeight: FontWeight.bold,
                                          fontSize: 11,
                                        ),
                                      ),
                                    );
                                  }(),
                                  // Group Title and Mode
                                  Row(
                                    children: [
                                      Text(
                                        event.mode == 'online' ? '(Online)' : '(Offline)',
                                        style: AppTypography.bodyMuted.copyWith(fontSize: 12),
                                      ),
                                      const SizedBox(width: 6),
                                      Text(
                                        event.name,
                                        style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
                                      ),
                                    ],
                                  ),
                                ],
                              ),
                            ),
                            subtitle: Padding(
                              padding: const EdgeInsets.symmetric(vertical: 12),
                              child: Row(
                                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                children: [
                                  // Occupancy progress bar
                                  Expanded(
                                    child: Column(
                                      crossAxisAlignment: CrossAxisAlignment.start,
                                      children: [
                                        Row(
                                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                          children: [
                                            Text(
                                              '${(fillPercentage * 100).toStringAsFixed(0)}% شغل',
                                              style: AppTypography.label.copyWith(fontSize: 10),
                                            ),
                                            Text(
                                              'الحجوزات: ${event.bookings.length} / ${event.capacity}',
                                              style: AppTypography.label.copyWith(fontSize: 11),
                                            ),
                                          ],
                                        ),
                                        const SizedBox(height: 6),
                                        ClipRRect(
                                          borderRadius: BorderRadius.circular(4),
                                          child: LinearProgressIndicator(
                                            value: fillPercentage,
                                            backgroundColor: AppColors.border,
                                            valueColor: AlwaysStoppedAnimation<Color>(
                                              isFull ? AppColors.error : AppColors.success,
                                            ),
                                            minHeight: 6,
                                          ),
                                        ),
                                      ],
                                    ),
                                  ),
                                  const SizedBox(width: 24),
                                  // Time and Days
                                  Column(
                                    crossAxisAlignment: CrossAxisAlignment.end,
                                    children: [
                                      Text(
                                        event.days.isNotEmpty ? event.days : 'غير محدد الأيام',
                                        style: AppTypography.body.copyWith(fontWeight: FontWeight.bold, fontSize: 13),
                                      ),
                                      const SizedBox(height: 4),
                                      Text(
                                        timeStr,
                                        style: AppTypography.mono.copyWith(
                                          color: AppColors.primary,
                                          fontWeight: FontWeight.bold,
                                          fontSize: 12,
                                        ),
                                      ),
                                    ],
                                  ),
                                ],
                              ),
                            ),
                            children: [
                              const Divider(color: AppColors.border, height: 1),
                              Padding(
                                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                                child: Row(
                                  children: [
                                    IconButton(
                                      icon: const Icon(Icons.delete_outline, color: AppColors.error),
                                      onPressed: () {
                                        showDialog(
                                          context: context,
                                          builder: (confirmContext) => AlertDialog(
                                            backgroundColor: AppColors.surface,
                                            title: Text('حذف المجموعة', style: AppTypography.title, textAlign: TextAlign.center),
                                            content: Text('هل أنت متأكد من حذف هذه المجموعة نهائياً؟', style: AppTypography.body, textAlign: TextAlign.center),
                                            actions: [
                                              TextButton(
                                                onPressed: () => Navigator.pop(confirmContext),
                                                child: Text('إلغاء', style: AppTypography.body.copyWith(color: AppColors.textMuted)),
                                              ),
                                              ElevatedButton(
                                                onPressed: () {
                                                  context.read<BookingsBloc>().add(BookingsDeleteRequested(event.id));
                                                  Navigator.pop(confirmContext);
                                                },
                                                style: ElevatedButton.styleFrom(backgroundColor: AppColors.error),
                                                child: const Text('حذف'),
                                              ),
                                            ],
                                          ),
                                        );
                                      },
                                    ),
                                    IconButton(
                                      icon: Icon(
                                        event.isActive ? Icons.toggle_on : Icons.toggle_off,
                                        color: event.isActive ? AppColors.success : AppColors.textMuted,
                                        size: 36,
                                      ),
                                      onPressed: () {
                                        context.read<BookingsBloc>().add(BookingsToggleRequested(event.id));
                                      },
                                    ),
                                    const Spacer(),
                                    TextButton.icon(
                                      icon: const Icon(Icons.people_outline, size: 16),
                                      label: Text('المشتركين (${event.bookings.length})'),
                                      onPressed: () => _showSubscribers(event),
                                      style: TextButton.styleFrom(
                                        foregroundColor: AppColors.primary,
                                        textStyle: AppTypography.label.copyWith(fontWeight: FontWeight.bold),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                      );
                    },
                  ),
                ),
              ],
            ),
          );
        },
      ),
      floatingActionButton: FloatingActionButton(
        backgroundColor: AppColors.primary,
        onPressed: () {
          showDialog(
            context: context,
            builder: (_) => const BookingFormDialog(),
          );
        },
        child: const Icon(Icons.add, color: AppColors.surface),
      ),
    );
  }

  Widget _buildSummaryCards(List<GroupAppointment> appointments) {
    final totalGroups = appointments.length;
    final totalBookings = appointments.fold<int>(0, (sum, e) => sum + e.bookings.length);
    final totalCapacity = appointments.fold<int>(0, (sum, e) => sum + e.capacity);
    final activeGroups = appointments.where((e) => e.isActive).length;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(
        children: [
          Expanded(
            child: _buildSummaryCard(
              label: 'إجمالي الحجوزات',
              value: '$totalBookings',
              icon: Icons.people,
              color: AppColors.primary,
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: _buildSummaryCard(
              label: 'نسبة الإشغال',
              value: totalCapacity > 0 ? '${((totalBookings / totalCapacity) * 100).toStringAsFixed(0)}%' : '0%',
              icon: Icons.percent,
              color: AppColors.success,
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: _buildSummaryCard(
              label: 'المجموعات النشطة',
              value: '$activeGroups / $totalGroups',
              icon: Icons.calendar_today,
              color: AppColors.secondary,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSummaryCard({
    required String label,
    required String value,
    required IconData icon,
    required Color color,
  }) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.01),
            blurRadius: 10,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Icon(icon, color: color, size: 18),
              const SizedBox(),
            ],
          ),
          const SizedBox(height: 8),
          FittedBox(
            fit: BoxFit.scaleDown,
            child: Text(
              value,
              style: AppTypography.title.copyWith(
                fontWeight: FontWeight.bold,
                fontSize: 15,
                color: color,
              ),
              textAlign: TextAlign.right,
            ),
          ),
          const SizedBox(height: 2),
          FittedBox(
            fit: BoxFit.scaleDown,
            child: Text(
              label,
              style: AppTypography.label.copyWith(fontSize: 9, color: AppColors.textMuted),
              textAlign: TextAlign.right,
            ),
          ),
        ],
      ),
    );
  }
}
