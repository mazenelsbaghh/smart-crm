import { render, screen, waitFor } from '@testing-library/react';
import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { adManagerApi } from '../api/ad-manager-api';
import { SettingsPanel } from '../components/SettingsPanel';
import type { AdvertisingConnection, AdvertisingEnvelope, MetaResourceCatalog } from '../types';

const originalGatewayFlag = vi.hoisted(() => {
  const original = process.env.NEXT_PUBLIC_ENABLE_EXPERIMENTAL_BAILEYS_AD_ATTRIBUTION;
  process.env.NEXT_PUBLIC_ENABLE_EXPERIMENTAL_BAILEYS_AD_ATTRIBUTION = 'false';
  return original;
});

vi.mock('../api/ad-manager-api', () => ({
  adManagerApi: {
    connection: vi.fn(),
    envelope: vi.fn(),
    resources: vi.fn(),
    gatewayStatus: vi.fn(),
  },
}));

const connection: AdvertisingConnection = {
  id: 'connection-1',
  version: 1,
  state: 'Connected',
  adAccountExternalId: 'account-1',
  pageExternalId: 'page-1',
  wabaExternalId: 'waba-1',
  phoneNumberExternalId: 'phone-1',
  datasetExternalId: 'dataset-1',
  integrationMode: 'CloudApiCoexistence',
};

const envelope: AdvertisingEnvelope = {
  id: 'envelope-1',
  version: 2,
  state: 'Active',
  dailyCap: 150,
  periodCap: 3_000,
  periodCapKind: 'Monthly',
  currency: 'EGP',
  allowedCountriesJson: '["EG"]',
  hardExcludedGeoJson: '[]',
  hardMinimumAge: 21,
  hardRequiredLanguagesJson: '["ar"]',
};

const resources: MetaResourceCatalog = {
  adAccounts: [{ id: 'account-1', name: 'Main Ads', currency: 'EGP', timezone: 'Africa/Cairo' }],
  pages: [{ id: 'page-1', name: 'Main Page' }],
  datasets: [{ id: 'dataset-1', name: 'Main Dataset' }],
  wabas: [{
    id: 'waba-1',
    name: 'Main WABA',
    phones: [{
      id: 'phone-1',
      displayPhoneNumber: '201000000000',
      verifiedName: 'Main WhatsApp',
      qualityRating: 'GREEN',
    }],
  }],
  grantedPermissions: [],
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => { resolve = resolvePromise; });
  return { promise, resolve };
}

beforeEach(() => vi.clearAllMocks());

afterAll(() => {
  if (originalGatewayFlag === undefined) {
    delete process.env.NEXT_PUBLIC_ENABLE_EXPERIMENTAL_BAILEYS_AD_ATTRIBUTION;
  } else {
    process.env.NEXT_PUBLIC_ENABLE_EXPERIMENTAL_BAILEYS_AD_ATTRIBUTION = originalGatewayFlag;
  }
});

describe('SettingsPanel requests', () => {
  it('loads connected production settings without requesting experimental Gateway status', async () => {
    vi.mocked(adManagerApi.connection).mockResolvedValue(connection);
    vi.mocked(adManagerApi.envelope).mockResolvedValue(envelope);
    vi.mocked(adManagerApi.resources).mockResolvedValue(resources);

    render(<SettingsPanel projectId="project-1" dailyCap={0} onSaved={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByLabelText('حساب الإعلانات')).toHaveValue('account-1');
    });

    expect(screen.getByLabelText('صفحة Facebook')).toHaveValue('page-1');
    expect(screen.getByLabelText('حساب WhatsApp Business')).toHaveValue('waba-1');
    expect(screen.getByLabelText('رقم واتساب')).toHaveValue('phone-1');
    expect(screen.getByLabelText('Dataset للتحويلات')).toHaveValue('dataset-1');
    expect(screen.getByLabelText('السقف اليومي')).toHaveValue(150);
    expect(screen.getByLabelText('السقف الشهري')).toHaveValue(3_000);
    expect(screen.getByText('تفويض الميزانية نشط حاليًا.')).toBeVisible();

    expect(adManagerApi.gatewayStatus).not.toHaveBeenCalled();
  });

  it('aborts a stale settings load before applying the next project', async () => {
    const staleConnection = deferred<AdvertisingConnection | null>();
    let staleSignal: AbortSignal | undefined;
    vi.mocked(adManagerApi.connection).mockImplementation((projectId, signal) => {
      if (projectId === 'old-project') {
        staleSignal = signal;
        return staleConnection.promise;
      }
      return Promise.resolve(connection);
    });
    vi.mocked(adManagerApi.envelope).mockResolvedValue(envelope);
    vi.mocked(adManagerApi.resources).mockResolvedValue(resources);

    const { rerender } = render(
      <SettingsPanel projectId="old-project" dailyCap={0} onSaved={vi.fn()} />,
    );
    await waitFor(() => expect(staleSignal).toBeDefined());

    rerender(<SettingsPanel projectId="new-project" dailyCap={0} onSaved={vi.fn()} />);

    expect(staleSignal?.aborted).toBe(true);
    await waitFor(() => expect(screen.getByLabelText('حساب الإعلانات')).toHaveValue('account-1'));
    staleConnection.resolve(connection);
  });
});
