export type ContentPostStatus =
  | 'Generating'
  | 'AwaitingApproval'
  | 'Approved'
  | 'Publishing'
  | 'Published'
  | 'GenerationFailed'
  | 'PublishFailed'
  | 'Rejected'
  | 'PublishUnknown';

export interface ContentPost {
  id: string;
  status: ContentPostStatus;
  isStyleSample: boolean;
  topic: string;
  visualHeadline: string;
  caption: string;
  imageModel: string;
  imageSize: string;
  knowledgeDocumentCount: number;
  scheduledForUtc?: string;
  generatedAtUtc?: string;
  approvedAtUtc?: string;
  publishedAtUtc?: string;
  facebookPostId?: string;
  error?: string;
  createdAt: string;
  imageUrl?: string;
}

export type ContentWeekPlanStatus =
  | 'Generating'
  | 'AwaitingApproval'
  | 'Approved'
  | 'Completed'
  | 'Rejected'
  | 'GenerationFailed';

export interface ContentWeekPlanItem {
  id: string;
  dayIndex: number;
  scheduledForUtc: string;
  topic: string;
  visualHeadline: string;
  caption: string;
  contentPostId?: string;
  postStatus?: ContentPostStatus;
  postPublishedAtUtc?: string;
  postError?: string;
  imageSize?: string;
  imageUrl?: string;
}

export interface ContentWeekPlan {
  id: string;
  status: ContentWeekPlanStatus;
  startDateLocal: string;
  dailyPublishTimeLocal: string;
  timezone: string;
  knowledgeDocumentCount: number;
  generatedAtUtc?: string;
  approvedAtUtc?: string;
  completedAtUtc?: string;
  error?: string;
  items: ContentWeekPlanItem[];
}

export interface ContentStudioData {
  imageModel: string;
  imageSize: string;
  aspectRatio: string;
  aiConfigured: boolean;
  knowledgeDocumentCount: number;
  connectedPages: Array<{ pageId: string; pageName: string }>;
  settings: {
    facebookPageId?: string;
    facebookPageName?: string;
    isEnabled: boolean;
    hasApprovedStyle: boolean;
    dailyPublishTimeLocal: string;
    timezone: string;
    nextPublishAtUtc?: string;
    lastPublishedAtUtc?: string;
    lastError?: string;
    stylePrompt: string;
    logoFileName?: string;
    logoUrl?: string;
    brandColors: string[];
    approvedSamplePostId?: string;
  };
  weeklyPlan?: ContentWeekPlan;
  weeklyPlans?: ContentWeekPlan[];
  posts: ContentPost[];
}

export interface UpdateContentSettings {
  facebookPageId?: string;
  dailyPublishTimeLocal: string;
  stylePrompt: string;
  isEnabled: boolean;
}

export type ContentVideoAspectRatio = '9:16' | '16:9';
export type ContentVideoResolution = '360p' | '720p' | '1080p';

export type ContentVideoStatus =
  | 'Planning'
  | 'AwaitingApproval'
  | 'Generating'
  | 'Assembling'
  | 'Ready'
  | 'PlanningFailed'
  | 'GenerationFailed'
  | 'AssemblyFailed';

export type ContentVideoSceneStatus =
  | 'Planned'
  | 'Queued'
  | 'Submitting'
  | 'Submitted'
  | 'Generating'
  | 'Completed'
  | 'RecoveryRequired'
  | 'SubmissionUncertain'
  | 'Failed';

export interface ContentVideoReadiness {
  configured: boolean;
  geminiApiKeyConfigured: boolean;
  geminiAgentPlatformApiKeyConfigured: boolean;
  enterpriseProjectId: string | null;
  model: string;
  supportedAspectRatios: ContentVideoAspectRatio[];
  supportedResolutions: ContentVideoResolution[];
  knowledgeDocumentCount: number;
  reason: string | null;
}

export interface ContentVideoSummary {
  id: string;
  status: ContentVideoStatus;
  ideaTitle: string;
  hook: string;
  summary: string;
  caption: string;
  aspectRatio: ContentVideoAspectRatio;
  resolution: ContentVideoResolution;
  sceneCount: number;
  requestedSceneCount: number;
  requestedSceneDurationSeconds: number;
  completedSceneCount: number;
  knowledgeWasTruncated: boolean;
  error: string | null;
  createdAt: string;
  updatedAt: string;
  finalVideoUrl: string | null;
}

export interface ContentVideoScene {
  id: string;
  sceneIndex: number;
  title: string;
  narrative: string;
  visualPrompt: string;
  audioPrompt: string;
  transitionPrompt: string;
  durationSeconds: number;
  status: ContentVideoSceneStatus;
  error: string | null;
  videoUrl: string | null;
}

export interface ContentVideo extends ContentVideoSummary {
  scenes: ContentVideoScene[];
}

export type ContentVideoSceneRetryIntent =
  | { mode: 'safe' }
  | { mode: 'confirmed-possible-duplicate' };

export interface ContentVideosData {
  readiness: ContentVideoReadiness;
  videos: ContentVideoSummary[];
}

export interface CreateContentVideoPlan {
  brief?: string;
  sceneCount: number;
  durationSeconds: number;
  aspectRatio: ContentVideoAspectRatio;
  resolution: Extract<ContentVideoResolution, '720p' | '1080p'>;
}

export interface ContentVideoPlanAccepted {
  id: string;
  status: 'Planning';
  message: string;
}
