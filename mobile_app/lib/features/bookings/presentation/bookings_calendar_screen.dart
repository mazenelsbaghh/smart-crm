import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../bloc/bookings_bloc.dart';
import '../data/models/appointment_model.dart';
import 'booking_form_dialog.dart';

class BookingsCalendarScreen extends StatefulWidget {
  const BookingsCalendarScreen({super.key});

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
    final initialMutationRevision = context
        .read<BookingsBloc>()
        .state
        .mutationRevision;
    showModalBottomSheet<void>(
      context: context,
      useSafeArea: true,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (bottomSheetContext) {
        return BlocConsumer<BookingsBloc, BookingsState>(
          listenWhen: (previous, current) =>
              previous.mutationRevision != current.mutationRevision,
          listener: (context, state) {
            if (state.lastMutationType == 'cancelBooking' &&
                state.mutationRevision > initialMutationRevision) {
              Navigator.pop(bottomSheetContext);
              ScaffoldMessenger.of(this.context).showSnackBar(
                const SnackBar(
                  content: Text('تم إلغاء الحجز.'),
                  backgroundColor: AppColors.success,
                ),
              );
            }
          },
          builder: (context, state) => Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'قائمة المشتركين - ${event.name}',
                  style: AppTypography.title.copyWith(
                    fontSize: 18,
                    fontWeight: FontWeight.bold,
                  ),
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
                          separatorBuilder: (context, index) =>
                              const Divider(color: AppColors.border),
                          itemBuilder: (context, index) {
                            final booking = event.bookings[index];
                            return ListTile(
                              contentPadding: EdgeInsets.zero,
                              leading: Container(
                                padding: const EdgeInsets.all(8),
                                decoration: BoxDecoration(
                                  color: AppColors.primary.withValues(
                                    alpha: 0.1,
                                  ),
                                  shape: BoxShape.circle,
                                ),
                                child: const Icon(
                                  Icons.person,
                                  color: AppColors.primary,
                                  size: 20,
                                ),
                              ),
                              title: Text(
                                booking.customerName,
                                style: AppTypography.body.copyWith(
                                  fontWeight: FontWeight.bold,
                                ),
                                textAlign: TextAlign.right,
                              ),
                              subtitle: Text(
                                booking.customerPhone,
                                style: AppTypography.mono.copyWith(
                                  fontSize: 12,
                                ),
                                textAlign: TextAlign.right,
                              ),
                              trailing: IconButton(
                                tooltip: 'إلغاء حجز ${booking.customerName}',
                                icon:
                                    state.mutationsInProgress.contains(
                                      booking.id,
                                    )
                                    ? const SizedBox.square(
                                        dimension: 18,
                                        child: CircularProgressIndicator(
                                          strokeWidth: 2,
                                        ),
                                      )
                                    : const Icon(
                                        Icons.cancel_outlined,
                                        color: AppColors.error,
                                      ),
                                onPressed:
                                    state.mutationsInProgress.contains(
                                      booking.id,
                                    )
                                    ? null
                                    : () async {
                                        final confirmed = await showDialog<bool>(
                                          context: context,
                                          builder: (dialogContext) => AlertDialog(
                                            title: const Text('إلغاء الحجز'),
                                            content: Text(
                                              'هل تريد إلغاء حجز ${booking.customerName}؟',
                                            ),
                                            actions: [
                                              TextButton(
                                                onPressed: () => Navigator.pop(
                                                  dialogContext,
                                                  false,
                                                ),
                                                child: const Text('رجوع'),
                                              ),
                                              ElevatedButton(
                                                onPressed: () => Navigator.pop(
                                                  dialogContext,
                                                  true,
                                                ),
                                                style: ElevatedButton.styleFrom(
                                                  backgroundColor:
                                                      AppColors.error,
                                                ),
                                                child: const Text(
                                                  'إلغاء الحجز',
                                                ),
                                              ),
                                            ],
                                          ),
                                        );
                                        if (confirmed == true &&
                                            context.mounted) {
                                          context.read<BookingsBloc>().add(
                                            BookingsBookingCancelRequested(
                                              booking.id,
                                            ),
                                          );
                                        }
                                      },
                              ),
                            );
                          },
                        ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  Future<void> _showCreateDialog() async {
    final created = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => const BookingFormDialog(),
    );
    if (created == true && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('تمت جدولة الموعد.'),
          backgroundColor: AppColors.success,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return BlocListener<BookingsBloc, BookingsState>(
      listenWhen: (previous, current) =>
          previous.mutationError != current.mutationError ||
          previous.mutationRevision != current.mutationRevision,
      listener: (context, state) {
        if (state.mutationError != null) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text(state.mutationError!),
              backgroundColor: AppColors.error,
            ),
          );
        } else if (state.lastMutationType == 'delete') {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text('تم حذف المجموعة.'),
              backgroundColor: AppColors.success,
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
            'المجموعات الحالية',
            style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
          ),
          centerTitle: true,
          actions: [
            IconButton(
              tooltip: 'تحديث المواعيد',
              icon: const Icon(Icons.refresh, color: AppColors.primary),
              onPressed: _fetchAppointments,
            ),
          ],
        ),
        body: BlocBuilder<BookingsBloc, BookingsState>(
          builder: (context, state) {
            if (state.loading && state.appointments.isEmpty) {
              return const AppLoadingSkeleton(rows: 6);
            }

            if (state.error != null && state.appointments.isEmpty) {
              return AppStateView(
                icon: Icons.cloud_off_outlined,
                title: 'تعذر تحميل المواعيد',
                message: state.error!,
                actionLabel: 'إعادة المحاولة',
                onAction: _fetchAppointments,
              );
            }

            // Sort appointments: active & not-full on top, active & full in middle, inactive at bottom,
            // then chronologically by time from lowest to highest.
            final sortedAppointments = List<GroupAppointment>.from(
              state.appointments,
            );
            int getRank(GroupAppointment g) {
              final isFull = g.capacity > 0 && g.bookings.length >= g.capacity;
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
              return AppStateView(
                icon: Icons.calendar_month_outlined,
                title: 'لا توجد مواعيد مجدولة',
                message: 'أضف مجموعة حجز لتظهر هنا.',
                actionLabel: 'جدولة موعد',
                onAction: _showCreateDialog,
              );
            }

            return RefreshIndicator(
              onRefresh: () async => _fetchAppointments(),
              color: AppColors.primary,
              child: Column(
                children: [
                  if (state.loading) const LinearProgressIndicator(),
                  if (state.error != null) _buildLoadWarning(state.error!),
                  _buildSummaryCards(sortedAppointments),
                  Expanded(
                    child: ListView.separated(
                      padding: const EdgeInsetsDirectional.only(
                        start: 16,
                        end: 16,
                        bottom: 16,
                      ),
                      itemCount: sortedAppointments.length,
                      separatorBuilder: (context, index) =>
                          const SizedBox(height: 16),
                      itemBuilder: (context, index) {
                        final event = sortedAppointments[index];
                        final mutationBusy = state.mutationsInProgress.contains(
                          event.id,
                        );
                        final isFull =
                            event.capacity > 0 &&
                            event.bookings.length >= event.capacity;
                        final hasValidDate =
                            event.dateTime.millisecondsSinceEpoch > 0;
                        final timeStr = hasValidDate
                            ? DateFormat(
                                'hh:mm a',
                                'ar',
                              ).format(event.dateTime.toLocal())
                            : 'وقت غير محدد';
                        final fillPercentage = event.capacity > 0
                            ? (event.bookings.length / event.capacity)
                                  .clamp(0, 1)
                                  .toDouble()
                            : 0.0;

                        return Container(
                          decoration: BoxDecoration(
                            color: AppColors.surface,
                            borderRadius: BorderRadius.circular(16),
                            border: Border.all(color: AppColors.border),
                          ),
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(16),
                            child: ExpansionTile(
                              backgroundColor: AppColors.surface,
                              collapsedBackgroundColor: AppColors.surface,
                              title: Padding(
                                padding: const EdgeInsets.only(top: 8),
                                child: Row(
                                  mainAxisAlignment:
                                      MainAxisAlignment.spaceBetween,
                                  children: [
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
                                        padding: const EdgeInsets.symmetric(
                                          horizontal: 10,
                                          vertical: 4,
                                        ),
                                        decoration: BoxDecoration(
                                          color: statusColor.withValues(
                                            alpha: 0.1,
                                          ),
                                          borderRadius: BorderRadius.circular(
                                            20,
                                          ),
                                        ),
                                        child: Text(
                                          statusText,
                                          style: AppTypography.label.copyWith(
                                            color: statusColor,
                                            fontWeight: FontWeight.bold,
                                            fontSize: 12,
                                          ),
                                        ),
                                      );
                                    }(),
                                    const SizedBox(width: 10),
                                    Expanded(
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        children: [
                                          Text(
                                            event.name,
                                            style: AppTypography.title,
                                            maxLines: 2,
                                            overflow: TextOverflow.ellipsis,
                                          ),
                                          Text(
                                            event.mode == 'online'
                                                ? 'عن بعد'
                                                : 'حضوري',
                                            style: AppTypography.bodyMuted,
                                          ),
                                        ],
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              subtitle: Padding(
                                padding: const EdgeInsets.symmetric(
                                  vertical: 12,
                                ),
                                child: Column(
                                  crossAxisAlignment:
                                      CrossAxisAlignment.stretch,
                                  children: [
                                    Wrap(
                                      alignment: WrapAlignment.spaceBetween,
                                      spacing: 12,
                                      runSpacing: 4,
                                      children: [
                                        Text(
                                          event.days.isNotEmpty
                                              ? event.days
                                              : 'غير محدد الأيام',
                                          style: AppTypography.body,
                                        ),
                                        Text(
                                          timeStr,
                                          style: AppTypography.mono.copyWith(
                                            color: AppColors.primary,
                                          ),
                                        ),
                                      ],
                                    ),
                                    const SizedBox(height: 10),
                                    Row(
                                      mainAxisAlignment:
                                          MainAxisAlignment.spaceBetween,
                                      children: [
                                        Text(
                                          '${(fillPercentage * 100).toStringAsFixed(0)}%',
                                          style: AppTypography.label,
                                        ),
                                        Flexible(
                                          child: Text(
                                            'الحجوزات: ${event.bookings.length} / ${event.capacity}',
                                            style: AppTypography.label,
                                          ),
                                        ),
                                      ],
                                    ),
                                    const SizedBox(height: 6),
                                    Semantics(
                                      label:
                                          'نسبة الإشغال ${(fillPercentage * 100).toStringAsFixed(0)} في المئة',
                                      child: LinearProgressIndicator(
                                        value: fillPercentage,
                                        backgroundColor: AppColors.border,
                                        valueColor:
                                            AlwaysStoppedAnimation<Color>(
                                              isFull
                                                  ? AppColors.error
                                                  : AppColors.success,
                                            ),
                                        minHeight: 8,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              children: [
                                const Divider(
                                  color: AppColors.border,
                                  height: 1,
                                ),
                                Padding(
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 16,
                                    vertical: 8,
                                  ),
                                  child: Wrap(
                                    alignment: WrapAlignment.spaceBetween,
                                    crossAxisAlignment:
                                        WrapCrossAlignment.center,
                                    spacing: 8,
                                    runSpacing: 8,
                                    children: [
                                      IconButton(
                                        tooltip: 'حذف المجموعة',
                                        icon: mutationBusy
                                            ? const SizedBox.square(
                                                dimension: 18,
                                                child:
                                                    CircularProgressIndicator(
                                                      strokeWidth: 2,
                                                    ),
                                              )
                                            : const Icon(
                                                Icons.delete_outline,
                                                color: AppColors.error,
                                              ),
                                        onPressed: mutationBusy
                                            ? null
                                            : () {
                                                showDialog(
                                                  context: context,
                                                  builder: (confirmContext) => AlertDialog(
                                                    backgroundColor:
                                                        AppColors.surface,
                                                    title: Text(
                                                      'حذف المجموعة',
                                                      style:
                                                          AppTypography.title,
                                                      textAlign:
                                                          TextAlign.center,
                                                    ),
                                                    content: Text(
                                                      event.bookings.isEmpty
                                                          ? 'هل أنت متأكد من حذف هذه المجموعة نهائياً؟'
                                                          : 'سيؤدي الحذف النهائي إلى إلغاء ${event.bookings.length} حجوزات مرتبطة بهذه المجموعة.',
                                                      style: AppTypography.body,
                                                      textAlign:
                                                          TextAlign.center,
                                                    ),
                                                    actions: [
                                                      TextButton(
                                                        onPressed: () =>
                                                            Navigator.pop(
                                                              confirmContext,
                                                            ),
                                                        child: Text(
                                                          'إلغاء',
                                                          style: AppTypography
                                                              .body
                                                              .copyWith(
                                                                color: AppColors
                                                                    .textMuted,
                                                              ),
                                                        ),
                                                      ),
                                                      ElevatedButton(
                                                        onPressed: () {
                                                          context
                                                              .read<
                                                                BookingsBloc
                                                              >()
                                                              .add(
                                                                BookingsDeleteRequested(
                                                                  event.id,
                                                                ),
                                                              );
                                                          Navigator.pop(
                                                            confirmContext,
                                                          );
                                                        },
                                                        style:
                                                            ElevatedButton.styleFrom(
                                                              backgroundColor:
                                                                  AppColors
                                                                      .error,
                                                            ),
                                                        child: const Text(
                                                          'حذف',
                                                        ),
                                                      ),
                                                    ],
                                                  ),
                                                );
                                              },
                                      ),
                                      IconButton(
                                        tooltip: event.isActive
                                            ? 'إيقاف الحجز'
                                            : 'تفعيل الحجز',
                                        icon: Icon(
                                          event.isActive
                                              ? Icons.toggle_on
                                              : Icons.toggle_off,
                                          color: event.isActive
                                              ? AppColors.success
                                              : AppColors.textMuted,
                                          size: 36,
                                        ),
                                        onPressed: mutationBusy
                                            ? null
                                            : () {
                                                context
                                                    .read<BookingsBloc>()
                                                    .add(
                                                      BookingsToggleRequested(
                                                        event.id,
                                                      ),
                                                    );
                                              },
                                      ),
                                      TextButton.icon(
                                        icon: const Icon(
                                          Icons.people_outline,
                                          size: 16,
                                        ),
                                        label: Text(
                                          'المشتركين (${event.bookings.length})',
                                        ),
                                        onPressed: () =>
                                            _showSubscribers(event),
                                        style: TextButton.styleFrom(
                                          foregroundColor: AppColors.primary,
                                          textStyle: AppTypography.label
                                              .copyWith(
                                                fontWeight: FontWeight.bold,
                                              ),
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
          tooltip: 'جدولة موعد جديد',
          backgroundColor: AppColors.primary,
          onPressed: _showCreateDialog,
          child: const Icon(Icons.add, color: AppColors.background),
        ),
      ),
    );
  }

  Widget _buildLoadWarning(String message) {
    return Semantics(
      liveRegion: true,
      child: Container(
        margin: const EdgeInsetsDirectional.fromSTEB(16, 12, 16, 0),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: AppColors.warning.withValues(alpha: 0.12),
          border: Border.all(color: AppColors.warning),
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          children: [
            const Icon(Icons.warning_amber_rounded, color: AppColors.warning),
            const SizedBox(width: 8),
            Expanded(child: Text('نعرض آخر بيانات متاحة. $message')),
            IconButton(
              tooltip: 'إعادة المحاولة',
              onPressed: _fetchAppointments,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildSummaryCards(List<GroupAppointment> appointments) {
    final totalGroups = appointments.length;
    final totalBookings = appointments.fold<int>(
      0,
      (sum, e) => sum + e.bookings.length,
    );
    final totalCapacity = appointments.fold<int>(
      0,
      (sum, e) => sum + e.capacity,
    );
    final activeGroups = appointments.where((e) => e.isActive).length;

    final cards = [
      _buildSummaryCard(
        label: 'إجمالي الحجوزات',
        value: '$totalBookings',
        icon: Icons.people,
        color: AppColors.primary,
      ),
      _buildSummaryCard(
        label: 'نسبة الإشغال',
        value: totalCapacity > 0
            ? '${((totalBookings / totalCapacity) * 100).toStringAsFixed(0)}%'
            : 'غير متاح',
        icon: Icons.percent,
        color: AppColors.success,
      ),
      _buildSummaryCard(
        label: 'المجموعات النشطة',
        value: '$activeGroups / $totalGroups',
        icon: Icons.calendar_today,
        color: AppColors.secondary,
      ),
    ];
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final scaledLabel = MediaQuery.textScalerOf(context).scale(12);
          final columns = constraints.maxWidth >= 680 && scaledLabel <= 16
              ? 3
              : constraints.maxWidth >= 340 && scaledLabel <= 18
              ? 2
              : 1;
          final cardWidth =
              (constraints.maxWidth - ((columns - 1) * 10)) / columns;
          return Wrap(
            spacing: 10,
            runSpacing: 10,
            children: cards
                .map((card) => SizedBox(width: cardWidth, child: card))
                .toList(),
          );
        },
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
          Text(
            value,
            style: AppTypography.title.copyWith(
              fontWeight: FontWeight.bold,
              fontSize: 15,
              color: color,
            ),
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: AppTypography.label.copyWith(
              fontSize: 12,
              color: AppColors.textMuted,
            ),
            textAlign: TextAlign.right,
          ),
        ],
      ),
    );
  }
}
