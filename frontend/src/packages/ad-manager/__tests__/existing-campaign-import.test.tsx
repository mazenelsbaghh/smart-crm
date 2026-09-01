import { render, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adManagerApi } from '../api/ad-manager-api';
import { ExistingCampaignImport } from '../components/ExistingCampaignImport';

vi.mock('../api/ad-manager-api', () => ({
  adManagerApi: {
    existingFacebookAds: vi.fn(),
    importFacebookAds: vi.fn(),
  },
}));

beforeEach(() => vi.clearAllMocks());

describe('ExistingCampaignImport request lifecycle', () => {
  it('loads once across parent rerenders and aborts the request on unmount', async () => {
    let requestSignal: AbortSignal | undefined;
    vi.mocked(adManagerApi.existingFacebookAds).mockImplementation((_projectId, signal) => {
      requestSignal = signal;
      return Promise.resolve([]);
    });
    const onImported = vi.fn().mockResolvedValue(undefined);
    const { rerender, unmount } = render(
      <ExistingCampaignImport projectId="project-1" dailyCap={100} onImported={onImported} />,
    );
    await waitFor(() => expect(adManagerApi.existingFacebookAds).toHaveBeenCalledOnce());

    rerender(<ExistingCampaignImport projectId="project-1" dailyCap={200} onImported={onImported} />);

    expect(adManagerApi.existingFacebookAds).toHaveBeenCalledOnce();
    unmount();
    expect(requestSignal?.aborted).toBe(true);
  });
});
