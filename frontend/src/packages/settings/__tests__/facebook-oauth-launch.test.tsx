import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FacebookAutomationPanel } from '../../../app/quran-challenge/facebook-automation';
import { AuthProvider } from '../../../context/auth-context';
import { api } from '../../../services/api';
import Settings from '../Settings';

vi.mock('next/navigation', () => ({
  useSearchParams: () => new URLSearchParams('section=connections'),
}));

const project = { id: 'project-1', name: 'المشروع', settings: { aiAutoReplyEnabled: false, timezone: 'Africa/Cairo' } };
const authorizationUrl = 'https://www.facebook.com/v22.0/dialog/oauth?state=project-1%3Acsrf';
const originalAdapter = api.defaults.adapter;
const surfaces = [
  { name: 'project settings', button: 'ربط صفحة فيسبوك', component: <Settings /> },
  { name: 'Quran automation', button: 'ربط Facebook', component: <FacebookAutomationPanel selection={{ surahNumber: 1, ayahNumber: 1, hiddenWordIndex: 0 }} timezone="Africa/Cairo" /> },
];

function response(config: InternalAxiosRequestConfig, data: unknown): AxiosResponse {
  return { config, data, status: 200, statusText: 'OK', headers: {} };
}

function serveApi(login: (config: InternalAxiosRequestConfig) => Promise<AxiosResponse>) {
  api.defaults.adapter = async (config) => {
    if (config.url === '/api/facebook/oauth/login') return login(config);
    if (config.url === '/api/projects') return response(config, [project]);
    if (config.url === `/api/projects/${project.id}`) return response(config, project);
    if (config.url === `/api/projects/${project.id}/facebook/pages` || config.url === '/api/whatsapp/accounts') return response(config, []);
    if (config.url === '/api/quran/facebook') return response(config, {
      appConfigured: true, connectedPages: [], facebookPageId: null, pageName: null,
      isEnabled: false, intervalHours: 24, captionTemplate: '{surah}',
      nextPublishAtUtc: null, lastPublishedAtUtc: null, lastReelId: null, lastError: null,
    });
    throw new Error(`Unexpected request: ${config.method} ${config.url}`);
  };
}

function mockPopup() {
  const popup = { closed: false, location: { replace: vi.fn() }, close: vi.fn(), focus: vi.fn() };
  const open = vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
  return { popup, open };
}

beforeEach(() => {
  localStorage.setItem('accessToken', 'owner-access-token');
  localStorage.setItem('user', JSON.stringify({ id: 'owner-1', email: 'owner@example.test', role: 'Owner', fullName: 'Owner' }));
  localStorage.setItem('activeProject', JSON.stringify(project));
});

afterEach(() => {
  api.defaults.adapter = originalAdapter;
  localStorage.clear();
});

describe.each(surfaces)('Facebook OAuth from $name', ({ button, component }) => {
  it('opens the popup immediately and obtains its destination with the authenticated API client', async () => {
    let request: InternalAxiosRequestConfig | undefined;
    let complete!: () => void;
    serveApi((config) => {
      request = config;
      return new Promise(resolve => { complete = () => resolve(response(config, { authorizationUrl })); });
    });
    const { popup, open } = mockPopup();
    render(<AuthProvider>{component}</AuthProvider>);

    fireEvent.click(await screen.findByRole('button', { name: button }));
    expect(open).toHaveBeenCalledWith('about:blank', 'facebook-oauth-login', expect.any(String));
    await waitFor(() => expect(request).toBeDefined());
    expect(request?.headers.get('Authorization')).toBe('Bearer owner-access-token');
    expect(request?.params).toEqual({ projectId: project.id });
    expect(popup.location.replace).not.toHaveBeenCalled();

    await act(async () => { complete(); });
    expect(popup.location.replace).toHaveBeenCalledWith(authorizationUrl);
    expect(popup.close).not.toHaveBeenCalled();
  });

  it('closes the unused popup and displays an initiation error', async () => {
    const message = 'تعذر بدء الربط من الخادم';
    serveApi(async (config) => {
      throw new AxiosError(message, 'ERR_BAD_RESPONSE', config, undefined, {
        ...response(config, { error: message }), status: 503, statusText: 'Service Unavailable',
      });
    });
    const { popup } = mockPopup();
    render(<AuthProvider>{component}</AuthProvider>);

    fireEvent.click(await screen.findByRole('button', { name: button }));
    expect(await screen.findByText(message)).toBeVisible();
    expect(popup.close).toHaveBeenCalledOnce();
    expect(popup.location.replace).not.toHaveBeenCalled();
  });
});
