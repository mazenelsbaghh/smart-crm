import { ArrowLeft, UserRoundCheck } from 'lucide-react';
import type { FunnelTransitionMetric } from './types';
import styles from './reports.module.css';

export function FunnelTransitions({ transitions }: { transitions: FunnelTransitionMetric[] }) {
  return (
    <section className={`${styles.section} ${styles.transitionSection}`} aria-labelledby="transition-analysis-title">
      <div className={styles.sectionHeading}>
        <div>
          <span className={styles.eyebrow}>تحليل كل خطوة</span>
          <h2 id="transition-analysis-title">لماذا يتوقف العملاء بين المراحل؟</h2>
        </div>
        <span className={styles.contextNote}>الأرقام تتغير حسب الفترة المختارة</span>
      </div>

      {transitions.length === 0 || transitions.every((transition) => transition.fromCount === 0) ? (
        <div className={styles.emptyInline}>
          <p>تفاصيل التسرب بين المراحل غير متاحة بعد.</p>
          <span>شغّل تحليل المحادثات لتحديد أين يتوقف العملاء ولماذا.</span>
        </div>
      ) : <div className={styles.transitionGrid}>
        {transitions.map((transition) => (
          <article className={styles.transitionCard} key={transition.key}>
            <div className={styles.transitionTitle}>
              <span>{transition.fromLabel}</span>
              <ArrowLeft size={15} aria-hidden="true" />
              <strong>{transition.toLabel}</strong>
            </div>
            {transition.fromCount === 0 ? (
              <div className={styles.emptyInline}>
                <p>لا توجد محادثات بدأت هذه المرحلة.</p>
                <span>لا يمكن حساب التحويل أو التسرب قبل توفر بيانات.</span>
              </div>
            ) : (
              <>
                <div className={styles.transitionNumbers}>
                  <div><span>بدأوا المرحلة</span><strong>{transition.fromCount.toLocaleString('ar-EG')}</strong></div>
                  <div><span>كملوا</span><strong>{transition.toCount.toLocaleString('ar-EG')}</strong></div>
                  <div data-tone="warning"><span>وقفوا</span><strong>{transition.dropOffCount.toLocaleString('ar-EG')}</strong></div>
                </div>
                <div className={styles.transitionRate} aria-hidden="true">
                  <span style={{ width: `${transition.conversionRate}%` }} />
                </div>
                <div className={styles.transitionRateLabels}>
                  <span>تحويل {transition.conversionRate.toLocaleString('ar-EG')}٪</span>
                  <span>تسرب {transition.dropOffRate.toLocaleString('ar-EG')}٪</span>
                </div>
                {transition.dropOffCount === 0 ? (
                  <p className={styles.transitionSuccess}>لا يوجد تسرب في هذه الخطوة.</p>
                ) : (
                  <div className={styles.transitionReasons}>
                    {transition.reasons.map((reason) => (
                      <div className={styles.transitionReason} key={reason.reason}>
                        <div><strong>{reason.label}</strong><span>{reason.percentage.toLocaleString('ar-EG')}٪ من المتوقفين</span></div>
                        <b>{reason.count.toLocaleString('ar-EG')}</b>
                      </div>
                    ))}
                  </div>
                )}
                {transition.needsFollowUp > 0 && (
                  <div className={styles.transitionFollowUp}>
                    <UserRoundCheck size={15} aria-hidden="true" />
                    {transition.needsFollowUp.toLocaleString('ar-EG')} محتاجين متابعة
                  </div>
                )}
              </>
            )}
          </article>
        ))}
      </div>}
    </section>
  );
}
