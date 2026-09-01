import type {
  AxiosAdapter,
  AxiosError,
  AxiosInstance,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

let axiosModule: typeof import('axios');
let axiosClient: typeof import('axios')['default'];
let api: AxiosInstance;

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function unauthorized(config: InternalAxiosRequestConfig): AxiosError {
  const response: AxiosResponse = {
    data: { code: 'UNAUTHORIZED' },
    status: 401,
    statusText: 'Unauthorized',
    headers: new axiosModule.AxiosHeaders(),
    config,
  };
  return new axiosModule.AxiosError(
    'Unauthorized',
    axiosModule.AxiosError.ERR_BAD_REQUEST,
    config,
    undefined,
    response,
  );
}

function ok(config: InternalAxiosRequestConfig, data: unknown = {}): AxiosResponse {
  return {
    data,
    status: 200,
    statusText: 'OK',
    headers: new axiosModule.AxiosHeaders(),
    config,
  };
}

function refreshResponse(accessToken: string, refreshToken: string): AxiosResponse<{
  accessToken: string;
  refreshToken: string;
}> {
  return {
    data: { accessToken, refreshToken },
    status: 200,
    statusText: 'OK',
    headers: new axiosModule.AxiosHeaders(),
    config: { headers: new axiosModule.AxiosHeaders() },
  };
}

beforeEach(async () => {
  vi.resetModules();
  vi.stubEnv('NEXT_PUBLIC_API_URL', 'http://localhost');
  localStorage.clear();
  window.history.replaceState(null, '', '/');
  axiosModule = await import('axios');
  axiosClient = axiosModule.default;
  ({ api } = await import('./api'));
});

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
  vi.unstubAllEnvs();
});

describe('API session refresh', () => {
  it('applies request context and preserves an explicit authorization header', async () => {
    localStorage.setItem('accessToken', 'stored-access');
    localStorage.setItem('activeProject', '{"id":"project-1"}');
    let capturedConfig: InternalAxiosRequestConfig | undefined;
    api.defaults.adapter = async (config) => {
      capturedConfig = config;
      return ok(config);
    };

    await api.get('/context', { headers: { authorization: 'ApiKey explicit' } });

    expect(capturedConfig?.timeout).toBe(15_000);
    expect(capturedConfig?.headers.get('Authorization')).toBe('ApiKey explicit');
    expect(capturedConfig?.headers.get('X-Project-Id')).toBe('project-1');
  });

  it('does not refresh more than once when retried requests still return 401', async () => {
    localStorage.setItem('accessToken', 'expired-access');
    localStorage.setItem('refreshToken', 'current-refresh');

    let adapterCalls = 0;
    const adapter: AxiosAdapter = async (config) => {
      adapterCalls += 1;
      throw unauthorized(config);
    };
    api.defaults.adapter = adapter;

    const refresh = deferred<AxiosResponse<{ accessToken: string; refreshToken: string }>>();
    const refreshSpy = vi.spyOn(axiosClient, 'post').mockReturnValue(refresh.promise);
    const requests = [api.get('/first'), api.get('/second')];

    await vi.waitFor(() => expect(adapterCalls).toBe(2));
    await vi.waitFor(() => expect(refreshSpy).toHaveBeenCalledTimes(1));
    refresh.resolve(refreshResponse('rejected-access', 'rotated-refresh'));

    const outcome = await Promise.allSettled(requests);

    expect(outcome).toEqual([
      expect.objectContaining({ status: 'rejected' }),
      expect.objectContaining({ status: 'rejected' }),
    ]);
    expect(refreshSpy).toHaveBeenCalledTimes(1);
    expect(adapterCalls).toBe(4);
  });

  it('shares one bounded refresh across concurrent requests and retries both with the new token', async () => {
    localStorage.setItem('accessToken', 'expired-access');
    localStorage.setItem('refreshToken', 'current-refresh');

    const seenAuthorizations: string[] = [];
    const adapter: AxiosAdapter = async (config) => {
      const authorization = String(config.headers.Authorization ?? '');
      seenAuthorizations.push(authorization);
      if (authorization === 'Bearer fresh-access') return ok(config, { path: config.url });
      throw unauthorized(config);
    };
    api.defaults.adapter = adapter;

    const refresh = deferred<AxiosResponse<{ accessToken: string; refreshToken: string }>>();
    const refreshSpy = vi.spyOn(axiosClient, 'post').mockReturnValue(refresh.promise);
    const requests = [api.get('/first'), api.get('/second')];

    await vi.waitFor(() => expect(refreshSpy).toHaveBeenCalledTimes(1));
    refresh.resolve(refreshResponse('fresh-access', 'fresh-refresh'));

    await expect(Promise.all(requests)).resolves.toHaveLength(2);
    expect(refreshSpy).toHaveBeenCalledWith(
      'http://localhost/api/auth/refresh',
      { refreshToken: 'current-refresh' },
      { timeout: 10_000 },
    );
    expect(seenAuthorizations).toEqual([
      'Bearer expired-access',
      'Bearer expired-access',
      'Bearer fresh-access',
      'Bearer fresh-access',
    ]);
    expect(localStorage.getItem('accessToken')).toBe('fresh-access');
    expect(localStorage.getItem('refreshToken')).toBe('fresh-refresh');
  });

  it('cancels one waiter immediately without canceling the shared refresh for other requests', async () => {
    localStorage.setItem('accessToken', 'expired-access');
    localStorage.setItem('refreshToken', 'current-refresh');

    let adapterCalls = 0;
    api.defaults.adapter = async (config) => {
      adapterCalls += 1;
      if (config.headers.Authorization === 'Bearer fresh-access') return ok(config);
      throw unauthorized(config);
    };

    const refresh = deferred<AxiosResponse<{ accessToken: string; refreshToken: string }>>();
    const refreshSpy = vi.spyOn(axiosClient, 'post').mockReturnValue(refresh.promise);
    const controller = new AbortController();
    const canceledRequest = api.get('/canceled', { signal: controller.signal });
    const activeRequest = api.get('/active');

    await vi.waitFor(() => expect(adapterCalls).toBe(2));
    await vi.waitFor(() => expect(refreshSpy).toHaveBeenCalledTimes(1));
    controller.abort();
    const cancellation = await canceledRequest.catch((error) => error);
    expect(axiosClient.isCancel(cancellation)).toBe(true);

    refresh.resolve(refreshResponse('fresh-access', 'fresh-refresh'));
    await expect(activeRequest).resolves.toMatchObject({ status: 200 });
    expect(refreshSpy).toHaveBeenCalledTimes(1);
    expect(adapterCalls).toBe(3);
  });

  it('settles concurrent waiters after a transient refresh failure and releases the lock', async () => {
    localStorage.setItem('accessToken', 'expired-access');
    localStorage.setItem('refreshToken', 'current-refresh');
    localStorage.setItem('user', '{"id":"user-1"}');
    localStorage.setItem('activeProject', '{"id":"project-1"}');

    api.defaults.adapter = async (config) => {
      if (config.headers.Authorization === 'Bearer fresh-access') return ok(config);
      throw unauthorized(config);
    };
    const timeout = new axiosModule.AxiosError('Refresh timed out', axiosModule.AxiosError.ETIMEDOUT);
    const failedRefresh = deferred<AxiosResponse<{ accessToken: string; refreshToken: string }>>();
    const refreshSpy = vi.spyOn(axiosClient, 'post')
      .mockReturnValueOnce(failedRefresh.promise)
      .mockResolvedValueOnce(refreshResponse('fresh-access', 'fresh-refresh'));

    const failedRequests = [api.get('/first'), api.get('/second')];
    await vi.waitFor(() => expect(refreshSpy).toHaveBeenCalledTimes(1));
    failedRefresh.reject(timeout);
    const failedOutcomes = await Promise.allSettled(failedRequests);
    expect(failedOutcomes).toEqual([
      { status: 'rejected', reason: timeout },
      { status: 'rejected', reason: timeout },
    ]);
    expect(localStorage.getItem('accessToken')).toBe('expired-access');
    expect(localStorage.getItem('refreshToken')).toBe('current-refresh');
    expect(localStorage.getItem('user')).toBe('{"id":"user-1"}');
    expect(localStorage.getItem('activeProject')).toBe('{"id":"project-1"}');

    await expect(api.get('/later')).resolves.toMatchObject({ status: 200 });
    expect(refreshSpy).toHaveBeenCalledTimes(2);
  });

  it('clears the current session when the refresh token is definitively rejected', async () => {
    localStorage.setItem('accessToken', 'expired-access');
    localStorage.setItem('refreshToken', 'rejected-refresh');
    localStorage.setItem('user', '{"id":"user-1"}');
    localStorage.setItem('activeProject', '{"id":"project-1"}');
    let adapterCalls = 0;
    api.defaults.adapter = async (config) => {
      adapterCalls += 1;
      throw unauthorized(config);
    };
    const refreshFailure = deferred<AxiosResponse<{ accessToken: string; refreshToken: string }>>();
    const refreshSpy = vi.spyOn(axiosClient, 'post').mockReturnValue(refreshFailure.promise);
    const requests = [api.get('/first'), api.get('/second')];

    await vi.waitFor(() => expect(adapterCalls).toBe(2));
    await vi.waitFor(() => expect(refreshSpy).toHaveBeenCalledTimes(1));
    const refreshConfig = { headers: new axiosModule.AxiosHeaders() } as InternalAxiosRequestConfig;
    const rejectedToken = unauthorized(refreshConfig);
    refreshFailure.reject(rejectedToken);
    const outcomes = await Promise.allSettled(requests);

    expect(outcomes).toEqual([
      { status: 'rejected', reason: rejectedToken },
      { status: 'rejected', reason: rejectedToken },
    ]);
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(localStorage.getItem('refreshToken')).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
    expect(localStorage.getItem('activeProject')).toBeNull();
  });

  it('does not overwrite a newer session when an older tab finishes refreshing', async () => {
    localStorage.setItem('accessToken', 'expired-access');
    localStorage.setItem('refreshToken', 'old-refresh');
    localStorage.setItem('user', '{"id":"user-1"}');

    api.defaults.adapter = async (config) => {
      if (config.headers.Authorization === 'Bearer newer-access') return ok(config);
      throw unauthorized(config);
    };
    const refresh = deferred<AxiosResponse<{ accessToken: string; refreshToken: string }>>();
    vi.spyOn(axiosClient, 'post').mockReturnValue(refresh.promise);
    const request = api.get('/cross-tab');

    await vi.waitFor(() => expect(axiosClient.post).toHaveBeenCalledTimes(1));
    localStorage.setItem('accessToken', 'newer-access');
    localStorage.setItem('refreshToken', 'newer-refresh');
    refresh.resolve(refreshResponse('stale-access', 'stale-refresh'));

    await expect(request).resolves.toMatchObject({ status: 200 });
    expect(localStorage.getItem('accessToken')).toBe('newer-access');
    expect(localStorage.getItem('refreshToken')).toBe('newer-refresh');
  });

  it('does not replay an old request after the browser switches to another user', async () => {
    localStorage.setItem('accessToken', 'expired-access');
    localStorage.setItem('refreshToken', 'old-refresh');
    localStorage.setItem('user', '{"id":"user-1"}');
    localStorage.setItem('activeProject', '{"id":"project-1"}');
    let adapterCalls = 0;
    api.defaults.adapter = async (config) => {
      adapterCalls += 1;
      if (config.headers.Authorization === 'Bearer user-2-access') return ok(config);
      throw unauthorized(config);
    };
    const refresh = deferred<AxiosResponse<{ accessToken: string; refreshToken: string }>>();
    vi.spyOn(axiosClient, 'post').mockReturnValue(refresh.promise);
    const oldRequest = api.get('/old-user-action');

    await vi.waitFor(() => expect(axiosClient.post).toHaveBeenCalledTimes(1));
    localStorage.setItem('accessToken', 'user-2-access');
    localStorage.setItem('refreshToken', 'user-2-refresh');
    localStorage.setItem('user', '{"id":"user-2"}');
    localStorage.setItem('activeProject', '{"id":"project-2"}');
    refresh.resolve(refreshResponse('stale-user-1-access', 'stale-user-1-refresh'));

    const rejection = await oldRequest.catch((error) => error);
    expect(axiosClient.isCancel(rejection)).toBe(true);
    expect(adapterCalls).toBe(1);
    expect(localStorage.getItem('accessToken')).toBe('user-2-access');
    expect(localStorage.getItem('refreshToken')).toBe('user-2-refresh');
    expect(localStorage.getItem('activeProject')).toBe('{"id":"project-2"}');
  });

  it.each(['/api/auth/login', '/api/auth/refresh'])(
    'does not recursively refresh a 401 from %s',
    async (path) => {
      localStorage.setItem('accessToken', 'expired-access');
      localStorage.setItem('refreshToken', 'current-refresh');
      api.defaults.adapter = async (config) => { throw unauthorized(config); };
      const refreshSpy = vi.spyOn(axiosClient, 'post');

      await expect(api.post(path)).rejects.toMatchObject({ response: { status: 401 } });
      expect(refreshSpy).not.toHaveBeenCalled();
    },
  );
});
