'use client';

import React, { use, useCallback, useEffect, useRef, useState } from 'react';
import axios from 'axios';
import { AlertCircle, Calendar, CheckCircle, Clock, RefreshCw, Smartphone, User, Users } from 'lucide-react';
import styles from './booking.module.css';

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost';
const DISPLAY_TIMEZONE = 'Africa/Cairo';

interface GroupAppointment {
  id: string;
  name: string;
  dateTime: string;
  capacity: number;
  bookedCount: number;
  slotsLeft: number;
  mode?: string;
  instructorName?: string;
  freeSessionDateTime?: string | null;
  courseSecondDateTime?: string | null;
}

interface PageProps {
  params: Promise<{ projectId: string }>;
}

export default function PublicBookingPage({ params }: PageProps) {
  const { projectId } = use(params);
  const [groups, setGroups] = useState<GroupAppointment[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [selectedGroupId, setSelectedGroupId] = useState('');
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const successHeadingRef = useRef<HTMLHeadingElement>(null);

  const fetchActiveGroups = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const response = await axios.get<GroupAppointment[]>(
        `${API_URL}/api/public/group-appointments/active/${projectId}`,
        { signal },
      );
      if (signal?.aborted) return;
      setGroups(response.data);
      setSelectedGroupId((current) => {
        if (response.data.some((group) => group.id === current && group.slotsLeft > 0)) return current;
        return response.data.find((group) => group.slotsLeft > 0)?.id ?? '';
      });
    } catch (requestError) {
      if (signal?.aborted || axios.isCancel(requestError)) return;
      console.error(requestError);
      setError('تعذر تحميل المواعيد المتاحة. تحقق من اتصالك ثم أعد المحاولة.');
      setGroups([]);
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => void fetchActiveGroups(controller.signal), 0);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [fetchActiveGroups]);

  useEffect(() => {
    if (success) successHeadingRef.current?.focus();
  }, [success]);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!selectedGroupId || !customerName.trim() || !customerPhone.trim()) {
      setError('أكمل بياناتك واختر المجموعة قبل تأكيد الحجز.');
      return;
    }

    const cleanPhone = normalizePhone(customerPhone);
    if (cleanPhone.length < 7 || cleanPhone.length > 15) {
      setError('أدخل رقم واتساب صحيحًا من 7 إلى 15 رقمًا، شاملًا كود الدولة.');
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      await axios.post(`${API_URL}/api/public/group-appointments/book`, {
        projectId,
        groupAppointmentId: selectedGroupId,
        customerName: customerName.trim(),
        customerPhone: cleanPhone,
      });
      setSuccess(true);
      setCustomerName('');
      setCustomerPhone('');
    } catch (requestError: unknown) {
      console.error(requestError);
      setError(getBookingError(requestError));
    } finally {
      setSubmitting(false);
    }
  };

  const selectedGroup = groups.find((group) => group.id === selectedGroupId);

  if (loading) {
    return (
      <main className={styles.page}>
        <section className={styles.card} aria-busy="true" aria-label="تحميل المواعيد المتاحة">
          <div className={styles.skeletonHeading} />
          <div className={styles.skeletonLine} />
          <div className={styles.skeletonGroup} />
          <div className={styles.skeletonGroup} />
          <p className={styles.loadingText}>جاري تحميل المواعيد المتاحة...</p>
        </section>
      </main>
    );
  }

  return (
    <main className={styles.page}>
      <section className={styles.card} aria-labelledby="booking-title">
        {success ? (
          <div className={styles.successState} role="status" aria-live="polite">
            <span className={styles.successIcon}><CheckCircle size={34} aria-hidden="true" /></span>
            <div>
              <h1 ref={successHeadingRef} tabIndex={-1}>تم تأكيد حجزك</h1>
              <p>سجلنا مقعدك{selectedGroup ? ` في ${selectedGroup.name}` : ''}. سنتواصل معك عبر واتساب لتأكيد التفاصيل.</p>
            </div>
            <button type="button" className={styles.primaryButton} onClick={() => {
              setSuccess(false);
              setSelectedGroupId('');
              void fetchActiveGroups();
            }}>
              حجز موعد آخر
            </button>
          </div>
        ) : (
          <>
            <header className={styles.header}>
              <span className={styles.headerIcon}><Calendar size={24} aria-hidden="true" /></span>
              <h1 id="booking-title">حجز موعد مجموعة</h1>
              <p>أدخل بياناتك واختر الموعد المناسب. الأوقات المعروضة بتوقيت القاهرة.</p>
            </header>

            {error && (
              <div id="booking-error" className={styles.errorAlert} role="alert" aria-live="assertive">
                <AlertCircle size={18} aria-hidden="true" />
                <span>{error}</span>
                {groups.length === 0 && (
                  <button type="button" onClick={() => void fetchActiveGroups()}>
                    <RefreshCw size={16} aria-hidden="true" /> إعادة المحاولة
                  </button>
                )}
              </div>
            )}

            {groups.length === 0 && !error ? (
              <div className={styles.emptyState}>
                <Calendar size={36} aria-hidden="true" />
                <h2>لا توجد مواعيد متاحة الآن</h2>
                <p>يمكنك العودة لاحقًا أو تحديث القائمة.</p>
                <button type="button" className={styles.secondaryButton} onClick={() => void fetchActiveGroups()}>
                  <RefreshCw size={17} aria-hidden="true" /> تحديث المواعيد
                </button>
              </div>
            ) : groups.length > 0 && (
              <form className={styles.form} onSubmit={handleSubmit} aria-busy={submitting} aria-describedby={error ? 'booking-error' : undefined}>
                <div className={styles.field}>
                  <label htmlFor="booking-name">الاسم بالكامل</label>
                  <div className={styles.inputShell}>
                    <User size={17} aria-hidden="true" />
                    <input
                      id="booking-name"
                      name="name"
                      type="text"
                      autoComplete="name"
                      value={customerName}
                      onChange={(event) => setCustomerName(event.target.value)}
                      placeholder="مثال: محمد أحمد"
                      required
                      disabled={submitting}
                    />
                  </div>
                </div>

                <div className={styles.field}>
                  <label htmlFor="booking-phone">رقم واتساب مع كود الدولة</label>
                  <div className={styles.inputShell} dir="ltr">
                    <Smartphone size={17} aria-hidden="true" />
                    <input
                      id="booking-phone"
                      name="tel"
                      type="tel"
                      inputMode="tel"
                      autoComplete="tel"
                      value={customerPhone}
                      onChange={(event) => setCustomerPhone(event.target.value)}
                      placeholder="201012345678"
                      aria-describedby="booking-phone-help"
                      required
                      disabled={submitting}
                    />
                  </div>
                  <small id="booking-phone-help">اكتب الأرقام فقط، من دون + أو مسافات.</small>
                </div>

                <fieldset className={styles.groupFieldset}>
                  <legend>المجموعات المتاحة</legend>
                  <p id="booking-timezone" className={styles.timezoneNote}>كل المواعيد بتوقيت القاهرة (Africa/Cairo).</p>
                  <div className={styles.groupList}>
                    {groups.map((group) => {
                      const isFull = group.slotsLeft <= 0;
                      const isSelected = selectedGroupId === group.id;
                      return (
                        <label key={group.id} className={`${styles.groupOption} ${isSelected ? styles.groupOptionSelected : ''} ${isFull ? styles.groupOptionDisabled : ''}`}>
                          <input
                            className={styles.radioInput}
                            type="radio"
                            name="group"
                            value={group.id}
                            checked={isSelected}
                            disabled={isFull || submitting}
                            onChange={() => setSelectedGroupId(group.id)}
                            aria-describedby={`group-${group.id}-details booking-timezone`}
                          />
                          <span className={styles.radioMark} aria-hidden="true" />
                          <span id={`group-${group.id}-details`} className={styles.groupCopy}>
                            <strong>{group.name || (group.mode === 'online' ? 'أونلاين' : 'في المركز')}</strong>
                            <span><Clock size={14} aria-hidden="true" /> أول جلسة: <time dateTime={group.dateTime}>{formatDate(group.dateTime)}</time></span>
                            {group.courseSecondDateTime && <span><Clock size={14} aria-hidden="true" /> الجلسة الثانية: <time dateTime={group.courseSecondDateTime}>{formatDate(group.courseSecondDateTime)}</time></span>}
                            {group.freeSessionDateTime && <span><Calendar size={14} aria-hidden="true" /> الجلسة المجانية: <time dateTime={group.freeSessionDateTime}>{formatDate(group.freeSessionDateTime)}</time></span>}
                            {group.instructorName && <span><User size={14} aria-hidden="true" /> المدرّب: {group.instructorName}</span>}
                          </span>
                          <span className={styles.capacity}>
                            <b className={isFull ? styles.full : styles.available}>{isFull ? 'مكتملة' : `${group.slotsLeft} مقاعد متاحة`}</b>
                            <small><Users size={13} aria-hidden="true" /> السعة {group.capacity}</small>
                          </span>
                        </label>
                      );
                    })}
                  </div>
                </fieldset>

                {selectedGroup && (
                  <p className={styles.confirmationNote} role="status">
                    راجع مواعيد المجموعة المختارة قبل التأكيد. سيُرسل فريقنا تفاصيل الحجز عبر واتساب.
                  </p>
                )}

                <button type="submit" className={styles.primaryButton} disabled={submitting || !selectedGroupId}>
                  {submitting ? 'جاري تأكيد الحجز...' : 'تأكيد الحجز'}
                </button>
              </form>
            )}
          </>
        )}
      </section>
    </main>
  );
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'موعد غير صالح';
  return new Intl.DateTimeFormat('ar-EG', {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: DISPLAY_TIMEZONE,
  }).format(date);
}

function normalizePhone(value: string) {
  return value
    .replace(/[٠-٩]/g, (digit) => String(digit.charCodeAt(0) - 0x0660))
    .replace(/[۰-۹]/g, (digit) => String(digit.charCodeAt(0) - 0x06f0))
    .replace(/\D/g, '');
}

function getBookingError(error: unknown) {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { error?: string; message?: string } | undefined;
    if (error.response?.status && error.response.status >= 400 && error.response.status < 500) {
      return data?.error || data?.message || 'تعذر تأكيد الحجز. راجع البيانات والمقاعد المتاحة ثم أعد المحاولة.';
    }
    return 'تعذر تأكيد الحجز. أعد المحاولة، ولن نسجل حجزًا مكررًا من هذه الشاشة.';
  }
  return 'تعذر تأكيد الحجز. أعد المحاولة.';
}
