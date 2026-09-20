'use client';

import { useEffect, useState } from 'react';
import { api } from '@/services/api';
import { ProcessingCase, ProcessingDetail, processingLabels } from './processing-types';
import { inboxLink, timeLabel } from './types';
import styles from './reply-review.module.css';

export default function ProcessingCaseDetail({ row, url, timezone, canManage, revision, onChanged }: {
  row: ProcessingCase; url: string; timezone: string; canManage: boolean; revision: number; onChanged: () => void;
}) {
  const [detail, setDetail] = useState<ProcessingDetail | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const endpoint = `${url}/conversations/${row.conversationId}`;
  useEffect(() => {
    const controller = new AbortController();
    void api.get<ProcessingDetail>(endpoint, { signal: controller.signal }).then(response => {
      if (!controller.signal.aborted) { setDetail(response.data); setError(''); }
    }).catch(() => { if (!controller.signal.aborted) setError('تعذر تحميل سجل المعالجة.'); });
    return () => controller.abort();
  }, [endpoint, revision, row.state, row.lastReviewedAtUtc]);
  async function action(kind: 'verify' | 'retry') {
    setBusy(true); setNotice('');
    try { await api.post(`${endpoint}/${kind}`, {}); setNotice('تمت إضافة المهمة للجدول.'); onChanged(); }
    catch { setError('تعذر جدولة المهمة. حدّث الصفحة وحاول مرة أخرى.'); }
    finally { setBusy(false); }
  }
  async function copy() {
    setBusy(true);
    try {
      const current = await api.get<ProcessingDetail>(endpoint);
      setDetail(current.data);
      if (current.data.draftOutdated || !current.data.draftContent) { setError('تغيرت المحادثة؛ جهّز مسودة حديثة قبل النسخ.'); return; }
      await navigator.clipboard.writeText(current.data.draftContent);
      setNotice('تم نسخ المسودة. راجعها وأرسلها من المحادثة؛ النظام سيعيد فحص الرد الجديد.');
    } catch { setError('تعذر النسخ. يمكنك تحديد النص بعد التأكد من حداثة المحادثة.'); }
    finally { setBusy(false); }
  }
  const href = inboxLink(row.conversationId, row.channel);
  return <div className={styles.processingDetail} aria-label={`معالجة ${row.customerName}`}>
    {row.summary && <p>{row.summary}</p>}
    {row.recommendation && <p><strong>المعالجة المقترحة: </strong>{row.recommendation}</p>}
    {row.lastError && <p className={styles.warning}>{row.lastError}</p>}
    {detail?.draftContent && <div className={styles.draft}>
      <label htmlFor={`saved-draft-${row.id}`}>مسودة محفوظة للمراجعة قبل الإرسال</label>
      {detail.draftOutdated && <p role="alert" className={styles.warning}>وصلت رسائل أحدث. هذه المسودة تحتاج تحديثًا.</p>}
      <textarea id={`saved-draft-${row.id}`} readOnly rows={5} value={detail.draftContent} />
      {canManage && <button onClick={() => void copy()} disabled={busy || detail.draftOutdated}>نسخ المسودة بعد التحقق</button>}
    </div>}
    <div className={styles.actions}>
      {href && <a href={href}>فتح المحادثة ↗</a>}
      {canManage && row.state !== 'Reviewing' && <>
        <button disabled={busy} onClick={() => void action('retry')}>إعادة المراجعة والمعالجة</button>
        {row.needsResolution && <button disabled={busy} onClick={() => void action('verify')}>جدولة التحقق من الحل</button>}
      </>}
    </div>
    {error && <p role="alert" className={styles.error}>{error}</p>}
    {notice && <p role="status" className={styles.note}>{notice}</p>}
    <h4>سجل المراجعات والمحاولات</h4>
    {detail ? <ol className={styles.runHistory}>{detail.runs.length ? detail.runs.map(run => <li key={run.id}>
      <strong>{processingLabels[run.outcome] ?? run.outcome}</strong>
      <span>{timeLabel(run.finishedAtUtc, timezone, true)} · محاولة {run.attempt}</span>
      {run.summary && <p>{run.summary}</p>}{run.error && <p className={styles.warning}>{run.error}</p>}
    </li>) : <li>لم تنتهِ محاولة بعد.</li>}</ol> : <p role="status">جاري تحميل السجل…</p>}
  </div>;
}
