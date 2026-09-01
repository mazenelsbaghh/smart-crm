import { api } from '../../../services/api';
import type {
  AnalysisQueueResult,
  AnalystAnswer,
  ConversationAnalysisItem,
  SalesIntelligenceDashboard,
  FollowUpPlanAction,
} from './types';

export interface ReportWindow {
  fromUtc: string;
  toUtc: string;
}

const base = (projectId: string) => `/api/projects/${projectId}/reports`;

export const reportsApi = {
  async dashboard(projectId: string, window: ReportWindow) {
    const response = await api.get<SalesIntelligenceDashboard>(`${base(projectId)}/sales-intelligence`, {
      params: window,
    });
    return response.data;
  },

  async refresh(projectId: string, window: ReportWindow) {
    const response = await api.post<AnalysisQueueResult>(`${base(projectId)}/sales-intelligence/analyze-all`, window);
    return response.data;
  },

  async ask(projectId: string, window: ReportWindow, question: string) {
    const response = await api.post<AnalystAnswer>(`${base(projectId)}/sales-intelligence/ask`, {
      ...window,
      question,
    });
    return response.data;
  },

  async analyze(projectId: string, conversationId: string) {
    const response = await api.post<ConversationAnalysisItem>(
      `${base(projectId)}/conversations/${conversationId}/analyze`,
    );
    return response.data;
  },

  async correctReason(projectId: string, conversationId: string, reason: string, notes?: string) {
    const response = await api.patch<ConversationAnalysisItem>(
      `${base(projectId)}/conversations/${conversationId}/analysis`,
      { reason, notes },
    );
    return response.data;
  },

  async queueFollowUpPlan(
    projectId: string,
    window: ReportWindow,
    action: FollowUpPlanAction,
    conversationId?: string,
    planToken?: string,
  ) {
    const response = await api.post<{ queued: number }>(`${base(projectId)}/sales-intelligence/follow-ups`, {
      ...window,
      action,
      ...(conversationId ? { conversationId } : {}),
      ...(planToken ? { planToken } : {}),
    });
    return response.data;
  },
};
