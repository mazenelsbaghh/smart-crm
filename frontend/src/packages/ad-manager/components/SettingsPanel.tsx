'use client';

import { useCallback, useEffect, useState } from 'react';
import { Megaphone, LoaderCircle, ShieldCheck } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { MetaResourceCatalog } from '../types';
import styles from '../AdManager.module.css';

type SettingsPanelProps = {
  projectId: string;
  loadResources: boolean;
  onSaved: () => Promise<unknown>;
};

export function SettingsPanel({ projectId, loadResources, onSaved }: SettingsPanelProps) {
  const [resources, setResources] = useState<MetaResourceCatalog | null>(null);
  const [account, setAccount] = useState('');
  const [page, setPage] = useState('');
  const [dataset, setDataset] = useState('');
  const [dailyCap, setDailyCap] = useState(300);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const applyCatalog = useCallback((catalog: MetaResourceCatalog) => {
    setResources(catalog);
    setAccount(catalog.adAccounts[0]?.id ?? '');
    setPage(catalog.pages[0]?.id ?? '');
    setDataset('');
  }, []);

  useEffect(() => {
    if (!loadResources || resources) return;

    const loadConnectedResources = async () => {
      setBusy(true);
      setMessage(null);
      try {
        const catalog = await adManagerApi.resources(projectId);
        applyCatalog(catalog);
        setMessage('تم ربط Facebook. اختر حساب الإعلانات والصفحة وحدّد السقف اليومي. الـPixel اختياري لحملات الموقع فقط.');
      } catch {
        setMessage('تم التفويض، لكن تعذّر تحميل الحسابات المتاحة. راجع صلاحيات حساب Facebook ثم أعد المحاولة.');
      } finally {
        setBusy(false);
      }
    };

    void loadConnectedResources();
  }, [applyCatalog, loadResources, projectId, resources]);

  const connect = async () => {
    setBusy(true); setMessage(null);
    try {
      const result = await adManagerApi.startOAuth(projectId);
      if (result.authorizationUrl.startsWith('http')) { window.location.assign(result.authorizationUrl); return; }
      const catalog = await adManagerApi.resources(projectId);
      applyCatalog(catalog);
      setMessage('تم الربط التجريبي. اختر الموارد وحدد السقف.');
    } catch { setMessage('تعذّر بدء ربط Facebook. راجع إعدادات Meta والصلاحيات.'); }
    finally { setBusy(false); }
  };

  const save = async () => {
    const selectedAccount = resources?.adAccounts.find(x => x.id === account);
    if (!account || !page || dailyCap <= 0) { setMessage('اختر حساب الإعلانات والصفحة وأدخل سقفًا صحيحًا.'); return; }
    setBusy(true); setMessage(null);
    try {
      await adManagerApi.selectConnection(projectId, { adAccountId: account, pageId: page, datasetId: dataset || undefined, currency: selectedAccount?.currency ?? 'EGP', timezone: selectedAccount?.timezone ?? 'Africa/Cairo' });
      await adManagerApi.saveEnvelope(projectId, { dailyCap, currency: selectedAccount?.currency ?? 'EGP', safetyReservePercent: 15, maximumIncreasePercent: 20, cooldownHours: 24, allowedCountries: ['EG'] });
      setMessage(dataset ? 'تم حفظ موارد Facebook والسقف اليومي مع هامش أمان 15%.' : 'تم الحفظ بدون Pixel. ستكون الحملات مخصّصة لرسائل Facebook وواتساب.'); await onSaved();
    } catch { setMessage('تعذّر حفظ الاتصال أو السقف. تأكد أن الموارد متوافقة.'); }
    finally { setBusy(false); }
  };

  return <div className={styles.connectionPanel}>
    <div><Megaphone size={22} /><h2>ربط Facebook Ads</h2><p>التوكن يُحفظ مشفّرًا على السيرفر ولا يظهر في المتصفح بعد الربط.</p></div>
    {!resources ? <button className={styles.primaryButton} onClick={() => void connect()} disabled={busy}>{busy ? <LoaderCircle size={17} /> : <Megaphone size={17} />} ربط حساب Facebook</button> : <div className={styles.formGrid}>
      <label>حساب الإعلانات<select value={account} onChange={(event) => setAccount(event.target.value)}>{resources.adAccounts.map(x => <option key={x.id} value={x.id}>{x.name} · {x.currency}</option>)}</select></label>
      <label>صفحة Facebook<select value={page} onChange={(event) => setPage(event.target.value)}>{resources.pages.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
      <label>Dataset / Pixel (اختياري)<select value={dataset} onChange={(event) => setDataset(event.target.value)}><option value="">بدون Pixel، لحملات الرسائل وواتساب</option>{resources.datasets.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
      <label>السقف اليومي<input type="number" min="1" step="1" value={dailyCap} onChange={(event) => setDailyCap(Number(event.target.value))} /></label>
      <div className={styles.formNote}><ShieldCheck size={17} /> لا تختَر Pixel غير تابع للمشروع. بدونه لا تُنشأ حملات تحويل موقع أو إرسال أحداث شراء إلى Meta.</div>
      <button className={styles.primaryButton} onClick={() => void save()} disabled={busy}>حفظ واختبار الجاهزية</button>
    </div>}
    {message && <p className={styles.inlineMessage} role="status">{message}</p>}
  </div>;
}
