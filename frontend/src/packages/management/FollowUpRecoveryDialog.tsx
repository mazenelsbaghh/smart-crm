'use client';

import { useEffect, useRef, useState, type FormEvent } from 'react';
import { isAxiosError } from 'axios';
import { RotateCcw } from 'lucide-react';
import Dialog from '../../components/shared/Dialog';
import StatusMessage from '../../components/ui/StatusMessage';
import { api } from '../../services/api';
import styles from './management.module.css';
import dialogStyles from './ManagementDialogs.module.css';

export interface FollowUpRecovery {
  code: 'CREATED' | 'ALREADY_ACTIVE';
  customerName: string;
  followUpId: string;
  dueDate: string;
  status: string;
}

const recoveryErrors: Record<string, string> = {
  INVALID_PHONE: 'اكتب رقم واتساب صحيحًا، مثل 01012345678 أو رقم دولي يبدأ بكود الدولة.',
  INVALID_DUE_DATE: 'اختر موعدًا قادمًا للمتابعة.',
  CUSTOMER_NOT_FOUND: 'الرقم غير موجود ضمن عملاء هذا المشروع. راجع الرقم أو أضف العميل أولًا.',
  AMBIGUOUS_CUSTOMER: 'الرقم مرتبط بأكثر من عميل. راجع سجلات العملاء قبل إرجاع المتابعة.',
  CUSTOMER_BLOCKED: 'هذا العميل في القائمة السوداء. راجع حالته من ملف العميل أولًا.',
  REMINDERS_DISABLED: 'متابعات واتساب موقوفة لهذا العميل أو إعداداتها تحتاج مراجعة. راجع ملفه أولًا.',
  CUSTOMER_HAS_NO_PHONE: 'ملف العميل لا يحتوي على رقم واتساب صالح. صحّح الرقم من ملفه أولًا.',
  CUSTOMER_PAID: 'العميل مسجّل كدافع بالفعل، لذلك لا يحتاج متابعة بيع جديدة.',
  NO_SUPPORTED_CONVERSATION: 'لا توجد محادثة واتساب متاحة للمتابعة بهذا الرقم. راجع المحادثة واتصال واتساب أولًا.',
  DELIVERY_UNKNOWN: 'هناك إرسال لم تتأكد نتيجته لهذا العميل. راجعه قبل المحاولة لتجنب تكرار الرسالة.',
  HUMAN_HANDOFF_PENDING: 'هذا العميل ينتظر رد الفريق. راجع المحادثة قبل إرجاع المتابعة.',
};

function initialDueDate() {
  const nextHour = new Date(Date.now() + 60 * 60 * 1000);
  return new Date(nextHour.getTime() - nextHour.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

export default function FollowUpRecoveryDialog({ projectId, onClose, onRecovered }: {
  projectId: string;
  onClose: () => void;
  onRecovered: (recovery: FollowUpRecovery, phone: string) => void;
}) {
  const [phone, setPhone] = useState('');
  const [dueDate, setDueDate] = useState(initialDueDate);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestRef = useRef<AbortController | null>(null);
  useEffect(() => () => requestRef.current?.abort(), []);
  const selectedDate = new Date(dueDate);
  const cairoDate = Number.isNaN(selectedDate.getTime()) ? null : selectedDate.toLocaleString('ar-EG', {
    timeZone: 'Africa/Cairo', dateStyle: 'medium', timeStyle: 'short',
  });

  async function recoverFollowUp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (requestRef.current) return;
    if (Number.isNaN(selectedDate.getTime()) || selectedDate.getTime() <= Date.now()) {
      setError(recoveryErrors.INVALID_DUE_DATE);
      return;
    }
    const controller = new AbortController();
    requestRef.current = controller;
    setSaving(true);
    setError(null);
    try {
      const response = await api.post<FollowUpRecovery>(`/api/projects/${projectId}/follow-ups/recover`, {
        phoneNumber: phone.trim(), dueDate: selectedDate.toISOString(),
      }, { signal: controller.signal, timeout: 30_000 });
      if (!controller.signal.aborted) onRecovered(response.data, phone.trim());
    } catch (failure) {
      if (!controller.signal.aborted) {
        const code = isAxiosError<{ code: string }>(failure) ? failure.response?.data?.code : undefined;
        setError((code && recoveryErrors[code]) || 'تعذر إرجاع المتابعة. احتفظنا بالرقم والموعد، حاول مرة أخرى.');
      }
    } finally {
      requestRef.current = null;
      if (!controller.signal.aborted) setSaving(false);
    }
  }

  return <Dialog label="إرجاع عميل للمتابعة" title="إرجاع عميل للمتابعة" onClose={onClose} closeDisabled={saving}
    footer={<>
      {error && <div className={dialogStyles.feedback}><StatusMessage tone="danger">{error}</StatusMessage></div>}
      <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={onClose} disabled={saving}>إلغاء</button>
      <button type="submit" form="follow-up-recovery-form" className={`${styles.btn} ${styles.btnPrimary}`} disabled={saving}>
        <RotateCcw size={17} aria-hidden="true" />{saving ? 'جاري الإرجاع…' : 'تأكيد الإرجاع للمتابعة'}
      </button>
    </>}>
    <form id="follow-up-recovery-form" className={dialogStyles.form} onSubmit={recoverFollowUp}>
      <p>أدخل رقم عميل فاتته المتابعة. سنجدوله لرسالة متابعة على واتساب في الموعد الذي تختاره.</p>
      <div className={styles.formGroup}>
        <label className={styles.label} htmlFor="recovery-phone">رقم واتساب العميل</label>
        <input id="recovery-phone" className={styles.input} type="tel" inputMode="tel" autoComplete="tel"
          dir="ltr" autoFocus required maxLength={40} placeholder="01012345678"
          value={phone} onChange={event => setPhone(event.target.value)} disabled={saving} aria-describedby="recovery-phone-hint" />
        <small id="recovery-phone-hint">رقم مصري أو رقم دولي بكود الدولة. يجب أن يكون العميل موجودًا في المشروع.</small>
      </div>
      <div className={styles.formGroup}>
        <label className={styles.label} htmlFor="recovery-due-date">موعد المتابعة بتوقيت جهازك</label>
        <input id="recovery-due-date" className={styles.input} type="datetime-local" required
          value={dueDate} onChange={event => setDueDate(event.target.value)} disabled={saving} aria-describedby="recovery-cairo-time" />
        <small id="recovery-cairo-time">{cairoDate ? `${cairoDate} بتوقيت القاهرة` : 'اختر موعدًا صحيحًا'}</small>
      </div>
      <StatusMessage tone="info">إذا كانت هناك متابعة شغالة، سنعرض موعدها ونحتفظ بها. المتابعات القديمة وسجل الرسائل يظلان محفوظين.</StatusMessage>
    </form>
  </Dialog>;
}
