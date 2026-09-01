export interface ReadinessItem { key: string; label: string; ready: boolean; reason?: string }
export interface AdvertisingReadiness { ready: boolean; items: ReadinessItem[] }
export type ResourceState = 'idle' | 'loading' | 'ready' | 'empty' | 'stale' | 'degraded' | 'failed';
export type OperationState = 'queued' | 'running' | 'succeeded' | 'failed' | 'needs-attention';
export interface MetricContext { startUtc: string; endUtc: string; timezoneIana: string; currency: string; attributionWindow: string; truthSource: string }
export interface CursorPage<T> { items: T[]; nextCursor?: string }
export interface ProviderDrift { field: string; planned?: string; configured?: string; effective?: string; severity: 'info' | 'warning' | 'blocking' }
export interface AdvertisingJobStatus { jobName: string; state: string; startedAtUtc: string; completedAtUtc?: string; errorType?: string }
export interface StopState { emergencyStop?: { id: string; trigger: string; state: string; reason: string; activatedAtUtc: string; progress: { total: number; succeeded: number; unknown: number; failed: number; pending: number; continuingSpend: boolean } } | null; disable?: { id: string; mode: string; state: string; completedAtUtc?: string; progress: { total: number; succeeded: number; pending: number; needsAttention: boolean; continuingSpend: boolean; pauseOngoing?: boolean; deliveryMayContinue?: boolean } } | null }
export interface AdvertisingOperations {
  connection?: { state: string; lastValidatedAtUtc?: string; lastSyncAtUtc?: string; lastErrorCode?: string; lastErrorSummary?: string; expiresAtUtc?: string; connected: boolean } | null;
  campaign?: { name: string; dailyBudget: number; effectiveStatus?: string; lastSyncedAtUtc?: string; importedAtUtc?: string; managementSource: string } | null;
  performance: { daysLoaded: number; snapshots: number; lastPulledAtUtc?: string; impressions: number; clicks: number; allTimeSpend: number };
  ai: { model: string; usesProjectApiKey: boolean; latestDecision?: { actionType: string; state: string; createdAt: string } | null };
  tracking: { healthy: boolean; state: string; evaluatedAtUtc?: string; mode: 'UNSAFE_NO_DATASET' | 'DATASET_AND_CRM'; openIncidents: { category: string; severity: string; summary: string; detectedAtUtc: string }[] };
  jobs: AdvertisingJobStatus[];
  lastFailure?: { jobName: string; errorType?: string; startedAtUtc: string } | null;
}
export interface AdvertisingOverview {
  asOfUtc: string;
  windowStartUtc: string;
  windowEndUtc: string;
  spend: number;
  revenue: number;
  roas: number;
  leads: number;
  qualifiedLeads: number;
  bookings: number;
  purchases: number;
  activeAds: number;
  totalAds: number;
  autopilot: boolean;
  emergencyStop: boolean;
  continuingSpend: boolean;
  disableState?: string;
  dailyCap: number;
  usableCap: number;
  aiModel: string;
  usesProjectApiKey: boolean;
  reportingTimezone: string;
  currency: string;
  attributionWindow: string;
  truthSource: string;
  readiness: AdvertisingReadiness;
  operations: AdvertisingOperations;
}

export interface ManagedAd { id: string; name: string; status: string; effectiveStatus: string; dailyBudget: number; publisherPlatform: string; managementSource: string; positionsJson: string; campaignExternalId?: string; adSetExternalId?: string; adExternalId?: string; providerStateHash?: string; lastSyncedAtUtc?: string; importedAtUtc?: string }
export interface ExistingFacebookAd { adId: string; adName: string; campaignId: string; campaignName: string; adSetName: string; status: string; effectiveStatus: string; objective: string; dailyBudget: number; publisherPlatforms: string[]; facebookPositions: string[]; instagramPositions: string[]; messengerPositions: string[]; audienceNetworkPositions: string[]; destination?: string; alreadyManaged: boolean; eligible: boolean; ineligibleReason?: string }
export interface Creative { id: string; sourceType: string; mediaType: string; eligibility: string; recommendationScore: number; recommendationEvidenceJson: string; fatigueState: string }
export interface CreativeComparison { id: string; name: string; mediaType: string; status: string; sourceExternalId?: string; recommendationEvidenceJson: string; adExternalId?: string; campaignExternalId?: string; dailyBudget: number; spend: number; impressions: number; clicks: number; results: number; cpa: number; verdict: string }
export interface Conversion { id: string; canonicalKey?: string; eventType: string; occurredAtUtc: string; currentValue?: number; currency?: string; truthState?: string; attributionState?: string; correctionState?: string; state: string; attributionMethod: string; attributionTouchId?: string }
export interface AttributionTouch { id: string; conversionId?: string; conversationId?: string; destinationId?: string; advertisementId?: string; method: string; hasClickIdentifier: boolean; providerAdExternalId?: string; touchedAtUtc: string }
export interface ConversionDelivery { id: string; conversionId: string; eventName: string; state: string; acceptedAtUtc?: string; nextAttemptAtUtc?: string; suppressionReason?: string }
export interface TrackingHealth { id: string; destinationId: string; state: string; trackingHealthPolicyVersion: number; inboundConversationCount: number; validReferralCount: number; referralCoverage?: number; exactMatchRate?: number; providerMatchQuality?: number; deliveryAcceptanceRate?: number; correctionRate?: number; eventDelayMinutesP95?: number; sourceFreshnessUtc?: string; reasonCodesJson: string; windowStartUtc: string; windowEndUtc: string; evaluatedAtUtc: string }
export interface AdDecision { id: string; actionType: string; targetType: string; riskClass: string; state: string; evidenceStartUtc: string; evidenceEndUtc: string; createdAt: string; reason?: string }
export interface AdDecisionDetail extends AdDecision { targetId?: string; evidenceJson: string; evidenceHash: string; reasonCodesJson: string; proposedChangeJson: string; evaluateAfterUtc?: string; reviews: { reviewerType: string; verdict: string; reasonsJson: string; modelVersion?: string; promptVersion?: string; reviewedAtUtc: string }[]; commands: { id: string; commandType: string; state: string; attemptCount: number; lastError?: string; claimedAtUtc?: string; sentAtUtc?: string; completedAtUtc?: string; reconciledAtUtc?: string; reconciliationEvidenceJson?: string }[]; impacts: { id: string; label: string; goal: string; evaluatedAtUtc: string; rollbackCommandId?: string }[] }
export interface MetaResource { id: string; name: string; currency?: string; timezone?: string; status?: number }
export interface MetaWhatsAppPhone { id: string; displayPhoneNumber: string; verifiedName: string; qualityRating: string }
export interface MetaWaba { id: string; name: string; phones: MetaWhatsAppPhone[] }
export interface MetaResourceCatalog { adAccounts: MetaResource[]; pages: MetaResource[]; datasets: MetaResource[]; wabas: MetaWaba[]; grantedPermissions: string[] }
export interface WhatsAppGatewayAccount { id: string; projectId: string; name: string; isDefault: boolean }
export interface WhatsAppGatewayStatus { projectId: string; whatsappAccountId?: string; status: 'Disconnected' | 'Initializing' | 'Reconnecting' | 'Connected'; phoneNumber?: string | null; error?: string | null }
export interface AdvertisingConnection { id: string; version: number; adAccountExternalId?: string; pageExternalId?: string; datasetExternalId?: string; wabaExternalId?: string; phoneNumberExternalId?: string; whatsAppAccountId?: string; integrationMode?: 'CloudApi' | 'CloudApiCoexistence' | 'BaileysObservedExperimental'; state: string; accountCurrency?: string; accountTimezone?: string }
export interface AdvertisingEnvelope { id: string; dailyCap: number; periodCap?: number; periodCapKind: string; currency: string; allowedCountriesJson: string; hardExcludedGeoJson: string; hardMinimumAge: number; hardRequiredLanguagesJson: string; state: string; version: number }
export interface FacebookPagePost { id: string; message?: string; mediaType: 'Image' | 'Video'; mediaUrl?: string; createdAtUtc?: string }
export interface AdvertisingOffer { id: string; name: string; type: string; price?: number; currency?: string; state: string; destinationsJson: string; marketsJson: string }
export interface AdvertisingStrategy {
  state: 'READY' | 'WAIT'; blockingReasons: string[];
  rankedOffers: { offerId: string; destinationId: string; name: string; type: string; primaryOutcome: string; price?: number; currency?: string; contributionMargin?: number; maximumSustainableCost?: number; currentCapacity?: number; attributionWindowDays: number; score: number; reasons: string[] }[];
  plan?: { id: string; offerId: string; name: string; businessGoal: string; objective: string; optimizationGoal: string; bidStrategy: string; budgetMode: string; dailyBudget: number; currency: string; placementMode: string; startsAtUtc: string; endsAtUtc?: string; specialAdCategory?: string; state: string } | null;
  providerSteps?: { operationType: string; state: string; errorCode?: string; errorSummary?: string; providerTargetId?: string }[];
}
export interface AudienceStrategy { id: string; offerId: string; version: number; includedGeoJson: string; excludedGeoJson: string; minimumAge: number; maximumAgeSuggestion?: number; requiredLanguagesJson: string; customAudienceExclusionsJson: string; audienceSuggestionsJson: string; specialCategoryConstraintsJson: string; estimatedReachJson: string; evidenceJson: string; state: string }
export interface AdvertisingExperiment { id: string; name: string; hypothesis: string; primaryVariable: string; businessOutcome: string; attributionWindowDays: number; minimumElapsedHours: number; minimumSpend: number; minimumAttributedOutcomes: number; minimumAttributionCoverage: number; correctionLagHours: number; confidencePolicyJson: string; budgetCap: number; stopRuleJson: string; state: string; startedAtUtc?: string; maturedAtUtc?: string; stoppedAtUtc?: string; conclusionJson: string; arms: { id: string; name: string; isControl: boolean; changedValueJson: string; allocatedBudget: number; state: string; evidenceJson: string }[] }
export interface DailyAdvertisingReport { date: string; timezone: string; currency: string; startUtc: string; endUtc: string; totals: { entrants: number; qualified: number; bookings: number; spend: number }; rows: { id: string; name: string; adExternalId?: string; source?: { sourceType: string; sourceExternalId?: string; mediaType: string }; entrants: number; qualified: number; bookings: number; spend: number; costPerEntrant?: number; costPerQualified?: number; qualificationRate?: number; bookingRate?: number }[]; unattributed: { entrants: number; qualified: number; bookings: number } }
