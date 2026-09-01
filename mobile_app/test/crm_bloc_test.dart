import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/crm/bloc/crm_bloc.dart';
import 'package:mobile_app/features/crm/data/models/crm_models.dart';
import 'package:mobile_app/features/crm/data/repositories/crm_repository.dart';

class FakeCrmRepository implements CrmRepository {
  final customer = Customer(
    id: 'customer-1',
    projectId: 'project-1',
    phoneNumber: '01000000000',
    name: 'الاسم الأصلي',
    city: 'القاهرة',
    leadScore: 70,
    tags: const [],
    notes: '',
    interests: const [],
    isBlacklisted: false,
  );
  final stage = PipelineStage(
    id: 'stage-1',
    projectId: 'project-1',
    name: 'جديدة',
    order: 1,
  );
  late final deal = Deal(
    id: 'deal-1',
    projectId: 'project-1',
    customerId: customer.id,
    title: 'صفقة',
    amount: 1000,
    pipelineStageId: stage.id,
    status: 0,
  );

  Completer<void>? customerGate;
  Completer<void>? dealGate;
  bool failCustomerUpdate = false;
  bool failDealUpdate = false;

  @override
  Future<List<Customer>> getCustomers(String projectId) async => [customer];

  @override
  Future<Customer> getCustomer(String customerId) async => customer;

  @override
  Future<Customer> updateCustomer(
    String customerId,
    Map<String, dynamic> data,
  ) async {
    await customerGate?.future;
    if (failCustomerUpdate) throw StateError('network unavailable');
    return customer;
  }

  @override
  Future<List<PipelineStage>> getPipelineStages(String projectId) async => [
    stage,
  ];

  @override
  Future<List<Deal>> getDeals(String projectId) async => [deal];

  @override
  Future<Deal> updateDealStage(String dealId, String pipelineStageId) async {
    await dealGate?.future;
    if (failDealUpdate) throw StateError('network unavailable');
    return deal.copyWith(pipelineStageId: pipelineStageId);
  }
}

void main() {
  late FakeCrmRepository repository;
  late CrmBloc bloc;

  setUp(() {
    repository = FakeCrmRepository();
    bloc = CrmBloc(crmRepository: repository);
  });

  tearDown(() => bloc.close());

  test('failed customer save does not publish a success revision', () async {
    final loaded = bloc.stream.firstWhere(
      (state) => !state.loadingCustomers && state.customers.isNotEmpty,
    );
    bloc.add(const CrmCustomersFetchRequested('project-1'));
    await loaded;
    repository
      ..customerGate = Completer<void>()
      ..failCustomerUpdate = true;

    final saving = bloc.stream.firstWhere((state) => state.customerSaving);
    bloc.add(
      const CrmCustomerUpdateRequested(
        customerId: 'customer-1',
        data: {'name': 'اسم جديد'},
      ),
    );
    await saving;

    final failed = bloc.stream.firstWhere(
      (state) => !state.customerSaving && state.customerSaveError != null,
    );
    repository.customerGate!.complete();
    final state = await failed;

    expect(state.customerSaveRevision, 0);
    expect(state.customers.single.name, 'الاسم الأصلي');
    expect(state.customerSaveError, isNot(contains('network unavailable')));
  });

  test('failed deal move restores its original pipeline stage', () async {
    final loaded = bloc.stream.firstWhere(
      (state) => !state.loadingPipeline && state.deals.isNotEmpty,
    );
    bloc.add(const CrmPipelineFetchRequested('project-1'));
    await loaded;
    repository
      ..dealGate = Completer<void>()
      ..failDealUpdate = true;

    final optimistic = bloc.stream.firstWhere(
      (state) => state.deals.single.pipelineStageId == 'stage-2',
    );
    bloc.add(
      const CrmDealStageUpdateRequested(
        dealId: 'deal-1',
        pipelineStageId: 'stage-2',
      ),
    );
    await optimistic;

    final rolledBack = bloc.stream.firstWhere(
      (state) =>
          state.deals.single.pipelineStageId == 'stage-1' &&
          state.dealMutationError != null,
    );
    repository.dealGate!.complete();
    final state = await rolledBack;

    expect(state.dealMutationsInProgress, isEmpty);
    expect(state.dealMutationError, isNot(contains('network unavailable')));
  });
}
