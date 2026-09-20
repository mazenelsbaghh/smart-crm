import Link from 'next/link';
import { ArrowUpLeft } from 'lucide-react';
import type { DailySalesMetric, FunnelMetric } from './types';
import styles from './reports.module.css';

export function FunnelView({
  funnel,
  daily,
  onSelectDay,
}: {
  funnel: FunnelMetric[];
  daily: DailySalesMetric[];
  onSelectDay: (date: string) => void;
}) {
  const maximum = Math.max(1, funnel[0]?.count ?? 1);
  return (
    <section className={styles.section} aria-labelledby="funnel-title">
      <div className={styles.sectionHeading}>
        <div><span className={styles.eyebrow}>مسار التحويل</span><h2 id="funnel-title">أين يتوقف العملاء؟</h2></div>
        <div className={styles.funnelHeadingActions}>
          <span className={styles.contextNote}>النسب بين كل مرحلة والتي قبلها</span>
          <Link className={styles.openFunnelPage} href="/management/reports/funnel">عرض الأيام بالتفصيل <ArrowUpLeft size={14} aria-hidden="true" /></Link>
        </div>
      </div>
      <div className={styles.funnel}>
        {funnel.map((item) => (
          <div className={styles.funnelRow} key={item.key}>
            <div className={styles.funnelMeta}><span>{item.label}</span><strong>{item.count.toLocaleString('ar-EG')}</strong></div>
            <div className={styles.track} aria-hidden="true"><span style={{ width: `${Math.max(item.count > 0 ? 4 : 0, item.count * 100 / maximum)}%` }} /></div>
            <span className={styles.rate}>{item.rateFromPrevious.toLocaleString('ar-EG')}٪</span>
          </div>
        ))}
      </div>

      {daily.length > 0 && (
        <div className={styles.tableScroll}>
          <p className={styles.dailyBookingNote}>«حجز» حسب يوم دخول الشات؛ «حجز في اليوم» حسب تاريخ تسجيل الحجز.</p>
          <table className={styles.dailyTable}>
            <caption className={styles.srOnly}>نتائج العملاء حسب يوم الدخول والحجوزات المسجلة يوميًا</caption>
            <thead><tr><th>يوم الدخول</th><th>شات</th><th>مؤهل</th><th>نية حجز</th><th>حجز</th><th>حجز في اليوم</th><th>دفع</th><th>حضور</th><th><span className={styles.srOnly}>إجراء</span></th></tr></thead>
            <tbody>{daily.map((day) => (
              <tr key={day.date}><td>{new Date(`${day.date}T12:00:00Z`).toLocaleDateString('ar-EG', { day: 'numeric', month: 'short' })}</td><td>{day.newConversations}</td><td>{day.qualified}</td><td>{day.bookingIntent}</td><td>{day.booked}</td><td>{day.bookedOnDate}</td><td>{day.paid}</td><td>{day.attended}</td><td><button className={styles.dayDrillButton} type="button" onClick={() => onSelectDay(day.date)}>تفاصيل اليوم</button></td></tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </section>
  );
}
