'use client';

import { useState } from 'react';
import { CheckCircle2, Download, LoaderCircle, RefreshCw, ShieldAlert } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { ExistingFacebookAd } from '../types';
import styles from '../AdManager.module.css';

export function ExistingCampaignImport({ projectId, onImported }: { projectId: string; onImported: () => Promise<unknown> }) {
  const [candidates, setCandidates] = useState<ExistingFacebookAd[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const discover = async (showHint = true) => {
    setBusy(true); if (showHint) setMessage(null);
    try {
      const existingAds = await adManagerApi.existingFacebookAds(projectId);
      setCandidates(existingAds);
      setSelected(existingAds.filter(ad => ad.eligible).map(ad => ad.adId));
      if (showHint) setMessage(existingAds.length ? 'راجع الإعلانات وحدد ما تريد ضمه لإدارة الـAI.' : 'لا توجد إعلانات في حساب Facebook المحدد.');
    } catch { setMessage('تعذّر قراءة الحملات الحالية من Facebook. راجع الاتصال والصلاحيات.'); }
    finally { setBusy(false); }
  };

  const importSelected = async () => {
    if (selected.length === 0) return;
    setBusy(true); setMessage(null);
    try {
      const imported = await adManagerApi.importFacebookAds(projectId, selected);
      await discover(false); await onImported();
      setMessage(`تم ضم ${imported.importedAds} إعلان، وحجز ${imported.reservedDailyBudget} من السقف اليومي لإدارتها. لم نغيّر حالتها على Facebook.`);
    } catch { setMessage('لم يتم الاستيراد. تأكد أن السقف يغطي ميزانيات الإعلانات وأن الـplacements Facebook فقط.'); setBusy(false); }
  };

  return <section className={styles.importPanel} aria-labelledby="existing-campaigns-title">
    <div className={styles.importHeader}>
      <div><Download size={20} /><h2 id="existing-campaigns-title">الحملات الموجودة على Facebook</h2><p>ضم الإعلانات الحالية للمتابعة والتحسين، بدون إعادة إنشائها أو تعديلها أثناء الاستيراد.</p></div>
      <button className={styles.secondaryButton} onClick={() => void discover(true)} disabled={busy}>{busy ? <LoaderCircle size={16} /> : <RefreshCw size={16} />} قراءة الحساب</button>
    </div>
    {candidates.length > 0 && <div className={styles.importList}>
      {candidates.map(ad => <label key={ad.adId} className={`${styles.importRow} ${ad.alreadyManaged ? styles.importedRow : ''}`}>
        <input type="checkbox" disabled={!ad.eligible || busy} checked={selected.includes(ad.adId)} onChange={() => setSelected(current => current.includes(ad.adId) ? current.filter(id => id !== ad.adId) : [...current, ad.adId])} />
        <span className={styles.importState}>{ad.alreadyManaged ? <CheckCircle2 size={17} /> : ad.eligible ? <Download size={17} /> : <ShieldAlert size={17} />}</span>
        <span><strong>{ad.adName}</strong><small>{ad.campaignName} · {ad.adSetName}</small></span>
        <span><strong>{ad.dailyBudget} يوميًا</strong><small>{ad.facebookPositions.join('، ') || 'مواضع غير محددة'}</small></span>
        <span><strong>{ad.effectiveStatus}</strong><small>{ad.alreadyManaged ? 'تحت الإدارة بالفعل' : ad.ineligibleReason ?? 'جاهز للاستيراد'}</small></span>
      </label>)}
      <div className={styles.importActions}><span>{selected.length} إعلان محدد</span><button className={styles.primaryButton} disabled={busy || selected.length === 0} onClick={() => void importSelected()}><Download size={16} /> ضم لإدارة AI</button></div>
    </div>}
    {message && <p className={styles.inlineMessage} role="status" aria-live="polite">{message}</p>}
  </section>;
}
