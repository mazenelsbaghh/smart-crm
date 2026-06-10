class Customer {
  final String id;
  final String projectId;
  final String phoneNumber;
  final String name;
  final String city;
  final int leadScore;
  final List<String> tags;
  final String notes;
  final double? budget;
  final List<String> interests;
  final String? pipelineStage;
  final String? label;
  final bool isBlacklisted;

  Customer({
    required this.id,
    required this.projectId,
    required this.phoneNumber,
    required this.name,
    required this.city,
    required this.leadScore,
    required this.tags,
    required this.notes,
    this.budget,
    required this.interests,
    this.pipelineStage,
    this.label,
    required this.isBlacklisted,
  });

  factory Customer.fromJson(Map<String, dynamic> json) {
    return Customer(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      phoneNumber: json['phoneNumber'] ?? '',
      name: json['name'] ?? '',
      city: json['city'] ?? '',
      leadScore: json['leadScore'] ?? 0,
      tags: List<String>.from(json['tags'] ?? []),
      notes: json['notes'] ?? '',
      budget: json['budget'] != null ? (json['budget'] as num).toDouble() : null,
      interests: List<String>.from(json['interests'] ?? []),
      pipelineStage: json['pipelineStage'],
      label: json['label'],
      isBlacklisted: json['isBlacklisted'] ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
        'name': name,
        'city': city,
        'leadScore': leadScore,
        'tags': tags,
        'notes': notes,
        'budget': budget,
        'interests': interests,
        'pipelineStage': pipelineStage,
        'label': label,
        'isBlacklisted': isBlacklisted,
      };
}

class PipelineStage {
  final String id;
  final String projectId;
  final String name;
  final int order;

  PipelineStage({
    required this.id,
    required this.projectId,
    required this.name,
    required this.order,
  });

  factory PipelineStage.fromJson(Map<String, dynamic> json) {
    return PipelineStage(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      name: json['name'] ?? '',
      order: json['order'] ?? 0,
    );
  }
}

class Deal {
  final String id;
  final String projectId;
  final String customerId;
  final String title;
  final double amount;
  final String pipelineStageId;
  final int status; // 0 = Open, 1 = Won, 2 = Lost
  final DateTime? closedAt;

  Deal({
    required this.id,
    required this.projectId,
    required this.customerId,
    required this.title,
    required this.amount,
    required this.pipelineStageId,
    required this.status,
    this.closedAt,
  });

  factory Deal.fromJson(Map<String, dynamic> json) {
    return Deal(
      id: json['id'] ?? '',
      projectId: json['projectId'] ?? '',
      customerId: json['customerId'] ?? '',
      title: json['title'] ?? '',
      amount: (json['amount'] ?? 0.0).toDouble(),
      pipelineStageId: json['pipelineStageId'] ?? '',
      status: json['status'] ?? 0,
      closedAt: DateTime.tryParse(json['closedAt'] ?? ''),
    );
  }

  Map<String, dynamic> toJson() => {
        'customerId': customerId,
        'title': title,
        'amount': amount,
        'pipelineStageId': pipelineStageId,
        'status': status,
      };
}
