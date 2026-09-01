'use client';

import { useCallback, useEffect, useState } from 'react';
import axios from 'axios';
import { CheckCircle2, Download, LoaderCircle, RefreshCw, ShieldAlert } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import ConfirmDialog from '../../../components/shared/ConfirmDialog';
import type { ExistingFacebookAd } from '../types';
import styles from '../AdManager.module.css';

type ExistingCampaignImportProps = {
  projectId: string;
  dailyCap: number;
  onImported: () => Promise<unknown>;
};

export function ExistingCampaignImport({ projectId, dailyCap, onImported }: ExistingCampaignImportProps) {
  const [candidates, setCandidates] = useState<ExistingFacebookAd[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [selectedWhatsAppAdId, setSelectedWhatsAppAdId] = useState<string | null>(null);
  const [confirmImport, setConfirmImport] = useState(false);
  const selectedWhatsAppAd = candidates.find(ad => ad.adId === selectedWhatsAppAdId && ad.eligible && ad.destination === 'WhatsApp');
  const requiresHigherCap = Boolean(selectedWhatsAppAd && selectedWhatsAppAd.dailyBudget > dailyCap);

  const loadCandidates = useCallback(async (signal?: AbortSignal) => {
    const existingAds = await adManagerApi.existingFacebookAds(projectId, signal);
    if (signal?.aborted) return null;
    setCandidates(existingAds);
    return existingAds;
  }, [projectId]);

  const discover = useCallback(async (feedbackMode: 'announce' | 'silent' = 'announce', signal?: AbortSignal) => {
    setBusy(true);
    if (feedbackMode === 'announce') setMessage(null);
    try {
      const existingAds = await loadCandidates(signal);
      if (existingAds && feedbackMode === 'announce') {
        setMessage(existingAds.length
          ? 'تمت قراءة الحملات. اختر حملة WhatsApp التي تريد أن يديرها الـAI.'
          : 'لا توجد إعلانات في حساب Facebook المحدد.');
      }
    } catch (error) {
      if (!signal?.aborted && !axios.isCancel(error)) {
        setMessage('تعذّر قراءة الحملات الحالية من Facebook. راجع الاتصال والصلاحيات.');
      }
    } finally {
      if (!signal?.aborted) setBusy(false);
    }
  }, [loadCandidates]);

  useEffect(() => {
    const controller = new AbortController();
    const load = window.setTimeout(() => void discover('silent', controller.signal), 0);
    return () => {
      controller.abort();
      window.clearTimeout(load);
    };
  }, [discover]);

  const importSelectedWhatsAppAd = async () => {
    if (!selectedWhatsAppAd) return;
    setBusy(true); setMessage(null);
    let imported: Awaited<ReturnType<typeof adManagerApi.importFacebookAds>>;
    try {
      imported = await adManagerApi.importFacebookAds(projectId, [selectedWhatsAppAd.adId]);
    } catch (error) {
      setMessage(importErrorMessage(error));
      setBusy(false);
      return;
    }

    setSelectedWhatsAppAdId(null);
    const refreshResults = await Promise.allSettled([loadCandidates(), onImported()]);
    const successMessage = `تم ضم حملة WhatsApp التي اخترتها، وحجز ${imported.reservedDailyBudget} من السقف اليومي لإدارتها. لم نغيّر مواضعها على Facebook.`;
    setMessage(refreshResults.some(result => result.status === 'rejected')
      ? `${successMessage} تعذّر تحديث بعض البيانات المعروضة الآن؛ استخدم زر التحديث بعد لحظات.`
      : successMessage);
    setBusy(false);
  };

  return <><section className={styles.importPanel} aria-labelledby="existing-campaigns-title">
    <div className={styles.importHeader}>
      <div><Download size={20} /><h2 id="existing-campaigns-title">الحملات الموجودة في الحساب</h2><p>تُقرأ عند فتح الصفحة وبعد ضم حملة. يظل زر التحديث متاحًا لقراءة أحدث تغيير فورًا.</p></div>
      <button className={styles.secondaryButton} onClick={() => void discover('announce')} disabled={busy}>{busy ? <LoaderCircle size={16} /> : <RefreshCw size={16} />} تحديث الآن</button>
    </div>
    {candidates.length > 0 && <div className={styles.importList}>
      {candidates.map(ad => <div key={ad.adId} className={`${styles.importRow} ${ad.alreadyManaged ? styles.importedRow : ''} ${selectedWhatsAppAd?.adId === ad.adId ? styles.selectedImportRow : ''}`}>
        <span className={styles.importState}>{ad.alreadyManaged ? <CheckCircle2 size={17} /> : ad.eligible ? <Download size={17} /> : <ShieldAlert size={17} />}</span>
        <span><strong>{ad.adName}</strong><small>{ad.campaignName} · {ad.adSetName}</small></span>
        <span><strong>{ad.dailyBudget} يوميًا</strong><small>{placementSummary(ad)}</small></span>
        <span><strong>{ad.effectiveStatus}</strong><small>{ad.alreadyManaged ? 'تحت الإدارة بالفعل' : ad.ineligibleReason ?? 'جاهز للاستيراد'}</small>{ad.eligible && ad.destination === 'WhatsApp' && <button type="button" className={styles.selectCampaignButton} aria-pressed={selectedWhatsAppAd?.adId === ad.adId} onClick={() => setSelectedWhatsAppAdId(ad.adId)} disabled={busy || ad.alreadyManaged}>{selectedWhatsAppAd?.adId === ad.adId ? 'تم الاختيار' : 'اختيار هذه الحملة'}</button>}</span>
      </div>)}
      <div className={styles.importActions}><span>{selectedWhatsAppAd ? requiresHigherCap ? `الحملة المختارة تحتاج سقفًا يوميًا لا يقل عن ${selectedWhatsAppAd.dailyBudget}. عدّله من الإعدادات أولًا.` : `الحملة المختارة: ${selectedWhatsAppAd.adName}` : 'اختر حملة WhatsApp أولًا.'}</span><button type="button" className={styles.primaryButton} disabled={busy || !selectedWhatsAppAd || requiresHigherCap} onClick={() => setConfirmImport(true)}><Download size={16} /> مراجعة ضم الحملة</button></div>
    </div>}
    {message && <p className={styles.inlineMessage} role="status" aria-live="polite">{message}</p>}
  </section><ConfirmDialog
    isOpen={confirmImport}
    title="ضم الحملة لإدارة الذكاء الاصطناعي؟"
    message={selectedWhatsAppAd ? `الحملة: ${selectedWhatsAppAd.adName} (${selectedWhatsAppAd.campaignName}). ميزانيتها اليومية ${selectedWhatsAppAd.dailyBudget} من سقف ${dailyCap}. سيبدأ النظام متابعة قراراتها ضمن التفويض، ولن يغيّر مواضعها الحالية على Meta.` : 'لم تعد هناك حملة صالحة محددة.'}
    confirmLabel="ضم الحملة"
    onCancel={() => setConfirmImport(false)}
    onConfirm={() => { setConfirmImport(false); void importSelectedWhatsAppAd(); }}
  /></>;
}

function importErrorMessage(error: unknown) {
  if (!axios.isAxiosError<{ code?: string; error?: string; message?: string }>(error)) return 'تعذّر ضم الحملة بسبب خطأ غير متوقع. لم يتم تغيير إدارتها.';
  const code = error.response?.data?.code;
  if (code === 'ADS_BUDGET_CAP_EXCEEDED' || code === 'ADS_DAILY_CAP_EXCEEDED') return 'ميزانية الحملة تتجاوز السقف اليومي. عدّل السقف أو خفّض ميزانية الحملة ثم حاول مرة أخرى.';
  if (error.response?.status === 401 || error.response?.status === 403) return 'ليس لديك صلاحية ضم هذه الحملة.';
  if (!error.response) return 'تعذّر الاتصال بالخادم. لم يتم ضم الحملة؛ تحقق من الشبكة وحاول مرة أخرى.';
  return error.response.data?.error ?? error.response.data?.message ?? 'رفض الخادم ضم الحملة. لم يتم تغيير إدارتها.';
}

function placementSummary(ad: ExistingFacebookAd) {
  const placements = [
    ad.facebookPositions.length && `Facebook: ${ad.facebookPositions.join('، ')}`,
    ad.instagramPositions.length && `Instagram: ${ad.instagramPositions.join('، ')}`,
    ad.messengerPositions.length && `Messenger: ${ad.messengerPositions.join('، ')}`,
    ad.audienceNetworkPositions.length && `Audience Network: ${ad.audienceNetworkPositions.join('، ')}`,
    ad.destination && `الوجهة: ${ad.destination}`,
  ].filter(Boolean);
  return placements.join(' | ') || 'لم يرجع Facebook تفاصيل المواضع لهذه الحملة';
}
