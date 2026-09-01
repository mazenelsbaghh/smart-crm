import { api } from '../../services/api';
import type {
  ContentStudioData,
  ContentVideo,
  ContentVideoPlanAccepted,
  ContentVideoSceneRetryIntent,
  ContentVideosData,
  CreateContentVideoPlan,
  UpdateContentSettings,
} from './types';

export const contentApi = {
  async get() {
    return (await api.get<ContentStudioData>('/api/content')).data;
  },

  async updateSettings(settings: UpdateContentSettings) {
    return (await api.put<ContentStudioData>('/api/content/settings', settings)).data;
  },

  async uploadLogo(file: File) {
    const form = new FormData();
    form.append('logo', file);
    return (await api.post<ContentStudioData>('/api/content/logo', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })).data;
  },

  async downloadAsset(assetUrl: string, signal: AbortSignal) {
    return (await api.get<Blob>(assetUrl, {
      responseType: 'blob',
      signal,
      timeout: 300_000,
    })).data;
  },

  async generateSample() {
    return (await api.post<{ message: string }>('/api/content/sample')).data;
  },

  async generateWeeklyPlan() {
    return (await api.post<{ message: string }>('/api/content/weekly-plan')).data;
  },

  async approveWeeklyPlan(planId: string) {
    return (await api.post<{ message: string }>(`/api/content/weekly-plans/${planId}/approve`)).data;
  },

  async approveWeeklyPlanItem(planId: string, itemId: string) {
    return (await api.post<{ message: string }>(`/api/content/weekly-plans/${planId}/items/${itemId}/approve`)).data;
  },

  async regenerateWeeklyPlanItem(planId: string, itemId: string) {
    return (await api.post<{ message: string }>(`/api/content/weekly-plans/${planId}/items/${itemId}/regenerate`)).data;
  },

  async regenerateWeeklyPlan(planId: string) {
    return (await api.post<{ message: string }>(`/api/content/weekly-plans/${planId}/regenerate`)).data;
  },

  async regenerate(postId: string) {
    return (await api.post<{ message: string }>(`/api/content/posts/${postId}/regenerate`)).data;
  },

  async approve(postId: string) {
    return (await api.post<{ message: string }>(`/api/content/posts/${postId}/approve`)).data;
  },

  async publish(postId: string) {
    return (await api.post<{ message: string }>(`/api/content/posts/${postId}/publish`)).data;
  },

  async getVideos(signal?: AbortSignal) {
    return (await api.get<ContentVideosData>('/api/content/videos', { signal })).data;
  },

  async getVideo(videoId: string, signal?: AbortSignal) {
    return (await api.get<ContentVideo>(`/api/content/videos/${videoId}`, { signal })).data;
  },

  async planVideo(request: CreateContentVideoPlan) {
    return (await api.post<ContentVideoPlanAccepted>('/api/content/videos/plan', request)).data;
  },

  async generateVideo(videoId: string) {
    return (await api.post<{ message: string }>(`/api/content/videos/${videoId}/generate`)).data;
  },

  async retryVideoScene(videoId: string, sceneId: string, intent: ContentVideoSceneRetryIntent) {
    return (await api.post<{ message: string }>(
      `/api/content/videos/${videoId}/scenes/${sceneId}/retry`,
      videoSceneRetryPayload(intent),
    )).data;
  },

  async retryVideoAssembly(videoId: string) {
    return (await api.post<{ message: string }>(`/api/content/videos/${videoId}/assembly/retry`)).data;
  },
};

function videoSceneRetryPayload(intent: ContentVideoSceneRetryIntent): { confirmPossibleDuplicate: boolean } {
  switch (intent.mode) {
    case 'safe': return { confirmPossibleDuplicate: false };
    case 'confirmed-possible-duplicate': return { confirmPossibleDuplicate: true };
  }
}
