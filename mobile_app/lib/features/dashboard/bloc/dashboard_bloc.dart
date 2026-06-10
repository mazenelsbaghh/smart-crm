import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:equatable/equatable.dart';
import '../data/repositories/dashboard_repository.dart';

// Events
abstract class DashboardEvent extends Equatable {
  const DashboardEvent();
  @override
  List<Object?> get props => [];
}

class DashboardLoadRequested extends DashboardEvent {
  final String projectId;
  const DashboardLoadRequested(this.projectId);
  @override
  List<Object?> get props => [projectId];
}

class DashboardRecalculateRequested extends DashboardEvent {
  final String projectId;
  const DashboardRecalculateRequested(this.projectId);
  @override
  List<Object?> get props => [projectId];
}

class DashboardSettingsUpdateRequested extends DashboardEvent {
  final String projectId;
  final Map<String, dynamic> settings;
  const DashboardSettingsUpdateRequested({required this.projectId, required this.settings});
  @override
  List<Object?> get props => [projectId, settings];
}

// States
class DashboardState extends Equatable {
  final List<Map<String, dynamic>> salesData;
  final List<Map<String, dynamic>> aiAccuracyData;
  final bool loading;
  final String? error;
  final bool settingsUpdateSuccess;

  const DashboardState({
    this.salesData = const [],
    this.aiAccuracyData = const [],
    this.loading = false,
    this.error,
    this.settingsUpdateSuccess = false,
  });

  DashboardState copyWith({
    List<Map<String, dynamic>>? salesData,
    List<Map<String, dynamic>>? aiAccuracyData,
    bool? loading,
    String? Function()? error,
    bool? settingsUpdateSuccess,
  }) {
    return DashboardState(
      salesData: salesData ?? this.salesData,
      aiAccuracyData: aiAccuracyData ?? this.aiAccuracyData,
      loading: loading ?? this.loading,
      error: error != null ? error() : this.error,
      settingsUpdateSuccess: settingsUpdateSuccess ?? this.settingsUpdateSuccess,
    );
  }

  @override
  List<Object?> get props => [salesData, aiAccuracyData, loading, error, settingsUpdateSuccess];
}

// BLoC
class DashboardBloc extends Bloc<DashboardEvent, DashboardState> {
  final DashboardRepository _dashboardRepository;

  DashboardBloc({required DashboardRepository dashboardRepository})
      : _dashboardRepository = dashboardRepository,
        super(const DashboardState()) {
    on<DashboardLoadRequested>(_onLoad);
    on<DashboardRecalculateRequested>(_onRecalculate);
    on<DashboardSettingsUpdateRequested>(_onUpdateSettings);
  }

  Future<void> _onLoad(DashboardLoadRequested event, Emitter<DashboardState> emit) async {
    emit(state.copyWith(loading: true, error: () => null));
    try {
      final sales = await _dashboardRepository.getAnalytics(event.projectId, 'Sales');
      final accuracy = await _dashboardRepository.getAnalytics(event.projectId, 'AI_Accuracy');
      emit(state.copyWith(salesData: sales, aiAccuracyData: accuracy, loading: false));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString()));
    }
  }

  Future<void> _onRecalculate(DashboardRecalculateRequested event, Emitter<DashboardState> emit) async {
    emit(state.copyWith(loading: true, error: () => null));
    try {
      await _dashboardRepository.recalculateAnalytics(event.projectId);
      final sales = await _dashboardRepository.getAnalytics(event.projectId, 'Sales');
      final accuracy = await _dashboardRepository.getAnalytics(event.projectId, 'AI_Accuracy');
      emit(state.copyWith(salesData: sales, aiAccuracyData: accuracy, loading: false));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString()));
    }
  }

  Future<void> _onUpdateSettings(DashboardSettingsUpdateRequested event, Emitter<DashboardState> emit) async {
    emit(state.copyWith(loading: true, error: () => null, settingsUpdateSuccess: false));
    try {
      await _dashboardRepository.updateProjectSettings(event.projectId, event.settings);
      emit(state.copyWith(loading: false, settingsUpdateSuccess: true));
    } catch (e) {
      emit(state.copyWith(loading: false, error: () => e.toString(), settingsUpdateSuccess: false));
    }
  }
}
