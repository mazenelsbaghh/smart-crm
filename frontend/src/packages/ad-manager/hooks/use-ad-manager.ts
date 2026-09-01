'use client';

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import axios from 'axios';
import { adManagerApi } from '../api/ad-manager-api';
import type { AdDecision, AdvertisingExperiment, AdvertisingOverview, AdvertisingStrategy, AttributionTouch, AudienceStrategy, Conversion, ConversionDelivery, Creative, CreativeComparison, DailyAdvertisingReport, ManagedAd, StopState, TrackingHealth } from '../types';

type ResourceKey = 'overview' | 'campaigns' | 'creatives' | 'creativeComparison' | 'conversions'
  | 'attributionTouches' | 'conversionDeliveries' | 'trackingHealth' | 'stopState' | 'decisions'
  | 'strategy' | 'audiences' | 'experiments' | 'dailyReport';
type ResourceRequest = [ResourceKey, Promise<unknown>];
type ResourceResult =
  | { key: ResourceKey; status: 'fulfilled' | 'ignored' }
  | { key: ResourceKey; status: 'rejected'; reason: unknown };
type ResourceLoadReason = 'project-change' | 'view-change' | 'scheduled-refresh' | 'manual-refresh';
type ResourceLoadRequest = {
  signal?: AbortSignal;
  reason: ResourceLoadReason;
};
type ActiveStopStateRequest = { projectId: string; controller: AbortController; promise: Promise<void> };
type ActiveFullRefresh = { key: string; promise: Promise<void> };
type MutationRefreshState = { key: string; trailingRequested: boolean; invalidated: boolean };
type ActiveMutationRefresh = { state: MutationRefreshState; completion: Promise<void> };

const resourceScopeKey = (projectId: string | undefined, activeView: string) => `${projectId ?? ''}:${activeView}`;

const resourceLabels: Record<ResourceKey, string> = {
  overview: 'النظرة العامة', campaigns: 'الحملات', creatives: 'المحتوى', creativeComparison: 'مقارنة المحتوى',
  conversions: 'نتائج واتساب', attributionTouches: 'دلائل الإسناد', conversionDeliveries: 'تسليم التحويلات',
  trackingHealth: 'صحة التتبع', stopState: 'حالة الإيقاف', decisions: 'قرارات AI', strategy: 'الاستراتيجية',
  audiences: 'الجمهور', experiments: 'الاختبارات', dailyReport: 'التقرير اليومي',
};

export interface AdManagerResourceIssue {
  key: ResourceKey;
  label: string;
  lastSuccessfulAt?: string;
  reason: string;
}

const resourceFailureReason = (error: unknown) => {
  if (!axios.isAxiosError<{ code?: string }>(error)) return 'خطأ غير متوقع؛ لم نستبدل آخر بيانات صحيحة.';
  const code = error.response?.data?.code;
  if (code === 'ADS_REPORTING_TIMEZONE_UNKNOWN') return 'توقيت حساب Meta غير موثّق؛ افتح الإعدادات وأعد اختيار الحساب.';
  if (code === 'ADS_REPORTING_TIMEZONE_INVALID') return 'توقيت حساب Meta غير صالح أو غير متاح على الخادم.';
  if (code === 'ADS_REPORTING_CURRENCY_UNKNOWN') return 'عملة حساب Meta غير موثّقة؛ أوقفنا عرض الميزانية بدل افتراض عملة.';
  if (error.response?.status === 401 || error.response?.status === 403) return 'لا توجد صلاحية لقراءة المورد.';
  if (error.response?.status === 429) return 'Meta أو الخادم حدّ الطلبات مؤقتًا؛ حاول لاحقًا.';
  if (!error.response) return 'تعذّر الوصول للخادم؛ تحقق من الشبكة.';
  return `رفض الخادم الطلب (HTTP ${error.response.status}).`;
};

const viewResourceRequests = (
  projectId: string,
  activeView: string,
  signal: AbortSignal,
) => {
  const requests: ResourceRequest[] = [];
  if (activeView === 'overview') requests.push(['dailyReport', adManagerApi.dailyReport(projectId, undefined, signal)]);
  if (activeView === 'strategy') requests.push(['strategy', adManagerApi.strategy(projectId, signal)]);
  if (activeView === 'campaigns' || activeView === 'experiments') requests.push(['campaigns', adManagerApi.campaigns(projectId, signal)]);
  if (activeView === 'creatives') requests.push(
    ['creatives', adManagerApi.creatives(projectId, signal)],
    ['creativeComparison', adManagerApi.creativeComparison(projectId, signal)],
    ['decisions', adManagerApi.decisions(projectId, signal)],
  );
  if (activeView === 'audiences') requests.push(['audiences', adManagerApi.audiences(projectId, signal)]);
  if (activeView === 'experiments') requests.push(['experiments', adManagerApi.experiments(projectId, signal)]);
  if (activeView === 'conversions') requests.push(
    ['conversions', adManagerApi.conversions(projectId, signal)],
    ['attributionTouches', adManagerApi.attributionTouches(projectId, signal)],
    ['conversionDeliveries', adManagerApi.conversionDeliveries(projectId, signal)],
    ['trackingHealth', adManagerApi.trackingHealth(projectId, signal)],
  );
  if (activeView === 'decisions') requests.push(['decisions', adManagerApi.decisions(projectId, signal)]);
  return requests;
};

const resourceRequestsForLoad = (
  projectId: string,
  activeView: string,
  signal: AbortSignal,
  reason: ResourceLoadReason,
) => {
  const requests = viewResourceRequests(projectId, activeView, signal);
  if (reason !== 'view-change') requests.unshift(['overview', adManagerApi.overview(projectId, signal)]);
  if (reason === 'project-change' || reason === 'manual-refresh') {
    requests.push(['stopState', adManagerApi.stopState(projectId, signal)]);
  }
  return requests;
};

const stopOperationInProgress = (stopState: StopState | null) =>
  stopState?.emergencyStop?.state === 'PausingManaged'
  || stopState?.disable?.state === 'PausingManaged';

const resourceIssueMessage = (resourceIssues: AdManagerResourceIssue[]) => {
  if (!resourceIssues.length) return null;
  const issueSummary = resourceIssues.map(issue => {
    const lastSuccess = issue.lastSuccessfulAt
      ? ` آخر نجاح ${new Intl.DateTimeFormat('ar-EG', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' }).format(new Date(issue.lastSuccessfulAt))} UTC.`
      : '';
    return `${issue.label}: ${issue.reason}${lastSuccess}`;
  }).join(' ');
  const headline = resourceIssues.some(issue => issue.key === 'overview')
    ? 'تعذّر تحميل مورد أساسي.'
    : 'تعذّر تحديث بعض الموارد واحتفظنا بآخر بيانات صحيحة.';
  return `${headline} ${issueSummary}`;
};

const failedResourceIssues = (
  resourceResults: ResourceResult[],
  lastSuccessByResource: Partial<Record<ResourceKey, string>>,
) => {
  const failures: AdManagerResourceIssue[] = [];
  resourceResults.forEach(resourceResult => {
    if (resourceResult.status !== 'rejected') return;
    const { key } = resourceResult;
    failures.push({
      key,
      label: resourceLabels[key],
      lastSuccessfulAt: lastSuccessByResource[key],
      reason: resourceFailureReason(resourceResult.reason),
    });
  });
  return failures;
};

export function useAdManager(projectId?: string, activeView = 'overview') {
  const [overview, setOverview] = useState<AdvertisingOverview | null>(null);
  const [campaigns, setCampaigns] = useState<ManagedAd[]>([]);
  const [creatives, setCreatives] = useState<Creative[]>([]);
  const [creativeComparison, setCreativeComparison] = useState<CreativeComparison[]>([]);
  const [conversions, setConversions] = useState<Conversion[]>([]);
  const [attributionTouches, setAttributionTouches] = useState<AttributionTouch[]>([]);
  const [conversionDeliveries, setConversionDeliveries] = useState<ConversionDelivery[]>([]);
  const [trackingHealth, setTrackingHealth] = useState<TrackingHealth[]>([]);
  const [stopState, setStopState] = useState<StopState | null>(null);
  const [decisions, setDecisions] = useState<AdDecision[]>([]);
  const [strategy, setStrategy] = useState<AdvertisingStrategy | null>(null);
  const [audiences, setAudiences] = useState<AudienceStrategy[]>([]);
  const [experiments, setExperiments] = useState<AdvertisingExperiment[]>([]);
  const [dailyReport, setDailyReport] = useState<DailyAdvertisingReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [resourceIssues, setResourceIssues] = useState<AdManagerResourceIssue[]>([]);
  const generation = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);
  const activeLoad = useRef<Promise<void> | null>(null);
  const activeFullRefresh = useRef<ActiveFullRefresh | null>(null);
  const activeMutationRefresh = useRef<ActiveMutationRefresh | null>(null);
  const activeStopStateRequest = useRef<ActiveStopStateRequest | null>(null);
  const loadedProject = useRef<string | undefined>(undefined);
  const currentProject = useRef(projectId);
  const currentResourceScope = useRef(resourceScopeKey(projectId, activeView));
  const latestStopState = useRef<StopState | null>(null);
  const freshness = useRef<Partial<Record<ResourceKey, string>>>({});
  const error = useMemo(() => resourceIssueMessage(resourceIssues), [resourceIssues]);

  useLayoutEffect(() => {
    const nextScope = resourceScopeKey(projectId, activeView);
    currentProject.current = projectId;
    if (currentResourceScope.current === nextScope) return;
    currentResourceScope.current = nextScope;
    activeRequest.current?.abort();
    activeLoad.current = null;
    activeFullRefresh.current = null;
    const oldMutationRefresh = activeMutationRefresh.current;
    if (oldMutationRefresh) {
      oldMutationRefresh.state.invalidated = true;
      oldMutationRefresh.state.trailingRequested = false;
      activeMutationRefresh.current = null;
    }
  }, [activeView, projectId]);

  const clearStopStateIssue = useCallback(() => {
    setResourceIssues(currentIssues => currentIssues.filter(issue => issue.key !== 'stopState'));
  }, []);

  const reportStopStateIssue = useCallback((failure: unknown) => {
    setResourceIssues(currentIssues => {
      const otherIssues = currentIssues.filter(issue => issue.key !== 'stopState');
      return [...otherIssues, {
        key: 'stopState',
        label: resourceLabels.stopState,
        lastSuccessfulAt: freshness.current.stopState,
        reason: resourceFailureReason(failure),
      }];
    });
  }, []);

  const applyStopStateOutcome = useCallback((
    requestedProject: string,
    controller: AbortController,
    outcome: PromiseSettledResult<StopState>,
  ) => {
    if (controller.signal.aborted || currentProject.current !== requestedProject) return;
    if (outcome.status === 'rejected') {
      reportStopStateIssue(outcome.reason);
      return;
    }
    freshness.current.stopState = new Date().toISOString();
    latestStopState.current = outcome.value;
    setStopState(outcome.value);
    clearStopStateIssue();
  }, [clearStopStateIssue, reportStopStateIssue]);

  const refreshStopState = useCallback(() => {
    if (!projectId) return Promise.resolve();
    const inFlightRequest = activeStopStateRequest.current;
    if (inFlightRequest?.projectId === projectId) return inFlightRequest.promise;

    const requestController = new AbortController();
    const stopStatePromise = Promise.allSettled([adManagerApi.stopState(projectId, requestController.signal)])
      .then(([stopStateOutcome]) => applyStopStateOutcome(projectId, requestController, stopStateOutcome));
    activeStopStateRequest.current = { projectId, controller: requestController, promise: stopStatePromise };
    const release = () => {
      if (activeStopStateRequest.current?.promise === stopStatePromise) activeStopStateRequest.current = null;
    };
    void stopStatePromise.then(release, release);
    return stopStatePromise;
  }, [applyStopStateOutcome, projectId]);

  const refreshStopDetailsFromOverview = useCallback((updatedOverview: AdvertisingOverview) => {
    const knownStopState = latestStopState.current;
    if (!updatedOverview.emergencyStop && knownStopState?.emergencyStop) {
      const clearedEmergencyStop = { ...knownStopState, emergencyStop: null };
      latestStopState.current = clearedEmergencyStop;
      setStopState(clearedEmergencyStop);
    }
    const emergencyDetailsMissing = updatedOverview.emergencyStop && !knownStopState?.emergencyStop;
    const disableProgressMissing = updatedOverview.disableState === 'PausingManaged'
      && knownStopState?.disable?.state !== 'PausingManaged';
    if (emergencyDetailsMissing || disableProgressMissing) void refreshStopState();
  }, [refreshStopState]);

  const applyResource = useCallback((key: ResourceKey, resourcePayload: unknown) => {
    switch (key) {
      case 'overview': setOverview(resourcePayload as AdvertisingOverview); break;
      case 'campaigns': setCampaigns(resourcePayload as ManagedAd[]); break;
      case 'creatives': setCreatives(resourcePayload as Creative[]); break;
      case 'creativeComparison': setCreativeComparison(resourcePayload as CreativeComparison[]); break;
      case 'conversions': setConversions(resourcePayload as Conversion[]); break;
      case 'attributionTouches': setAttributionTouches(resourcePayload as AttributionTouch[]); break;
      case 'conversionDeliveries': setConversionDeliveries(resourcePayload as ConversionDelivery[]); break;
      case 'trackingHealth': setTrackingHealth(resourcePayload as TrackingHealth[]); break;
      case 'stopState':
        latestStopState.current = resourcePayload as StopState;
        setStopState(resourcePayload as StopState);
        break;
      case 'decisions': setDecisions(resourcePayload as AdDecision[]); break;
      case 'strategy': setStrategy(resourcePayload as AdvertisingStrategy); break;
      case 'audiences': setAudiences(resourcePayload as AudienceStrategy[]); break;
      case 'experiments': setExperiments(resourcePayload as AdvertisingExperiment[]); break;
      case 'dailyReport': setDailyReport(resourcePayload as DailyAdvertisingReport); break;
    }
  }, []);

  const clearProjectResources = useCallback(() => {
    setOverview(null); setCampaigns([]); setCreatives([]); setCreativeComparison([]); setConversions([]);
    setAttributionTouches([]); setConversionDeliveries([]); setTrackingHealth([]); setStopState(null);
    setDecisions([]); setStrategy(null); setAudiences([]); setExperiments([]); setDailyReport(null);
    latestStopState.current = null;
    freshness.current = {};
  }, []);

  const loadResources = useCallback(async ({
    signal: externalSignal,
    reason: loadReason,
  }: ResourceLoadRequest) => {
    const loadScope = resourceScopeKey(projectId, activeView);
    if (currentResourceScope.current !== loadScope) return;
    activeRequest.current?.abort();
    const requestController = new AbortController();
    activeRequest.current = requestController;
    const requestGeneration = ++generation.current;
    const forwardAbort = () => requestController.abort();
    if (externalSignal?.aborted) requestController.abort();
    else externalSignal?.addEventListener('abort', forwardAbort, { once: true });

    if (loadReason === 'project-change') clearProjectResources();
    if (!projectId) {
      externalSignal?.removeEventListener('abort', forwardAbort);
      if (activeRequest.current === requestController) activeRequest.current = null;
      loadedProject.current = undefined;
      setLoading(false); setRefreshing(false); setResourceIssues([]);
      return;
    }
    const resourceRequests = resourceRequestsForLoad(projectId, activeView, requestController.signal, loadReason);
    if (loadReason === 'project-change') {
      setLoading(true);
      setRefreshing(false);
    } else if (resourceRequests.length) setRefreshing(true);

    if (!resourceRequests.length) {
      externalSignal?.removeEventListener('abort', forwardAbort);
      if (activeRequest.current === requestController) activeRequest.current = null;
      return;
    }

    const settleResource = async ([key, resourceRequest]: ResourceRequest): Promise<ResourceResult> => {
      const [resourceOutcome] = await Promise.allSettled([resourceRequest]);
      if (resourceOutcome.status === 'rejected') return { key, status: 'rejected', reason: resourceOutcome.reason };
      if (requestController.signal.aborted || requestGeneration !== generation.current
        || currentResourceScope.current !== loadScope) {
        return { key, status: 'ignored' };
      }

      freshness.current[key] = new Date().toISOString();
      applyResource(key, resourceOutcome.value);
      if (key === 'overview' && loadReason === 'scheduled-refresh') {
        refreshStopDetailsFromOverview(resourceOutcome.value as AdvertisingOverview);
      }
      return { key, status: 'fulfilled' };
    };

    try {
      const resourceResults = await Promise.all(resourceRequests.map(settleResource));
      if (requestController.signal.aborted || requestGeneration !== generation.current
        || currentResourceScope.current !== loadScope) return;
      const loadIssues = failedResourceIssues(resourceResults, freshness.current);

      const includesStopState = resourceResults.some(resourceResult => resourceResult.key === 'stopState');
      setResourceIssues(currentIssues => {
        const previousStopIssue = currentIssues.find(issue => issue.key === 'stopState');
        return !includesStopState && previousStopIssue ? [previousStopIssue, ...loadIssues] : loadIssues;
      });
      if (loadReason === 'project-change') loadedProject.current = projectId;
    } finally {
      externalSignal?.removeEventListener('abort', forwardAbort);
      if (activeRequest.current === requestController) activeRequest.current = null;
      if (requestGeneration === generation.current) {
        setLoading(false);
        setRefreshing(false);
      }
    }
  }, [activeView, applyResource, clearProjectResources, projectId, refreshStopDetailsFromOverview]);

  const startLoad = useCallback((loadRequest: ResourceLoadRequest) => {
    if (currentResourceScope.current !== resourceScopeKey(projectId, activeView)) return Promise.resolve();
    const loadPromise = loadResources(loadRequest);
    activeLoad.current = loadPromise;
    const release = () => {
      if (activeLoad.current === loadPromise) activeLoad.current = null;
    };
    void loadPromise.then(release, release);
    return loadPromise;
  }, [activeView, loadResources, projectId]);

  const refreshWhenIdle = useCallback((signal: AbortSignal) => {
    if (activeLoad.current) return activeLoad.current;
    return startLoad({ signal, reason: 'scheduled-refresh' });
  }, [startLoad]);

  const beginFullRefresh = useCallback(() => {
    const refreshKey = resourceScopeKey(projectId, activeView);
    if (currentResourceScope.current !== refreshKey) return Promise.resolve();
    const refreshPromise = startLoad({ reason: 'manual-refresh' });
    activeFullRefresh.current = { key: refreshKey, promise: refreshPromise };
    const release = () => {
      if (activeFullRefresh.current?.promise === refreshPromise) activeFullRefresh.current = null;
    };
    void refreshPromise.then(release, release);
    return refreshPromise;
  }, [activeView, projectId, startLoad]);

  const refresh = useCallback(() => {
    const refreshKey = resourceScopeKey(projectId, activeView);
    if (currentResourceScope.current !== refreshKey) return Promise.resolve();
    const inFlightRefresh = activeFullRefresh.current;
    if (inFlightRefresh?.key === refreshKey) return inFlightRefresh.promise;
    return beginFullRefresh();
  }, [activeView, beginFullRefresh, projectId]);

  const drainMutationRefreshes = useCallback(async (refreshState: MutationRefreshState) => {
    try {
      while (!refreshState.invalidated && currentResourceScope.current === refreshState.key) {
        refreshState.trailingRequested = false;
        await beginFullRefresh();
        if (!refreshState.trailingRequested) break;
      }
    } finally {
      if (activeMutationRefresh.current?.state === refreshState) activeMutationRefresh.current = null;
    }
  }, [beginFullRefresh]);

  const refreshAfterMutation = useCallback(() => {
    const refreshKey = resourceScopeKey(projectId, activeView);
    if (currentResourceScope.current !== refreshKey) return Promise.resolve();
    const inFlightMutationRefresh = activeMutationRefresh.current;
    if (inFlightMutationRefresh?.state.key === refreshKey) {
      inFlightMutationRefresh.state.trailingRequested = true;
      return inFlightMutationRefresh.completion;
    }
    const refreshState: MutationRefreshState = { key: refreshKey, trailingRequested: false, invalidated: false };
    const completion = drainMutationRefreshes(refreshState);
    activeMutationRefresh.current = { state: refreshState, completion };
    return completion;
  }, [activeView, drainMutationRefreshes, projectId]);

  useEffect(() => {
    const effectController = new AbortController();
    const requestBackgroundRefresh = () => {
      if (effectController.signal.aborted) return;
      void refreshWhenIdle(effectController.signal);
    };
    const initialLoadTask = window.setTimeout(() => {
      const loadReason = loadedProject.current === projectId ? 'view-change' : 'project-change';
      void startLoad({ signal: effectController.signal, reason: loadReason });
    }, 0);
    const pollTimer = window.setInterval(() => {
      if (!document.hidden) requestBackgroundRefresh();
    }, 120_000);
    const handleVisibility = () => {
      if (!document.hidden) requestBackgroundRefresh();
    };
    document.addEventListener('visibilitychange', handleVisibility);
    return () => {
      effectController.abort(); activeRequest.current?.abort(); window.clearTimeout(initialLoadTask); window.clearInterval(pollTimer);
      document.removeEventListener('visibilitychange', handleVisibility);
    };
  }, [projectId, refreshWhenIdle, startLoad]);

  const pollingStopProgress = stopOperationInProgress(stopState);
  useEffect(() => {
    if (!pollingStopProgress) return;
    const requestProgress = () => {
      if (!document.hidden) void refreshStopState();
    };
    const pollTimer = window.setInterval(requestProgress, 5_000);
    document.addEventListener('visibilitychange', requestProgress);
    return () => {
      window.clearInterval(pollTimer);
      document.removeEventListener('visibilitychange', requestProgress);
    };
  }, [pollingStopProgress, refreshStopState]);

  useEffect(() => () => {
    activeStopStateRequest.current?.controller.abort();
  }, [projectId]);

  return { overview, campaigns, creatives, creativeComparison, conversions, attributionTouches, conversionDeliveries,
    trackingHealth, stopState, decisions, strategy, audiences, experiments, dailyReport, loading, refreshing, error,
    resourceIssues, refresh, refreshAfterMutation };
}
