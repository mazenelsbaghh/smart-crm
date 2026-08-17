import { api } from '../../../services/api';
import type { AdDecision, AdvertisingConnection, AdvertisingOffer, AdvertisingOverview, Conversion, Creative, ExistingFacebookAd, FacebookPagePost, ManagedAd, MetaResourceCatalog } from '../types';

const base = (projectId: string) => `/api/projects/${projectId}/ad-manager`;

export const adManagerApi = {
  overview: async (projectId: string) => (await api.get<AdvertisingOverview>(`${base(projectId)}/overview`)).data,
  syncNow: async (projectId: string) => (await api.post<{ queued: boolean; message: string }>(`${base(projectId)}/sync-now`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  campaigns: async (projectId: string) => (await api.get<ManagedAd[]>(`${base(projectId)}/campaigns`)).data,
  existingFacebookAds: async (projectId: string) => (await api.get<ExistingFacebookAd[]>(`${base(projectId)}/campaigns/facebook-existing`)).data,
  importFacebookAds: async (projectId: string, adIds: string[]) => (await api.post<{ importedAds: number; existingAds: number; reservedDailyBudget: number }>(`${base(projectId)}/campaigns/import-facebook`, { adIds })).data,
  creatives: async (projectId: string) => (await api.get<Creative[]>(`${base(projectId)}/creatives`)).data,
  conversions: async (projectId: string) => (await api.get<Conversion[]>(`${base(projectId)}/conversions`)).data,
  decisions: async (projectId: string) => (await api.get<AdDecision[]>(`${base(projectId)}/decisions`)).data,
  offers: async (projectId: string) => (await api.get<AdvertisingOffer[]>(`${base(projectId)}/offers`)).data,
  pagePosts: async (projectId: string) => (await api.get<FacebookPagePost[]>(`${base(projectId)}/facebook/page-posts`)).data,
  importPosts: async (projectId: string, posts: FacebookPagePost[]) => (await api.post<{ creativeIds: string[] }>(`${base(projectId)}/creatives/import-posts`, { posts: posts.map(({ id, mediaType, createdAtUtc }) => ({ id, mediaType, createdAtUtc })) })).data,
  activateLaunch: async (projectId: string, input: { offerId: string; creativeIds: string[]; name: string; objective: string; destinationUrl: string; optimizationEvent: string; customEventType?: string }) =>
    (await api.post<{ ads: number; providerState: 'ACTIVATION_QUEUED' | 'PAUSED_PENDING_AI_REVIEW'; queuedCommands: number }>(`${base(projectId)}/launch-plans/activate`, input, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  startOAuth: async (projectId: string) => (await api.post<{ authorizationUrl: string }>(`${base(projectId)}/facebook/oauth/start`)).data,
  resources: async (projectId: string, adAccountId?: string) => (await api.get<MetaResourceCatalog>(`${base(projectId)}/facebook/resources`, { params: { adAccountId } })).data,
  connection: async (projectId: string) => (await api.get<AdvertisingConnection | null>(`${base(projectId)}/connection`)).data,
  selectConnection: async (projectId: string, input: { adAccountId: string; pageId: string; datasetId?: string; currency: string; timezone: string }) => api.put(`${base(projectId)}/connection`, input, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  saveEnvelope: async (projectId: string, input: { dailyCap: number; periodCap?: number; currency: string; safetyReservePercent: number; maximumIncreasePercent: number; cooldownHours: number; allowedCountries: string[] }) => api.put(`${base(projectId)}/envelope`, input, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  disable: async (projectId: string) => api.post(`${base(projectId)}/autopilot/disable`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  enable: async (projectId: string) => api.post(`${base(projectId)}/autopilot/enable`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  emergencyStop: async (projectId: string, reason: string) => api.post(`${base(projectId)}/emergency-stop`, { reason }, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  resume: async (projectId: string) => api.post(`${base(projectId)}/emergency-stop/resume`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
};
