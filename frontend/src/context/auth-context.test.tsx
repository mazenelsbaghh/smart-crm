import { useState } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../services/api';
import { authService, type Project } from '../services/auth';
import { AuthProvider, useAuth } from './auth-context';

const principal = {
  id: 'owner-1',
  email: 'owner@example.test',
  role: 'Owner',
};

const project: Project = {
  id: 'project-1',
  name: 'مساحة الاختبار',
  settings: { aiAutoReplyEnabled: true },
};

function jwtFor(payload: Record<string, string>) {
  const encode = (value: object) => btoa(JSON.stringify(value))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
  return `${encode({ alg: 'none', typ: 'JWT' })}.${encode(payload)}.`;
}

const accessToken = jwtFor({ sub: principal.id, email: principal.email, role: principal.role });

function loginPayload(user: Record<string, string> = principal) {
  return { accessToken, refreshToken: 'refresh-token', user };
}

function AuthProbe() {
  const { user, activeProject, loading, login } = useAuth();
  const [loginError, setLoginError] = useState('');

  return (
    <>
      <button
        type="button"
        onClick={() => void login(principal.email, 'secret').catch((error: unknown) => {
          setLoginError(error instanceof Error ? error.message : 'UNKNOWN_ERROR');
        })}
      >
        دخول
      </button>
      <output aria-label="حالة تحميل المصادقة">{loading ? 'loading' : 'ready'}</output>
      <output aria-label="المستخدم المصادق">
        {user ? `${user.id}|${user.email}|${user.role}|${user.fullName}` : 'signed-out'}
      </output>
      <output aria-label="مساحة العمل النشطة">
        {activeProject
          ? `${activeProject.id}|${activeProject.name}|${activeProject.settings?.aiAutoReplyEnabled ? 'ai-enabled' : 'ai-disabled'}`
          : 'no-project'}
      </output>
      <output aria-label="خطأ تسجيل الدخول">{loginError}</output>
    </>
  );
}

function mockProjectRequests(detail: Project = project) {
  return vi.spyOn(api, 'get').mockImplementation(async (url) => {
    if (url === '/api/projects') return { data: [{ id: detail.id }] } as never;
    if (url === `/api/projects/${detail.id}`) return { data: detail } as never;
    throw new Error(`Unexpected URL: ${url}`);
  });
}

describe('authentication contract', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it.each([
    ['user id', { ...principal, id: 'another-user' }],
    ['email', { ...principal, email: 'attacker@example.test' }],
    ['role', { ...principal, role: 'Admin' }],
  ])('rejects a login response whose %s conflicts with the signed token', async (_field, user) => {
    vi.spyOn(api, 'post').mockResolvedValue({ data: loginPayload(user) } as never);

    await expect(authService.login(principal.email, 'secret')).rejects.toThrow('INVALID_LOGIN_RESPONSE');
  });

  it('loads the authorized project detail before exposing a successful session', async () => {
    vi.spyOn(api, 'post').mockResolvedValue({ data: loginPayload() } as never);
    mockProjectRequests();
    render(<AuthProvider><AuthProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByLabelText('حالة تحميل المصادقة')).toHaveTextContent('ready'));

    await userEvent.click(screen.getByRole('button', { name: 'دخول' }));

    await waitFor(() => expect(screen.getByLabelText('مساحة العمل النشطة')).toHaveTextContent(
      'project-1|مساحة الاختبار|ai-enabled',
    ));
    expect(screen.getByLabelText('المستخدم المصادق')).toHaveTextContent(
      'owner-1|owner@example.test|Owner|owner',
    );
    expect(JSON.parse(localStorage.getItem('activeProject') ?? 'null')).toEqual(project);
  });

  it('keeps the user signed out when authorized project details cannot be loaded', async () => {
    vi.spyOn(api, 'post').mockResolvedValue({ data: loginPayload() } as never);
    vi.spyOn(api, 'get').mockImplementation(async (url) => {
      if (url === '/api/projects') return { data: [{ id: project.id }] } as never;
      throw new Error('project detail unavailable');
    });
    render(<AuthProvider><AuthProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByLabelText('حالة تحميل المصادقة')).toHaveTextContent('ready'));

    await userEvent.click(screen.getByRole('button', { name: 'دخول' }));

    await waitFor(() => expect(screen.getByLabelText('خطأ تسجيل الدخول')).toHaveTextContent('project detail unavailable'));
    expect(screen.getByLabelText('المستخدم المصادق')).toHaveTextContent('signed-out');
    expect(screen.getByLabelText('مساحة العمل النشطة')).toHaveTextContent('no-project');
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
    expect(localStorage.getItem('activeProject')).toBeNull();
  });

  it('2026-08 active-project storage regression refreshes once without a request loop', async () => {
    localStorage.setItem('user', JSON.stringify({ ...principal, fullName: 'Owner' }));
    const get = mockProjectRequests();
    render(<AuthProvider><AuthProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByLabelText('مساحة العمل النشطة')).toHaveTextContent(project.name));

    act(() => {
      window.dispatchEvent(new StorageEvent('storage', { key: 'activeProject', newValue: JSON.stringify(project) }));
    });

    await waitFor(() => {
      const detailRequests = get.mock.calls.filter(([url]) => url === `/api/projects/${project.id}`);
      expect(detailRequests).toHaveLength(2);
    });
    await act(async () => Promise.resolve());
    expect(get.mock.calls.filter(([url]) => url === `/api/projects/${project.id}`)).toHaveLength(2);
  });
});
