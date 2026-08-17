export interface ReadinessItem { key: string; label: string; ready: boolean; reason?: string }
export interface AdvertisingReadiness { ready: boolean; items: ReadinessItem[] }
export interface AdvertisingJobStatus { jobName: string; state: string; startedAtUtc: string; completedAtUtc?: string; errorType?: string }
export interface AdvertisingOperations {
  connection?: { state: string; lastValidatedAtUtc?: string; lastSyncAtUtc?: string; lastErrorCode?: string; lastErrorSummary?: string; expiresAtUtc?: string; connected: boolean } | null;
  campaign?: { name: string; dailyBudget: number; effectiveStatus?: string; lastSyncedAtUtc?: string; importedAtUtc?: string; managementSource: string } | null;
  performance: { daysLoaded: number; snapshots: number; lastPulledAtUtc?: string; impressions: number; clicks: number; allTimeSpend: number };
  ai: { model: string; usesProjectApiKey: boolean; latestDecision?: { actionType: string; state: string; createdAt: string } | null };
  tracking: { healthy: boolean; mode: 'CRM_WHATSAPP' | 'DATASET_AND_CRM'; openIncidents: { category: string; severity: string; summary: string; detectedAtUtc: string }[] };
  jobs: AdvertisingJobStatus[];
  lastFailure?: { jobName: string; errorType?: string; startedAtUtc: string } | null;
}
export interface AdvertisingOverview {
  asOfUtc: string;
  spend: number;
  revenue: number;
  roas: number;
  leads: number;
  bookings: number;
  purchases: number;
  activeAds: number;
  totalAds: number;
  autopilot: boolean;
  emergencyStop: boolean;
  dailyCap: number;
  usableCap: number;
  aiModel: string;
  usesProjectApiKey: boolean;
  readiness: AdvertisingReadiness;
  operations: AdvertisingOperations;
}

export interface ManagedAd { id: string; name: string; status: string; effectiveStatus: string; dailyBudget: number; publisherPlatform: string; managementSource: string; positionsJson: string; lastSyncedAtUtc?: string; importedAtUtc?: string }
export interface ExistingFacebookAd { adId: string; adName: string; campaignId: string; campaignName: string; adSetName: string; status: string; effectiveStatus: string; objective: string; dailyBudget: number; publisherPlatforms: string[]; facebookPositions: string[]; instagramPositions: string[]; messengerPositions: string[]; audienceNetworkPositions: string[]; destination?: string; alreadyManaged: boolean; eligible: boolean; ineligibleReason?: string }
export interface Creative { id: string; sourceType: string; mediaType: string; eligibility: string; recommendationScore: number; recommendationEvidenceJson: string; fatigueState: string }
export interface CreativeComparison { id: string; name: string; mediaType: string; status: string; spend: number; impressions: number; clicks: number; results: number; cpa: number; verdict: string }
export interface Conversion { id: string; eventType: string; occurredAtUtc: string; currentValue?: number; currency?: string; state: string; attributionMethod: string }
export interface AdDecision { id: string; actionType: string; targetType: string; riskClass: string; state: string; evidenceStartUtc: string; evidenceEndUtc: string; createdAt: string }
export interface MetaResource { id: string; name: string; currency?: string; timezone?: string; status?: number }
export interface MetaResourceCatalog { adAccounts: MetaResource[]; pages: MetaResource[]; datasets: MetaResource[] }
export interface AdvertisingConnection { adAccountExternalId?: string; pageExternalId?: string; datasetExternalId?: string; state: string; accountCurrency?: string; accountTimezone?: string }
export interface FacebookPagePost { id: string; message?: string; mediaType: 'Image' | 'Video'; mediaUrl?: string; createdAtUtc?: string }
export interface AdvertisingOffer { id: string; name: string; type: string; price?: number; currency?: string; state: string; destinationsJson: string; marketsJson: string }
