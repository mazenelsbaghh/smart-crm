import { AlertTriangle, CheckCircle2, CircleDollarSign, Route, Sparkles, Target } from 'lucide-react';
import type { AdvertisingStrategy } from '../types';
import styles from '../AdManager.module.css';

export function StrategyView({ strategy }: { strategy: AdvertisingStrategy | null }) {
  if (!strategy) return <section className={styles.empty}><AlertTriangle /><h2>الاستراتيجية لم تُبنَ بعد</h2><p>انشر معلومات العرض والسوق في عقل الشركة أولًا.</p></section>;
  const winner = strategy.rankedOffers[0];
  const plan = strategy.plan;
  return <section className={styles.strategyStack}><div className={styles.detailPanel}><header>{strategy.state === 'READY' ? <CheckCircle2 /> : <AlertTriangle />}<div><h2>{strategy.state === 'READY' ? 'الاستراتيجية جاهزة للتنفيذ الآمن' : 'القرار ينتظر معلومات أو صلاحيات'}</h2><p>كل قرار مربوط بعرض ووجهة واتساب موثّقين، ولا يبدأ إنفاق قبل اجتياز بوابات الأمان.</p></div></header>
    {strategy.state === 'WAIT' && <strong className={styles.waitStatus}>WAIT — لا إجراء مالي</strong>}
    {strategy.blockingReasons.length > 0 && <ul>{strategy.blockingReasons.map(reason => <li key={reason}>{reason}</li>)}</ul>}
  </div>
  {winner && <article className={styles.strategyCard}><header className={styles.detailHeading}><Sparkles size={19} /><div><strong>العرض المختار: {winner.name}</strong><p>درجة الأولوية {winner.score.toLocaleString('ar-EG')} — الأعلى بين {strategy.rankedOffers.length} عرض مؤهل.</p></div></header>
    <dl className={styles.factGrid}>
      <div><dt>النتيجة التي يحاسب عليها</dt><dd>{label(winner.primaryOutcome)}</dd></div><div><dt>نافذة القياس</dt><dd>{winner.attributionWindowDays} أيام من نقرة واتساب</dd></div>
      <div><dt>هامش المساهمة</dt><dd>{money(winner.contributionMargin, winner.currency)}</dd></div><div><dt>أقصى تكلفة مستدامة</dt><dd>{money(winner.maximumSustainableCost, winner.currency)}</dd></div>
      <div><dt>الطاقة اليومية المتاحة</dt><dd>{winner.currentCapacity ?? 'غير محددة — يراقبها النظام'}</dd></div><div><dt>سبب الاختيار</dt><dd>وجهة مصرح بها + دليل موثّق + طاقة متاحة</dd></div>
    </dl>
  </article>}
  {plan && <article className={styles.strategyCard}><header className={styles.detailHeading}><Route size={19} /><div><strong>خطة الحملة: {plan.name}</strong><p>الحالة {label(plan.state)} · الإنشاء عند Meta يتم متوقفًا أولًا للفحص.</p></div></header>
    <div className={styles.strategyFlow}><span><Target size={16} />{label(plan.businessGoal)}</span><b>←</b><span>واتساب فقط</span><b>←</b><span><CircleDollarSign size={16} />{label(plan.optimizationGoal)}</span></div>
    <dl className={styles.factGrid}>
      <div><dt>هدف Meta</dt><dd>{label(plan.objective)}</dd></div><div><dt>التحسين</dt><dd>{label(plan.optimizationGoal)}</dd></div>
      <div><dt>المزايدة</dt><dd>{label(plan.bidStrategy)}</dd></div><div><dt>الميزانية اليومية القصوى</dt><dd>{money(plan.dailyBudget, plan.currency)}</dd></div>
      <div><dt>توزيع الميزانية</dt><dd>{label(plan.budgetMode)}</dd></div><div><dt>المواضع</dt><dd>{label(plan.placementMode)}</dd></div>
    </dl>
  </article>}
  {!!strategy.providerSteps?.length && <article className={styles.strategyCard}><header className={styles.detailHeading}><CheckCircle2 size={19} /><div><strong>تنفيذ الخطة عند Meta</strong><p>تفصيل كل خطوة بدل إخفائها خلف حالة عامة.</p></div></header>
    <ol className={styles.executionSteps}>{strategy.providerSteps.map((step, index) => <li key={`${step.operationType}-${index}`} className={step.state === 'Succeeded' ? styles.stepSucceeded : step.state === 'Failed' ? styles.stepFailed : undefined}>
      <span>{step.state === 'Succeeded' ? '✓' : step.state === 'Failed' ? '!' : '…'}</span><div><strong>{operationLabel(step.operationType)}</strong><small>{step.state === 'Succeeded' ? 'تم الإنشاء متوقفًا وتم حفظ معرّف Meta' : step.state === 'Failed' ? `رفضت Meta الخطوة — ${step.errorCode ?? 'خطأ غير مصنف'}` : label(step.state)}</small></div>
    </li>)}</ol>
  </article>}
  </section>;
}

const labels: Record<string, string> = { QualifiedLead: 'عميل واتساب مؤهل', EnrollmentPaid: 'بيع/اشتراك مدفوع', OUTCOME_ENGAGEMENT: 'محادثات واتساب', MESSAGING_CONVERSATIONS: 'بدء محادثة واتساب', LOWEST_COST_WITHOUT_CAP: 'أقل تكلفة لأكبر حجم', Campaign: 'ميزانية على مستوى الحملة', DynamicEligibleMeta: 'Advantage+ تلقائي حسب الأهلية', Ready: 'جاهزة', READY: 'جاهزة' };
const label = (value?: string) => value ? (labels[value] ?? value.replaceAll('_', ' ')) : 'غير محدد';
const money = (value?: number, currency = 'EGP') => value == null ? 'غير محدد' : new Intl.NumberFormat('ar-EG', { style: 'currency', currency, maximumFractionDigits: 0 }).format(value);
const operationLabel = (value: string) => ({ CreateCampaign: 'إنشاء الحملة', ValidateAdSet: 'فحص Ad Set', CreateAdSet: 'إنشاء Ad Set', CreateCreative: 'إنشاء الكرياتيف', CreateAd: 'إنشاء الإعلان النهائي' })[value] ?? value;
