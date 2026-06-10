import '../../../../core/services/api_client.dart';
import '../models/crm_models.dart';

class CrmRepository {
  final ApiClient _apiClient;

  CrmRepository({required ApiClient apiClient}) : _apiClient = apiClient;

  Future<List<Customer>> getCustomers(String projectId) async {
    final response = await _apiClient.dio.get('/api/projects/$projectId/customers');
    final List list = response.data ?? [];
    return list.map((item) => Customer.fromJson(item)).toList();
  }

  Future<Customer> getCustomer(String customerId) async {
    final response = await _apiClient.dio.get('/api/customers/$customerId');
    return Customer.fromJson(response.data);
  }

  Future<Customer> updateCustomer(String customerId, Map<String, dynamic> data) async {
    final response = await _apiClient.dio.put('/api/customers/$customerId', data: data);
    return Customer.fromJson(response.data);
  }

  Future<List<PipelineStage>> getPipelineStages(String projectId) async {
    final response = await _apiClient.dio.get('/api/projects/$projectId/pipelines/stages');
    final List list = response.data ?? [];
    return list.map((item) => PipelineStage.fromJson(item)).toList();
  }

  Future<List<Deal>> getDeals(String projectId) async {
    final response = await _apiClient.dio.get('/api/projects/$projectId/deals');
    final List list = response.data ?? [];
    return list.map((item) => Deal.fromJson(item)).toList();
  }

  Future<Deal> createDeal(String projectId, Map<String, dynamic> data) async {
    final response = await _apiClient.dio.post('/api/projects/$projectId/deals', data: data);
    return Deal.fromJson(response.data);
  }

  Future<Deal> updateDealStage(String dealId, String pipelineStageId) async {
    final response = await _apiClient.dio.put('/api/deals/$dealId/stage', data: {
      'pipelineStageId': pipelineStageId,
    });
    return Deal.fromJson(response.data);
  }

  Future<Deal> updateDealStatus(String dealId, int status) async {
    final response = await _apiClient.dio.put('/api/deals/$dealId/status', data: {
      'status': status,
    });
    return Deal.fromJson(response.data);
  }
}
