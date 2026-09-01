import { useRef, useState } from 'react';
import Link from 'next/link';
import { ArrowUpLeft, CalendarPlus, Check, Clock4, LoaderCircle, Send, UsersRound } from 'lucide-react';
import type { FollowUpPlanAction, FollowUpPlanSummary, OpportunityItem } from './types';
import styles from './reports.module.css';

type ActionState = 'idle' | 'scheduling' | 'scheduled' | 'sending' | 'sent';

interface OpportunityQueueProps {
  opportunities: OpportunityItem[];
  plan: FollowUpPlanSummary;
  canManage: boolean;
  actionsDisabled?: boolean;
  onSchedule: (opportunity: OpportunityItem) => Promise<void>;
  onSend: (opportunity: OpportunityItem) => Promise<void>;
  onQueuePlan: (action: FollowUpPlanAction) => Promise<boolean>;
}

const inboxPaths: Partial<Record<string, string>> = {
  WhatsApp: '/inbox',
  Messenger: '/inbox/messenger',
  FacebookComment: '/inbox/comments',
};

const conversationHref = (opportunity: OpportunityItem) => {
  const inboxPath = inboxPaths[opportunity.channel];
  return inboxPath
    ? `${inboxPath}?conversationId=${encodeURIComponent(opportunity.conversationId)}`
    : null;
};

const recommendedActionLabels: Record<OpportunityItem['recommendedAction'], string> = {
  SendNow: 'المقترح: إرسال الآن',
  Schedule: 'المقترح: جدولة 24 ساعة',
  Scheduled: 'مجدول بالفعل',
  OpenConversation: 'المقترح: افتح المحادثة ورد يدويًا',
};

export function OpportunityQueue({ opportunities, plan, canManage, actionsDisabled = false, onSchedule, onSend, onQueuePlan }: OpportunityQueueProps) {
  const [states, setStates] = useState<Record<string, ActionState>>({});
  const [armedConversationId, setArmedConversationId] = useState<string | null>(null);
  const [armedPlanAction, setArmedPlanAction] = useState<FollowUpPlanAction | null>(null);
  const [runningPlanAction, setRunningPlanAction] = useState<FollowUpPlanAction | null>(null);
  const mutationLockRef = useRef(false);
  const handled = (action: OpportunityItem['recommendedAction']) => opportunities.filter((opportunity) =>
    opportunity.recommendedAction === action && ['scheduled', 'sent'].includes(states[opportunity.conversationId])).length;
  const sendNowCount = Math.max(0, plan.sendNow - handled('SendNow'));
  const scheduleCount = Math.max(0, plan.schedule - handled('Schedule'));
  const scheduledCount = plan.scheduled + opportunities.filter((opportunity) =>
    opportunity.recommendedAction !== 'Scheduled' && states[opportunity.conversationId] === 'scheduled').length;
  const itemMutationRunning = Object.values(states).some((state) => state === 'scheduling' || state === 'sending');

  const runAction = async (
    opportunity: OpportunityItem,
    pending: ActionState,
    completed: ActionState,
    action: (item: OpportunityItem) => Promise<void>,
  ) => {
    if (mutationLockRef.current) return;
    mutationLockRef.current = true;
    setStates((current) => ({ ...current, [opportunity.conversationId]: pending }));
    try {
      await action(opportunity);
      setStates((current) => ({ ...current, [opportunity.conversationId]: completed }));
    } catch {
      setStates((current) => ({ ...current, [opportunity.conversationId]: 'idle' }));
    } finally {
      mutationLockRef.current = false;
    }
  };

  const send = (opportunity: OpportunityItem) => {
    if (armedConversationId !== opportunity.conversationId) {
      setArmedConversationId(opportunity.conversationId);
      return;
    }
    setArmedConversationId(null);
    void runAction(opportunity, 'sending', 'sent', onSend);
  };

  const queuePlan = async (action: FollowUpPlanAction) => {
    if (armedPlanAction !== action) {
      setArmedPlanAction(action);
      return;
    }
    if (mutationLockRef.current) return;
    mutationLockRef.current = true;
    setArmedPlanAction(null);
    setRunningPlanAction(action);
    try {
      const completed = await onQueuePlan(action);
      if (!completed) setArmedPlanAction(action);
    } finally {
      mutationLockRef.current = false;
      setRunningPlanAction(null);
    }
  };

  return (
    <section className={styles.section} aria-labelledby="opportunities-title">
      <div className={styles.sectionHeading}>
        <div><span className={styles.eyebrow}>قابلة للاسترجاع</span><h2 id="opportunities-title">فرص تحتاج متابعة</h2></div>
        <span className={styles.countPill}>{opportunities.length.toLocaleString('ar-EG')}</span>
      </div>
      <div className={styles.followUpPlan} aria-label="خطة المتابعات لكل الفرص">
        <button type="button" data-action="send" disabled={!canManage || actionsDisabled || sendNowCount === 0 || runningPlanAction !== null || itemMutationRunning} onClick={() => void queuePlan('SendNow')}><Send size={16} aria-hidden="true" /><span>{runningPlanAction === 'SendNow' ? 'جاري تجهيز الإرسال…' : armedPlanAction === 'SendNow' ? `تأكيد إرسال ${sendNowCount.toLocaleString('ar-EG')}` : 'يتبعت الآن'}</span><strong>{sendNowCount.toLocaleString('ar-EG')}</strong></button>
        <button type="button" data-action="schedule" disabled={!canManage || actionsDisabled || scheduleCount === 0 || runningPlanAction !== null || itemMutationRunning} onClick={() => void queuePlan('Schedule')}><CalendarPlus size={16} aria-hidden="true" /><span>{runningPlanAction === 'Schedule' ? 'جاري الجدولة…' : armedPlanAction === 'Schedule' ? `تأكيد جدولة ${scheduleCount.toLocaleString('ar-EG')}` : 'يتجدول 24 ساعة'}</span><strong>{scheduleCount.toLocaleString('ar-EG')}</strong></button>
        <div data-action="done"><UsersRound size={16} aria-hidden="true" /><span>مجدول بالفعل</span><strong>{scheduledCount.toLocaleString('ar-EG')}</strong></div>
      </div>
      {opportunities.length === 0 ? (
        <div className={styles.emptyInline}><p>لا توجد فرص متابعة مؤكدة حاليًا.</p><span>القائمة تظهر بعد تحليل المحادثات غير المحولة.</span></div>
      ) : (
        <div className={styles.opportunityList}>{opportunities.map((item) => {
          const state = states[item.conversationId] ?? (item.recommendedAction === 'Scheduled' ? 'scheduled' : 'idle');
          const busy = state === 'scheduling' || state === 'sending';
          const href = conversationHref(item);
          return <article className={styles.opportunity} key={item.conversationId}>
            <div className={styles.priority} data-level={item.priority >= 80 ? 'high' : item.priority >= 50 ? 'medium' : 'low'}><span>أولوية</span><strong>{item.priority}</strong></div>
            <div className={styles.opportunityBody}>
              <div className={styles.opportunityTitle}><strong>{item.customerName}</strong><span>{item.reasonLabel}</span></div>
              <p>{item.summary}</p>
              <div className={styles.nextAction}><b>الخطوة التالية:</b> {item.recommendation}</div>
              <span className={styles.recommendedAction} data-action={item.recommendedAction}>{recommendedActionLabels[item.recommendedAction]}</span>
              <span className={styles.timestamp}><Clock4 size={13} aria-hidden="true" />آخر رسالة {new Date(item.lastMessageAtUtc).toLocaleString('ar-EG', { timeZone: 'Africa/Cairo', dateStyle: 'short', timeStyle: 'short' })}</span>
            </div>
            <div className={styles.opportunityActions}>
              {canManage && item.channel === 'WhatsApp' && <>
                <button type="button" disabled={actionsDisabled || busy || runningPlanAction !== null || itemMutationRunning || state === 'scheduled' || state === 'sent'} onClick={() => void runAction(item, 'scheduling', 'scheduled', onSchedule)}>
                  {state === 'scheduling' ? <LoaderCircle className={styles.spinning} size={15} aria-hidden="true" /> : state === 'scheduled' ? <Check size={15} aria-hidden="true" /> : <CalendarPlus size={15} aria-hidden="true" />}
                  {state === 'scheduled' ? 'اتجدولت' : state === 'scheduling' ? 'بجدول…' : 'جدولة 24 ساعة'}
                </button>
                <button className={armedConversationId === item.conversationId ? styles.confirmSend : styles.sendNow} type="button" disabled={actionsDisabled || busy || runningPlanAction !== null || itemMutationRunning || state === 'scheduled' || state === 'sent'} onClick={() => send(item)}>
                  {state === 'sending' ? <LoaderCircle className={styles.spinning} size={15} aria-hidden="true" /> : state === 'sent' ? <Check size={15} aria-hidden="true" /> : <Send size={15} aria-hidden="true" />}
                  {state === 'sent' ? 'اتبعت' : state === 'sending' ? 'بيبعت…' : armedConversationId === item.conversationId ? 'تأكيد الإرسال' : 'إرسال الآن'}
                </button>
              </>}
              {href && <Link className={`${styles.openConversation} ${item.channel === 'WhatsApp' ? '' : styles.manualConversation}`} href={href} aria-label={`فتح محادثة ${item.customerName}`}><ArrowUpLeft size={18} aria-hidden="true" />{item.channel !== 'WhatsApp' && <span>فتح المحادثة</span>}</Link>}
            </div>
          </article>;
        })}</div>
      )}
    </section>
  );
}
