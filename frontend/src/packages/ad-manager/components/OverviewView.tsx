import type { AdvertisingOverview } from '../types';
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

  return <>
    <MetricContext value={{ startUtc: overview.windowStartUtc, endUtc: overview.windowEndUtc,
      timezoneIana: overview.reportingTimezone, currency: overview.currency,
      attributionWindow: overview.attributionWindow, truthSource: overview.truthSource }} />
    <ReadinessPanel readiness={overview.readiness} onConfigure={onConfigure} canManage={canManage} />
    <section className={styles.metrics} aria-label="مؤشرات الأداء">
      <article><span>الصرف</span><strong>{money(overview.spend, overview.currency)}</strong><small>من سقف {money(overview.dailyCap, overview.currency)} يوميًا</small></article>
      <article><span>الإيراد المؤكد</span><strong>{money(overview.revenue, overview.currency)}</strong><small>ROAS {overview.roas}×</small></article>
      <article><span>نتائج البيزنس</span><strong>{overview.bookings}</strong><small>{overview.purchases} شراء، {overview.leads} محادثة جديدة، {overview.qualifiedLeads} مؤهل</small></article>
      <article><span>الإعلانات</span><strong>{overview.activeAds}</strong><small>من {overview.totalAds} إعلان</small></article>
    </section>
    <section className={styles.allocation} aria-labelledby="daily-spend-title">
      <div><h2 id="daily-spend-title">سلطة الصرف اليومية</h2><p>السقف القابل للاستخدام بعد هامش الأمان: {money(overview.usableCap, overview.currency)}</p></div>
      <label className={styles.budgetProgressLabel} htmlFor="daily-spend-progress">
        <span>المصروف {money(overview.spend, overview.currency)}</span>
        <span>المتبقي {money(remainingBudget, overview.currency)}</span>
      </label>
      <progress id="daily-spend-progress" className={styles.budgetProgress} max={budgetMaximum} value={budgetProgress}>
        {Math.round((budgetProgress / budgetMaximum) * 100)}%
      </progress>
      {overview.spend > overview.usableCap && <p className={styles.inlineError} role="alert">تجاوز الصرف السقف القابل للاستخدام بمقدار {money(overview.spend - overview.usableCap, overview.currency)}.</p>}
    </section>
  </>;
}
