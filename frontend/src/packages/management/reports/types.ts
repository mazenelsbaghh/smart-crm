export interface FunnelMetric {
  key: string;
  label: string;
  count: number;
  rateFromPrevious: number;
}

export interface DailySalesMetric {
  date: string;
  newConversations: number;
  responded: number;
  qualified: number;
  bookingIntent: number;
  booked: number;
  paid: number;
  attended: number;
}

export interface ReasonMetric {
  reason: string;
  label: string;
  count: number;
  percentage: number;
}

export interface FunnelDropOffReason {
  reason: string;
  label: string;
  count: number;
  percentage: number;
  needsFollowUp: number;
}

export interface FunnelTransitionMetric {
  key: string;
  fromLabel: string;
  toLabel: string;
  fromCount: number;
  toCount: number;
  dropOffCount: number;
  conversionRate: number;
  dropOffRate: number;
  needsFollowUp: number;
  reasons: FunnelDropOffReason[];
}

export interface AnalysisEvidence {
  messageId: string;
  quote: string;
}

export interface OpportunityItem {
  conversationId: string;
  customerId: string;
  customerName: string;
  channel: 'WhatsApp' | 'Messenger' | 'FacebookComment';
  priority: number;
  stage: string;
  reason: string;
  reasonLabel: string;
  summary: string;
  recommendation: string;
  recommendedAction: 'SendNow' | 'Schedule' | 'Scheduled' | 'OpenConversation';
  actionToken: string;
  scheduledForUtc?: string | null;
  lastMessageAtUtc: string;
}

export interface FollowUpPlanSummary {
  sendNow: number;
  schedule: number;
  scheduled: number;
  sendNowToken: string;
  scheduleToken: string;
}

export type FollowUpPlanAction = 'SendNow' | 'Schedule';

export interface ConversationAnalysisItem {
  conversationId: string;
  customerId: string;
  customerName: string;
  channel: string;
  stage: string;
  outcome: string;
  reason: string;
  reasonLabel: string;
  summary: string;
  recommendation: string;
  confidence: number;
  replyQualityScore: number;
  followUpPriority: number;
  needsFollowUp: boolean;
  missedOpportunity: boolean;
  manuallyCorrected: boolean;
  evidence: AnalysisEvidence[];
  conversationStartedAtUtc: string;
  lastMessageAtUtc: string;
  analyzedAtUtc: string;
}

export interface AiDigest {
  executiveSummary: string;
  findings: string[];
  recommendations: string[];
  risks: string[];
  generatedAtUtc: string;
  model: string;
}

export interface SalesIntelligenceDashboard {
  projectId: string;
  windowStartUtc: string;
  windowEndUtc: string;
  timezone: string;
  generatedAtUtc: string;
  totalConversations: number;
  uniqueCustomers: number;
  activeConversations: number;
  analyzedConversations: number;
  analysisCoverage: number;
  bookingConversionRate: number;
  paymentConversionRate: number;
  medianFirstResponseMinutes: number;
  funnel: FunnelMetric[];
  funnelTransitions: FunnelTransitionMetric[];
  daily: DailySalesMetric[];
  reasons: ReasonMetric[];
  followUpPlan: FollowUpPlanSummary;
  opportunities: OpportunityItem[];
  analyses: ConversationAnalysisItem[];
  aiDigest?: AiDigest | null;
}

export interface RefreshResult {
  requested: number;
  analyzed: number;
  skipped: number;
  errors: string[];
  digest?: AiDigest | null;
}

export interface AnalysisQueueResult {
  pending: number;
  jobId: string;
}

export interface AnalystAnswer {
  answer: string;
  conversationIds: string[];
  generatedAtUtc: string;
  model: string;
  totalConversations: number;
  analyzedConversations: number;
  detailedAnalysesReviewed: number;
  analysisCoverage: number;
}

export type ReportPreset = 'today' | '7d' | '30d' | 'custom';
