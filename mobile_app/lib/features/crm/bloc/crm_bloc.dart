import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/user_facing_error.dart';
import '../data/models/crm_models.dart';
import '../data/repositories/crm_repository.dart';

// Events
abstract class CrmEvent extends Equatable {
  const CrmEvent();
  @override
  List<Object?> get props => [];
}

class CrmSessionCleared extends CrmEvent {
  const CrmSessionCleared();
}

class CrmCustomersFetchRequested extends CrmEvent {
  final String projectId;
  const CrmCustomersFetchRequested(this.projectId);
  @override
  List<Object?> get props => [projectId];
}

class CrmCustomerUpdateRequested extends CrmEvent {
  final String customerId;
  final Map<String, dynamic> data;
  const CrmCustomerUpdateRequested({
    required this.customerId,
    required this.data,
  });
  @override
  List<Object?> get props => [customerId, data];
}

class CrmPipelineFetchRequested extends CrmEvent {
  final String projectId;
  const CrmPipelineFetchRequested(this.projectId);
  @override
  List<Object?> get props => [projectId];
}

class CrmDealStageUpdateRequested extends CrmEvent {
  final String dealId;
  final String pipelineStageId;
  const CrmDealStageUpdateRequested({
    required this.dealId,
    required this.pipelineStageId,
  });
  @override
  List<Object?> get props => [dealId, pipelineStageId];
}

// States
class CrmState extends Equatable {
  final List<Customer> customers;
  final List<PipelineStage> stages;
  final List<Deal> deals;
  final bool loadingCustomers;
  final bool loadingPipeline;
  final String? error;
  final bool customerSaving;
  final int customerSaveRevision;
  final String? customerSaveError;
  final Set<String> dealMutationsInProgress;
  final String? dealMutationError;

  const CrmState({
    this.customers = const [],
    this.stages = const [],
    this.deals = const [],
    this.loadingCustomers = false,
    this.loadingPipeline = false,
    this.error,
    this.customerSaving = false,
    this.customerSaveRevision = 0,
    this.customerSaveError,
    this.dealMutationsInProgress = const {},
    this.dealMutationError,
  });

  CrmState copyWith({
    List<Customer>? customers,
    List<PipelineStage>? stages,
    List<Deal>? deals,
    bool? loadingCustomers,
    bool? loadingPipeline,
    String? Function()? error,
    bool? customerSaving,
    int? customerSaveRevision,
    String? Function()? customerSaveError,
    Set<String>? dealMutationsInProgress,
    String? Function()? dealMutationError,
  }) {
    return CrmState(
      customers: customers ?? this.customers,
      stages: stages ?? this.stages,
      deals: deals ?? this.deals,
      loadingCustomers: loadingCustomers ?? this.loadingCustomers,
      loadingPipeline: loadingPipeline ?? this.loadingPipeline,
      error: error != null ? error() : this.error,
      customerSaving: customerSaving ?? this.customerSaving,
      customerSaveRevision: customerSaveRevision ?? this.customerSaveRevision,
      customerSaveError: customerSaveError != null
          ? customerSaveError()
          : this.customerSaveError,
      dealMutationsInProgress:
          dealMutationsInProgress ?? this.dealMutationsInProgress,
      dealMutationError: dealMutationError != null
          ? dealMutationError()
          : this.dealMutationError,
    );
  }

  @override
  List<Object?> get props => [
    customers,
    stages,
    deals,
    loadingCustomers,
    loadingPipeline,
    error,
    customerSaving,
    customerSaveRevision,
    customerSaveError,
    dealMutationsInProgress,
    dealMutationError,
  ];
}

// BLoC
class CrmBloc extends Bloc<CrmEvent, CrmState> {
  final CrmRepository _crmRepository;
  int _sessionGeneration = 0;

  CrmBloc({required CrmRepository crmRepository})
    : _crmRepository = crmRepository,
      super(const CrmState()) {
    on<CrmSessionCleared>(_onSessionCleared);
    on<CrmCustomersFetchRequested>(_onCustomersFetch);
    on<CrmCustomerUpdateRequested>(_onCustomerUpdate);
    on<CrmPipelineFetchRequested>(_onPipelineFetch);
    on<CrmDealStageUpdateRequested>(_onDealStageUpdate);
  }

  void _onSessionCleared(CrmSessionCleared event, Emitter<CrmState> emit) {
    _sessionGeneration++;
    emit(const CrmState());
  }

  Future<void> _onCustomersFetch(
    CrmCustomersFetchRequested event,
    Emitter<CrmState> emit,
  ) async {
    final sessionGeneration = _sessionGeneration;
    emit(state.copyWith(loadingCustomers: true, error: () => null));
    try {
      final list = await _crmRepository.getCustomers(event.projectId);
      if (sessionGeneration != _sessionGeneration) return;
      emit(state.copyWith(customers: list, loadingCustomers: false));
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          loadingCustomers: false,
          error: () => userFacingError(e),
        ),
      );
    }
  }

  Future<void> _onCustomerUpdate(
    CrmCustomerUpdateRequested event,
    Emitter<CrmState> emit,
  ) async {
    if (state.customerSaving) return;
    final sessionGeneration = _sessionGeneration;
    emit(state.copyWith(customerSaving: true, customerSaveError: () => null));
    try {
      final updated = await _crmRepository.updateCustomer(
        event.customerId,
        event.data,
      );
      if (sessionGeneration != _sessionGeneration) return;
      final list = state.customers
          .map((c) => c.id == updated.id ? updated : c)
          .toList();
      emit(
        state.copyWith(
          customers: list,
          customerSaving: false,
          customerSaveRevision: state.customerSaveRevision + 1,
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          customerSaving: false,
          customerSaveError: () => userFacingError(e),
        ),
      );
    }
  }

  Future<void> _onPipelineFetch(
    CrmPipelineFetchRequested event,
    Emitter<CrmState> emit,
  ) async {
    if (state.loadingPipeline || state.dealMutationsInProgress.isNotEmpty) {
      return;
    }
    final sessionGeneration = _sessionGeneration;
    emit(state.copyWith(loadingPipeline: true, error: () => null));
    try {
      final pipelinePayloads = await Future.wait<Object>([
        _crmRepository.getPipelineStages(event.projectId),
        _crmRepository.getDeals(event.projectId),
      ]);
      final stages = pipelinePayloads[0] as List<PipelineStage>;
      final deals = pipelinePayloads[1] as List<Deal>;
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(stages: stages, deals: deals, loadingPipeline: false),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(loadingPipeline: false, error: () => userFacingError(e)),
      );
    }
  }

  Future<void> _onDealStageUpdate(
    CrmDealStageUpdateRequested event,
    Emitter<CrmState> emit,
  ) async {
    if (state.loadingPipeline ||
        state.dealMutationsInProgress.contains(event.dealId)) {
      return;
    }
    final original = state.deals
        .where((deal) => deal.id == event.dealId)
        .firstOrNull;
    if (original == null || original.pipelineStageId == event.pipelineStageId) {
      return;
    }
    final sessionGeneration = _sessionGeneration;
    final pending = {...state.dealMutationsInProgress, event.dealId};
    emit(
      state.copyWith(
        deals: state.deals
            .map(
              (deal) => deal.id == event.dealId
                  ? deal.copyWith(pipelineStageId: event.pipelineStageId)
                  : deal,
            )
            .toList(),
        dealMutationsInProgress: pending,
        dealMutationError: () => null,
      ),
    );
    try {
      final updated = await _crmRepository.updateDealStage(
        event.dealId,
        event.pipelineStageId,
      );
      if (sessionGeneration != _sessionGeneration) return;
      final list = state.deals
          .map((d) => d.id == updated.id ? updated : d)
          .toList();
      emit(
        state.copyWith(
          deals: list,
          dealMutationsInProgress: {...state.dealMutationsInProgress}
            ..remove(event.dealId),
        ),
      );
    } catch (e) {
      if (sessionGeneration != _sessionGeneration) return;
      emit(
        state.copyWith(
          deals: state.deals
              .map((deal) => deal.id == original.id ? original : deal)
              .toList(),
          dealMutationsInProgress: {...state.dealMutationsInProgress}
            ..remove(event.dealId),
          dealMutationError: () => userFacingError(e),
        ),
      );
    }
  }
}
