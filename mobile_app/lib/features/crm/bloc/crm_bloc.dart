import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:equatable/equatable.dart';
import '../data/models/crm_models.dart';
import '../data/repositories/crm_repository.dart';

// Events
abstract class CrmEvent extends Equatable {
  const CrmEvent();
  @override
  List<Object?> get props => [];
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
  const CrmCustomerUpdateRequested({required this.customerId, required this.data});
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
  const CrmDealStageUpdateRequested({required this.dealId, required this.pipelineStageId});
  @override
  List<Object?> get props => [dealId, pipelineStageId];
}

class CrmDealStatusUpdateRequested extends CrmEvent {
  final String dealId;
  final int status;
  const CrmDealStatusUpdateRequested({required this.dealId, required this.status});
  @override
  List<Object?> get props => [dealId, status];
}

class CrmDealCreateRequested extends CrmEvent {
  final String projectId;
  final Map<String, dynamic> data;
  const CrmDealCreateRequested({required this.projectId, required this.data});
  @override
  List<Object?> get props => [projectId, data];
}

// States
class CrmState extends Equatable {
  final List<Customer> customers;
  final List<PipelineStage> stages;
  final List<Deal> deals;
  final bool loadingCustomers;
  final bool loadingPipeline;
  final String? error;

  const CrmState({
    this.customers = const [],
    this.stages = const [],
    this.deals = const [],
    this.loadingCustomers = false,
    this.loadingPipeline = false,
    this.error,
  });

  CrmState copyWith({
    List<Customer>? customers,
    List<PipelineStage>? stages,
    List<Deal>? deals,
    bool? loadingCustomers,
    bool? loadingPipeline,
    String? Function()? error,
  }) {
    return CrmState(
      customers: customers ?? this.customers,
      stages: stages ?? this.stages,
      deals: deals ?? this.deals,
      loadingCustomers: loadingCustomers ?? this.loadingCustomers,
      loadingPipeline: loadingPipeline ?? this.loadingPipeline,
      error: error != null ? error() : this.error,
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
      ];
}

// BLoC
class CrmBloc extends Bloc<CrmEvent, CrmState> {
  final CrmRepository _crmRepository;

  CrmBloc({required CrmRepository crmRepository})
      : _crmRepository = crmRepository,
        super(const CrmState()) {
    on<CrmCustomersFetchRequested>(_onCustomersFetch);
    on<CrmCustomerUpdateRequested>(_onCustomerUpdate);
    on<CrmPipelineFetchRequested>(_onPipelineFetch);
    on<CrmDealStageUpdateRequested>(_onDealStageUpdate);
    on<CrmDealStatusUpdateRequested>(_onDealStatusUpdate);
    on<CrmDealCreateRequested>(_onDealCreate);
  }

  Future<void> _onCustomersFetch(CrmCustomersFetchRequested event, Emitter<CrmState> emit) async {
    emit(state.copyWith(loadingCustomers: true, error: () => null));
    try {
      final list = await _crmRepository.getCustomers(event.projectId);
      emit(state.copyWith(customers: list, loadingCustomers: false));
    } catch (e) {
      emit(state.copyWith(loadingCustomers: false, error: () => e.toString()));
    }
  }

  Future<void> _onCustomerUpdate(CrmCustomerUpdateRequested event, Emitter<CrmState> emit) async {
    try {
      final updated = await _crmRepository.updateCustomer(event.customerId, event.data);
      final list = state.customers.map((c) => c.id == updated.id ? updated : c).toList();
      emit(state.copyWith(customers: list));
    } catch (e) {
      emit(state.copyWith(error: () => e.toString()));
    }
  }

  Future<void> _onPipelineFetch(CrmPipelineFetchRequested event, Emitter<CrmState> emit) async {
    emit(state.copyWith(loadingPipeline: true, error: () => null));
    try {
      final stages = await _crmRepository.getPipelineStages(event.projectId);
      final deals = await _crmRepository.getDeals(event.projectId);
      emit(state.copyWith(stages: stages, deals: deals, loadingPipeline: false));
    } catch (e) {
      emit(state.copyWith(loadingPipeline: false, error: () => e.toString()));
    }
  }

  Future<void> _onDealStageUpdate(CrmDealStageUpdateRequested event, Emitter<CrmState> emit) async {
    try {
      final updated = await _crmRepository.updateDealStage(event.dealId, event.pipelineStageId);
      final list = state.deals.map((d) => d.id == updated.id ? updated : d).toList();
      emit(state.copyWith(deals: list));
    } catch (e) {
      emit(state.copyWith(error: () => e.toString()));
    }
  }

  Future<void> _onDealStatusUpdate(CrmDealStatusUpdateRequested event, Emitter<CrmState> emit) async {
    try {
      final updated = await _crmRepository.updateDealStatus(event.dealId, event.status);
      final list = state.deals.map((d) => d.id == updated.id ? updated : d).toList();
      emit(state.copyWith(deals: list));
    } catch (e) {
      emit(state.copyWith(error: () => e.toString()));
    }
  }

  Future<void> _onDealCreate(CrmDealCreateRequested event, Emitter<CrmState> emit) async {
    try {
      final created = await _crmRepository.createDeal(event.projectId, event.data);
      emit(state.copyWith(deals: [...state.deals, created]));
    } catch (e) {
      emit(state.copyWith(error: () => e.toString()));
    }
  }
}
