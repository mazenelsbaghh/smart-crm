'use client';

import { Fragment, useEffect, useState } from 'react';
import { api } from '@/services/api';
import ReviewScheduleControls from './ReviewScheduleControls';
import ProcessingCaseDetail from './ProcessingCaseDetail';
import { ProcessingReport, processingLabels } from './processing-types';
import { numberLabel, reviewUrl, timeLabel } from './types';
import styles from './reply-review.module.css';

export default function ReviewProcessingPanel({ projectId, timezone, canManage, refreshSignal }: {
  projectId: string; timezone: string; canManage: boolean; refreshSignal: number;
}) {
  const [report, setReport] = useState<ProcessingReport | null>(null);
  const [page, setPage] = useState(1);
  const [view, setView] = useState('active');
  const [revision, setRevision] = useState(0);
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const url = `${reviewUrl(projectId)}/processing`;
  const refresh = () => setRevision(value => value + 1);
  useEffect(() => {
    const controller = new AbortController();
    void api.get<ProcessingReport>(url, { params: { page, view }, signal: controller.signal }).then(response => {
      if (!controller.signal.aborted) { setReport(response.data); setError(''); }
    }).catch(() => { if (!controller.signal.aborted) setError('تعذر تحديث طابور المعالجة. قد تكون البيانات المعروضة أقدم.'); });
    return () => controller.abort();
  }, [url, page, view, revision, refreshSignal]);
  useEffect(() => {
    const timer = window.setInterval(() => { if (!document.hidden) setRevision(value => value + 1); }, 20_000);
    return () => window.clearInterval(timer);
  }, []);
  async function scan() {
    setBusy(true); setNotice('');
    try { await api.post(`${url}/scan`, {}); setNotice('تمت جدولة الدفعة التالية خلال دقيقة.'); refresh(); }
    catch { setError('تعذر جدولة الفحص. تأكد أن الجدولة مفعّلة وحاول مرة أخرى.'); }
    finally { setBusy(false); }
  }
  return <section className={styles.processing} aria-label="جدولة المراجعة والمعالجة">
    <div className={styles.sectionHeading}><div><h2>المراجعة والمعالجة المجدولة</h2>
      <p className={styles.note}>مراجعة ← مسودة معالجة ← رد فعلي ← تحقق بالدليل</p></div>
      <div className={styles.controls}><button onClick={refresh}>تحديث الطابور</button>
        {canManage && <button disabled={busy || !report?.schedule.enabled} onClick={() => void scan()}>جدولة فحص الآن</button>}</div></div>
    {error && <p role="alert" className={styles.error}>{error}</p>}
    {notice && <p role="status" className={styles.note}>{notice}</p>}
    {report ? <>
      <div className={styles.processingStatus}>
        <strong>{report.schedule.enabled ? 'الجدولة مفعّلة' : 'الجدولة متوقفة'}</strong>
        <span>الفحص كل {numberLabel(report.schedule.intervalMinutes)} دقيقة</span>
        {report.schedule.enabled && <span>الموعد التالي: {timeLabel(report.schedule.nextScanAtUtc, timezone, true)}</span>}
        <span>{report.schedule.lastScanAtUtc ? `آخر فحص: ${timeLabel(report.schedule.lastScanAtUtc, timezone, true)}` : 'بانتظار أول فحص'}</span>
      </div>
      {!report.analysisConfigured && <p role="alert" className={styles.warningPanel}>مفتاح التحليل غير مضبوط. أضف مفتاح Gemini من إعدادات المشروع حتى تُنفذ المراجعات.</p>}
      {canManage && <ReviewScheduleControls initial={report.schedule} url={url} onSaved={refresh} />}
      <div className={styles.processingCounts} aria-label="حالات المعالجة الحالية">
        {Object.entries(processingLabels).map(([state, label]) => <span key={state}>{label}: <strong>{numberLabel(report.counts[state] ?? 0)}</strong></span>)}
      </div>
      <div className={styles.sectionHeading}><h3>طابور المعالجة الحالي</h3><div className={styles.tabs} role="group" aria-label="فلتر المعالجة">
        {[['active', 'المهام المفتوحة'], ['all', 'الكل والنتائج']].map(([key, label]) => <button key={key} aria-pressed={view === key}
          onClick={() => { setView(key); setPage(1); setSelected(null); setReport(null); }}>{label}</button>)}</div></div>
      {report.cases.length ? <div className={styles.tableScroll}><table><thead><tr>
        <th>المحادثة</th><th>الحالة والخطوة التالية</th><th>آخر مراجعة</th><th>المعالجة</th>
      </tr></thead><tbody>{report.cases.map(row => <Fragment key={row.id}><tr>
        <td><strong>{row.customerName}</strong><span>{row.channel}</span></td>
        <td>{processingLabels[row.state] ?? row.state}{['Queued', 'RetryScheduled', 'VerifyScheduled', 'DraftReady'].includes(row.state) &&
          <span>{row.state === 'DraftReady' ? 'فحص تنفيذ المعالجة' : 'التنفيذ'}: {timeLabel(row.nextRunAtUtc, timezone, true)}</span>}</td>
        <td>{row.lastReviewedAtUtc ? timeLabel(row.lastReviewedAtUtc, timezone, true) : 'لم تُراجع بعد'}<span>المحاولات: {numberLabel(row.attempts)}</span></td>
        <td><button aria-expanded={selected === row.id} onClick={() => setSelected(selected === row.id ? null : row.id)}>
          {row.hasDraft ? 'المسودة وسجل المعالجة' : 'عرض نتيجة المعالجة'}</button></td>
      </tr>{selected === row.id && <tr><td colSpan={4}><ProcessingCaseDetail row={row} url={url} timezone={timezone}
        canManage={canManage} revision={revision} onChanged={refresh} /></td></tr>}</Fragment>)}</tbody></table></div>
        : <p className={styles.note}>لا توجد مهام في هذا الفلتر. الفحص المجدول يضيف المحادثات التي تحتاج مراجعة.</p>}
      <div className={styles.pagination}><button disabled={page === 1} onClick={() => { setPage(page - 1); setSelected(null); setReport(null); }}>السابق</button>
        <span>صفحة {numberLabel(page)} · {numberLabel(report.total)} مهمة</span>
        <button disabled={page * report.pageSize >= report.total} onClick={() => { setPage(page + 1); setSelected(null); setReport(null); }}>التالي</button></div>
      <p className={styles.note}>الطابور يعرض الحالة الحالية عبر الأيام. تُفحص آخر ٤٨ ساعة تلقائيًا، ويمكن جدولة محادثة أقدم من تفاصيلها. تعثر المهمة يعيد المحاولة حتى ٣ مرات؛ لا يُعتبر إنشاء المسودة حلًا للمشكلة.</p>
    </> : <p role="status">جاري تحميل الجدولة والطابور…</p>}
  </section>;
}
