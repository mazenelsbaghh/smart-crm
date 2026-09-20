'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, CalendarDays, Trophy } from 'lucide-react';
import { useAuth } from '../../../context/auth-context';
import { reportsApi, type ReportWindow } from './reports-api';
import { reportDateWindow } from './report-window';
import type { SalesIntelligenceDashboard } from './types';
import styles from './reports.module.css';

type DailyKey = 'newConversations' | 'qualified' | 'bookingIntent' | 'booked' | 'bookedOnDate' | 'paid' | 'attended';
type StageLeader = { key: DailyKey; label: string; maximum: number; dates: string[] };

const stages: { key: DailyKey; label: string }[] = [
  { key: 'newConversations', label: 'شات جديد' },
  { key: 'qualified', label: 'عميل مؤهل' },
  { key: 'bookingIntent', label: 'نية حجز' },
  { key: 'booked', label: 'حجز' },
  { key: 'bookedOnDate', label: 'حجز في اليوم' },
  { key: 'paid', label: 'دفع' },
  { key: 'attended', label: 'حضور' },
];

const inputDate = (date: Date) => {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const formatDay = (date: string) => new Date(`${date}T12:00:00Z`).toLocaleDateString('ar-EG', {
  weekday: 'short', day: 'numeric', month: 'short',
});

const stageLeaders = (daily: SalesIntelligenceDashboard['daily']): StageLeader[] => stages.map((stage) => {
  const maximum = Math.max(0, ...daily.map((day) => day[stage.key]));
  const dates = maximum > 0 ? daily.filter((day) => day[stage.key] === maximum).map((day) => day.date) : [];
  return { ...stage, maximum, dates };
});

function BestDays({ leaders, dayCount }: { leaders: StageLeader[]; dayCount: number }) {
  return <section aria-labelledby="best-days-title">
    <div className={styles.analyticsHeading}>
      <div><span className={styles.eyebrow}>ملخص الفترة</span><h2 id="best-days-title">أعلى يوم لكل مؤشر</h2></div>
      <span>{dayCount.toLocaleString('ar-EG')} أيام معروضة</span>
    </div>
    <div className={styles.bestDaysGrid}>{leaders.map((leader) => (
      <article className={styles.bestDay} key={leader.key}>
        <div><Trophy size={15} aria-hidden="true" /><span>{leader.label}</span></div>
        <strong>{leader.maximum.toLocaleString('ar-EG')}</strong>
        <span>{leader.dates.length > 0 ? leader.dates.map(formatDay).join('، ') : 'لا توجد بيانات'}</span>
      </article>
    ))}</div>
  </section>;
}

function DailyBreakdown({ dashboard, leaders }: { dashboard: SalesIntelligenceDashboard; leaders: StageLeader[] }) {
  return <section className={styles.dailyBreakdown} aria-labelledby="daily-breakdown-title">
    <div className={styles.analyticsHeading}>
      <div><span className={styles.eyebrow}>التفاصيل اليومية</span><h2 id="daily-breakdown-title">كل الأيام من البداية للنهاية</h2></div>
      <span>الأرقام المميزة هي الأعلى في العمود</span>
    </div>
    <p className={styles.dailyBookingNote}>«حجز» حسب يوم دخول الشات؛ «حجز في اليوم» حسب تاريخ تسجيل الحجز.</p>
    {dashboard.daily.length === 0 ? <div className={styles.emptyInline}><p>لا توجد نتائج في هذه الفترة</p><span>جرّب توسيع تاريخ البداية والنهاية.</span></div> : (
      <div className={styles.tableScroll}><table className={`${styles.dailyTable} ${styles.expandedDailyTable}`}>
        <thead><tr><th>اليوم</th>{stages.map((stage) => <th key={stage.key}>{stage.label}</th>)}</tr></thead>
        <tbody>{dashboard.daily.map((day) => <tr key={day.date}>
          <th scope="row">{formatDay(day.date)}</th>
          {stages.map((stage) => {
            const isLeader = day[stage.key] > 0 && leaders.find(({ key }) => key === stage.key)?.dates.includes(day.date);
            return <td className={isLeader ? styles.leadingValue : undefined} key={stage.key}>{day[stage.key].toLocaleString('ar-EG')}{isLeader && <span className={styles.srOnly}>، الأعلى في الفترة</span>}</td>;
          })}
        </tr>)}</tbody>
      </table></div>
    )}
  </section>;
}

export default function FunnelAnalyticsPage() {
  const { activeProject } = useAuth();
  const [fromDate, setFromDate] = useState(() => inputDate(new Date(Date.now() - 6 * 86_400_000)));
  const [toDate, setToDate] = useState(() => inputDate(new Date()));
  const [windowRange, setWindowRange] = useState<ReportWindow>(() => reportDateWindow(fromDate, toDate)!);
  const [dashboard, setDashboard] = useState<SalesIntelligenceDashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    if (!activeProject) {
      setError('تعذر تحديد المشروع النشط.');
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      setDashboard(await reportsApi.dashboard(activeProject.id, windowRange));
    } catch {
      setError('تعذر تحميل تحليل الأيام. تحقق من اتصال الخادم ثم حاول مرة أخرى.');
    } finally {
      setLoading(false);
    }
  }, [activeProject, windowRange]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const leaders = useMemo(() => stageLeaders(dashboard?.daily ?? []), [dashboard]);

  const applyRange = () => {
    const next = reportDateWindow(fromDate, toDate);
    if (!next) {
      setError('اختر تاريخ بداية ونهاية صحيحين؛ تاريخ النهاية لا يسبق البداية.');
      return;
    }
    setWindowRange(next);
  };

  return (
    <main className={`${styles.page} ${styles.funnelAnalyticsPage}`}>
      <header className={styles.funnelPageHeader}>
        <div>
          <Link className={styles.backLink} href="/management/reports"><ArrowRight size={15} aria-hidden="true" /> مدير المبيعات</Link>
          <h1>الأيام الأعلى في مسار التحويل</h1>
          <p>قارن نتائج العملاء حسب يوم دخول الشات، وشوف عدد الحجوزات المسجلة في كل يوم.</p>
        </div>
      </header>

      <div className={styles.customRangeBar}>
        <div className={styles.customRangeTitle}><CalendarDays size={17} aria-hidden="true" /><span>حدد الفترة التي تريد فهمها</span></div>
        <label>من<input aria-label="تاريخ بداية الفترة" type="date" value={fromDate} max={toDate} onChange={(event) => setFromDate(event.target.value)} /></label>
        <label>إلى<input aria-label="تاريخ نهاية الفترة" type="date" value={toDate} min={fromDate} onChange={(event) => setToDate(event.target.value)} /></label>
        <button type="button" disabled={loading} onClick={applyRange}>{loading ? 'جاري العرض…' : 'عرض الفترة'}</button>
      </div>

      {error && <div className={styles.errorBanner} role="alert">{error}</div>}
      {loading && !dashboard ? <div className={styles.funnelPageLoading} role="status">جاري تحميل الأيام…</div> : dashboard && (
        <>
          <BestDays leaders={leaders} dayCount={dashboard.daily.length} />
          <DailyBreakdown dashboard={dashboard} leaders={leaders} />
        </>
      )}
    </main>
  );
}
