import { api } from './api';

export interface User {
  id: string;
  email: string;
  fullName: string;
  role: string;
}

export interface Project {
  id: string;
  name: string;
  settings: {
    aiAutoReplyEnabled: boolean;
    timezone?: string;
  } | null;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  user: User;
}

interface LoginApiResponse {
  accessToken: string;
  refreshToken: string;
  user?: Partial<User>;
}

function parseJwt(token: string) {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(jsonPayload);
  } catch {
    return null;
  }
}

export function getUserFromToken(token: string): User | null {
  const payload = parseJwt(token);
  if (!payload) return null;
  const id = typeof payload.sub === 'string' ? payload.sub : '';
  const email = typeof payload.email === 'string' ? payload.email : '';
  if (!id || !email) return null;
  const roleClaim = payload.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
  return {
    id,
    email,
    fullName: email.split('@')[0],
    role: typeof roleClaim === 'string' ? roleClaim : 'Agent',
  };
}

function resolveLoginUser(apiUser: Partial<User> | undefined, accessToken: string): User | null {
  const tokenUser = getUserFromToken(accessToken);
  if (!tokenUser) return null;

  const apiId = typeof apiUser?.id === 'string' ? apiUser.id.trim() : '';
  const apiEmail = typeof apiUser?.email === 'string' ? apiUser.email.trim() : '';
  const apiRole = typeof apiUser?.role === 'string' ? apiUser.role.trim() : '';
  if (
    (apiId && apiId !== tokenUser.id)
    || (apiEmail && apiEmail.toLocaleLowerCase('en-US') !== tokenUser.email.toLocaleLowerCase('en-US'))
    || (apiRole && apiRole.toLocaleLowerCase('en-US') !== tokenUser.role.toLocaleLowerCase('en-US'))
  ) return null;

  return {
    id: tokenUser.id,
    email: tokenUser.email,
    role: tokenUser.role,
    fullName: typeof apiUser?.fullName === 'string' && apiUser.fullName.trim()
      ? apiUser.fullName.trim()
      : tokenUser.fullName,
  };
}

export const authService = {
  async login(email: string, password: string): Promise<LoginResponse> {
    const response = await api.post<LoginApiResponse>('/api/auth/login', {
      email,
      password,
    });
    const { accessToken, refreshToken, user } = response.data;
    if (typeof accessToken !== 'string' || !accessToken.trim() || typeof refreshToken !== 'string' || !refreshToken.trim()) {
      throw new Error('INVALID_LOGIN_RESPONSE');
    }
    const resolvedUser = resolveLoginUser(user, accessToken);
    if (!resolvedUser) throw new Error('INVALID_LOGIN_RESPONSE');

    return {
      accessToken,
      refreshToken,
      user: resolvedUser,
    };
  },

  saveSession(session: LoginResponse): void {
    localStorage.setItem('accessToken', session.accessToken);
    localStorage.setItem('refreshToken', session.refreshToken);
    localStorage.setItem('user', JSON.stringify(session.user));
  },

  async logout(): Promise<void> {
    try {
      const storedRefreshToken = localStorage.getItem('refreshToken');
      if (storedRefreshToken) {
        await api.post('/api/auth/logout', { refreshToken: storedRefreshToken });
      }
    } catch (e) {
      console.error('Error during logout api request', e);
    }
  },

  clearSession(): void {
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('user');
    localStorage.removeItem('activeProject');
  },

  getCurrentUser(): User | null {
    if (typeof window === 'undefined') return null;
    const userStr = localStorage.getItem('user');
    if (!userStr) return null;
    try {
      const stored = JSON.parse(userStr) as Partial<User>;
      const id = typeof stored.id === 'string' ? stored.id.trim() : '';
      const email = typeof stored.email === 'string' ? stored.email.trim() : '';
      const role = typeof stored.role === 'string' ? stored.role.trim() : '';
      if (!id || !email || !role) return null;
      return {
        id,
        email,
        role,
        fullName: typeof stored.fullName === 'string' && stored.fullName.trim()
          ? stored.fullName.trim()
          : email.split('@')[0],
      };
    } catch {
      return null;
    }
  },

  setActiveProject(project: Project): void {
    if (typeof window === 'undefined') return;
    localStorage.setItem('activeProject', JSON.stringify(project));
  },

  clearActiveProject(): void {
    if (typeof window === 'undefined') return;
    localStorage.removeItem('activeProject');
  },
};
