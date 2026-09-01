'use client';

import { useState } from 'react';
import { isAxiosError } from 'axios';
import { Bot, CheckCircle2, Film, ImageIcon, Play, TimerReset } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { AdDecision, Creative } from '../types';
import styles from '../AdManager.module.css';

export function CreativeLab({ projectId, creatives, onChanged, latestTest, canManage = true }: { projectId: string; creatives: Creative[]; onChanged: () => Promise<unknown>; latestTest?: AdDecision; canManage?: boolean }) {
  const videos = creatives.filter(creative => creative.mediaType === 'Video').length;
  const images = creatives.filter(creative => creative.mediaType === 'Image').length;
  const [checking, setChecking] = useState(false);
  const [testOutcome, setTestOutcome] = useState<string | null>(null);

  const checkNow = async () => {
    if (!projectId) return;
    setChecking(true);
    setTestOutcome(null);
    try {
      const testRun = await adManagerApi.startWhatsAppTest(projectId);
      setTestOutcome(testRun.createdAds > 0 ? `تم إنشاء ${testRun.createdAds} اختبار وإرساله لمراجعة الأمان.` : testRun.reason);
      await onChanged();
    } catch (error) {
      if (!isAxiosError(error)) throw error;
      setTestOutcome('تعذر فحص المحتوى الآن. راجع اتصال Facebook ثم أعد المحاولة.');
    } finally {
      setChecking(false);
    }
  };

  return <div className={styles.creativeLab}>
    <div className={styles.labHeader}><div><Bot size={22} aria-hidden="true" /><h2>اختبارات محتوى WhatsApp</h2><p>يسحب النظام بوستات الصفحة وفيديوهاتها، ثم يختبر الأنسب داخل نفس حملة WhatsApp بعد اكتمال الجاهزية. ويمكن للمدير طلب فحص فوري عند إضافة محتوى جديد.</p></div><span className={styles.autoBadge}><CheckCircle2 size={16} aria-hidden="true" /> فحص مجدول</span></div>
    <div className={styles.labFlow} aria-label="خطوات اختبار المحتوى"><span>يسحب المحتوى الجديد</span><span>يختار نسختين</span><span>يراجع AI والأمان</span><span>يقارن المحادثات والتكلفة</span><span>يبقي الفائز</span></div>
    {latestTest && <p className={styles.testStatus}><strong>آخر دورة: {latestTest.state === 'Executed' ? 'تم إنشاء الاختبار' : latestTest.state === 'Waiting' ? 'بانتظار محتوى أو بيانات كافية' : latestTest.state}</strong>{latestTest.reason && <span>{latestTest.reason}</span>}</p>}
    <p className={styles.labSummary}><Film size={16} /> {videos} فيديو <ImageIcon size={16} /> {images} صورة <TimerReset size={16} /> يفحص محتوى جديد كل 6 ساعات، ولا يبدأ اختبارًا مكررًا لنفس الإعلان.</p>
    {videos === 0 && <p className={styles.testStatus}><strong>لا يوجد فيديو مؤهل حاليًا.</strong><span>انشر فيديو جديدًا على صفحة Facebook، ثم استخدم الفحص الفوري ليتم قراءته وترشيحه.</span></p>}
    <div className={styles.controlRow}>
      {canManage ? <button className={styles.primaryButton} type="button" onClick={() => void checkNow()} disabled={checking || !projectId}><Play size={16} aria-hidden="true" /> {checking ? 'جارٍ فحص المحتوى…' : 'فحص وتشغيل اختبار الآن'}</button>
        : <p className={styles.readOnlyBadge}>العرض فقط؛ طلب اختبار جديد يحتاج دور مدير.</p>}
      {testOutcome && <p className={styles.testStatus} role="status" aria-live="polite">{testOutcome}</p>}
    </div>
  </div>;
}
