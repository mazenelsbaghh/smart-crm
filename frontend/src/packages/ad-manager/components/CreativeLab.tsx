'use client';

import { Bot, CheckCircle2, Film, ImageIcon, TimerReset } from 'lucide-react';
import type { AdDecision, Creative } from '../types';
import styles from '../AdManager.module.css';

export function CreativeLab({ creatives, latestTest }: { projectId: string; creatives: Creative[]; onChanged: () => Promise<unknown>; latestTest?: AdDecision }) {
  const videos = creatives.filter(creative => creative.mediaType === 'Video').length;
  const images = creatives.filter(creative => creative.mediaType === 'Image').length;
  return <div className={styles.creativeLab}>
    <div className={styles.labHeader}><div><Bot size={22} /><h2>اختبارات WhatsApp تعمل تلقائيًا</h2><p>لا تحتاج لاختيار بوست أو الضغط على أي زر. النظام يسحب بوستات الصفحة وفيديوهاتها، ثم يختبر الأنسب داخل نفس حملة WhatsApp.</p></div><span className={styles.autoBadge}><CheckCircle2 size={16} /> Autopilot</span></div>
    <div className={styles.testFlow}><span>يسحب المحتوى الجديد</span><span>يختار نسختين</span><span>يراجع AI والأمان</span><span>يقارن المحادثات والتكلفة</span><span>يبقي الفائز</span></div>
    {latestTest && <p className={styles.testStatus}><strong>آخر دورة: {latestTest.state === 'Executed' ? 'تم إنشاء الاختبار' : latestTest.state === 'Waiting' ? 'بانتظار محتوى أو بيانات كافية' : latestTest.state}</strong>{latestTest.reason && <span>{latestTest.reason}</span>}</p>}
    <p className={styles.labSummary}><Film size={16} /> {videos} فيديو <ImageIcon size={16} /> {images} صورة <TimerReset size={16} /> يفحص محتوى جديد يوميًا، ولا يبدأ اختبارًا مكررًا لنفس الإعلان.</p>
  </div>;
}
