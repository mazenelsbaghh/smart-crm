import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/user_facing_error.dart';
import '../data/models/appointment_model.dart';
import '../data/repositories/bookings_repository.dart';

// Events
abstract class BookingsEvent extends Equatable {
  const BookingsEvent();
  @override
  List<Object?> get props => [];
}

class BookingsSessionCleared extends BookingsEvent {
  const BookingsSessionCleared();
}

class BookingsFetchRequested extends BookingsEvent {}

class BookingsCreateRequested extends BookingsEvent {
  final Map<String, dynamic> data;
  const BookingsCreateRequested(this.data);
  @override
  List<Object?> get props => [data];
}

class BookingsDeleteRequested extends BookingsEvent {
  final String id;
  const BookingsDeleteRequested(this.id);
  @override
  List<Object?> get props => [id];
}

class BookingsBookingCancelRequested extends BookingsEvent {
  final String bookingId;
  const BookingsBookingCancelRequested(this.bookingId);
  @override
  List<Object?> get props => [bookingId];
}

class BookingsToggleRequested extends BookingsEvent {
  final String id;
  const BookingsToggleRequested(this.id);
  @override
  List<Object?> get props => [id];
}

// States
class BookingsState extends Equatable {
  final List<GroupAppointment> appointments;
  final bool loading;
  final String? error;
  final bool creating;
  final int createRevision;
  final String? createError;
  final Set<String> mutationsInProgress;
  final int mutationRevision;
  final String? mutationError;
  final String? lastMutationType;
  final String? lastMutationId;

  const BookingsState({
    this.appointments = const [],
    this.loading = false,
    this.error,
    this.creating = false,
    this.createRevision = 0,
    this.createError,
    this.mutationsInProgress = const {},
    this.mutationRevision = 0,
    this.mutationError,
    this.lastMutationType,
    this.lastMutationId,
  });

  BookingsState copyWith({
    List<GroupAppointment>? appointments,
    bool? loading,
    String? Function()? error,
    bool? creating,
    int? createRevision,
    String? Function()? createError,
    Set<String>? mutationsInProgress,
    int? mutationRevision,
    String? Function()? mutationError,
    String? Function()? lastMutationType,
    String? Function()? lastMutationId,
  }) {
    return BookingsState(
      appointments: appointments ?? this.appointments,
      loading: loading ?? this.loading,
      error: error != null ? error() : this.error,
      creating: creating ?? this.creating,
      createRevision: createRevision ?? this.createRevision,
      createError: createError != null ? createError() : this.createError,
      mutationsInProgress: mutationsInProgress ?? this.mutationsInProgress,
      mutationRevision: mutationRevision ?? this.mutationRevision,
      mutationError: mutationError != null
          ? mutationError()
          : this.mutationError,
      lastMutationType: lastMutationType != null
          ? lastMutationType()
          : this.lastMutationType,
      lastMutationId: lastMutationId != null
          ? lastMutationId()
          : this.lastMutationId,
    );
  }

  @override
  List<Object?> get props => [
    appointments,
    loading,
    error,
    creating,
    createRevision,
    createError,
    mutationsInProgress,
    mutationRevision,
    mutationError,
    lastMutationType,
    lastMutationId,
  ];
}

// BLoC
class BookingsBloc extends Bloc<BookingsEvent, BookingsState> {
  final BookingsRepository _bookingsRepository;
  int _sessionGeneration = 0;

  BookingsBloc({required BookingsRepository bookingsRepository})
    : _bookingsRepository = bookingsRepository,
      super(const BookingsState()) {
    on<BookingsSessionCleared>(_onSessionCleared);
    on<BookingsFetchRequested>(_onFetch);
    on<BookingsCreateRequested>(_onCreate);
    on<BookingsDeleteRequested>(_onDelete);
    on<BookingsBookingCancelRequested>(_onCancelBooking);
    on<BookingsToggleRequested>(_onToggle);
  }

  void _onSessionCleared(
    BookingsSessionCleared event,
    Emitter<BookingsState> emit,
  ) {
    _sessionGeneration++;
    emit(const BookingsState());
  }

  Future<void> _onFetch(
    BookingsFetchRequested event,
    Emitter<BookingsState> emit,
  ) async {
    if (state.loading ||
        state.creating ||
        state.mutationsInProgress.isNotEmpty) {
      return;
    }
    final sessionGeneration = _sessionGeneration;
    emit(state.copyWith(loading: true, error: () => null));
    try {
      final list = await _bookingsRepository.getAppointments();
      if (sessionGeneration != _sessionGeneration) return;
      emit(state.copyWith(appointments: list, loading: false));
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(state.copyWith(loading: false, error: () => userFacingError(e)));
    }
  }

  Future<void> _onCreate(
    BookingsCreateRequested event,
    Emitter<BookingsState> emit,
  ) async {
    if (state.creating) return;
    final sessionGeneration = _sessionGeneration;
    emit(state.copyWith(creating: true, createError: () => null));
    try {
      final created = await _bookingsRepository.createAppointment(event.data);
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          appointments: [...state.appointments, created],
          creating: false,
          createRevision: state.createRevision + 1,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(creating: false, createError: () => userFacingError(e)),
      );
    }
  }

  Future<void> _onDelete(
    BookingsDeleteRequested event,
    Emitter<BookingsState> emit,
  ) async {
    if (state.mutationsInProgress.contains(event.id)) return;
    final sessionGeneration = _sessionGeneration;
    emit(
      state.copyWith(
        mutationsInProgress: {...state.mutationsInProgress, event.id},
        mutationError: () => null,
      ),
    );
    try {
      await _bookingsRepository.deleteAppointment(event.id);
      if (sessionGeneration != _sessionGeneration) return;
      final list = state.appointments.where((a) => a.id != event.id).toList();
      emit(
        state.copyWith(
          appointments: list,
          mutationsInProgress: {...state.mutationsInProgress}..remove(event.id),
          mutationRevision: state.mutationRevision + 1,
          lastMutationType: () => 'delete',
          lastMutationId: () => event.id,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          mutationsInProgress: {...state.mutationsInProgress}..remove(event.id),
          mutationError: () => userFacingError(e),
        ),
      );
    }
  }

  Future<void> _onCancelBooking(
    BookingsBookingCancelRequested event,
    Emitter<BookingsState> emit,
  ) async {
    if (state.mutationsInProgress.contains(event.bookingId)) return;
    final sessionGeneration = _sessionGeneration;
    emit(
      state.copyWith(
        mutationsInProgress: {...state.mutationsInProgress, event.bookingId},
        mutationError: () => null,
      ),
    );
    try {
      await _bookingsRepository.cancelBooking(event.bookingId);
      if (sessionGeneration != _sessionGeneration) return;
      final list = state.appointments
          .map(
            (appointment) => appointment.copyWith(
              bookings: appointment.bookings
                  .where((booking) => booking.id != event.bookingId)
                  .toList(),
            ),
          )
          .toList();
      emit(
        state.copyWith(
          appointments: list,
          mutationsInProgress: {...state.mutationsInProgress}
            ..remove(event.bookingId),
          mutationRevision: state.mutationRevision + 1,
          lastMutationType: () => 'cancelBooking',
          lastMutationId: () => event.bookingId,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          mutationsInProgress: {...state.mutationsInProgress}
            ..remove(event.bookingId),
          mutationError: () => userFacingError(e),
        ),
      );
    }
  }

  Future<void> _onToggle(
    BookingsToggleRequested event,
    Emitter<BookingsState> emit,
  ) async {
    if (state.mutationsInProgress.contains(event.id)) return;
    final sessionGeneration = _sessionGeneration;
    final originalAppointment = state.appointments
        .where((appointment) => appointment.id == event.id)
        .firstOrNull;
    if (originalAppointment == null) return;
    final requestedActiveState = !originalAppointment.isActive;

    final updatedList = state.appointments.map((a) {
      if (a.id == event.id) {
        return a.copyWith(isActive: requestedActiveState);
      }
      return a;
    }).toList();

    emit(
      state.copyWith(
        appointments: updatedList,
        mutationsInProgress: {...state.mutationsInProgress, event.id},
        mutationError: () => null,
      ),
    );

    try {
      await _bookingsRepository.toggleAppointment(event.id);
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          appointments: state.appointments
              .map(
                (appointment) => appointment.id == event.id
                    ? appointment.copyWith(isActive: requestedActiveState)
                    : appointment,
              )
              .toList(),
          mutationsInProgress: {...state.mutationsInProgress}..remove(event.id),
          mutationRevision: state.mutationRevision + 1,
          lastMutationType: () => 'toggle',
          lastMutationId: () => event.id,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          appointments: state.appointments
              .map(
                (appointment) => appointment.id == event.id
                    ? originalAppointment
                    : appointment,
              )
              .toList(),
          mutationsInProgress: {...state.mutationsInProgress}..remove(event.id),
          mutationError: () => userFacingError(e),
        ),
      );
    }
  }
}
