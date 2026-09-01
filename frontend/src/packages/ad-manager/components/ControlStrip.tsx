'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle, CheckCircle2, Gauge, ShieldAlert } from 'lucide-react';
import type { StopState } from '../types';
import styles from '../AdManager.module.css';

interface Props { ready: boolean; autopilot: boolean; emergencyStop: boolean; continuingSpend?: boolean; busy: boolean; updatedAt?: string; reportingTimezone?: string; stopState?: StopState | null; canManage?: boolean; onStop: () => void; onResume: () => void }

export function ControlStrip({ ready, autopilot, emergencyStop, continuingSpend, busy, updatedAt, reportingTimezone, stopState, canManage = true, onStop, onResume }: Props) {
  const normalStopProgress = stopState?.disable?.progress;
  const emergencyProgress = stopState?.emergencyStop?.progress;
  const normalStopInProgress = Boolean(stopState?.disable && (normalStopProgress?.pending || normalStopProgress?.pauseOngoing || normalStopProgress?.deliveryMayContinue));
  const spendStillRunning = Boolean(continuingSpend || emergencyProgress?.continuingSpend || normalStopProgress?.deliveryMayContinue || normalStopProgress?.continuingSpend);
  const unsafe = emergencyStop || normalStopInProgress || !ready;
  const [stale, setStale] = useState(!updatedAt);
  useEffect(() => {
    const evaluate = () => setStale(!updatedAt || Date.now() - new Date(updatedAt).getTime() > 90_000);
    evaluate();
    const timer = window.setInterval(evaluate, 30_000);
    return () => window.clearInterval(timer);
  }, [updatedAt]);
  return <aside className={`${styles.controlStrip} ${unsafe ? styles.controlStripUnsafe : ''}`} aria-label="حالة وأمان مدير الإعلانات">
    <div className={styles.controlStatus} role="status" aria-live="polite">
      {unsafe ? <AlertTriangle size={18} /> : <CheckCircle2 size={18} />}
      <div><strong>{emergencyStop ? 'الإعلانات المملوكة متوقفة للحماية' : ready ? 'النظام جاهز وتحت المراقبة' : 'لن يبدأ صرف قبل اكتمال الجاهزية'}</strong>
        <small><Gauge size={13} aria-hidden="true" /> الذكاء الإعلاني {autopilot ? 'يعمل' : 'متوقف'} · {stale ? 'البيانات قديمة — آخر تحديث' : 'آخر تحديث'} {formatUpdatedAt(updatedAt, reportingTimezone)}</small></div>
    </div>
    {spendStillRunning && <span className={styles.continuingSpend} role="alert">بعض الإعلانات الحالية قد تظل تصرف حتى تؤكد Meta الإيقاف</span>}
    {emergencyProgress && <span className={styles.stopProgress} role="status">تقدم الإيقاف الطارئ: {emergencyProgress.succeeded}/{emergencyProgress.total} · معلّق {emergencyProgress.pending} · يحتاج مراجعة {emergencyProgress.failed + emergencyProgress.unknown}</span>}
    {normalStopProgress && <span className={styles.stopProgress} role="status">تقدم الإيقاف العادي: {normalStopProgress.succeeded}/{normalStopProgress.total} · معلّق {normalStopProgress.pending}{stopState?.disable?.progress.needsAttention ? ' · توجد عناصر تحتاج مراجعة' : ''}</span>}
    {!canManage ? <span className={styles.readOnlyBadge}>عرض فقط — يلزم دور مدير للتغيير</span>
      : emergencyStop ? <button className={styles.secondaryButton} onClick={onResume} disabled={busy}>فحص الاستعادة الآمنة</button>
        : <button className={styles.dangerButton} onClick={onStop} disabled={busy}><ShieldAlert size={17} aria-hidden="true" /> إيقاف طارئ</button>}
  </aside>;
}

function formatUpdatedAt(updatedAt: string | undefined, timezone: string | undefined) {
  if (!updatedAt) return '—';
  try {
    return new Intl.DateTimeFormat('ar-EG', { timeStyle: 'short', timeZone: timezone || 'UTC' }).format(new Date(updatedAt));
  } catch {
    return new Intl.DateTimeFormat('ar-EG', { timeStyle: 'short', timeZone: 'UTC' }).format(new Date(updatedAt)) + ' UTC';
  }
}
