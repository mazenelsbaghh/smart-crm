import { StrictMode, type ReactNode } from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { adManagerApi, cursorItems } from '../api/ad-manager-api';
import { useAdManager } from '../hooks/use-ad-manager';
import type { AdvertisingOverview } from '../types';

vi.mock('../api/ad-manager-api', async importOriginal => {
  const original = await importOriginal<typeof import('../api/ad-manager-api')>();
  const method = () => vi.fn();
  return { ...original, adManagerApi: { overview: method(), campaigns: method(), creatives: method(), creativeComparison: method(), conversions: method(), attributionTouches: method(), conversionDeliveries: method(), trackingHealth: method(), stopState: method(), decisions: method(), strategy: method(), audiences: method(), experiments: method(), dailyReport: method(), operation: method() } };
});

const overview = (currency: string): AdvertisingOverview => ({
  asOfUtc: '2026-08-19T00:00:00Z', windowStartUtc: '2026-08-18T21:00:00Z', windowEndUtc: '2026-08-19T00:00:00Z', spend: 0, revenue: 0, roas: 0, leads: 0, qualifiedLeads: 0, bookings: 0, purchases: 0,
  activeAds: 0, totalAds: 0, autopilot: false, emergencyStop: false, continuingSpend: false, dailyCap: 100,
  usableCap: 85, aiModel: 'gemini', usesProjectApiKey: false, reportingTimezone: 'Africa/Cairo', currency,
  attributionWindow: '7d click', truthSource: 'CRM', readiness: { ready: false, items: [] },
  operations: { performance: { daysLoaded: 0, snapshots: 0, impressions: 0, clicks: 0, allTimeSpend: 0 }, ai: { model: 'gemini', usesProjectApiKey: false }, tracking: { healthy: false, state: 'Unknown', mode: 'UNSAFE_NO_DATASET', openIncidents: [] }, jobs: [] }
});

const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; };
const StrictModeWrapper = ({ children }: { children: ReactNode }) => <StrictMode>{children}</StrictMode>;

beforeEach(() => {
  vi.clearAllMocks();
  for (const key of ['campaigns', 'creatives', 'creativeComparison', 'conversions', 'attributionTouches', 'conversionDeliveries', 'trackingHealth', 'decisions', 'audiences', 'experiments'] as const)
    vi.mocked(adManagerApi[key]).mockResolvedValue([] as never);
  vi.mocked(adManagerApi.strategy).mockResolvedValue({ state: 'WAIT', blockingReasons: [], rankedOffers: [] });
  vi.mocked(adManagerApi.stopState).mockResolvedValue({ emergencyStop: null, disable: null });
  vi.mocked(adManagerApi.dailyReport).mockResolvedValue({
    date: '2026-08-19', timezone: 'Africa/Cairo', currency: 'EGP', startUtc: '2026-08-18T21:00:00Z', endUtc: '2026-08-19T21:00:00Z',
    totals: { entrants: 0, qualified: 0, bookings: 0, spend: 0 }, rows: [], unattributed: { entrants: 0, qualified: 0, bookings: 0 },
  });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('useAdManager project safety', () => {
  it('2026-08 Strict Mode regression completes the first visible load', async () => {
    vi.mocked(adManagerApi.overview).mockResolvedValue(overview('EGP'));

    const { result } = renderHook(() => useAdManager('project'), { wrapper: StrictModeWrapper });

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.overview?.currency).toBe('EGP');
  });

  it('publishes the overview before a slower secondary report finishes', async () => {
    const slowReport = deferred<Awaited<ReturnType<typeof adManagerApi.dailyReport>>>();
    vi.mocked(adManagerApi.overview).mockResolvedValue(overview('EGP'));
    vi.mocked(adManagerApi.dailyReport).mockReturnValue(slowReport.promise);

    const { result } = renderHook(() => useAdManager('project'));

    await waitFor(() => expect(result.current.overview?.currency).toBe('EGP'));
    expect(result.current.loading).toBe(true);

    await act(async () => slowReport.resolve({
      date: '2026-08-19', timezone: 'Africa/Cairo', currency: 'EGP',
      startUtc: '2026-08-18T21:00:00Z', endUtc: '2026-08-19T21:00:00Z',
      totals: { entrants: 0, qualified: 0, bookings: 0, spend: 0 }, rows: [],
      unattributed: { entrants: 0, qualified: 0, bookings: 0 },
    }));
    await waitFor(() => expect(result.current.loading).toBe(false));
  });

  it('does not queue a duplicate visibility refresh while current data is loading', async () => {
    const firstRequest = deferred<AdvertisingOverview>();
    let firstSignal: AbortSignal | undefined;
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    vi.mocked(adManagerApi.overview)
      .mockImplementationOnce((_projectId, signal) => {
        firstSignal = signal;
        return firstRequest.promise;
      })
      .mockResolvedValue(overview('EGP'));
    const { result } = renderHook(() => useAdManager('project'));
    await waitFor(() => expect(adManagerApi.overview).toHaveBeenCalledOnce());

    act(() => document.dispatchEvent(new Event('visibilitychange')));
    expect(adManagerApi.overview).toHaveBeenCalledOnce();
    expect(firstSignal?.aborted).toBe(false);

    await act(async () => firstRequest.resolve(overview('EGP')));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(adManagerApi.overview).toHaveBeenCalledOnce();
    expect(result.current.overview?.currency).toBe('EGP');
  });

  it('coalesces repeated manual refreshes and exposes familiar refresh feedback', async () => {
    vi.mocked(adManagerApi.overview).mockResolvedValueOnce(overview('EGP'));
    const { result } = renderHook(() => useAdManager('project'));
    await waitFor(() => expect(result.current.loading).toBe(false));

    const refreshedOverview = deferred<AdvertisingOverview>();
    vi.mocked(adManagerApi.overview).mockReturnValue(refreshedOverview.promise);
    act(() => {
      void result.current.refresh();
      void result.current.refresh();
    });

    expect(result.current.refreshing).toBe(true);
    expect(adManagerApi.overview).toHaveBeenCalledTimes(2);
    expect(adManagerApi.stopState).toHaveBeenCalledTimes(2);

    await act(async () => refreshedOverview.resolve(overview('USD')));
    await waitFor(() => expect(result.current.refreshing).toBe(false));
    expect(result.current.overview?.currency).toBe('USD');
  });

  it('runs a fresh read after mutation instead of reusing an older manual refresh', async () => {
    vi.mocked(adManagerApi.overview).mockResolvedValueOnce(overview('EGP'));
    const { result } = renderHook(() => useAdManager('project'));
    await waitFor(() => expect(result.current.loading).toBe(false));

    const olderManualRefresh = deferred<AdvertisingOverview>();
    const postMutationRefresh = deferred<AdvertisingOverview>();
    vi.mocked(adManagerApi.overview)
      .mockReturnValueOnce(olderManualRefresh.promise)
      .mockReturnValueOnce(postMutationRefresh.promise);
    act(() => {
      void result.current.refresh();
      void result.current.refreshAfterMutation();
    });

    expect(adManagerApi.overview).toHaveBeenCalledTimes(3);
    await act(async () => olderManualRefresh.resolve(overview('STALE')));
    expect(result.current.overview?.currency).toBe('EGP');

    await act(async () => postMutationRefresh.resolve(overview('USD')));
    await waitFor(() => expect(result.current.refreshing).toBe(false));
    expect(result.current.overview?.currency).toBe('USD');
  });

  it('runs a trailing refresh when a second mutation finishes during the first refresh', async () => {
    vi.mocked(adManagerApi.overview).mockResolvedValueOnce(overview('EGP'));
    const { result } = renderHook(() => useAdManager('project'));
    await waitFor(() => expect(result.current.loading).toBe(false));

    const firstMutationRefresh = deferred<AdvertisingOverview>();
    const trailingMutationRefresh = deferred<AdvertisingOverview>();
    vi.mocked(adManagerApi.overview)
      .mockReturnValueOnce(firstMutationRefresh.promise)
      .mockReturnValueOnce(trailingMutationRefresh.promise);
    let firstCompletion!: Promise<void>;
    let secondCompletion!: Promise<void>;
    act(() => {
      firstCompletion = result.current.refreshAfterMutation();
      secondCompletion = result.current.refreshAfterMutation();
    });

    expect(adManagerApi.overview).toHaveBeenCalledTimes(2);
    await act(async () => firstMutationRefresh.resolve(overview('STALE')));
    await waitFor(() => expect(adManagerApi.overview).toHaveBeenCalledTimes(3));

    await act(async () => {
      trailingMutationRefresh.resolve(overview('USD'));
      await expect(Promise.all([firstCompletion, secondCompletion])).resolves.toEqual([undefined, undefined]);
    });
    await waitFor(() => expect(result.current.refreshing).toBe(false));
    expect(result.current.overview?.currency).toBe('USD');
  });

  it('invalidates an old mutation refresh and its trailing read after a project switch', async () => {
    const oldMutationRefresh = deferred<AdvertisingOverview>();
    const newProjectLoad = deferred<AdvertisingOverview>();
    let oldProjectReads = 0;
    let newProjectSignal: AbortSignal | undefined;
    vi.mocked(adManagerApi.overview).mockImplementation((project, signal) => {
      if (project === 'old') {
        oldProjectReads += 1;
        return oldProjectReads === 1 ? Promise.resolve(overview('EGP')) : oldMutationRefresh.promise;
      }
      newProjectSignal = signal;
      return newProjectLoad.promise;
    });
    const { result, rerender } = renderHook(({ project }) => useAdManager(project), {
      initialProps: { project: 'old' },
    });
    await waitFor(() => expect(result.current.loading).toBe(false));
    const oldRefreshAfterMutation = result.current.refreshAfterMutation;
    let firstOldCompletion!: Promise<void>;
    let queuedOldCompletion!: Promise<void>;
    act(() => {
      firstOldCompletion = oldRefreshAfterMutation();
      queuedOldCompletion = oldRefreshAfterMutation();
    });
    expect(adManagerApi.overview).toHaveBeenCalledTimes(2);

    rerender({ project: 'new' });
    await waitFor(() => expect(newProjectSignal).toBeDefined());
    let lateOldCompletion!: Promise<void>;
    act(() => { lateOldCompletion = oldRefreshAfterMutation(); });
    await expect(lateOldCompletion).resolves.toBeUndefined();

    await act(async () => {
      oldMutationRefresh.resolve(overview('STALE'));
      await expect(Promise.all([firstOldCompletion, queuedOldCompletion])).resolves.toEqual([undefined, undefined]);
    });
    expect(adManagerApi.overview).toHaveBeenCalledTimes(3);
    expect(newProjectSignal?.aborted).toBe(false);
    expect(result.current.overview?.currency).not.toBe('STALE');

    await act(async () => newProjectLoad.resolve(overview('USD')));
    await waitFor(() => expect(result.current.overview?.currency).toBe('USD'));
  });

  it('does not let a late previous-project response replace current data', async () => {
    const oldRequest = deferred<AdvertisingOverview>();
    vi.mocked(adManagerApi.overview).mockImplementation(project => project === 'old' ? oldRequest.promise : Promise.resolve(overview('EGP')));
    const { result, rerender } = renderHook(({ project }) => useAdManager(project), { initialProps: { project: 'old' } });
    await waitFor(() => expect(adManagerApi.overview).toHaveBeenCalled());
    rerender({ project: 'new' });
    await waitFor(() => expect(result.current.overview?.currency).toBe('EGP'));
    await act(async () => oldRequest.resolve(overview('STALE')));
    expect(result.current.overview?.currency).toBe('EGP');
  });

  it('replaces visible data with the scheduled refresh result after two minutes', async () => {
    vi.useFakeTimers();
    vi.mocked(adManagerApi.overview)
      .mockResolvedValueOnce(overview('EGP'))
      .mockResolvedValue(overview('USD'));
    const { result } = renderHook(() => useAdManager('project'));
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(result.current.overview?.currency).toBe('EGP');
    await act(async () => { await vi.advanceTimersByTimeAsync(120_000); });
    expect(result.current.overview?.currency).toBe('USD');
  });

  it('loads stop details once across tabs and ordinary polling', async () => {
    vi.useFakeTimers();
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    vi.mocked(adManagerApi.overview).mockResolvedValue(overview('EGP'));
    const { rerender } = renderHook(({ view }) => useAdManager('project', view), {
      initialProps: { view: 'overview' },
    });

    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(adManagerApi.stopState).toHaveBeenCalledOnce();

    rerender({ view: 'decisions' });
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(adManagerApi.stopState).toHaveBeenCalledOnce();

    await act(async () => { await vi.advanceTimersByTimeAsync(120_000); });
    expect(adManagerApi.stopState).toHaveBeenCalledOnce();
    expect(adManagerApi.overview).toHaveBeenCalledTimes(2);
  });

  it('discovers an externally triggered stop without restoring stop polling on every cycle', async () => {
    vi.useFakeTimers();
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    vi.mocked(adManagerApi.overview)
      .mockResolvedValueOnce(overview('EGP'))
      .mockResolvedValue({ ...overview('EGP'), emergencyStop: true });
    vi.mocked(adManagerApi.stopState)
      .mockResolvedValueOnce({ emergencyStop: null, disable: null })
      .mockResolvedValue({
        emergencyStop: { id: 'external-stop', trigger: 'TrackingUnsafe', state: 'PausingManaged', reason: 'Tracking unsafe', activatedAtUtc: '2026-08-19T00:00:00Z', progress: { total: 1, succeeded: 0, unknown: 0, failed: 0, pending: 1, continuingSpend: true } },
        disable: null,
      });
    const { result } = renderHook(() => useAdManager('project'));

    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(adManagerApi.stopState).toHaveBeenCalledOnce();

    await act(async () => { await vi.advanceTimersByTimeAsync(120_000); });
    expect(adManagerApi.stopState).toHaveBeenCalledTimes(2);
    expect(result.current.stopState?.emergencyStop?.id).toBe('external-stop');
  });

  it('coalesces stop polling ticks and visibility refreshes while one stop request is pending', async () => {
    vi.useFakeTimers();
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    vi.mocked(adManagerApi.overview).mockResolvedValue(overview('EGP'));
    const pendingStopRefresh = deferred<Awaited<ReturnType<typeof adManagerApi.stopState>>>();
    vi.mocked(adManagerApi.stopState)
      .mockResolvedValueOnce({
        emergencyStop: { id: 'stop', trigger: 'Manual', state: 'PausingManaged', reason: 'Manual', activatedAtUtc: '2026-08-19T00:00:00Z', progress: { total: 1, succeeded: 0, unknown: 0, failed: 0, pending: 1, continuingSpend: true } },
        disable: null,
      })
      .mockReturnValueOnce(pendingStopRefresh.promise)
      .mockResolvedValue({
        emergencyStop: { id: 'stop', trigger: 'Manual', state: 'Paused', reason: 'Manual', activatedAtUtc: '2026-08-19T00:00:00Z', progress: { total: 1, succeeded: 1, unknown: 0, failed: 0, pending: 0, continuingSpend: false } },
        disable: null,
      });
    const { result } = renderHook(() => useAdManager('project'));

    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(result.current.stopState?.emergencyStop?.state).toBe('PausingManaged');

    await act(async () => { await vi.advanceTimersByTimeAsync(5_000); });
    expect(adManagerApi.stopState).toHaveBeenCalledTimes(2);
    expect(adManagerApi.overview).toHaveBeenCalledOnce();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(15_000);
      document.dispatchEvent(new Event('visibilitychange'));
    });
    expect(adManagerApi.stopState).toHaveBeenCalledTimes(2);

    await act(async () => pendingStopRefresh.resolve({
      emergencyStop: { id: 'stop', trigger: 'Manual', state: 'PausingManaged', reason: 'Manual', activatedAtUtc: '2026-08-19T00:00:00Z', progress: { total: 1, succeeded: 0, unknown: 0, failed: 0, pending: 1, continuingSpend: true } },
      disable: null,
    }));
    await act(async () => { await vi.advanceTimersByTimeAsync(5_000); });
    expect(adManagerApi.stopState).toHaveBeenCalledTimes(3);
    expect(result.current.stopState?.emergencyStop?.state).toBe('Paused');
  });

  it('keeps successful dashboard data when one secondary resource fails', async () => {
    vi.mocked(adManagerApi.overview).mockResolvedValue(overview('EGP'));
    vi.mocked(adManagerApi.decisions).mockRejectedValue(new Error('429'));

    const { result } = renderHook(() => useAdManager('project', 'decisions'));

    await waitFor(() => expect(result.current.overview?.currency).toBe('EGP'));
    expect(result.current.error).toContain('قرارات AI');
    expect(result.current.error).toContain('احتفظنا بآخر بيانات صحيحة');
  });

  it('normalizes array and cursor response shapes', () => {
    expect(cursorItems([{ id: 1 }])).toEqual({ items: [{ id: 1 }] });
    expect(cursorItems({ items: [{ id: 2 }], nextCursor: 'next' })).toEqual({ items: [{ id: 2 }], nextCursor: 'next' });
  });
});
