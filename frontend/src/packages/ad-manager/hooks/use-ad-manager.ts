'use client';

import { useCallback, useEffect, useState } from 'react';
import { adManagerApi } from '../api/ad-manager-api';
import type { AdDecision, AdvertisingOverview, Conversion, Creative, ManagedAd } from '../types';

export function useAdManager(projectId?: string) {
  const [overview, setOverview] = useState<AdvertisingOverview | null>(null);
  const [campaigns, setCampaigns] = useState<ManagedAd[]>([]);
  const [creatives, setCreatives] = useState<Creative[]>([]);
  const [conversions, setConversions] = useState<Conversion[]>([]);
  const [decisions, setDecisions] = useState<AdDecision[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setOverview(null); setCampaigns([]); setCreatives([]); setConversions([]); setDecisions([]);
    if (!projectId) { setLoading(false); return; }
    setLoading(true); setError(null);
    try {
      const [nextOverview, nextCampaigns, nextCreatives, nextConversions, nextDecisions] = await Promise.all([
        adManagerApi.overview(projectId), adManagerApi.campaigns(projectId), adManagerApi.creatives(projectId), adManagerApi.conversions(projectId), adManagerApi.decisions(projectId),
      ]);
      setOverview(nextOverview); setCampaigns(nextCampaigns); setCreatives(nextCreatives); setConversions(nextConversions); setDecisions(nextDecisions);
    } catch {
      setError('تعذّر تحميل بيانات مدير الإعلانات. راجع الاتصال ثم حاول مرة أخرى.');
    } finally { setLoading(false); }
  }, [projectId]);

  useEffect(() => {
    const task = window.setTimeout(() => void refresh(), 0);
    return () => window.clearTimeout(task);
  }, [refresh]);
  return { overview, campaigns, creatives, conversions, decisions, loading, error, refresh };
}
