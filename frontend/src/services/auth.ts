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
    whatsappConnected: boolean;
    whatsappNumber: string | null;
    aiAutoReplyEnabled: boolean;
    leadScoreThreshold: number;
  };
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  user: User;
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
  } catch (e) {
    return null;
  }
}

export function getUserFromToken(token: string): User | null {
  const payload = parseJwt(token);
  if (!payload) return null;
  const role = payload.role || payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] || "Agent";
  return {
    id: payload.sub || "",
    email: payload.email || "",
    fullName: (payload.email || "").split('@')[0] || "Agent",
    role: role,
  };
}

export const authService = {
  async login(email: string, password: string): Promise<LoginResponse> {
    const response = await api.post<LoginResponse>('/api/auth/login', {
      email,
      password,
    });
    const { accessToken, refreshToken, user } = response.data;
    
    const userFromToken = getUserFromToken(accessToken);
    const resolvedUser = user || userFromToken;

    localStorage.setItem('accessToken', accessToken);
    localStorage.setItem('refreshToken', refreshToken);
    if (resolvedUser) {
      localStorage.setItem('user', JSON.stringify(resolvedUser));
    }
    
    // Auto load projects for the logged in user
    try {
      const projectsResponse = await api.get<Project[]>('/api/projects');
      if (projectsResponse.data && projectsResponse.data.length > 0) {
        localStorage.setItem('activeProject', JSON.stringify(projectsResponse.data[0]));
      }
    } catch (e) {
      console.error('Error fetching user projects during login', e);
    }
    
    return {
      accessToken,
      refreshToken,
      user: resolvedUser!,
    };
  },

  async register(email: string, password: string, fullName: string): Promise<any> {
    const response = await api.post('/api/auth/register', {
      email,
      password,
      fullName,
    });
    return response.data;
  },

  async logout(): Promise<void> {
    try {
      const storedRefreshToken = localStorage.getItem('refreshToken');
      if (storedRefreshToken) {
        await api.post('/api/auth/logout', { refreshToken: storedRefreshToken });
      }
    } catch (e) {
      console.error('Error during logout api request', e);
    } finally {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('user');
      localStorage.removeItem('activeProject');
      
      if (typeof window !== 'undefined') {
        window.location.href = '/';
      }
    }
  },

  getCurrentUser(): User | null {
    if (typeof window === 'undefined') return null;
    const userStr = localStorage.getItem('user');
    if (!userStr) return null;
    try {
      return JSON.parse(userStr);
    } catch {
      return null;
    }
  },

  getActiveProject(): Project | null {
    if (typeof window === 'undefined') return null;
    const projectStr = localStorage.getItem('activeProject');
    if (!projectStr) return null;
    try {
      return JSON.parse(projectStr);
    } catch {
      return null;
    }
  },

  setActiveProject(project: Project): void {
    if (typeof window === 'undefined') return;
    localStorage.setItem('activeProject', JSON.stringify(project));
    // Trigger storage event manually for same-page listeners
    window.dispatchEvent(new Event('storage'));
  },
};
