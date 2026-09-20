import type { DailyReview, ReviewMessage } from '../types';
import type { ProcessingReport } from '../processing-types';

export const processingFixture: ProcessingReport = {
  schedule: { enabled: true, prepareDrafts: true, intervalMinutes: 5, quietMinutes: 5, verifyAfterMinutes: 30, batchSize: 20,
    nextScanAtUtc: '2026-09-07T15:05:00Z', lastScanAtUtc: '2026-09-07T15:00:00Z' },
  analysisConfigured: true, counts: { Queued: 8, DraftReady: 1, Resolved: 3 }, page: 1, pageSize: 20, total: 1,
  cases: [{ id: 'case-1', conversationId: 'conversation-1', customerName: 'عميلة اختبار — سهيلة', channel: 'WhatsApp', state: 'DraftReady', phase: 'Verify',
    nextRunAtUtc: '2026-09-07T15:30:00Z', lastReviewedAtUtc: '2026-09-07T15:00:00Z', attempts: 1, qualityScore: 25,
    summary: 'تكرر طلب الرقم بعد وصوله.', recommendation: 'راجع الرقم وأجب عن سؤال الموعد.', lastError: null, hasDraft: true, needsResolution: true }],
};

export const reviewFixture: DailyReview = {
  date: '2026-09-07', timezone: 'Africa/Cairo', generatedAtUtc: '2026-09-07T15:00:00Z',
  windowStartUtc: '2026-09-06T21:00:00Z', windowEndUtc: '2026-09-07T21:00:00Z', page: 1, pageSize: 30, filteredCount: 2,
  summary: { conversations: 14, needsAttention: 2, awaitingAnalysis: 4, waitingForHuman: 1, followUpsDue: 3, followUpsSent: 1, followUpsOverdue: 1, followUpsUnknown: 1 },
  conversations: [
    { conversationId: 'conversation-1', customerId: 'customer-1', customerName: 'عميلة اختبار — سهيلة', channel: 'WhatsApp', status: 'Pending',
      humanHandoffPending: true, incomingCount: 5, outgoingCount: 8, longestResponseMinutes: 17, waitingSinceUtc: '2026-09-07T12:00:00Z', lastActivityAtUtc: '2026-09-07T12:00:00Z',
      issues: [{ code: 'RepeatedReply', label: 'رد متكرر يحتاج مراجعة', source: 'Messages', messageIds: ['reply-1'] }, { code: 'HumanHandoff', label: 'ينتظر موظف حاليًا', source: 'System', messageIds: [] }],
      analysisStatus: 'Current', replyQualityScore: 25, summary: 'تكرر طلب رقم الهاتف بعد أن أرسلته العميلة بالفعل.', recommendation: 'راجع الرقم الموجود وأجب عن سؤال الموعد مباشرة.', analyzedAtUtc: '2026-09-07T12:01:00Z' },
    { conversationId: 'conversation-2', customerId: 'customer-2', customerName: 'عميل اختبار — أحمد', channel: 'Messenger', status: 'Open',
      humanHandoffPending: false, incomingCount: 2, outgoingCount: 1, longestResponseMinutes: 9, waitingSinceUtc: null, lastActivityAtUtc: '2026-09-07T11:00:00Z',
      issues: [{ code: 'SlowReply', label: 'رد بعد أكثر من ٥ دقائق', source: 'Messages', messageIds: [] }],
      analysisStatus: 'Missing', replyQualityScore: null, summary: null, recommendation: null, analyzedAtUtc: null },
  ],
  followUps: [
    { id: 'followup-1', customerId: 'customer-2', customerName: 'عميل اختبار — أحمد', conversationId: 'conversation-2', channel: 'Messenger', type: 'Nurturing', status: 'Completed', health: 'Sent',
      dueAtUtc: '2026-09-07T10:00:00Z', sentAtUtc: '2026-09-07T10:07:00Z', delayMinutes: 7, sentMessageId: 'sent-1', content: 'متابعة تجريبية: حبيت أتأكد إن تفاصيل الموعد وصلتك.', customerReplied: true },
    { id: 'followup-2', customerId: 'customer-1', customerName: 'عميلة اختبار — سهيلة', conversationId: 'conversation-1', channel: 'WhatsApp', type: 'Nurturing', status: 'Completed', health: 'Unverified',
      dueAtUtc: '2026-09-07T10:00:00Z', sentAtUtc: null, delayMinutes: null, sentMessageId: null, content: 'متابعة قديمة للتجربة', customerReplied: false },
  ],
};
export const messagesFixture: ReviewMessage[] = [
  { id: 'incoming-1', conversationId: 'conversation-1', senderType: 'Customer', direction: 'Incoming', messageType: 'Text', content: 'أنا بعت الرقم قبل كده، السيشن الساعة كام؟', createdAt: '2026-09-07T11:40:00Z', status: 'Delivered', mediaUrl: null, mediaType: null },
  { id: 'reply-1', conversationId: 'conversation-1', senderType: 'AI', direction: 'Outgoing', messageType: 'Text', content: 'ممكن رقم الموبايل الأول؟', createdAt: '2026-09-07T11:57:00Z', status: 'Sent', mediaUrl: null, mediaType: null },
  { id: 'incoming-2', conversationId: 'conversation-1', senderType: 'Customer', direction: 'Incoming', messageType: 'Voice', content: 'رسالة صوتية', transcription: 'ممكن حد يرد عليا ويفيدني؟', createdAt: '2026-09-07T12:00:00Z', status: 'Delivered', mediaUrl: null, mediaType: 'Voice', assetId: 'voice-test' },
];
