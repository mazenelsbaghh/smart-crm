class GroupAppointment {
  final String id;
  final String projectId;
  final String name;
  final DateTime dateTime;
  final int capacity;
  final bool isActive;
  final String days;
  final String mode; // "online" or "offline"
  final List<GroupAppointmentBooking> bookings;

  GroupAppointment({
    required this.id,
    required this.projectId,
    required this.name,
    required this.dateTime,
    required this.capacity,
    required this.isActive,
    required this.days,
    required this.mode,
    required this.bookings,
  });

  factory GroupAppointment.fromJson(Map<String, dynamic> json) {
    var bookingsList = json['bookings'] as List? ?? [];
    return GroupAppointment(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      name: json['name'] ?? '',
      dateTime: DateTime.tryParse(json['dateTime'] ?? '') ?? DateTime.now(),
      capacity: json['capacity'] ?? 0,
      isActive: json['isActive'] ?? true,
      days: json['days'] ?? '',
      mode: json['mode'] ?? 'offline',
      bookings: bookingsList.map((item) => GroupAppointmentBooking.fromJson(item)).toList(),
    );
  }

  Map<String, dynamic> toJson() => {
        'name': name,
        'dateTime': dateTime.toIso8601String(),
        'capacity': capacity,
        'isActive': isActive,
        'days': days,
        'mode': mode,
      };
}

class GroupAppointmentBooking {
  final String id;
  final String projectId;
  final String groupAppointmentId;
  final String customerId;
  final String customerName;
  final String customerPhone;

  GroupAppointmentBooking({
    required this.id,
    required this.projectId,
    required this.groupAppointmentId,
    required this.customerId,
    required this.customerName,
    required this.customerPhone,
  });

  factory GroupAppointmentBooking.fromJson(Map<String, dynamic> json) {
    return GroupAppointmentBooking(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      groupAppointmentId: json['groupAppointmentId'] ?? '',
      customerId: json['customerId'] ?? '',
      customerName: json['customerName'] ?? '',
      customerPhone: json['customerPhone'] ?? '',
    );
  }

  Map<String, dynamic> toJson() => {
        'groupAppointmentId': groupAppointmentId,
        'customerId': customerId,
        'customerName': customerName,
        'customerPhone': customerPhone,
      };
}
