'use client';

import React, { createContext, useCallback, useContext, useRef, useState, useEffect } from 'react';
import { authService, User, Project } from '../services/auth';
import { api } from '../services/api';

interface AuthContextType {
  user: User | null;
  activeProject: Project | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshProjects: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);
const PROJECT_REQUEST_TIMEOUT_MS = 15_000;

interface ProjectSummary {
  id: string;
}

async function fetchAuthorizedProject(accessToken?: string): Promise<Project | null> {
  const requestConfig = {
    timeout: PROJECT_REQUEST_TIMEOUT_MS,
    ...(accessToken ? { headers: { Authorization: `Bearer ${accessToken}` } } : {}),
  };
  const projectsResponse = await api.get<ProjectSummary[]>('/api/projects', requestConfig);
  const projectId = projectsResponse.data[0]?.id;
  if (!projectId) return null;
  return (await api.get<Project>(`/api/projects/${projectId}`, requestConfig)).data;
}

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [activeProject, setActiveProject] = useState<Project | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const authRevisionRef = useRef(0);

  const refreshProjects = useCallback(async () => {
    const revision = authRevisionRef.current;
    try {
      const nextActiveProject = await fetchAuthorizedProject();
      if (revision !== authRevisionRef.current) return;

      if (nextActiveProject) {
        setActiveProject(nextActiveProject);
        authService.setActiveProject(nextActiveProject);
      } else {
        setActiveProject(null);
        authService.clearActiveProject();
      }
    } catch (error) {
      console.error('Failed to refresh authorized project', error);
    }
  }, []);

  // Initialize and check local storage
  useEffect(() => {
    const initializeAuth = async () => {
      const revision = ++authRevisionRef.current;
      const storedUser = authService.getCurrentUser();

      if (storedUser) {
        setUser(storedUser);
        setActiveProject(null);

        try {
          const authorizedProject = await fetchAuthorizedProject();
          if (revision !== authRevisionRef.current) return;
          setActiveProject(authorizedProject);
          if (authorizedProject) {
            authService.setActiveProject(authorizedProject);
          } else {
            authService.clearActiveProject();
          }
        } catch (error) {
          if (revision !== authRevisionRef.current) return;
          console.error('Failed to restore projects during auth bootstrap', error);
          setActiveProject(null);
          authService.clearActiveProject();
        }
      }
      if (revision === authRevisionRef.current) setLoading(false);
    };

    initializeAuth();

    // Listen for storage events from other tabs/pages
    const handleStorageChange = (event: StorageEvent) => {
      if (event.key === 'activeProject') {
        if (authService.getCurrentUser()) void refreshProjects();
        return;
      }
      if (event.key !== 'user') return;
      authRevisionRef.current += 1;
      const nextUser = authService.getCurrentUser();
      setUser(nextUser);
      setActiveProject(null);
      if (nextUser) void refreshProjects();
    };
    window.addEventListener('storage', handleStorageChange);
    return () => window.removeEventListener('storage', handleStorageChange);
  }, [refreshProjects]);

  const login = async (email: string, password: string) => {
    const revision = ++authRevisionRef.current;
    setLoading(true);
    setActiveProject(null);
    try {
      const response = await authService.login(email, password);
      if (revision !== authRevisionRef.current) return;
      const authorizedProject = await fetchAuthorizedProject(response.accessToken);
      if (revision !== authRevisionRef.current) return;
      if (!authorizedProject) throw new Error('NO_AUTHORIZED_PROJECT');

      authService.setActiveProject(authorizedProject);
      authService.saveSession(response);
      setUser(response.user);
      setActiveProject(authorizedProject);
    } finally {
      if (revision === authRevisionRef.current) setLoading(false);
    }
  };

  const logout = async () => {
    const revision = ++authRevisionRef.current;
    setLoading(true);
    try {
      await authService.logout();
    } finally {
      if (revision !== authRevisionRef.current) return;
      authService.clearSession();
      setUser(null);
      setActiveProject(null);
      setLoading(false);
      window.location.href = '/';
    }
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        activeProject,
        loading,
        login,
        logout,
        refreshProjects,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
