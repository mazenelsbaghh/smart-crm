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
    setDate(nextDate); setLoading(true); setError(null);
    try { setReport(await adManagerApi.dailyReport(projectId, nextDate)); }
    catch { setError('تعذّر تحميل تقرير هذا اليوم. احتفظنا بآخر تقرير متاح؛ حاول مرة أخرى.'); }
    finally { setLoading(false); }
  };
  if (!report) return <section className={styles.empty}><CalendarDays /><h2>التقرير اليومي غير متاح الآن</h2><p>سيظهر بعد تحميل بيانات واتساب والنتائج التجارية.</p></section>;
  return <section className={styles.dailyReport} aria-busy={loading}>
    <header className={styles.dailyReportHeader}><div><p className={styles.sectionEyebrow}>تقرير الإسناد اليومي</p><h2>تقرير اليوم حسب الإعلان</h2><p>النافذة الدقيقة: {dateTime(report.startUtc, report.timezone)} — {dateTime(report.endUtc, report.timezone)} · {report.timezone}</p></div><label><span>اليوم المختار</span><strong>{arabicDate(date)}</strong><input type="date" value={date} onChange={event => void load(event.target.value)} /></label></header>
    {error && <div className={styles.inlineError} role="alert">{error} <button type="button" className={styles.textButton} onClick={() => void load(date)}>إعادة المحاولة</button></div>}
    <div className={styles.dailyTotals}><article><MessageCircleMore aria-hidden="true" /><span>دخل واتساب</span><strong>{number(report.totals.entrants)}</strong></article><article><UserCheck aria-hidden="true" /><span>عملاء مؤهلون</span><strong>{number(report.totals.qualified)}</strong></article><article><CalendarCheck2 aria-hidden="true" /><span>حجوزات مؤكدة</span><strong>{number(report.totals.bookings)}</strong></article><article><span>الصرف</span><strong>{money(report.totals.spend, report.currency)}</strong></article></div>
    <div className={styles.reportTableWrap}><table className={styles.reportTable}><caption>نتائج الإعلانات ليوم {arabicDate(report.date)} بتوقيت {report.timezone}</caption><thead><tr><th scope="col">الإعلان والمصدر</th><th scope="col">دخل واتساب</th><th scope="col">مؤهل</th><th scope="col">حجز</th><th scope="col">نسبة التأهيل</th><th scope="col">نسبة الحجز</th><th scope="col">تكلفة المؤهل</th><th scope="col">الصرف</th></tr></thead><tbody>
      {report.rows.map(row => <tr key={row.id}><td data-label="الإعلان"><strong>{row.name}</strong><small>{row.source ? `${row.source.mediaType} · ${row.source.sourceType}` : 'مصدر غير مسجل'}{row.adExternalId ? ` · ${row.adExternalId}` : ''}</small></td><td data-label="دخل واتساب">{number(row.entrants)}</td><td data-label="مؤهل">{number(row.qualified)}</td><td data-label="حجز">{number(row.bookings)}</td><td data-label="نسبة التأهيل">{percent(row.qualificationRate)}</td><td data-label="نسبة الحجز">{percent(row.bookingRate)}</td><td data-label="تكلفة المؤهل">{money(row.costPerQualified, report.currency)}</td><td data-label="الصرف">{money(row.spend, report.currency)}</td></tr>)}
      {!report.rows.length && <tr><td colSpan={8}>لا توجد زيارات منسوبة لإعلان في هذا اليوم.</td></tr>}
    </tbody></table></div>
    {(report.unattributed.entrants > 0 || report.unattributed.qualified > 0 || report.unattributed.bookings > 0) && <div className={styles.unattributed}><AlertTriangle /><div><strong>نتائج تحتاج استكمال الربط</strong><span>{number(report.unattributed.entrants)} محادثة بلا إعلان · {number(report.unattributed.qualified)} تأهيل و{number(report.unattributed.bookings)} حجز بلا محادثة موثقة. لا تدخل هذه النتائج في مسار التحويل حتى لا تعطيك أرقامًا مضللة.</span></div></div>}
  </section>;
}
