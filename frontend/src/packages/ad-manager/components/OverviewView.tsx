import type { AdvertisingOverview } from '../types';
import { CalendarCheck, CircleDollarSign, Megaphone, TrendingUp } from 'lucide-react';
import { MetricContext } from './MetricContext';
import { ReadinessPanel } from './ReadinessPanel';
import styles from '../AdManager.module.css';

const money = (value: number, currency: string) => currency
  ? new Intl.NumberFormat('ar-EG', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value)
  : `${new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 2 }).format(value)} (العملة غير متاحة)`;

export function OverviewView({ overview, onConfigure, canManage = true }: { overview: AdvertisingOverview; onConfigure: () => void; canManage?: boolean }) {
  const budgetMaximum = Math.max(overview.usableCap, overview.spend, 1);
  const budgetProgress = Math.min(overview.spend, budgetMaximum);
  const remainingBudget = Math.max(overview.usableCap - overview.spend, 0);
  const spendAvailable = overview.spendAvailable !== false;

  return <>
    <MetricContext value={{ startUtc: overview.windowStartUtc, endUtc: overview.windowEndUtc,
      timezoneIana: overview.reportingTimezone, currency: overview.currency,
      attributionWindow: overview.attributionWindow, truthSource: overview.truthSource }} />
    <section className={styles.metrics} aria-label="مؤشرات الأداء">
      <article><span><CircleDollarSign size={17} aria-hidden="true" />الصرف</span><strong>{spendAvailable ? money(overview.spend, overview.currency) : 'غير متاح'}</strong><small>من سقف {money(overview.dailyCap, overview.currency)} يوميًا</small></article>
      <article><span><TrendingUp size={17} aria-hidden="true" />الإيراد المؤكد للمشروع</span><strong>{money(overview.revenue, overview.currency)}</strong><small>ROAS المنسوب للإعلانات {overview.roas == null ? 'غير متاح' : `${overview.roas}×`}</small></article>
      <article><span><CalendarCheck size={17} aria-hidden="true" />الحجوزات</span><strong>{overview.bookings}</strong><small>{overview.purchases} شراء، {overview.leads} عميل محتمل، {overview.qualifiedLeads} مؤهل</small></article>
      <article><span><Megaphone size={17} aria-hidden="true" />الإعلانات النشطة</span><strong>{overview.activeAds}</strong><small>من {overview.totalAds} إعلان</small></article>
    </section>
    {!spendAvailable && <p className={styles.inlineError} role="status">لم تتوفر بيانات صرف متجانسة لهذه الفترة؛ لا نعرضها كصفر أو نحسب منها ميزانية متبقية.</p>}
    {(overview.excludedRevenueCurrencies ?? 0) > 0 && <p className={styles.inlineError} role="status">الإيراد جزئي: {overview.excludedRevenueCurrencies} نتائج بعملة أخرى أو بلا عملة موثقة مستبعدة. لم نستخدم سعر تحويل مفترض.</p>}
    <ReadinessPanel readiness={overview.readiness} onConfigure={onConfigure} canManage={canManage} />
    {spendAvailable && <section className={styles.allocation} aria-labelledby="daily-spend-title">
      <div><h2 id="daily-spend-title"><CircleDollarSign size={19} aria-hidden="true" />حدود الصرف اليومية</h2><p>السقف القابل للاستخدام بعد هامش الأمان: {money(overview.usableCap, overview.currency)}</p></div>
      <label className={styles.budgetProgressLabel} htmlFor="daily-spend-progress">
        <span>المصروف {money(overview.spend, overview.currency)}</span>
        <span>المتبقي {money(remainingBudget, overview.currency)}</span>
      </label>
      <progress id="daily-spend-progress" className={styles.budgetProgress} max={budgetMaximum} value={budgetProgress}>
        {Math.round((budgetProgress / budgetMaximum) * 100)}%
      </progress>
      {overview.spend > overview.usableCap && <p className={styles.inlineError} role="alert">تجاوز الصرف السقف القابل للاستخدام بمقدار {money(overview.spend - overview.usableCap, overview.currency)}.</p>}
    </section>}
  </>;
}
