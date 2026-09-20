import { useId, useState } from 'react';
import type { FollowUpDispatchOptions, FollowUpPlanAction } from './types';
import { followUpDayCounts, followUpDuration, followUpScheduleError } from './follow-up-schedule';
import styles from './reports.module.css';

interface FollowUpPlanEditorProps {
  action: FollowUpPlanAction;
  available: number;
  disabled: boolean;
  running: boolean;
  onConfirm: (options: FollowUpDispatchOptions) => void;
  onCancel: () => void;
}

export function FollowUpPlanEditor({ action, available, disabled, running, onConfirm, onCancel }: FollowUpPlanEditorProps) {
  const [options, setOptions] = useState<FollowUpDispatchOptions>({
    count: Math.min(available, 100), minIntervalSeconds: 30, maxIntervalSeconds: 60,
    scheduleDays: action === 'Schedule' ? Math.min(available, 3) : 1,
  });
  const id = useId();
  const error = followUpScheduleError(options, available, action);
  const dayCounts = error ? [] : followUpDayCounts(options.count, action === 'Schedule' ? options.scheduleDays : 1);
  const change = (field: keyof FollowUpDispatchOptions, input: string) => setOptions(current => ({ ...current, [field]: Number(input) }));
  const dayOffset = Math.max(0, dayCounts.length - 1) * 86_400;
  const lastDayGaps = Math.max(0, (dayCounts.at(-1) ?? 1) - 1);

  return (
    <form className={styles.planEditor} aria-label="إعداد خطة الإرسال" onSubmit={event => {
      event.preventDefault();
      if (!error && !disabled && !running) onConfirm(options);
    }}>
      <div className={styles.planEditorHeading}>
        <strong>{action === 'SendNow' ? 'تجهيز الإرسال الآن' : 'توزيع المتابعات على الأيام'}</strong>
        <span>{available.toLocaleString('ar-EG')} عميل متاح، الأعلى أولوية أولًا</span>
      </div>
      <fieldset disabled={disabled || running} className={styles.planFields}>
        <label htmlFor={`${id}-count`}>عدد الرسائل<input id={`${id}-count`} type="number" inputMode="numeric" min={1} max={Math.min(available, 10_000)} step={1} value={options.count || ''} onChange={event => change('count', event.target.value)} /></label>
        <label htmlFor={`${id}-min`}>الفاصل من (ثانية)<input id={`${id}-min`} type="number" inputMode="numeric" min={1} max={3600} step={1} value={options.minIntervalSeconds || ''} onChange={event => change('minIntervalSeconds', event.target.value)} /></label>
        <label htmlFor={`${id}-max`}>الفاصل إلى (ثانية)<input id={`${id}-max`} type="number" inputMode="numeric" min={options.minIntervalSeconds || 1} max={3600} step={1} value={options.maxIntervalSeconds || ''} onChange={event => change('maxIntervalSeconds', event.target.value)} /></label>
        {action === 'Schedule' && <label htmlFor={`${id}-days`}>التوزيع على كام يوم؟<input id={`${id}-days`} type="number" inputMode="numeric" min={1} max={Math.min(options.count, 365)} step={1} value={options.scheduleDays || ''} onChange={event => change('scheduleDays', event.target.value)} /></label>}
      </fieldset>
      <div className={styles.planEstimate} aria-live="polite" aria-atomic="true">
        {error ? <p role="alert">{error}</p> : <>
          <p><b>المدة المتوقعة بعد البداية:</b> من {followUpDuration(dayOffset + lastDayGaps * options.minIntervalSeconds)} إلى {followUpDuration(dayOffset + lastDayGaps * options.maxIntervalSeconds)}.</p>
          <span>{action === 'Schedule' ? 'أول دفعة بكرة في نفس التوقيت المحلي للمشروع، ثم دفعة كل يوم. ' : ''}الفاصل عشوائي بين الحدين. تجهيز الرسائل وتأخر الاتصال قد يزيدان المدة.</span>
        </>}
      </div>
      {action === 'Schedule' && dayCounts.length > 0 && <details className={styles.planDistribution} open>
        <summary>توزيع {options.count.toLocaleString('ar-EG')} رسالة على {dayCounts.length.toLocaleString('ar-EG')} أيام حسب الأولوية</summary>
        <ol>{dayCounts.map((count, index) => <li key={index}><span>اليوم {(index + 1).toLocaleString('ar-EG')}{index === 0 ? '، أعلى أولوية' : ''}</span><b>{count.toLocaleString('ar-EG')} رسالة</b><span>من {followUpDuration((count - 1) * options.minIntervalSeconds)} إلى {followUpDuration((count - 1) * options.maxIntervalSeconds)}</span></li>)}</ol>
      </details>}
      <div className={styles.planEditorActions}>
        <button type="submit" disabled={!!error || disabled || running}>{running ? 'جاري تجهيز الخطة…' : `تأكيد ${action === 'SendNow' ? 'إرسال' : 'جدولة'} ${options.count.toLocaleString('ar-EG')} رسالة`}</button>
        <button type="button" disabled={running} onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
