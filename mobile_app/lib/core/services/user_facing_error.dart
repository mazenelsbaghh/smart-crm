import 'package:dio/dio.dart';

String userFacingError(Object error) {
  if (error is DioException) {
    if (error.type == DioExceptionType.connectionError ||
        error.type == DioExceptionType.connectionTimeout ||
        error.type == DioExceptionType.receiveTimeout ||
        error.type == DioExceptionType.sendTimeout) {
      return 'تعذر الاتصال بالخادم. تحقق من الإنترنت وحاول مرة أخرى.';
    }

    final status = error.response?.statusCode;
    if (status == 401 &&
        error.requestOptions.path.contains('/api/auth/login')) {
      return 'البريد الإلكتروني أو كلمة المرور غير صحيحة.';
    }
    if (status == 401) return 'انتهت الجلسة. سجّل الدخول مرة أخرى.';
    if (status == 403) return 'ليست لديك صلاحية لتنفيذ هذا الإجراء.';
    if (status == 404) return 'تعذر العثور على البيانات المطلوبة.';
    if (status != null && status >= 500) {
      return 'الخدمة غير متاحة مؤقتًا. حاول مرة أخرى بعد قليل.';
    }

    final data = error.response?.data;
    if (data is Map<String, dynamic>) {
      final message = data['message'] ?? data['error'] ?? data['title'];
      if (message is String && message.trim().isNotEmpty) return message.trim();
    }
  }

  return 'حدث خطأ غير متوقع. حاول مرة أخرى.';
}
