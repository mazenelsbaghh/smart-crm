import axios, { type AxiosError, type GenericAbortSignal, type InternalAxiosRequestConfig } from 'axios';

interface RetriableRequestConfig extends InternalAxiosRequestConfig {
  _retry?: boolean;
}

interface RefreshedSession {
  accessToken: string;
  refreshToken: string;
}

interface RefreshAttempt {
  storage: Storage;
  refreshToken: string;
  principalId: string | null;
}

const API_URL = process.env.NEXT_PUBLIC_API_URL !== undefined ? process.env.NEXT_PUBLIC_API_URL : 'http://localhost';
const API_REQUEST_TIMEOUT_MS = 15_000;
const REFRESH_REQUEST_TIMEOUT_MS = 10_000;
const REFRESH_URL = `${API_URL.replace(/\/+$/, '')}/api/auth/refresh`;

export const api = axios.create({
  baseURL: API_URL,
  timeout: API_REQUEST_TIMEOUT_MS,
  headers: {
    'Content-Type': 'application/json',
  },
});

const browserStorage = () => typeof window === 'undefined' ? null : window.localStorage;

const clearSession = (storage: Storage) => {
  storage.removeItem('accessToken');
  storage.removeItem('refreshToken');
  storage.removeItem('user');
  storage.removeItem('activeProject');

  if (window.location.pathname !== '/') window.location.assign('/');
};

const storedUserId = (storage: Storage) => {
  const storedUser = storage.getItem('user');
  if (!storedUser) return null;
  try {
    const user = JSON.parse(storedUser) as { id?: unknown };
    return typeof user.id === 'string' && user.id.trim() ? user.id.trim() : null;
  } catch {
    return null;
  }
};

const accessTokenSubject = (accessToken: string | null) => {
  if (!accessToken) return null;
  try {
    const encodedPayload = accessToken.split('.')[1];
    if (!encodedPayload) return null;
    const base64 = encodedPayload.replace(/-/g, '+').replace(/_/g, '/');
    const paddedBase64 = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=');
    const payload = JSON.parse(atob(paddedBase64)) as { sub?: unknown };
    return typeof payload.sub === 'string' && payload.sub.trim() ? payload.sub.trim() : null;
  } catch {
    return null;
  }
};

const storedPrincipalId = (storage: Storage) =>
  accessTokenSubject(storage.getItem('accessToken')) ?? storedUserId(storage);

const requireRefreshAttempt = (): RefreshAttempt => {
  const storage = browserStorage();
  if (!storage) throw new Error('Token refresh is only available in the browser');

  const refreshToken = storage.getItem('refreshToken');
  if (refreshToken) return { storage, refreshToken, principalId: storedPrincipalId(storage) };

  clearSession(storage);
  throw new Error('No refresh token found');
};

const parseRefreshResponse = (payload: unknown): RefreshedSession => {
  if (!payload || typeof payload !== 'object') throw new Error('Invalid token refresh response');
  const response = payload as Partial<RefreshedSession>;
  if (typeof response.accessToken !== 'string' || !response.accessToken.trim()
      || typeof response.refreshToken !== 'string' || !response.refreshToken.trim()) {
    throw new Error('Invalid token refresh response');
  }
  return { accessToken: response.accessToken, refreshToken: response.refreshToken };
};

const isDefinitiveRefreshRejection = (error: unknown) => {
  if (!axios.isAxiosError(error)) return false;
  const status = error.response?.status;
  return status === 400 || status === 401 || status === 403;
};

const newerSessionAccessToken = ({ storage, refreshToken, principalId }: RefreshAttempt) => {
  const currentRefreshToken = storage.getItem('refreshToken');
  if (!currentRefreshToken || currentRefreshToken === refreshToken) return null;
  const currentAccessToken = storage.getItem('accessToken');
  if (principalId && storedPrincipalId(storage) === principalId && currentAccessToken) return currentAccessToken;
  throw new axios.CanceledError('Session changed while the access token was refreshing');
};

const refreshAttemptIsCurrent = ({ storage, refreshToken }: RefreshAttempt) =>
  storage.getItem('refreshToken') === refreshToken;

const requestRefreshedSession = async (refreshToken: string) => {
  const response = await axios.post<RefreshedSession>(
    REFRESH_URL,
    { refreshToken },
    { timeout: REFRESH_REQUEST_TIMEOUT_MS },
  );
  return parseRefreshResponse(response.data);
};

const storeRefreshedSession = (storage: Storage, refreshed: RefreshedSession) => {
  storage.setItem('accessToken', refreshed.accessToken);
  storage.setItem('refreshToken', refreshed.refreshToken);
};

const refreshAccessToken = async (): Promise<string> => {
  const refreshAttempt = requireRefreshAttempt();

  try {
    const refreshed = await requestRefreshedSession(refreshAttempt.refreshToken);
    if (!refreshAttemptIsCurrent(refreshAttempt)) {
      const newerAccessToken = newerSessionAccessToken(refreshAttempt);
      if (newerAccessToken) return newerAccessToken;
      throw new Error('Session changed while the access token was refreshing');
    }

    storeRefreshedSession(refreshAttempt.storage, refreshed);
    return refreshed.accessToken;
  } catch (error) {
    const newerAccessToken = newerSessionAccessToken(refreshAttempt);
    if (newerAccessToken) return newerAccessToken;
    if (isDefinitiveRefreshRejection(error) && refreshAttemptIsCurrent(refreshAttempt))
      clearSession(refreshAttempt.storage);
    throw error;
  }
};

let refreshPromise: Promise<string> | null = null;

const sharedRefresh = () => {
  if (refreshPromise) return refreshPromise;

  const inFlightRefresh = refreshAccessToken();
  refreshPromise = inFlightRefresh;
  const release = () => {
    if (refreshPromise === inFlightRefresh) refreshPromise = null;
  };
  void inFlightRefresh.then(release, release);
  return inFlightRefresh;
};

const waitForRefresh = (
  refreshRequest: Promise<string>,
  signal: GenericAbortSignal | undefined,
) => {
  if (!signal?.addEventListener) return refreshRequest;
  if (signal.aborted) return Promise.reject(new axios.CanceledError('Request canceled'));

  return new Promise<string>((resolve, reject) => {
    const cleanup = () => signal.removeEventListener?.('abort', cancel);
    const cancel = () => {
      cleanup();
      reject(new axios.CanceledError('Request canceled'));
    };
    signal.addEventListener?.('abort', cancel, { once: true });
    void refreshRequest.then(
      (token) => { cleanup(); resolve(token); },
      (error) => { cleanup(); reject(error); },
    );
  });
};

api.interceptors.request.use(
  (config) => {
    const storage = browserStorage();
    if (!storage) return config;

    const token = storage.getItem('accessToken');
    if (token && !config.headers.has('Authorization')) {
      config.headers.set('Authorization', `Bearer ${token}`);
    }

    const activeProject = storage.getItem('activeProject');
    if (activeProject) {
      try {
        const project = JSON.parse(activeProject);
        if (project?.id) config.headers.set('X-Project-Id', project.id);
      } catch (error) {
        console.error('Error parsing active project from localStorage', error);
      }
    }
    return config;
  },
  (error) => Promise.reject(error),
);

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as RetriableRequestConfig | undefined;
    const isAuthRequest = originalRequest?.url?.includes('/api/auth/login')
      || originalRequest?.url?.includes('/api/auth/refresh');
    if (error.response?.status !== 401 || !originalRequest || originalRequest._retry || isAuthRequest) {
      return Promise.reject(error);
    }

    originalRequest._retry = true;
    const accessToken = await waitForRefresh(sharedRefresh(), originalRequest.signal);
    originalRequest.headers.set('Authorization', `Bearer ${accessToken}`);
    return api(originalRequest);
  },
);
