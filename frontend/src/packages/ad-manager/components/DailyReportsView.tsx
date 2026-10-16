import { useEffect, useRef, useState } from 'react';
import { CalendarDays, MessageCircleMore, UserCheck, CalendarCheck2, AlertTriangle } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { DailyAdvertisingReport } from '../types';
import styles from '../AdManager.module.css';

const number = (value: number) => value.toLocaleString('ar-EG', { maximumFractionDigits: 2 });
const percent = (value?: number) => value == null ? '—' : `${(value * 100).toLocaleString('ar-EG', { maximumFractionDigits: 1 })}%`;
const money = (value: number | undefined, currency: string) => value == null ? '—' : new Intl.NumberFormat('ar-EG', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value);
const arabicDate = (value: string) => value
  ? new Intl.DateTimeFormat('ar-EG', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`))
  : 'اختر اليوم';
const dateTime = (value: string, timezone: string) => new Intl.DateTimeFormat('ar-EG', {
  dateStyle: 'medium', timeStyle: 'short', timeZone: timezone,
}).format(new Date(value));

export function DailyReportsView({ projectId, initial }: { projectId: string; initial: DailyAdvertisingReport | null }) {
  const [report, setReport] = useState(initial);
  const [date, setDate] = useState(initial?.date ?? '');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const previousInitialRef = useRef(initial);
  const activeRequest = useRef<AbortController | null>(null);

  useEffect(() => () => activeRequest.current?.abort(), [projectId]);

  useEffect(() => {
    const previousInitial = previousInitialRef.current;
    previousInitialRef.current = initial;
    const timer = window.setTimeout(() => {
      setReport((current) => current?.date === previousInitial?.date ? initial : current);
      setDate((current) => current === (previousInitial?.date ?? '') ? (initial?.date ?? '') : current);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [initial, projectId]);

  const load = async (nextDate: string) => {
    if (!nextDate) return;
    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setDate(nextDate); setLoading(true); setError(null);
    try {
      const nextReport = await adManagerApi.dailyReport(projectId, nextDate, controller.signal);
      if (!controller.signal.aborted) setReport(nextReport);
    }
    catch {
      if (!controller.signal.aborted) setError('تعذّر تحميل تقرير هذا اليوم. احتفظنا بآخر تقرير متاح؛ حاول مرة أخرى.');
    }
    finally { if (!controller.signal.aborted) setLoading(false); }
  };
  if (!report) return <section className={styles.empty}><CalendarDays /><h2>التقرير اليومي غير متاح الآن</h2><p>سيظهر بعد تحميل بيانات واتساب والنتائج التجارية.</p></section>;
  const spendAvailable = report.spendAvailable !== false;
  return <section className={styles.dailyReport} aria-busy={loading}>
    <header className={styles.dailyReportHeader}><div><p className={styles.sectionEyebrow}>تقرير الإسناد اليومي</p><h2>تقرير اليوم حسب الإعلان</h2><p>النافذة الدقيقة: {dateTime(report.startUtc, report.timezone)} — {dateTime(report.endUtc, report.timezone)} · {report.timezone}</p></div><label><span>اليوم المختار</span><strong>{arabicDate(date)}</strong><input type="date" value={date} onChange={event => void load(event.target.value)} /></label></header>
    {error && <div className={styles.inlineError} role="alert">{error} <button type="button" className={styles.textButton} onClick={() => void load(date)}>إعادة المحاولة</button></div>}
    {!spendAvailable && <p className={styles.inlineError} role="status">بيانات الصرف لهذا اليوم ناقصة أو تضم عملة مختلفة؛ لم نحسب تكلفة النتيجة عند غياب بيانات الإعلان.</p>}
    <div className={styles.dailyTotals}><article><MessageCircleMore aria-hidden="true" /><span>دخل واتساب</span><strong>{number(report.totals.entrants)}</strong></article><article><UserCheck aria-hidden="true" /><span>عملاء مؤهلون</span><strong>{number(report.totals.qualified)}</strong></article><article><CalendarCheck2 aria-hidden="true" /><span>حجوزات مؤكدة</span><strong>{number(report.totals.bookings)}</strong></article><article><span>الصرف</span><strong>{spendAvailable ? money(report.totals.spend, report.currency) : 'غير متاح'}</strong></article></div>
    <div className={styles.reportTableWrap} tabIndex={0} role="region" aria-label="جدول نتائج الإعلانات اليومية، قابل للتمرير"><table className={styles.reportTable}><caption>نتائج الإعلانات ليوم {arabicDate(report.date)} بتوقيت {report.timezone}</caption><thead><tr><th scope="col">الإعلان والمصدر</th><th scope="col">دخل واتساب</th><th scope="col">مؤهل</th><th scope="col">حجز</th><th scope="col">نسبة التأهيل</th><th scope="col">نسبة الحجز</th><th scope="col">تكلفة المؤهل</th><th scope="col">الصرف</th></tr></thead><tbody>
      {report.rows.map(row => <tr key={row.id}><td data-label="الإعلان"><strong>{row.name}</strong><small>{row.source ? `${row.source.mediaType} · ${row.source.sourceType}` : 'مصدر غير مسجل'}{row.adExternalId ? ` · ${row.adExternalId}` : ''}</small></td><td data-label="دخل واتساب">{number(row.entrants)}</td><td data-label="مؤهل">{number(row.qualified)}</td><td data-label="حجز">{number(row.bookings)}</td><td data-label="نسبة التأهيل">{percent(row.qualificationRate)}</td><td data-label="نسبة الحجز">{percent(row.bookingRate)}</td><td data-label="تكلفة المؤهل">{money(row.costPerQualified, report.currency)}</td><td data-label="الصرف">{(row.spendAvailable ?? spendAvailable) ? money(row.spend, report.currency) : 'غير متاح'}</td></tr>)}
      {!report.rows.length && <tr><td colSpan={8}>لا توجد زيارات منسوبة لإعلان في هذا اليوم.</td></tr>}
    </tbody></table></div>
    {(report.unattributed.entrants > 0 || report.unattributed.qualified > 0 || report.unattributed.bookings > 0) && <div className={styles.unattributed}><AlertTriangle /><div><strong>نتائج تحتاج استكمال الربط</strong><span>{number(report.unattributed.entrants)} محادثة بلا إعلان · {number(report.unattributed.qualified)} مؤهل و{number(report.unattributed.bookings)} حجز من هذه المحادثات. تظهر ضمن إجمالي اليوم منفصلة عن صفوف الإعلانات.</span></div></div>}
    {report.unlinkedOutcomes && (report.unlinkedOutcomes.qualified > 0 || report.unlinkedOutcomes.bookings > 0) && <p className={styles.unattributed}>{number(report.unlinkedOutcomes.qualified)} مؤهل و{number(report.unlinkedOutcomes.bookings)} حجز بلا محادثة واردة موثقة في هذا اليوم؛ مستبعدة من مسار التحويل اليومي.</p>}
  </section>;
}
