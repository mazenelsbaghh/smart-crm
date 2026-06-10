import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:equatable/equatable.dart';
import '../data/models/appointment_model.dart';
import '../data/repositories/bookings_repository.dart';

// Events
abstract class BookingsEvent extends Equatable {
  const BookingsEvent();
  @override
  List<Object?> get props => [];
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

class BookingsBookingRequested extends BookingsEvent {
  final Map<String, dynamic> data;
  const BookingsBookingRequested(this.data);
  @override
  List<Object?> get props => [data];
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

  const BookingsState({
    this.appointments = const [],
    this.loading = false,
    this.error,
  });

  BookingsState copyWith({
    List<GroupAppointment>? appointments,
    bool? loading,
    String? Function()? error,
  }) {
    return BookingsState(
      appointments: appointments ?? this.appointments,
      loading: loading ?? this.loading,
      error: error != null ? error() : this.error,
    );
  }

  @override
  List<Object?> get props => [appointments, loading, error];
}

// BLoC
class BookingsBloc extends Bloc<BookingsEvent, BookingsState> {
  final BookingsRepository _bookingsRepository;

  BookingsBloc({required BookingsRepository bookingsRepository})
      : _bookingsRepository = bookingsRepository,
        super(const BookingsState()) {
    on<BookingsFetchRequested>(_onFetch);
    on<BookingsCreateRequested>(_onCreate);
    on<BookingsDeleteRequested>(_onDelete);
    on<BookingsBookingRequested>(_onBook);
    on<BookingsBookingCancelRequested>(_onCancelBooking);
    on<BookingsToggleRequested>(_onToggle);
  }

  Future<void> _onFetch(BookingsFetchRequested event, Emitter<BookingsState> emit) async {
    emit(state.copyWith(loading: true, error: () => null));
    try {
      final list = await _bookingsRepository.getAppointments();
      emit(state.copyWith(appointments: list, loading: false));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString()));
    }
  }

  Future<void> _onCreate(BookingsCreateRequested event, Emitter<BookingsState> emit) async {
    emit(state.copyWith(loading: true, error: () => null));
    try {
      final created = await _bookingsRepository.createAppointment(event.data);
      emit(state.copyWith(
        appointments: [...state.appointments, created],
        loading: false,
      ));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString()));
    }
  }

  Future<void> _onDelete(BookingsDeleteRequested event, Emitter<BookingsState> emit) async {
    emit(state.copyWith(loading: true, error: () => null));
    try {
      await _bookingsRepository.deleteAppointment(event.id);
      final list = state.appointments.where((a) => a.id != event.id).toList();
      emit(state.copyWith(appointments: list, loading: false));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString()));
    }
  }

  Future<void> _onBook(BookingsBookingRequested event, Emitter<BookingsState> emit) async {
    emit(state.copyWith(loading: true, error: () => null));
    try {
      await _bookingsRepository.bookAppointment(event.data);
      final list = await _bookingsRepository.getAppointments();
      emit(state.copyWith(appointments: list, loading: false));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString()));
    }
  }

  Future<void> _onCancelBooking(BookingsBookingCancelRequested event, Emitter<BookingsState> emit) async {
    emit(state.copyWith(loading: true, error: () => null));
    try {
      await _bookingsRepository.cancelBooking(event.bookingId);
      final list = await _bookingsRepository.getAppointments();
      emit(state.copyWith(appointments: list, loading: false));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString()));
    }
  }

  Future<void> _onToggle(BookingsToggleRequested event, Emitter<BookingsState> emit) async {
    final originalAppointments = state.appointments;

    // Optimistically toggle the isActive status of the clicked group
    final updatedList = state.appointments.map((a) {
      if (a.id == event.id) {
        return GroupAppointment(
          id: a.id,
          projectId: a.projectId,
          name: a.name,
          dateTime: a.dateTime,
          capacity: a.capacity,
          isActive: !a.isActive,
          days: a.days,
          mode: a.mode,
          bookings: a.bookings,
        );
      }
      return a;
    }).toList();

    emit(state.copyWith(appointments: updatedList));

    try {
      await _bookingsRepository.toggleAppointment(event.id);
      final list = await _bookingsRepository.getAppointments();
      emit(state.copyWith(appointments: list));
    } catch (e) {
      emit(state.copyWith(appointments: originalAppointments, error: () => e.toString()));
    }
  }
}
