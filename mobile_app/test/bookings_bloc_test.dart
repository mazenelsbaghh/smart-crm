import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/bookings/bloc/bookings_bloc.dart';
import 'package:mobile_app/features/bookings/data/models/appointment_model.dart';
import 'package:mobile_app/features/bookings/data/repositories/bookings_repository.dart';

class FakeBookingsRepository implements BookingsRepository {
  FakeBookingsRepository(this.appointments);

  List<GroupAppointment> appointments;
  final Set<String> cancelledBookings = {};
  Completer<void>? toggleGate;
  bool failToggle = false;

  @override
  Future<List<GroupAppointment>> getAppointments() async => appointments;

  @override
  Future<void> cancelBooking(String bookingId) async {
    cancelledBookings.add(bookingId);
  }

  @override
  Future<void> toggleAppointment(String id) async {
    await toggleGate?.future;
    if (failToggle) throw StateError('network unavailable');
  }

  @override
  Future<GroupAppointment> createAppointment(Map<String, dynamic> data) =>
      throw UnimplementedError();

  @override
  Future<void> deleteAppointment(String id) async {}
}

final _booking = GroupAppointmentBooking(
  id: 'booking-1',
  projectId: 'project-1',
  groupAppointmentId: 'appointment-1',
  customerId: 'customer-1',
  customerName: 'عميل',
  customerPhone: '01000000000',
);

final _appointment = GroupAppointment(
  id: 'appointment-1',
  projectId: 'project-1',
  name: 'جلسة اختبار',
  dateTime: DateTime.utc(2026, 8, 26, 18),
  capacity: 10,
  isActive: true,
  days: 'الأربعاء',
  mode: 'offline',
  bookings: [_booking],
);

Future<void> _fetch(BookingsBloc bloc) async {
  final loaded = bloc.stream.firstWhere(
    (state) => !state.loading && state.appointments.isNotEmpty,
  );
  bloc.add(BookingsFetchRequested());
  await loaded;
}

void main() {
  late FakeBookingsRepository repository;
  late BookingsBloc bloc;

  setUp(() {
    repository = FakeBookingsRepository([_appointment]);
    bloc = BookingsBloc(bookingsRepository: repository);
  });

  tearDown(() => bloc.close());

  test('confirmed cancellation removes only the persisted booking', () async {
    await _fetch(bloc);
    final completed = bloc.stream.firstWhere(
      (state) =>
          state.mutationRevision == 1 &&
          state.lastMutationType == 'cancelBooking',
    );

    bloc.add(const BookingsBookingCancelRequested('booking-1'));
    final state = await completed;

    expect(repository.cancelledBookings, contains('booking-1'));
    expect(state.appointments.single.bookings, isEmpty);
  });

  test('failed activation change restores the server-backed value', () async {
    await _fetch(bloc);
    repository
      ..toggleGate = Completer<void>()
      ..failToggle = true;

    final optimistic = bloc.stream.firstWhere(
      (state) => state.appointments.single.isActive == false,
    );
    bloc.add(const BookingsToggleRequested('appointment-1'));
    await optimistic;

    final rolledBack = bloc.stream.firstWhere(
      (state) =>
          state.appointments.single.isActive && state.mutationError != null,
    );
    repository.toggleGate!.complete();
    final state = await rolledBack;

    expect(state.mutationsInProgress, isEmpty);
    expect(state.mutationError, isNot(contains('network unavailable')));
  });
}
