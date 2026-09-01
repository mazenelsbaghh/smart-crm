import { CirclePause, FlaskConical, Scale, TrendingUp, Trophy } from 'lucide-react';
import type { AdvertisingExperiment, ManagedAd } from '../types';
import { StructuredJsonSummary, structuredJsonText } from './StructuredJsonSummary';
import styles from '../AdManager.module.css';

export function ExperimentsView({ rows, ads }: { rows: AdvertisingExperiment[]; ads: ManagedAd[] }) {
  const controlActive = ads.some(ad => ad.status === 'Active' || ad.effectiveStatus === 'ACTIVE');
  if (!rows.length) return <section className={styles.testJourney}><header><FlaskConical aria-hidden="true" /><div><h2>لا يوجد اختبار مسجّل حاليًا</h2><p>{controlActive ? 'توجد حملة نشطة، لكن لم يُنشأ تعريف اختبار أو ذراع مقارنة بعد. لن نفترض أن المقارنة قيد التجهيز.' : 'لا توجد حملة نشطة أو تجربة مسجلة؛ لن يبدأ الاختبار أو الصرف قبل اجتياز بوابات الأمان.'}</p></div></header>
    <div className={styles.testFlow}>
      <article><span>1</span><strong>محتوى ضابط</strong><small>{controlActive ? 'حملة نشطة؛ لم تربط بتجربة بعد' : 'لم يُسجّل'}</small></article><article><span>2</span><strong>محتوى بديل</strong><small>لم يُنشأ</small></article>
      <article><span>3</span><strong>جمع الدليل</strong><small>72 ساعة + 10 نتائج مؤهلة، وليس مجرد رسائل</small></article><article><span>4</span><strong>قرار تلقائي</strong><small>فائز، إيقاف الاثنين، أو WAIT</small></article>
    </div>
    <div className={styles.decisionRules}>
      <div><Trophy /><strong>فائز واضح</strong><span>تكلفة النتيجة أقل 15% على الأقل: يوقف الخاسر ويزيد الفائز تدريجيًا داخل السقف.</span></div>
      <div><CirclePause /><strong>التكلفة مرتفعة</strong><span>لو الفيديوهان أعلى من التكلفة المستدامة: يوقف الاثنين ولا يختار فائزًا وهميًا.</span></div>
      <div><Scale /><strong>الفرق غير كافٍ</strong><span>يستمر WAIT أو يبدأ اختبار فيديو جديد؛ لا يضاعف الميزانية بدون دليل.</span></div>
    </div>
  </section>;
  return <section className={styles.detailList} aria-label="اختبارات الإعلانات">{rows.map(row => <article key={row.id}>
    <header className={styles.detailHeading}><FlaskConical size={19} /><div><strong>{row.name} · {row.state}</strong><p>{row.hypothesis}</p></div></header>
    <dl className={styles.factGrid}>
      <div><dt>المتغير الوحيد</dt><dd>{row.primaryVariable}</dd></div><div><dt>النتيجة التجارية</dt><dd>{row.businessOutcome}</dd></div>
      <div><dt>نافذة الإسناد</dt><dd>{row.attributionWindowDays} أيام</dd></div><div><dt>سقف الاختبار</dt><dd>{row.budgetCap.toLocaleString('ar-EG')}</dd></div>
      <div><dt>شرط النضج</dt><dd>{row.minimumElapsedHours} ساعة، صرف {row.minimumSpend.toLocaleString('ar-EG')}، {row.minimumAttributedOutcomes} نتائج</dd></div>
      <div><dt>جودة الإسناد المطلوبة</dt><dd>{(row.minimumAttributionCoverage * 100).toLocaleString('ar-EG')}% مع انتظار تصحيحات {row.correctionLagHours} ساعة</dd></div>
      <div><dt>قاعدة الإيقاف</dt><dd><StructuredJsonSummary raw={row.stopRuleJson} emptyLabel="لم تُحفظ قاعدة إيقاف." /></dd></div><div><dt>الخلاصة</dt><dd><StructuredJsonSummary raw={row.conclusionJson} emptyLabel="لم تصدر خلاصة بعد؛ الحالة WAIT." /></dd></div>
    </dl>
    <div className={styles.experimentDecision}><TrendingUp size={18} /><div><strong>{decisionTitle(row)}</strong><span>{decisionDescription(row)}</span></div></div>
    <div className={styles.armList}>{row.arms.map(arm => <div key={arm.id}><strong>{arm.isControl ? 'Control' : 'Variant'} · {arm.name}</strong><span>{arm.state} · ميزانية {arm.allocatedBudget.toLocaleString('ar-EG')}</span><small>{structuredJsonText(arm.changedValueJson, 'بدون تغيير')}</small></div>)}</div>
  </article>)}</section>;
}

const decisionTitle = (row: AdvertisingExperiment) => row.state === 'Completed' ? 'صدر قرار الاختبار' : row.state === 'Running' ? 'نجمع دليلًا الآن' : row.state === 'NeedsAttention' ? 'الاختبار متوقف للحماية' : 'الاختبار لم يبدأ الصرف';
const decisionDescription = (row: AdvertisingExperiment) => row.state === 'Completed' ? structuredJsonText(row.conclusionJson, 'راجع نتيجة الفائز') : row.state === 'Running' ? 'لن نعلن فائزًا قبل الوقت والنتائج والتغطية المطلوبة.' : row.state === 'NeedsAttention' ? 'لن يتم تشغيل أو زيادة أي ذراع قبل معالجة السبب.' : 'الذراعان يظلان متوقفين حتى اجتياز مراجعة Meta وبوابات الأمان.';
