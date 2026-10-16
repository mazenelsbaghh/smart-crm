'use client';

import { useEffect, useId, useState } from 'react';
import { CalendarCheck2, RefreshCw, Users } from 'lucide-react';
import Button from '../../components/ui/Button';
import { api } from '../../services/api';
import styles from './GroupBookingStats.module.css';

interface BookingStats {
  totalBooked: number;
  bookedToday: number;
  timezone: string;
  dayStartUtc: string;
  dayEndUtc: string;
  asOfUtc: string;
  courseType: 'regular' | 'intensive';
}

interface GroupBookingStatsProps {
  projectId: string;
  courseType: BookingStats['courseType'];
  refreshKey: number;
}

interface LoadedStats extends GroupBookingStatsProps {
  retry: number;
  stats: BookingStats | null;
  error: boolean;
}

const countFormat = new Intl.NumberFormat('ar-EG');
const timeFormat = new Intl.DateTimeFormat('ar-EG', {
  timeZone: 'Africa/Cairo', dateStyle: 'short', timeStyle: 'short',
});

export default function GroupBookingStats({ projectId, courseType, refreshKey }: GroupBookingStatsProps) {
  const [loaded, setLoaded] = useState<LoadedStats | null>(null);
  const [retry, setRetry] = useState(0);
  const labelId = useId();
  const current = loaded?.projectId === projectId && loaded.courseType === courseType ? loaded : null;
  const stats = current?.stats ?? null;
  const loading = !current || current.refreshKey !== refreshKey || current.retry !== retry;
  const error = !loading && current?.error === true;

  useEffect(() => {
    const controller = new AbortController();
    api.get<BookingStats>('/api/group-appointments/booking-stats', {
      params: { courseType }, signal: controller.signal,
    }).then(({ data }) => {
      if (controller.signal.aborted) return;
      if (data.courseType !== courseType || !Number.isSafeInteger(data.totalBooked) || data.totalBooked < 0
        || !Number.isSafeInteger(data.bookedToday) || data.bookedToday < 0 || data.bookedToday > data.totalBooked
        || !Number.isFinite(Date.parse(data.asOfUtc))) throw new Error('Invalid booking statistics');
      setLoaded({ projectId, courseType, refreshKey, retry, stats: data, error: false });
    }).catch(() => {
      if (controller.signal.aborted) return;
      setLoaded(previous => ({
        projectId, courseType, refreshKey, retry, error: true,
        stats: previous?.projectId === projectId && previous.courseType === courseType ? previous.stats : null,
      }));
    });
    return () => controller.abort();
  }, [projectId, courseType, refreshKey, retry]);

  const metrics = [
    { key: 'today', label: 'حجوزات سُجلت اليوم', count: stats?.bookedToday, hint: 'يشمل الحجز الجديد والنقل بين المجموعات', Icon: CalendarCheck2 },
    { key: 'total', label: 'إجمالي الحجوزات الحالية', count: stats?.totalBooked, hint: 'حسب نوع الكورس المختار', Icon: Users },
  ];

  return <section className={styles.stats} aria-label="إحصائيات الحجوزات">
    <div className={styles.cards} aria-busy={loading}>
      {metrics.map(({ key, label, count, hint, Icon }) => <div key={key} className={styles.card}
        role="group" aria-labelledby={`${labelId}-${key}`} data-metric={key}>
        <dl>
        <dt id={`${labelId}-${key}`}><Icon size={18} aria-hidden="true" />{label}</dt>
        <dd className={styles.value}>{count !== undefined ? countFormat.format(count) : loading
          ? <><span className={styles.skeleton} aria-hidden="true" /><span className={styles.srOnly}>جاري التحميل</span></>
          : <span className={styles.unavailable}>غير متاح</span>}</dd>
        <dd className={styles.hint}>{hint}</dd>
        </dl>
      </div>)}
    </div>
    <div className={styles.feedback}>
      <p role={error ? 'alert' : 'status'} className={error ? styles.error : undefined}>
        {error ? stats ? 'تعذر تحديث الإحصائيات. المعروض آخر بيانات متاحة.' : 'تعذر تحميل إحصائيات الحجوزات.'
          : loading ? stats ? 'جاري التحديث. الأرقام المعروضة من آخر تحميل.' : 'جاري تحميل إحصائيات الحجوزات.'
            : 'اليوم بتوقيت القاهرة. تشمل المدفوع وغير المدفوع والمجموعات غير النشطة، وتستبعد الحجوزات المحذوفة.'}
        {stats && <> <time dateTime={stats.asOfUtc}>آخر تحميل: {timeFormat.format(new Date(stats.asOfUtc))} بتوقيت القاهرة</time></>}
      </p>
      {error && <Button variant="ghost" onClick={() => setRetry(value => value + 1)}>
        <RefreshCw size={15} aria-hidden="true" />إعادة المحاولة
      </Button>}
    </div>
  </section>;
}
