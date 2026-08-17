'use client';

import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, Download, LoaderCircle, RefreshCw, ShieldAlert } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { ExistingFacebookAd } from '../types';
import styles from '../AdManager.module.css';

type ExistingCampaignImportProps = {
  projectId: string;
  dailyCap: number;
  refreshToken: string;
  onImported: () => Promise<unknown>;
};

export function ExistingCampaignImport({ projectId, dailyCap, refreshToken, onImported }: ExistingCampaignImportProps) {
  const [candidates, setCandidates] = useState<ExistingFacebookAd[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const whatsAppCandidates = candidates
    .filter(ad => ad.eligible && ad.destination === 'WhatsApp')
    .sort((left, right) => left.dailyBudget - right.dailyBudget || Number(right.effectiveStatus === 'ACTIVE') - Number(left.effectiveStatus === 'ACTIVE'));
  const recommendedWhatsAppAd = whatsAppCandidates.find(ad => ad.dailyBudget <= dailyCap) ?? whatsAppCandidates[0];
  const requiresHigherCap = Boolean(recommendedWhatsAppAd && recommendedWhatsAppAd.dailyBudget > dailyCap);

  const discover = useCallback(async (showHint = true) => {
    setBusy(true); if (showHint) setMessage(null);
    try {
      const existingAds = await adManagerApi.existingFacebookAds(projectId);
      setCandidates(existingAds);
      if (showHint) setMessage(existingAds.length ? 'تمت قراءة الحملات. اختر زر إدارة حملة WhatsApp المقترحة إذا أردت أن يتولاها الـAI.' : 'لا توجد إعلانات في حساب Facebook المحدد.');
    } catch { setMessage('تعذّر قراءة الحملات الحالية من Facebook. راجع الاتصال والصلاحيات.'); }
    finally { setBusy(false); }
  }, [projectId]);

  useEffect(() => {
    const load = window.setTimeout(() => void discover(false), 0);
    return () => window.clearTimeout(load);
  }, [discover, refreshToken]);

  const importRecommendedWhatsAppAd = async () => {
    if (!recommendedWhatsAppAd) return;
    setBusy(true); setMessage(null);
    try {
      const imported = await adManagerApi.importFacebookAds(projectId, [recommendedWhatsAppAd.adId]);
      await discover(false); await onImported();
      setMessage(`تم ضم حملة WhatsApp المقترحة، وحجز ${imported.reservedDailyBudget} من السقف اليومي لإدارتها. لم نغيّر مواضعها على Facebook.`);
    } catch { setMessage('تعذّر ضم الحملة. ارفع السقف اليومي من الإعدادات أو خفّض ميزانية الحملة في Facebook أولًا.'); setBusy(false); }
  };

  return <section className={styles.importPanel} aria-labelledby="existing-campaigns-title">
    <div className={styles.importHeader}>
      <div><Download size={20} /><h2 id="existing-campaigns-title">الحملات الموجودة في الحساب</h2><p>تُقرأ تلقائيًا عند فتح الصفحة وبعد كل حفظ. يظل زر التحديث متاحًا لقراءة أحدث تغيير فورًا.</p></div>
      <button className={styles.secondaryButton} onClick={() => void discover(true)} disabled={busy}>{busy ? <LoaderCircle size={16} /> : <RefreshCw size={16} />} تحديث الآن</button>
    </div>
    {candidates.length > 0 && <div className={styles.importList}>
      {candidates.map(ad => <div key={ad.adId} className={`${styles.importRow} ${ad.alreadyManaged ? styles.importedRow : ''}`}>
        <span className={styles.importState}>{ad.alreadyManaged ? <CheckCircle2 size={17} /> : ad.eligible ? <Download size={17} /> : <ShieldAlert size={17} />}</span>
        <span><strong>{ad.adName}</strong><small>{ad.campaignName} · {ad.adSetName}</small></span>
        <span><strong>{ad.dailyBudget} يوميًا</strong><small>{placementSummary(ad)}</small></span>
        <span><strong>{ad.effectiveStatus}</strong><small>{ad.alreadyManaged ? 'تحت الإدارة بالفعل' : ad.ineligibleReason ?? 'جاهز للاستيراد'}</small></span>
      </div>)}
      <div className={styles.importActions}><span>{recommendedWhatsAppAd ? requiresHigherCap ? `الحملة الأقل ميزانية تحتاج سقفًا يوميًا لا يقل عن ${recommendedWhatsAppAd.dailyBudget}. عدّله من الإعدادات أولًا.` : `الحملة المقترحة: ${recommendedWhatsAppAd.adName}` : 'لا توجد حملة WhatsApp جاهزة للإدارة.'}</span><button className={styles.primaryButton} disabled={busy || !recommendedWhatsAppAd || requiresHigherCap} onClick={() => void importRecommendedWhatsAppAd()}><Download size={16} /> ضم حملة WhatsApp لإدارة AI</button></div>
    </div>}
    {message && <p className={styles.inlineMessage} role="status" aria-live="polite">{message}</p>}
  </section>;
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
