import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/auth/data/models/user_model.dart';

void main() {
  test(
    'settings update preserves hidden controls without exposing secrets',
    () {
      final settings = ProjectSettings.fromJson({
        'aiAutoReplyEnabled': true,
        'timezone': 'Africa/Cairo',
        'geminiApiKey': 'must-not-survive',
        'geminiApiKeyConfigured': true,
        'geminiModel': 'gemini-3.6-flash',
        'aiTonePreference': 'هادئ',
        'aiTargetAudience': 'العملاء',
        'replyDelay': 7,
        'maxDailyMessages': 900,
        'isGroupAppointmentsEnabled': true,
        'isWhatsAppGroupAutomationEnabled': true,
        'groupAutomationManagerPhone': '201000000000',
        'activeInstructors': 'أحمد، منى',
        'humanTransferEnabled': true,
        'humanTransferPhone': '201111111111',
        'isTalkTipsTrialGateEnabled': true,
        'messengerAiAutoReplyEnabled': true,
        'messengerReplyDelay': 11,
        'commentsAiAutoReplyEnabled': true,
        'commentsReplyDelay': 13,
        'systemPrompt': 'تعليمات المشروع',
        'aiBehavior': {
          'identity': {'brandName': 'سمارت كاستمر'},
        },
      });

      final updatePayload = settings.toUpdateJson();
      final cachedPayload = settings.toJson();

      expect(updatePayload['isWhatsAppGroupAutomationEnabled'], isTrue);
      expect(updatePayload['humanTransferEnabled'], isTrue);
      expect(updatePayload['messengerAiAutoReplyEnabled'], isTrue);
      expect(updatePayload['commentsAiAutoReplyEnabled'], isTrue);
      expect(updatePayload['systemPrompt'], 'تعليمات المشروع');
      expect(updatePayload['aiBehavior'], isA<Map<String, dynamic>>());
      expect(updatePayload, isNot(contains('geminiApiKey')));
      expect(cachedPayload['geminiApiKey'], isEmpty);
      expect(settings.geminiApiKey, isEmpty);
    },
  );
}
