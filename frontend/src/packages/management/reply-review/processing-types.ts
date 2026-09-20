export interface ReviewSchedule {
  enabled: boolean; prepareDrafts: boolean; intervalMinutes: number; quietMinutes: number;
  verifyAfterMinutes: number; batchSize: number; nextScanAtUtc: string; lastScanAtUtc: string | null;
}
export interface ProcessingCase {
  id: string; conversationId: string; customerName: string; channel: string; state: string; phase: string;
  nextRunAtUtc: string; lastReviewedAtUtc: string | null; attempts: number; qualityScore: number | null;
  summary: string; recommendation: string; lastError: string | null; hasDraft: boolean; needsResolution: boolean;
}
export interface ProcessingReport {
  schedule: ReviewSchedule; analysisConfigured: boolean; counts: Record<string, number>;
  page: number; pageSize: number; total: number; cases: ProcessingCase[];
}
export interface ProcessingDetail {
  draftContent: string; draftOutdated: boolean; draftGeneratedAtUtc: string | null;
  runs: { id: string; startedAtUtc: string; finishedAtUtc: string; outcome: string; attempt: number;
    qualityScore: number | null; summary: string; error: string | null }[];
}
export const processingLabels: Record<string, string> = {
  Queued: 'في الطابور', Reviewing: 'جاري المراجعة', RetryScheduled: 'إعادة محاولة مجدولة',
  DraftReady: 'مسودة جاهزة للمراجعة', NeedsHuman: 'يحتاج موظفًا', VerifyScheduled: 'تحقق مجدول',
  Reviewed: 'تمت المراجعة', Resolved: 'المعالجة مؤكدة بالدليل', Failed: 'تعذّر بعد ٣ محاولات',
};
