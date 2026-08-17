export interface ReadinessItem { key: string; label: string; ready: boolean; reason?: string }
export interface AdvertisingReadiness { ready: boolean; items: ReadinessItem[] }
export interface AdvertisingOverview {
  asOfUtc: string;
  spend: number;
  revenue: number;
  roas: number;
  leads: number;
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
}

export interface ManagedAd { id: string; name: string; status: string; effectiveStatus: string; dailyBudget: number; publisherPlatform: string; managementSource: string; positionsJson: string; lastSyncedAtUtc?: string; importedAtUtc?: string }
export interface ExistingFacebookAd { adId: string; adName: string; campaignId: string; campaignName: string; adSetName: string; status: string; effectiveStatus: string; objective: string; dailyBudget: number; publisherPlatforms: string[]; facebookPositions: string[]; instagramPositions: string[]; messengerPositions: string[]; audienceNetworkPositions: string[]; destination?: string; alreadyManaged: boolean; eligible: boolean; ineligibleReason?: string }
export interface Creative { id: string; sourceType: string; mediaType: string; eligibility: string; recommendationScore: number; recommendationEvidenceJson: string; fatigueState: string }
export interface Conversion { id: string; eventType: string; occurredAtUtc: string; currentValue?: number; currency?: string; state: string; attributionMethod: string }
export interface AdDecision { id: string; actionType: string; targetType: string; riskClass: string; state: string; evidenceStartUtc: string; evidenceEndUtc: string; createdAt: string }
export interface MetaResource { id: string; name: string; currency?: string; timezone?: string; status?: number }
export interface MetaResourceCatalog { adAccounts: MetaResource[]; pages: MetaResource[]; datasets: MetaResource[] }
export interface AdvertisingConnection { adAccountExternalId?: string; pageExternalId?: string; datasetExternalId?: string; state: string; accountCurrency?: string; accountTimezone?: string }
export interface FacebookPagePost { id: string; message?: string; mediaType: 'Image' | 'Video'; mediaUrl?: string; createdAtUtc?: string }
export interface AdvertisingOffer { id: string; name: string; type: string; price?: number; currency?: string; state: string; destinationsJson: string; marketsJson: string }
