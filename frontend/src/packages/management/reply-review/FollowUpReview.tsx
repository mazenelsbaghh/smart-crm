'use client';

import { useState } from 'react';
import { healthLabels, inboxLink, minutesLabel, numberLabel, ReviewFollowUp, timeLabel } from './types';
import styles from './reply-review.module.css';

export default function FollowUpReview({ rows, timezone, generatedAtUtc }: { rows: ReviewFollowUp[]; timezone: string; generatedAtUtc: string }) {
  const [problemsOnly, setProblemsOnly] = useState(false);
  const visible = problemsOnly ? rows.filter(row => ['Overdue', 'Failed', 'Unknown', 'Unverified'].includes(row.health)) : rows;
  const sent = rows.filter(row => row.health === 'Sent').length;
  return <section className={styles.followups} aria-label="فحص المتابعات">
    <div className={styles.sectionHeading}><div><h2>هل المتابعة ماشية صح؟</h2><p className={styles.muted}>{numberLabel(rows.length)} متابعة في اليوم · {numberLabel(sent)} بإرسال مسجل</p></div>
      <a href="/management/follow-ups">إدارة المتابعات ↗</a></div>
    <p className={styles.note}>«إرسال مسجل» يعني أن النظام سجل نجاح طلب الإرسال، ولا يثبت القراءة أو التسليم النهائي. المحتوى المعروض هو الرسالة المسجلة أو ملاحظات المتابعة عند غيابها.</p>
    <label className={styles.checkbox}><input type="checkbox" checked={problemsOnly} onChange={e => setProblemsOnly(e.target.checked)} /> الحالات التي تحتاج مراجعة فقط</label>
    {visible.length ? <div className={styles.tableScroll}><table><thead><tr><th>العميل / الحالة</th><th>الموعد المحدد</th><th>الإرسال الفعلي</th><th>فرق التوقيت</th><th>المحتوى / رد العميل</th></tr></thead>
      <tbody>{visible.map(row => { const href = row.conversationId ? inboxLink(row.conversationId, row.channel) : null;
        return <tr key={row.id}><td>{href ? <a href={href}>{row.customerName}</a> : <strong>{row.customerName}</strong>}
          <span className={row.health === 'Sent' ? styles.muted : styles.warning}>{healthLabels[row.health] ?? row.status}</span></td>
          <td><time dateTime={row.dueAtUtc}>{timeLabel(row.dueAtUtc, timezone, true)}</time></td>
          <td>{row.sentAtUtc ? <time dateTime={row.sentAtUtc}>{timeLabel(row.sentAtUtc, timezone, true)}</time> : 'غير مسجل'}</td>
          <td>{delayLabel(row, generatedAtUtc)}</td>
          <td><details><summary>{row.sentMessageId ? 'عرض الرسالة المسجلة' : 'عرض ملاحظات المتابعة'}</summary><p dir="auto">{row.content || 'لا يوجد نص مسجل.'}</p></details>
            {row.sentAtUtc && <span className={styles.muted}>{row.customerReplied ? 'وصل رد بعد المتابعة' : 'لم يُسجل رد بعدها'}</span>}</td></tr>;
      })}</tbody></table></div> : <p className={styles.empty}>لا توجد متابعات مطابقة في اليوم المختار.</p>}
  </section>;
}

function delayLabel(row: ReviewFollowUp, generatedAtUtc: string) {
  if (row.health === 'Overdue') {
    const elapsedMinutes = (new Date(generatedAtUtc).getTime() - new Date(row.dueAtUtc).getTime()) / 60_000;
    return `متأخرة حاليًا ${minutesLabel(elapsedMinutes)}`;
  }
  if (row.delayMinutes == null) return '—';
  if (row.delayMinutes < 0) return `مبكر ${minutesLabel(-row.delayMinutes)}`;
  return row.delayMinutes === 0 ? 'في الموعد' : `متأخر ${minutesLabel(row.delayMinutes)}`;
}
