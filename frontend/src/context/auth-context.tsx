'use client';

import React, { createContext, useContext, useState, useEffect } from 'react';
import { authService, User, Project } from '../services/auth';
import { api } from '../services/api';

interface AuthContextType {
  user: User | null;
  activeProject: Project | null;
  projects: Project[];
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, fullName: string) => Promise<void>;
  logout: () => Promise<void>;
  switchProject: (projectId: string) => void;
  refreshProjects: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [activeProject, setActiveProject] = useState<Project | null>(null);
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  // Initialize and check local storage
  useEffect(() => {
    const initializeAuth = async () => {
      const storedUser = authService.getCurrentUser();
      const storedProject = authService.getActiveProject();

      if (storedUser) {
        setUser(storedUser);
        if (storedProject) {
          setActiveProject(storedProject);
        }
        
        // Fetch projects list
        try {
          const response = await api.get<Project[]>('/api/projects');
          setProjects(response.data);
          
          // If no active project or stored project is not in list, set first
          if (response.data.length > 0) {
            const exists = storedProject ? response.data.find(p => p.id === storedProject.id) : null;
            if (!exists) {
              const defaultProj = response.data[0];
              setActiveProject(defaultProj);
              authService.setActiveProject(defaultProj);
            } else {
              // Refresh active project settings from server
              setActiveProject(exists);
              authService.setActiveProject(exists);
            }
          }
        } catch (error) {
          console.error('Failed to restore projects during auth bootstrap', error);
        }
      }
      setLoading(false);
    };

    initializeAuth();

    // Listen for storage events from other tabs/pages
    const handleStorageChange = () => {
      setUser(authService.getCurrentUser());
      setActiveProject(authService.getActiveProject());
    };
    window.addEventListener('storage', handleStorageChange);
    return () => window.removeEventListener('storage', handleStorageChange);
  }, []);

  const login = async (email: string, password: string) => {
    setLoading(true);
    try {
      const response = await authService.login(email, password);
      setUser(response.user);
      
      // Fetch projects
      const projectsResponse = await api.get<Project[]>('/api/projects');
      setProjects(projectsResponse.data);
      if (projectsResponse.data.length > 0) {
        const defaultProj = projectsResponse.data[0];
        setActiveProject(defaultProj);
        authService.setActiveProject(defaultProj);
      }
    } finally {
      setLoading(false);
    }
  };

  const register = async (email: string, password: string, fullName: string) => {
    await authService.register(email, password, fullName);
  };

  const logout = async () => {
    setLoading(true);
    try {
      await authService.logout();
    } finally {
      setUser(null);
      setActiveProject(null);
      setProjects([]);
      setLoading(false);
    }
  };

  const switchProject = (projectId: string) => {
    const target = projects.find((p) => p.id === projectId);
    if (target) {
      setActiveProject(target);
      authService.setActiveProject(target);
    }
  };

  const refreshProjects = async () => {
    try {
      const response = await api.get<Project[]>('/api/projects');
      setProjects(response.data);
      if (activeProject) {
        const fresh = response.data.find((p) => p.id === activeProject.id);
        if (fresh) {
          setActiveProject(fresh);
          authService.setActiveProject(fresh);
        }
      }
    } catch (e) {
      console.error('Failed to refresh projects list', e);
    }
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        activeProject,
        projects,
        loading,
        login,
        register,
        logout,
        switchProject,
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
