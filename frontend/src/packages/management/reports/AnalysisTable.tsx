import { useState } from 'react';
import { ChevronDown, RefreshCw } from 'lucide-react';
import type { ConversationAnalysisItem } from './types';
import styles from './reports.module.css';

const reasons = [
  ['Unknown', 'السبب غير معروف'], ['NoReplyAfterFollowUp', 'لم يرد بعد المتابعة'], ['PriceObjection', 'اعتراض على السعر'],
  ['ScheduleMismatch', 'المواعيد غير مناسبة'], ['NoAvailability', 'لا توجد سعة'], ['UnclearOffer', 'العرض غير واضح'],
  ['SlowResponse', 'تأخر الرد'], ['MissingFollowUp', 'لم تتم المتابعة'], ['MissingBookingData', 'بيانات الحجز ناقصة'],
  ['BookingTechnicalFailure', 'مشكلة تقنية'], ['NeedsMoreTime', 'يحتاج وقتًا'], ['DecisionMakerUnavailable', 'ينتظر صاحب القرار'],
  ['ChoseCompetitor', 'اختار منافسًا'], ['NotQualified', 'غير مؤهل'], ['SpamOrSupport', 'دعم أو Spam'],
  ['TrustConcern', 'مشكلة ثقة'], ['PaymentIssue', 'مشكلة دفع'], ['OtherExplicitReason', 'سبب صريح آخر'],
];

interface Props {
  rows: ConversationAnalysisItem[];
  canManage: boolean;
  onReanalyze: (conversationId: string) => Promise<void>;
  onCorrect: (conversationId: string, reason: string) => Promise<void>;
}

export function AnalysisTable({ rows, canManage, onReanalyze, onCorrect }: Props) {
  const [openId, setOpenId] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setActionError] = useState('');
  const run = async (id: string, action: () => Promise<void>) => {
    setBusyId(id);
    setActionError('');
    try {
      await action();
    } catch {
      setActionError('تعذر حفظ التعديل. أعد المحاولة.');
    } finally {
      setBusyId(null);
    }
  };

  return (
    <section className={styles.section} aria-labelledby="analyses-title">
      <div className={styles.sectionHeading}><div><span className={styles.eyebrow}>مراجعة قابلة للتدقيق</span><h2 id="analyses-title">تحليل كل محادثة</h2></div><span className={styles.contextNote}>السبب، الثقة، والدليل الأصلي</span></div>
      {rows.length === 0 ? <div className={styles.emptyInline}><p>لم تُحلل محادثات هذه الفترة بعد.</p><span>اضغط «حلّل الآن» للبدء.</span></div> : (
        <div className={styles.analysisList}>{rows.map((row) => {
          const isOpen = openId === row.conversationId;
          return <article className={styles.analysisRow} key={row.conversationId}>
            <button className={styles.analysisSummary} type="button" aria-expanded={isOpen} onClick={() => setOpenId(isOpen ? null : row.conversationId)}>
              <span className={styles.customerCell}><strong>{row.customerName}</strong><small>{row.channel} · {new Date(row.lastMessageAtUtc).toLocaleDateString('ar-EG')}</small></span>
              <span className={styles.stageBadge}>{stageLabel(row.stage)}</span>
              <span className={styles.reasonCell}>{row.reasonLabel}{row.manuallyCorrected && <small>مصحح يدويًا</small>}</span>
              <span className={styles.qualityCell}><small>جودة الرد</small><strong>{row.replyQualityScore}</strong></span>
              <span className={styles.confidence}>{Math.round(row.confidence * 100).toLocaleString('ar-EG')}٪ ثقة</span>
              <ChevronDown size={17} className={isOpen ? styles.chevronOpen : ''} aria-hidden="true" />
            </button>
            {isOpen && <div className={styles.analysisDetails}>
              <div><h3>القراءة</h3><p>{row.summary || 'لا يوجد ملخص.'}</p></div>
              <div><h3>الخطوة المقترحة</h3><p>{row.recommendation || 'لا يوجد إجراء مقترح.'}</p></div>
              <div><h3>الدليل</h3>{row.evidence.length ? <blockquote>{row.evidence.map((evidence) => <p key={evidence.messageId}>«{evidence.quote}»</p>)}</blockquote> : <p>لم يحتفظ النظام باقتباس مطابق حرفيًا، لذلك السبب يحتاج مراجعة.</p>}</div>
              {canManage && <div className={styles.analysisActions}>
                <label>تصحيح السبب<select value={row.reason} disabled={busyId === row.conversationId} onChange={(event) => void run(row.conversationId, () => onCorrect(row.conversationId, event.target.value))}>{reasons.map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></label>
                <button type="button" disabled={busyId === row.conversationId} onClick={() => void run(row.conversationId, () => onReanalyze(row.conversationId))}><RefreshCw size={15} aria-hidden="true" />إعادة التحليل</button>
              </div>}
              {actionError && openId === row.conversationId && <p className={styles.inlineError} role="alert">{actionError}</p>}
            </div>}
          </article>;
        })}</div>
      )}
    </section>
  );
}

const stageLabel = (stage: string) => ({ New: 'شات جديد', Engaged: 'تم التفاعل', Qualified: 'مؤهل', BookingIntent: 'نية حجز', Booked: 'حجز', Paid: 'دفع', Attended: 'حضر' }[stage] ?? stage);
