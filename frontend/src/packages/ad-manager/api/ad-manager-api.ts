import { api } from '../../../services/api';
import type { AdDecision, AdDecisionDetail, AdvertisingConnection, AdvertisingEnvelope, AdvertisingExperiment, AdvertisingOffer, AdvertisingOverview, AdvertisingStrategy, AttributionTouch, AudienceStrategy, Conversion, ConversionDelivery, Creative, CreativeComparison, DailyAdvertisingReport, ExistingFacebookAd, FacebookPagePost, ManagedAd, MetaResourceCatalog, StopState, TrackingHealth, WhatsAppGatewayAccount, WhatsAppGatewayStatus } from '../types';

const base = (projectId: string) => `/api/projects/${projectId}/ad-manager`;
const mutationHeaders = (version?: number) => ({ 'Idempotency-Key': crypto.randomUUID(), ...(version === undefined ? {} : { 'If-Match': `"${version}"` }) });
export const cursorItems = <T>(payload: { items?: T[]; nextCursor?: string } | T[]) => Array.isArray(payload)
  ? { items: payload } : { items: payload.items ?? [], nextCursor: payload.nextCursor };

export const adManagerApi = {
  overview: async (projectId: string, signal?: AbortSignal) => (await api.get<AdvertisingOverview>(`${base(projectId)}/overview`, { signal })).data,
  syncNow: async (projectId: string) => (await api.post<{ queued: boolean; message: string }>(`${base(projectId)}/sync-now`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  campaigns: async (projectId: string, signal?: AbortSignal) => (await api.get<ManagedAd[]>(`${base(projectId)}/campaigns`, { signal })).data,
  existingFacebookAds: async (projectId: string, signal?: AbortSignal) => (await api.get<ExistingFacebookAd[]>(`${base(projectId)}/campaigns/facebook-existing`, { signal })).data,
  importFacebookAds: async (projectId: string, adIds: string[]) => (await api.post<{ importedAds: number; existingAds: number; reservedDailyBudget: number }>(`${base(projectId)}/campaigns/import-facebook`, { adIds }, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  creatives: async (projectId: string, signal?: AbortSignal) => (await api.get<Creative[]>(`${base(projectId)}/creatives`, { signal })).data,
  creativeComparison: async (projectId: string, signal?: AbortSignal) => (await api.get<CreativeComparison[]>(`${base(projectId)}/creative-comparison`, { signal })).data,
  conversions: async (projectId: string, signal?: AbortSignal) => (await api.get<Conversion[]>(`${base(projectId)}/conversions`, { signal })).data,
  attributionTouches: async (projectId: string, signal?: AbortSignal) => (await api.get<AttributionTouch[]>(`${base(projectId)}/outcomes/touches`, { signal })).data,
  conversionDeliveries: async (projectId: string, signal?: AbortSignal) => (await api.get<ConversionDelivery[]>(`${base(projectId)}/outcomes/deliveries`, { signal })).data,
  trackingHealth: async (projectId: string, signal?: AbortSignal) => (await api.get<TrackingHealth[]>(`${base(projectId)}/tracking-health`, { signal })).data,
  decisions: async (projectId: string, signal?: AbortSignal) => (await api.get<AdDecision[]>(`${base(projectId)}/decisions`, { signal })).data,
  decision: async (projectId: string, decisionId: string, signal?: AbortSignal) => (await api.get<AdDecisionDetail>(`${base(projectId)}/decisions/${decisionId}`, { signal })).data,
  offers: async (projectId: string) => (await api.get<AdvertisingOffer[]>(`${base(projectId)}/offers`)).data,
  strategy: async (projectId: string, signal?: AbortSignal) => (await api.get<AdvertisingStrategy>(`${base(projectId)}/strategy`, { signal })).data,
  audiences: async (projectId: string, signal?: AbortSignal) => (await api.get<AudienceStrategy[]>(`${base(projectId)}/audiences`, { signal })).data,
  experiments: async (projectId: string, signal?: AbortSignal) => (await api.get<AdvertisingExperiment[]>(`${base(projectId)}/experiments`, { signal })).data,
  dailyReport: async (projectId: string, date?: string, signal?: AbortSignal) => (await api.get<DailyAdvertisingReport>(`${base(projectId)}/daily-reports`, { params: { date }, signal })).data,
  pagePosts: async (projectId: string) => (await api.get<FacebookPagePost[]>(`${base(projectId)}/facebook/page-posts`)).data,
  importPosts: async (projectId: string, posts: FacebookPagePost[]) => (await api.post<{ creativeIds: string[] }>(`${base(projectId)}/creatives/import-posts`, { posts: posts.map(({ id, mediaType, createdAtUtc }) => ({ id, mediaType, createdAtUtc })) })).data,
  startWhatsAppTest: async (projectId: string) => (await api.post<{ createdAds: number; state: string; reason: string }>(`${base(projectId)}/whatsapp-tests/start`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  activateLaunch: async (projectId: string, input: { offerId: string; creativeIds: string[]; name: string; objective: string; destinationUrl: string; optimizationEvent: string; customEventType?: string }) =>
    (await api.post<{ ads: number; providerState: 'ACTIVATION_QUEUED' | 'PAUSED_PENDING_AI_REVIEW'; queuedCommands: number }>(`${base(projectId)}/launch-plans/activate`, input, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  startOAuth: async (projectId: string) => (await api.post<{ authorizationUrl: string }>(`${base(projectId)}/facebook/oauth/start`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  resources: async (projectId: string, adAccountId?: string, signal?: AbortSignal) => (await api.get<MetaResourceCatalog>(`${base(projectId)}/facebook/resources`, { params: { adAccountId }, signal })).data,
  connection: async (projectId: string, signal?: AbortSignal) => (await api.get<AdvertisingConnection | null>(`${base(projectId)}/connection`, { signal })).data,
  envelope: async (projectId: string, signal?: AbortSignal) => (await api.get<AdvertisingEnvelope | null>(`${base(projectId)}/envelope`, { signal })).data,
  whatsAppAccounts: async (projectId: string, signal?: AbortSignal) => (await api.get<WhatsAppGatewayAccount[]>('/api/whatsapp/accounts', { params: { projectId }, signal })).data,
  gatewayStatus: async (projectId: string, whatsappAccountId?: string, signal?: AbortSignal) => (await api.get<WhatsAppGatewayStatus>('/api/whatsapp/session/status', { params: { projectId, whatsappAccountId }, signal })).data,
  selectConnection: async (projectId: string, input: { adAccountId: string; pageId: string; wabaId?: string; phoneNumberId?: string; datasetId?: string; whatsAppAccountId?: string; integrationMode: 'CloudApiCoexistence' | 'CloudApi' | 'BaileysObservedExperimental' }) =>
    (await api.put<{ connectionId: string; destinationId: string; capabilitySnapshotId: string; state: string }>(`${base(projectId)}/connection`, input, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  saveEnvelope: async (projectId: string, input: { offerId: string; destinationId: string; dailyCap: number; periodCap?: number; periodCapKind: 'Monthly' | 'Total'; currency: string; safetyReservePercent: number; maximumIncreasePercent: number; cooldownHours: number; allowedCountries: string[]; excludedCountries: string[]; minimumAge: number; requiredLanguages: string[]; customAudienceExclusions: string[]; reportingTimezoneIana: string }) =>
    (await api.put<{ id: string; state: string; dailyCap: number; currency: string; version: number }>(`${base(projectId)}/envelope`, input, { headers: { 'Idempotency-Key': crypto.randomUUID() } })).data,
  activateEnvelope: async (projectId: string, envelopeId: string, version: number) =>
    (await api.post(`${base(projectId)}/envelope/${envelopeId}/activate`, {}, { headers: mutationHeaders(version) })).data,
  disable: async (projectId: string, mode: 'PauseManaged' | 'LeaveRunning' = 'PauseManaged', acknowledgeContinuingSpend = false) => api.post(`${base(projectId)}/autopilot/disable`, { mode, reason: 'Operator requested normal stop', acknowledgeContinuingSpend }, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  enable: async (projectId: string) => api.post(`${base(projectId)}/autopilot/enable`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  emergencyStop: async (projectId: string, reason: string) => api.post(`${base(projectId)}/emergency-stop`, { reason }, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  resume: async (projectId: string) => api.post(`${base(projectId)}/emergency-stop/resume`, {}, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  stopState: async (projectId: string, signal?: AbortSignal) => (await api.get<StopState>(`${base(projectId)}/stop-state`, { signal })).data,
  incidents: async (projectId: string, signal?: AbortSignal) => (await api.get(`${base(projectId)}/incidents`, { signal })).data,
  disconnect: async (projectId: string, connectionId: string, version: number, mode: 'PauseManaged' | 'LeaveRunning' = 'PauseManaged') =>
    (await api.delete(`${base(projectId)}/connection/${connectionId}`, { headers: mutationHeaders(version), data: { mode } })).data,
  audit: async (projectId: string, cursor?: string, signal?: AbortSignal) => cursorItems((await api.get(`${base(projectId)}/audit`, { params: { cursor }, signal })).data),
  changes: async (projectId: string, cursor?: string, signal?: AbortSignal) => cursorItems((await api.get(`${base(projectId)}/changes`, { params: { cursor }, signal })).data),
};
