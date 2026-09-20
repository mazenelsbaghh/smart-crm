import { api } from '../../services/api';
import type {
  ContentStudioData,
  ContentVideo,
  ContentVideoPlanAccepted,
  ContentVideoSceneRetryIntent,
  ContentVideosData,
  CreateContentVideoPlan,
  UpdateContentSettings,
  ContentDocumentDetail,
  ContentDocumentKind,
  ContentDocumentSummary,
  ContentDocumentCountSuggestion,
  ContentDocumentPreview,
  ContentDocumentPreviewRequest,
  CardGameIdea,
  ContentCardGameDetail,
  ContentCardGameSummary,
  CreateContentCardGame,
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

  async listCardGames() {
    return (await api.get<{ games: ContentCardGameSummary[] }>('/api/content/card-games')).data;
  },

  async getCardGame(id: string) {
    return (await api.get<ContentCardGameDetail>(`/api/content/card-games/${id}`)).data;
  },

  async suggestCardGames(workshopBrief: string) {
    return (await api.post<{ ideas: CardGameIdea[] }>('/api/content/card-games/ideas', { workshopBrief }, {
      timeout: 120_000,
    })).data;
  },

  async createCardGame(input: CreateContentCardGame) {
    return (await api.post<{ id: string; message: string }>('/api/content/card-games', input, {
      timeout: 120_000,
    })).data;
  },

  async generateCardGameDesign(id: string) {
    return (await api.post<{ message: string }>(`/api/content/card-games/${id}/design`)).data;
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

  async getDocuments(signal?: AbortSignal) {
    return (await api.get<{ documents: ContentDocumentSummary[] }>('/api/content/documents', { signal })).data;
  },

  async getDocument(documentId: string, signal?: AbortSignal) {
    return (await api.get<ContentDocumentDetail>(`/api/content/documents/${documentId}`, { signal })).data;
  },

  async createDocument(request: ContentDocumentPreviewRequest & { previewFingerprint: string }) {
    return (await api.post<{ id: string; ids?: string[]; status: ContentDocumentSummary['status']; message: string }>('/api/content/documents', request)).data;
  },

  async suggestDocumentPageCount(request: { kind: ContentDocumentKind; content: string }, signal?: AbortSignal) {
    return (await api.post<ContentDocumentCountSuggestion>('/api/content/documents/suggest-page-count', request, { signal })).data;
  },

  async previewDocument(request: ContentDocumentPreviewRequest, signal?: AbortSignal) {
    return (await api.post<ContentDocumentPreview>('/api/content/documents/preview', request, { signal, timeout: 60_000 })).data;
  },

  async updateDocumentPage(documentId: string, pageId: string, title: string, body: string) {
    await api.put(`/api/content/documents/${documentId}/pages/${pageId}`, { title, body });
  },

  async addDocumentPage(documentId: string, page: { title: string; body: string; beforePageId: string | null }) {
    return (await api.post<{ id: string; pageIndex: number; message: string }>(`/api/content/documents/${documentId}/pages`, page)).data;
  },

  async reorderDocumentPages(documentId: string, pageIds: string[]) {
    await api.put(`/api/content/documents/${documentId}/pages/order`, { pageIds });
  },

  async regenerateDocumentPageImage(documentId: string, pageId: string) {
    return (await api.post<{ message: string }>(`/api/content/documents/${documentId}/pages/${pageId}/regenerate-image`)).data;
  },

  async regenerateDocumentImages(documentId: string) {
    return (await api.post<{ message: string }>(`/api/content/documents/${documentId}/regenerate-images`)).data;
  },
};

function videoSceneRetryPayload(intent: ContentVideoSceneRetryIntent): { confirmPossibleDuplicate: boolean } {
  switch (intent.mode) {
    case 'safe': return { confirmPossibleDuplicate: false };
    case 'confirmed-possible-duplicate': return { confirmPossibleDuplicate: true };
  }
}
