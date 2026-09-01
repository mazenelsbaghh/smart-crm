import { Clock3, MessageSquareText, Sparkles, TicketCheck } from 'lucide-react';
import type { SalesIntelligenceDashboard } from './types';
import styles from './reports.module.css';

export function MetricStrip({ dashboard }: { dashboard: SalesIntelligenceDashboard }) {
  const metrics = [
    { label: 'الشاتات الجديدة', value: dashboard.totalConversations.toLocaleString('ar-EG'), hint: `${dashboard.uniqueCustomers} عميل فريد`, icon: MessageSquareText },
    { label: 'تحويل إلى حجز', value: `${dashboard.bookingConversionRate.toLocaleString('ar-EG')}٪`, hint: 'خلال 30 يومًا من بداية الشات', icon: TicketCheck },
    { label: 'تحويل إلى دفع', value: `${dashboard.paymentConversionRate.toLocaleString('ar-EG')}٪`, hint: 'من مجموعة دخول العملاء', icon: Sparkles },
    { label: 'وسيط أول رد', value: `${dashboard.medianFirstResponseMinutes.toLocaleString('ar-EG')} د`, hint: 'محسوب من الرسائل الفعلية', icon: Clock3 },
  ];

  return (
    <section className={styles.metricStrip} aria-label="المؤشرات الأساسية">
      {metrics.map(({ label, value, hint, icon: Icon }) => (
        <div className={styles.metric} key={label}>
          <div className={styles.metricLabel}><Icon size={16} aria-hidden="true" />{label}</div>
          <strong>{value}</strong>
          <span>{hint}</span>
        </div>
      ))}
    </section>
  );
}
