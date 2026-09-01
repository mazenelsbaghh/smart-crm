import '../../../../core/services/api_client.dart';
import '../models/appointment_model.dart';

class BookingsRepository {
  final ApiClient _apiClient;

  BookingsRepository({required ApiClient apiClient}) : _apiClient = apiClient;

  Future<List<GroupAppointment>> getAppointments() async {
    final response = await _apiClient.dio.get('/api/group-appointments');
    final List list = response.data ?? [];
    return list.map((item) => GroupAppointment.fromJson(item)).toList();
  }

  Future<GroupAppointment> createAppointment(Map<String, dynamic> data) async {
    final response = await _apiClient.dio.post(
      '/api/group-appointments',
      data: data,
    );
    return GroupAppointment.fromJson(response.data);
  }

  Future<void> deleteAppointment(String id) async {
    await _apiClient.dio.delete('/api/group-appointments/$id');
  }

  Future<void> toggleAppointment(String id) async {
    await _apiClient.dio.patch('/api/group-appointments/$id/toggle');
  }

  Future<void> cancelBooking(String bookingId) async {
    await _apiClient.dio.delete('/api/group-appointments/bookings/$bookingId');
  }
}
