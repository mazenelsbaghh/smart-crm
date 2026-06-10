import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:equatable/equatable.dart';
import '../data/repositories/dashboard_repository.dart';
import '../../crm/data/repositories/crm_repository.dart';

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
  final int totalCustomers;
  final int activeDeals;
  final double closedWonRevenue;
  final int avgLeadScore;
  final bool loading;
  final String? error;
  final bool settingsUpdateSuccess;
  final bool whatsappConnected;

  const DashboardState({
    this.salesData = const [],
    this.aiAccuracyData = const [],
    this.totalCustomers = 0,
    this.activeDeals = 0,
    this.closedWonRevenue = 0.0,
    this.avgLeadScore = 0,
    this.loading = false,
    this.error,
    this.settingsUpdateSuccess = false,
    this.whatsappConnected = false,
  });

  DashboardState copyWith({
    List<Map<String, dynamic>>? salesData,
    List<Map<String, dynamic>>? aiAccuracyData,
    int? totalCustomers,
    int? activeDeals,
    double? closedWonRevenue,
    int? avgLeadScore,
    bool? loading,
    String? Function()? error,
    bool? settingsUpdateSuccess,
    bool? whatsappConnected,
  }) {
    return DashboardState(
      salesData: salesData ?? this.salesData,
      aiAccuracyData: aiAccuracyData ?? this.aiAccuracyData,
      totalCustomers: totalCustomers ?? this.totalCustomers,
      activeDeals: activeDeals ?? this.activeDeals,
      closedWonRevenue: closedWonRevenue ?? this.closedWonRevenue,
      avgLeadScore: avgLeadScore ?? this.avgLeadScore,
      loading: loading ?? this.loading,
      error: error != null ? error() : this.error,
      settingsUpdateSuccess: settingsUpdateSuccess ?? this.settingsUpdateSuccess,
      whatsappConnected: whatsappConnected ?? this.whatsappConnected,
    );
  }

  @override
  List<Object?> get props => [
        salesData,
        aiAccuracyData,
        totalCustomers,
        activeDeals,
        closedWonRevenue,
        avgLeadScore,
        loading,
        error,
        settingsUpdateSuccess,
        whatsappConnected,
      ];
}

// BLoC
class DashboardBloc extends Bloc<DashboardEvent, DashboardState> {
  final DashboardRepository _dashboardRepository;
  final CrmRepository _crmRepository;

  DashboardBloc({
    required DashboardRepository dashboardRepository,
    required CrmRepository crmRepository,
  })  : _dashboardRepository = dashboardRepository,
        _crmRepository = crmRepository,
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
      
      final customers = await _crmRepository.getCustomers(event.projectId);
      final deals = await _crmRepository.getDeals(event.projectId);
      final whatsappConnected = await _dashboardRepository.getWhatsAppStatus(event.projectId);

      final totalCustomers = customers.length;
      final activeDeals = deals.where((d) => d.status == 0).length;
      final closedWonRevenue = deals.where((d) => d.status == 1).fold<double>(0.0, (sum, d) => sum + d.amount);
      final avgLeadScore = customers.isEmpty
          ? 0
          : (customers.fold<int>(0, (sum, c) => sum + c.leadScore) / totalCustomers).round();

      emit(state.copyWith(
        salesData: sales,
        aiAccuracyData: accuracy,
        totalCustomers: totalCustomers,
        activeDeals: activeDeals,
        closedWonRevenue: closedWonRevenue,
        avgLeadScore: avgLeadScore,
        whatsappConnected: whatsappConnected,
        loading: false,
      ));
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
      
      final customers = await _crmRepository.getCustomers(event.projectId);
      final deals = await _crmRepository.getDeals(event.projectId);
      final whatsappConnected = await _dashboardRepository.getWhatsAppStatus(event.projectId);

      final totalCustomers = customers.length;
      final activeDeals = deals.where((d) => d.status == 0).length;
      final closedWonRevenue = deals.where((d) => d.status == 1).fold<double>(0.0, (sum, d) => sum + d.amount);
      final avgLeadScore = customers.isEmpty
          ? 0
          : (customers.fold<int>(0, (sum, c) => sum + c.leadScore) / totalCustomers).round();

      emit(state.copyWith(
        salesData: sales,
        aiAccuracyData: accuracy,
        totalCustomers: totalCustomers,
        activeDeals: activeDeals,
        closedWonRevenue: closedWonRevenue,
        avgLeadScore: avgLeadScore,
        whatsappConnected: whatsappConnected,
        loading: false,
      ));
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
