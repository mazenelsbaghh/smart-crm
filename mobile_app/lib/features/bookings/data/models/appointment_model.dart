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
    final bookingsList = json['bookings'] as List? ?? [];
    final rawCapacity = json['capacity'];
    return GroupAppointment(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      name: json['name'] ?? '',
      dateTime:
          DateTime.tryParse(json['dateTime']?.toString() ?? '') ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      capacity: rawCapacity is num
          ? rawCapacity.round()
          : int.tryParse(rawCapacity?.toString() ?? '') ?? 0,
      isActive: json['isActive'] ?? true,
      days: json['days'] ?? '',
      mode: json['mode'] ?? 'offline',
      bookings: bookingsList
          .whereType<Map>()
          .map(
            (item) => GroupAppointmentBooking.fromJson(
              Map<String, dynamic>.from(item),
            ),
          )
          .toList(),
    );
  }

  GroupAppointment copyWith({
    String? name,
    DateTime? dateTime,
    int? capacity,
    bool? isActive,
    String? days,
    String? mode,
    List<GroupAppointmentBooking>? bookings,
  }) {
    return GroupAppointment(
      id: id,
      projectId: projectId,
      name: name ?? this.name,
      dateTime: dateTime ?? this.dateTime,
      capacity: capacity ?? this.capacity,
      isActive: isActive ?? this.isActive,
      days: days ?? this.days,
      mode: mode ?? this.mode,
      bookings: bookings ?? this.bookings,
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
