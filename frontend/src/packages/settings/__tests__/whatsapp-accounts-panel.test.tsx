import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../../../services/api';
import WhatsAppAccountsPanel, { type WhatsAppAccount } from '../WhatsAppAccountsPanel';

const originalAdapter = api.defaults.adapter;

vi.mock('qrcode', () => ({
  default: {
    toDataURL: vi.fn((value: string) => Promise.resolve(`data:image/png;base64,${btoa(value)}`)),
  },
}));

const projectId = '11111111-1111-1111-1111-111111111111';
const mainAccount: WhatsAppAccount = {
  id: '22222222-2222-2222-2222-222222222222',
  projectId,
  name: 'المبيعات الرئيسي',
  isDefault: true,
};
const branchAccount: WhatsAppAccount = {
  id: '33333333-3333-3333-3333-333333333333',
  projectId,
  name: 'فرع الجيزة',
  isDefault: false,
};

const statusResponse = (accountId: string, status: 'Connected' | 'Disconnected', phoneNumber: string | null = null) => ({
  projectId, whatsappAccountId: accountId, status, phoneNumber,
});

const ok = (config: InternalAxiosRequestConfig, data: unknown): AxiosResponse => ({
  data,
  status: 200,
  statusText: 'OK',
  headers: {},
  config,
});

const requestBody = (config: InternalAxiosRequestConfig) => (
  typeof config.data === 'string' ? JSON.parse(config.data) as unknown : config.data
);

describe('WhatsAppAccountsPanel', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    api.defaults.adapter = originalAdapter;
  });

  it('يعزل ربط وفصل حسابين ويستخدم الحساب المحدد في كل طلب', async () => {
    const user = userEvent.setup();
    let releaseStartSession!: () => void;
    const startSessionGate = new Promise<void>((resolve) => { releaseStartSession = resolve; });
    const requests: InternalAxiosRequestConfig[] = [];
    api.defaults.adapter = async (config) => {
      requests.push(config);
      const accountId = config.params?.whatsappAccountId;
      if (config.method === 'get' && config.url === '/api/whatsapp/accounts') {
        return ok(config, [mainAccount, branchAccount]);
      }
      if (config.method === 'get' && config.url === '/api/whatsapp/session/status') {
        if (accountId === mainAccount.id) {
          return ok(config, statusResponse(mainAccount.id, 'Connected', '201000000001'));
        }
        if (accountId === branchAccount.id) {
          return ok(config, statusResponse(branchAccount.id, 'Disconnected'));
        }
      }
      if (config.method === 'put' && config.url === `/api/whatsapp/accounts/${branchAccount.id}`) {
        return ok(config, {});
      }
      if (config.method === 'post' && config.url === '/api/whatsapp/session/start') {
        await startSessionGate;
        return ok(config, {});
      }
      if (config.method === 'get' && config.url === '/api/whatsapp/session/qr' && accountId === branchAccount.id) {
        return ok(config, { qr: 'branch-qr' });
      }
      if (config.method === 'post' && config.url === '/api/whatsapp/session/disconnect') {
        return ok(config, {});
      }
      throw new Error(`Unexpected request: ${String(config.method)} ${String(config.url)} (${String(accountId)})`);
    };

    render(<WhatsAppAccountsPanel projectId={projectId} />);

    const mainRow = await screen.findByRole('listitem', { name: mainAccount.name });
    const branchRow = screen.getByRole('listitem', { name: branchAccount.name });
    expect(requests.some((request) => request.url === '/api/whatsapp/accounts'
      && request.params?.projectId === projectId)).toBe(true);
    expect(requests.some((request) => request.url === '/api/whatsapp/session/status'
      && request.params?.projectId === projectId
      && request.params?.whatsappAccountId === mainAccount.id)).toBe(true);
    expect(requests.some((request) => request.url === '/api/whatsapp/session/status'
      && request.params?.projectId === projectId
      && request.params?.whatsappAccountId === branchAccount.id)).toBe(true);
    expect(within(mainRow).getByText('متصل')).toBeInTheDocument();
    expect(within(mainRow).getByText('+201000000001')).toBeInTheDocument();
    expect(within(branchRow).getByText('غير متصل')).toBeInTheDocument();

    await user.click(within(branchRow).getByRole('button', { name: `تعيين حساب ${branchAccount.name} كافتراضي` }));
    await waitFor(() => {
      const request = requests.find((candidate) => (
        candidate.method === 'put'
        && candidate.url === `/api/whatsapp/accounts/${branchAccount.id}`
      ));
      expect(request).toBeDefined();
      expect(requestBody(request!)).toEqual({
        projectId,
        name: branchAccount.name,
        isDefault: true,
      });
    });
    expect(await within(branchRow).findByText('افتراضي')).toBeInTheDocument();

    await user.click(within(branchRow).getByRole('button', { name: `ربط حساب ${branchAccount.name}` }));
    await waitFor(() => {
      const request = requests.find((candidate) => (
        candidate.method === 'post'
        && candidate.url === '/api/whatsapp/session/start'
      ));
      expect(request).toBeDefined();
      expect(requestBody(request!)).toEqual({
        projectId,
        whatsappAccountId: branchAccount.id,
      });
    });
    expect(within(branchRow).getByRole('button', { name: `ربط حساب ${branchAccount.name}` })).toBeDisabled();
    expect(within(mainRow).getByRole('button', { name: `فصل حساب ${mainAccount.name}` })).toBeEnabled();
    await act(async () => {
      releaseStartSession();
      await startSessionGate;
    });
    expect(await within(branchRow).findByRole('img', { name: `كود ربط حساب ${branchAccount.name}` })).toBeInTheDocument();
    expect(within(mainRow).getByText('متصل')).toBeInTheDocument();

    await user.click(within(mainRow).getByRole('button', { name: `فصل حساب ${mainAccount.name}` }));
    expect(screen.getByRole('dialog', { name: `فصل حساب «${mainAccount.name}»؟` })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'تأكيد الفصل' }));
    await waitFor(() => {
      const request = requests.find((candidate) => (
        candidate.method === 'post'
        && candidate.url === '/api/whatsapp/session/disconnect'
      ));
      expect(request).toBeDefined();
      expect(requestBody(request!)).toEqual({
        projectId,
        whatsappAccountId: mainAccount.id,
      });
    });
    expect(await within(mainRow).findByText('غير متصل')).toBeInTheDocument();
    expect(within(branchRow).getByRole('img', { name: `كود ربط حساب ${branchAccount.name}` })).toBeInTheDocument();
  });

  it('يتجاهل استجابة حالة قديمة للحساب نفسه ولا يغيّر حالة الحساب الآخر', async () => {
    const user = userEvent.setup();
    let resolveOlderStatus!: (value: ReturnType<typeof statusResponse>) => void;
    const olderStatus = new Promise<ReturnType<typeof statusResponse>>((resolve) => { resolveOlderStatus = resolve; });
    let mainStatusRequestCount = 0;

    api.defaults.adapter = async (config) => {
      if (config.method === 'get' && config.url === '/api/whatsapp/accounts') {
        return ok(config, [mainAccount, branchAccount]);
      }
      const accountId = config.params?.whatsappAccountId;
      if (config.method === 'get' && config.url === '/api/whatsapp/session/status' && accountId === mainAccount.id) {
        mainStatusRequestCount += 1;
        if (mainStatusRequestCount === 1) return ok(config, await olderStatus);
        return ok(config, statusResponse(mainAccount.id, 'Connected', '201000000009'));
      }
      if (config.method === 'get' && config.url === '/api/whatsapp/session/status' && accountId === branchAccount.id) {
        return ok(config, statusResponse(branchAccount.id, 'Disconnected'));
      }
      throw new Error(`Unexpected request: ${String(config.method)} ${String(config.url)} (${String(accountId)})`);
    };

    render(<WhatsAppAccountsPanel projectId={projectId} />);
    const mainRow = await screen.findByRole('listitem', { name: mainAccount.name });
    const branchRow = screen.getByRole('listitem', { name: branchAccount.name });
    expect(await within(branchRow).findByText('غير متصل')).toBeInTheDocument();

    await user.click(within(mainRow).getByRole('button', { name: `تحديث حالة حساب ${mainAccount.name}` }));
    expect(await within(mainRow).findByText('متصل')).toBeInTheDocument();
    expect(within(mainRow).getByText('+201000000009')).toBeInTheDocument();

    await act(async () => {
      resolveOlderStatus(statusResponse(mainAccount.id, 'Disconnected'));
      await olderStatus;
    });

    expect(within(mainRow).getByText('متصل')).toBeInTheDocument();
    expect(within(mainRow).queryByText('غير متصل')).not.toBeInTheDocument();
    expect(within(branchRow).getByText('غير متصل')).toBeInTheDocument();
  });
});
