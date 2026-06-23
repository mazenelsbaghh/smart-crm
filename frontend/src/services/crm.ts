import { api } from './api';

export interface Customer {
  id: string;
  projectId: string;
  phoneNumber: string;
  name: string;
  city: string;
  leadScore: number;
  tags: string[];
  notes: string;
  budget: number | null;
  interests: string[];
  pipelineStage?: string;
  label?: string;
  isBlacklisted?: boolean;
  purchaseProbability?: number;
  aiInsights?: string | null;
  automationRules?: string | null;
}

export interface CustomerTask {
  id: string;
  projectId: string;
  customerId: string;
  title: string;
  isCompleted: boolean;
  dueDate?: string;
}

export interface PipelineStage {
  id: string;
  projectId: string;
  name: string;
  order: number;
}

export interface Deal {
  id: string;
  projectId: string;
  customerId: string;
  title: string;
  amount: number;
  pipelineStageId: string;
  status: 0 | 1 | 2; // Open, Won, Lost
  closedAt: string | null;
}

export interface AnalyticsSnapshot {
  id: string;
  projectId: string;
  metricType: 'Sales' | 'AI_Accuracy' | 'Team_Performance' | string;
  metricValue: number;
  snapshotDate: string;
}

export const crmService = {
  getCustomers: async (projectId: string): Promise<Customer[]> => {
    const response = await api.get<Customer[]>(`/api/projects/${projectId}/customers`);
    return response.data;
  },

  getCustomer: async (customerId: string): Promise<Customer> => {
    const response = await api.get<Customer>(`/api/customers/${customerId}`);
    return response.data;
  },

  updateCustomer: async (customerId: string, data: Partial<Customer>): Promise<Customer> => {
    const response = await api.put<Customer>(`/api/customers/${customerId}`, data);
    return response.data;
  },

  getPipelineStages: async (projectId: string): Promise<PipelineStage[]> => {
    const response = await api.get<PipelineStage[]>(`/api/projects/${projectId}/pipelines/stages`);
    return response.data;
  },

  getDeals: async (projectId: string): Promise<Deal[]> => {
    const response = await api.get<Deal[]>(`/api/projects/${projectId}/deals`);
    return response.data;
  },

  createDeal: async (projectId: string, data: Partial<Deal>): Promise<Deal> => {
    const response = await api.post<Deal>(`/api/projects/${projectId}/deals`, data);
    return response.data;
  },

  updateDealStage: async (dealId: string, pipelineStageId: string): Promise<Deal> => {
    const response = await api.put<Deal>(`/api/deals/${dealId}/stage`, { pipelineStageId });
    return response.data;
  },

  updateDealStatus: async (dealId: string, status: 0 | 1 | 2): Promise<Deal> => {
    const response = await api.put<Deal>(`/api/deals/${dealId}/status`, { status });
    return response.data;
  },

  getAnalytics: async (projectId: string, type: string): Promise<AnalyticsSnapshot[]> => {
    const response = await api.get<AnalyticsSnapshot[]>(`/api/projects/${projectId}/analytics/${type}`);
    return response.data;
  },

  recalculateAnalytics: async (projectId: string): Promise<{ message: string }> => {
    const response = await api.post<{ message: string }>(`/api/projects/${projectId}/analytics/recalculate`);
    return response.data;
  },

  getCustomerTasks: async (customerId: string): Promise<CustomerTask[]> => {
    const response = await api.get<CustomerTask[]>(`/api/customers/${customerId}/tasks`);
    return response.data;
  },

  createCustomerTask: async (customerId: string, title: string, dueDate?: string): Promise<CustomerTask> => {
    const response = await api.post<CustomerTask>(`/api/customers/${customerId}/tasks`, { title, dueDate });
    return response.data;
  },

  updateCustomerTask: async (taskId: string, data: Partial<CustomerTask>): Promise<CustomerTask> => {
    const response = await api.put<CustomerTask>(`/api/customers/tasks/${taskId}`, data);
    return response.data;
  },

  deleteCustomerTask: async (taskId: string): Promise<void> => {
    await api.delete(`/api/customers/tasks/${taskId}`);
  },
};
