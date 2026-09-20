import type { Message } from '@/types/chat';

export interface ReviewIssue { code: string; label: string; source: string; messageIds: string[] }
export interface ReviewConversation {
  conversationId: string; customerId: string; customerName: string; channel: string; status: string;
  humanHandoffPending: boolean; incomingCount: number; outgoingCount: number;
  longestResponseMinutes: number | null; waitingSinceUtc: string | null; lastActivityAtUtc: string;
  issues: ReviewIssue[]; analysisStatus: 'Missing' | 'Stale' | 'Later' | 'Current';
  replyQualityScore: number | null; summary: string | null; recommendation: string | null; analyzedAtUtc: string | null;
}
export interface ReviewFollowUp {
  id: string; customerId: string; customerName: string; conversationId: string | null; channel: string | null;
  type: string; status: string; health: string; dueAtUtc: string; sentAtUtc: string | null;
  delayMinutes: number | null; sentMessageId: string | null; content: string; customerReplied: boolean;
}
export interface DailyReview {
  date: string; timezone: string; generatedAtUtc: string; windowStartUtc: string; windowEndUtc: string;
  summary: { conversations: number; needsAttention: number; awaitingAnalysis: number; waitingForHuman: number;
    followUpsDue: number; followUpsSent: number; followUpsOverdue: number; followUpsUnknown: number };
  page: number; pageSize: number; filteredCount: number; conversations: ReviewConversation[]; followUps: ReviewFollowUp[];
}
export interface ReviewMessage extends Message { direction: string; messageType: string }
export interface ReplyDraft { content: string; basedOnMessageId: string; generatedAtUtc: string }
export const analysisLabels = { Missing: 'لم يُحلّل بعد', Stale: 'التحليل يحتاج تحديث', Later: 'التحليل يشمل أيامًا لاحقة', Current: 'تحليل محدث' };
export const healthLabels: Record<string, string> = { Sent: 'إرسال مسجل', Overdue: 'متأخرة', Failed: 'فشلت', Unknown: 'الإرسال غير محسوم', Unverified: 'مكتملة بلا دليل إرسال', Stopped: 'متوقفة', Scheduled: 'مجدولة' };
export const reviewUrl = (projectId: string) => `/api/projects/${projectId}/reports/daily-review`;
export function inboxLink(id: string, channel: string | null) {
  const path = { WhatsApp: '/inbox', Messenger: '/inbox/messenger', FacebookComment: '/inbox/comments' }[channel ?? ''];
  return path ? `${path}?conversationId=${encodeURIComponent(id)}` : null;
}
export function timeLabel(value: string, timezone: string, date = false) {
  return new Intl.DateTimeFormat('ar-EG', { timeZone: timezone, hour: '2-digit', minute: '2-digit',
    ...(date ? { day: 'numeric', month: 'short' } as const : {}) }).format(new Date(value));
}
export const numberLabel = (value: number) => value.toLocaleString('ar-EG', { maximumFractionDigits: 1 });
export function minutesLabel(value: number) { return `${numberLabel(Math.max(0, value))} دقيقة`; }
