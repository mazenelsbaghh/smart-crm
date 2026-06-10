import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/crm/bloc/crm_bloc.dart';
import 'package:mobile_app/features/crm/data/repositories/crm_repository.dart';
import 'package:mobile_app/features/crm/data/models/crm_models.dart';

class MockCrmRepository implements CrmRepository {
  @override
  Future<List<Customer>> getCustomers(String projectId) async {
    return [];
  }

  @override
  Future<Customer> getCustomer(String customerId) async {
    throw UnimplementedError();
  }

  @override
  Future<Customer> updateCustomer(String customerId, Map<String, dynamic> data) async {
    throw UnimplementedError();
  }

  @override
  Future<List<PipelineStage>> getPipelineStages(String projectId) async {
    return [];
  }

  @override
  Future<List<Deal>> getDeals(String projectId) async {
    return [];
  }

  @override
  Future<Deal> createDeal(String projectId, Map<String, dynamic> data) async {
    throw UnimplementedError();
  }

  @override
  Future<Deal> updateDealStage(String dealId, String pipelineStageId) async {
    throw UnimplementedError();
  }

  @override
  Future<Deal> updateDealStatus(String dealId, int status) async {
    throw UnimplementedError();
  }
}

void main() {
  late MockCrmRepository mockCrmRepository;
  late CrmBloc crmBloc;

  setUp(() {
    mockCrmRepository = MockCrmRepository();
    crmBloc = CrmBloc(crmRepository: mockCrmRepository);
  });

  tearDown(() {
    crmBloc.close();
  });

  test('initial state has empty customers, stages, deals, and no error', () {
    expect(crmBloc.state, const CrmState());
  });
}
