import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/user_facing_error.dart';
import '../../crm/data/repositories/crm_repository.dart';
import '../data/repositories/dashboard_repository.dart';

// Events
abstract class DashboardEvent extends Equatable {
  const DashboardEvent();
  @override
  List<Object?> get props => [];
}

class DashboardSessionCleared extends DashboardEvent {
  const DashboardSessionCleared();
}

class DashboardLoadRequested extends DashboardEvent {
  final String projectId;
  const DashboardLoadRequested(this.projectId);
  @override
  List<Object?> get props => [projectId];
}

class DashboardSettingsUpdateRequested extends DashboardEvent {
  final String projectId;
  final Map<String, dynamic> settings;
  const DashboardSettingsUpdateRequested({
    required this.projectId,
    required this.settings,
  });
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
  final bool settingsUpdating;
  final bool settingsUpdateSuccess;
  final String? settingsUpdateError;
  final bool? whatsappConnected;
  final bool hasLoaded;
  final bool salesAvailable;
  final bool aiAccuracyAvailable;
  final bool customersAvailable;
  final bool dealsAvailable;
  final DateTime? lastUpdatedAt;

  const DashboardState({
    this.salesData = const [],
    this.aiAccuracyData = const [],
    this.totalCustomers = 0,
    this.activeDeals = 0,
    this.closedWonRevenue = 0.0,
    this.avgLeadScore = 0,
    this.loading = false,
    this.error,
    this.settingsUpdating = false,
    this.settingsUpdateSuccess = false,
    this.settingsUpdateError,
    this.whatsappConnected,
    this.hasLoaded = false,
    this.salesAvailable = false,
    this.aiAccuracyAvailable = false,
    this.customersAvailable = false,
    this.dealsAvailable = false,
    this.lastUpdatedAt,
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
    bool? settingsUpdating,
    bool? settingsUpdateSuccess,
    String? Function()? settingsUpdateError,
    bool? Function()? whatsappConnected,
    bool? hasLoaded,
    bool? salesAvailable,
    bool? aiAccuracyAvailable,
    bool? customersAvailable,
    bool? dealsAvailable,
    DateTime? lastUpdatedAt,
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
      settingsUpdating: settingsUpdating ?? this.settingsUpdating,
      settingsUpdateSuccess:
          settingsUpdateSuccess ?? this.settingsUpdateSuccess,
      settingsUpdateError: settingsUpdateError != null
          ? settingsUpdateError()
          : this.settingsUpdateError,
      whatsappConnected: whatsappConnected != null
          ? whatsappConnected()
          : this.whatsappConnected,
      hasLoaded: hasLoaded ?? this.hasLoaded,
      salesAvailable: salesAvailable ?? this.salesAvailable,
      aiAccuracyAvailable: aiAccuracyAvailable ?? this.aiAccuracyAvailable,
      customersAvailable: customersAvailable ?? this.customersAvailable,
      dealsAvailable: dealsAvailable ?? this.dealsAvailable,
      lastUpdatedAt: lastUpdatedAt ?? this.lastUpdatedAt,
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
    settingsUpdating,
    settingsUpdateSuccess,
    settingsUpdateError,
    whatsappConnected,
    hasLoaded,
    salesAvailable,
    aiAccuracyAvailable,
    customersAvailable,
    dealsAvailable,
    lastUpdatedAt,
  ];
}

// BLoC
class DashboardBloc extends Bloc<DashboardEvent, DashboardState> {
  final DashboardRepository _dashboardRepository;
  final CrmRepository _crmRepository;
  int _sessionGeneration = 0;
  int _loadGeneration = 0;

  DashboardBloc({
    required DashboardRepository dashboardRepository,
    required CrmRepository crmRepository,
  }) : _dashboardRepository = dashboardRepository,
       _crmRepository = crmRepository,
       super(const DashboardState()) {
    on<DashboardSessionCleared>(_onSessionCleared);
    on<DashboardLoadRequested>(_onLoad);
    on<DashboardSettingsUpdateRequested>(_onUpdateSettings);
  }

  void _onSessionCleared(
    DashboardSessionCleared event,
    Emitter<DashboardState> emit,
  ) {
    _sessionGeneration++;
    _loadGeneration++;
    emit(const DashboardState());
  }

  Future<void> _onLoad(
    DashboardLoadRequested event,
    Emitter<DashboardState> emit,
  ) async {
    final sessionGeneration = _sessionGeneration;
    final loadGeneration = ++_loadGeneration;
    emit(state.copyWith(loading: true, error: () => null));
    try {
      final salesFuture = _capture(
        _dashboardRepository.getAnalytics(event.projectId, 'Sales'),
      );
      final accuracyFuture = _capture(
        _dashboardRepository.getAnalytics(event.projectId, 'AI_Accuracy'),
      );
      final customersFuture = _capture(
        _crmRepository.getCustomers(event.projectId),
      );
      final dealsFuture = _capture(_crmRepository.getDeals(event.projectId));
      final whatsappFuture = _capture(
        _dashboardRepository.getWhatsAppStatus(event.projectId),
      );

      final salesResult = await salesFuture;
      final accuracyResult = await accuracyFuture;
      final customersResult = await customersFuture;
      final dealsResult = await dealsFuture;
      final whatsappResult = await whatsappFuture;
      if (sessionGeneration != _sessionGeneration ||
          loadGeneration != _loadGeneration) {
        return;
      }

      final customers = customersResult.value;
      final deals = dealsResult.value;
      final failures = <String>[
        if (salesResult.error != null) 'المبيعات',
        if (accuracyResult.error != null) 'دقة الرد الآلي',
        if (customersResult.error != null) 'العملاء',
        if (dealsResult.error != null) 'الصفقات',
        if (whatsappResult.error != null) 'اتصال واتساب',
      ];
      final anySuccess = [
        salesResult,
        accuracyResult,
        customersResult,
        dealsResult,
        whatsappResult,
      ].any((result) => result.error == null);

      final totalCustomers = customers?.length ?? state.totalCustomers;
      final activeDeals = deals == null
          ? state.activeDeals
          : deals.where((deal) => deal.status == 0).length;
      final closedWonRevenue = deals == null
          ? state.closedWonRevenue
          : deals
                .where((deal) => deal.status == 1)
                .fold<double>(0, (sum, deal) => sum + deal.amount);
      final avgLeadScore = customers == null
          ? state.avgLeadScore
          : customers.isEmpty
          ? 0
          : (customers.fold<int>(0, (sum, c) => sum + c.leadScore) /
                    customers.length)
                .round();

      emit(
        state.copyWith(
          salesData: salesResult.value ?? state.salesData,
          aiAccuracyData: accuracyResult.value ?? state.aiAccuracyData,
          totalCustomers: totalCustomers,
          activeDeals: activeDeals,
          closedWonRevenue: closedWonRevenue,
          avgLeadScore: avgLeadScore,
          whatsappConnected: () => whatsappResult.error == null
              ? whatsappResult.value
              : state.whatsappConnected,
          loading: false,
          hasLoaded: state.hasLoaded || anySuccess,
          salesAvailable: salesResult.error == null || state.salesAvailable,
          aiAccuracyAvailable:
              accuracyResult.error == null || state.aiAccuracyAvailable,
          customersAvailable:
              customersResult.error == null || state.customersAvailable,
          dealsAvailable: dealsResult.error == null || state.dealsAvailable,
          lastUpdatedAt: anySuccess ? DateTime.now() : state.lastUpdatedAt,
          error: () =>
              failures.isEmpty ? null : 'تعذر تحديث: ${failures.join('، ')}.',
          settingsUpdateSuccess: false,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration ||
          loadGeneration != _loadGeneration) {
        return;
      }
      emit(
        state.copyWith(
          loading: false,
          error: () => userFacingError(e),
          settingsUpdateSuccess: false,
        ),
      );
    }
  }

  Future<_LoadResult<T>> _capture<T>(Future<T> future) async {
    try {
      return _LoadResult(value: await future);
    } catch (error) {
      return _LoadResult(error: error);
    }
  }

  Future<void> _onUpdateSettings(
    DashboardSettingsUpdateRequested event,
    Emitter<DashboardState> emit,
  ) async {
    final sessionGeneration = _sessionGeneration;
    emit(
      state.copyWith(
        settingsUpdating: true,
        settingsUpdateSuccess: false,
        settingsUpdateError: () => null,
      ),
    );
    try {
      await _dashboardRepository.updateProjectSettings(
        event.projectId,
        event.settings,
      );
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          settingsUpdating: false,
          settingsUpdateSuccess: true,
          settingsUpdateError: () => null,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          settingsUpdating: false,
          settingsUpdateSuccess: false,
          settingsUpdateError: () => userFacingError(e),
        ),
      );
    }
  }
}

class _LoadResult<T> {
  const _LoadResult({this.value, this.error});

  final T? value;
  final Object? error;
}
