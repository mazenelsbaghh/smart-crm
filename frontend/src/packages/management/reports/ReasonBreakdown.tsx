import type { ReasonMetric } from './types';
import styles from './reports.module.css';

export function ReasonBreakdown({ reasons }: { reasons: ReasonMetric[] }) {
  const maximum = Math.max(1, ...reasons.map((reason) => reason.count));
  return (
    <section className={styles.section} aria-labelledby="reasons-title">
      <div className={styles.sectionHeading}>
        <div>
          <span className={styles.eyebrow}>اعتراضات وتسرب</span>
          <h2 id="reasons-title">أسباب عدم الحجز</h2>
          <span className={styles.contextNote}>للمحادثات المتوقفة والمفقودة فقط</span>
        </div>
      </div>
      {reasons.length === 0 ? (
        <div className={styles.emptyInline}><p>لا توجد أسباب محللة في الفترة.</p><span>شغّل التحليل أو غيّر الفترة.</span></div>
      ) : (
        <div className={styles.reasonList}>{reasons.map((reason) => (
          <div className={styles.reasonRow} key={reason.reason}>
            <div><strong>{reason.label}</strong><span>{reason.count.toLocaleString('ar-EG')} محادثة</span></div>
            <div className={styles.reasonTrack} aria-hidden="true"><span style={{ width: `${reason.count * 100 / maximum}%` }} /></div>
            <b>{reason.percentage.toLocaleString('ar-EG')}٪</b>
          </div>
        ))}</div>
      )}
    </section>
  );
}
