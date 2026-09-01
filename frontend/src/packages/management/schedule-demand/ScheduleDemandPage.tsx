'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import axios from 'axios';
import {
  CalendarCheck2, CalendarClock, CheckCheck, MapPin, RefreshCw, Search, Send,
  UsersRound, X,
} from 'lucide-react';
import { useAuth } from '../../../context/auth-context';
import { api } from '../../../services/api';
import styles from './schedule-demand.module.css';

interface DemandRow {
  customerId: string;
  customerName: string;
  phoneNumber: string;
  channel: string;
  requestedScheduleText: string;
  requestedScheduleLabel: string;
  lastMessageAtUtc: string;
}

interface OpenAppointment {
  groupId: string;
  name: string;
  mode: string;
  dateTimeUtc: string;
  days: string;
  instructorName: string;
  availableSlots: number;
}

interface DemandOverview {
  totalPeople: number;
  distinctSchedules: number;
  groups: Array<{ label: string; peopleCount: number }>;
  rows: DemandRow[];
  openAppointments: OpenAppointment[];
}

interface SendResult {
  selected: number;
  queued: number;
  skippedDuplicate: number;
  skippedNoContact: number;
  skippedNoEligibleAppointments: number;
}

const errorText = (error: unknown, fallback: string) => axios.isAxiosError<{ error?: string }>(error)
  ? error.response?.data?.error || fallback
  : fallback;

export default function ScheduleDemandPage() {
  const { activeProject, user } = useAuth();
  const [overview, setOverview] = useState<DemandOverview | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState('');
  const [scheduleFilter, setScheduleFilter] = useState('all');
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const canManage = user?.role === 'Owner' || user?.role === 'Admin';
  const timezone = activeProject?.settings?.timezone || 'Africa/Cairo';

  const load = useCallback(async () => {
    if (!activeProject) {
      setLoading(false);
      setError('تعذّر تحديد المشروع النشط.');
      return;
    }
    setLoading(true);
    setError('');
    try {
      const response = await api.get<DemandOverview>(
        `/api/projects/${activeProject.id}/reports/sales-intelligence/schedule-demand`,
        { params: { all: true } },
      );
      setOverview(response.data);
      setSelected((current) => new Set([...current].filter((id) => response.data.rows.some((row) => row.customerId === id))));
    } catch (requestError) {
      setError(errorText(requestError, 'تعذّر تحميل طلبات المواعيد. حاول مرة أخرى.'));
    } finally {
      setLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const visibleRows = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase('ar');
    return (overview?.rows ?? []).filter((row) => {
      const matchesSchedule = scheduleFilter === 'all' || row.requestedScheduleLabel === scheduleFilter;
      const matchesQuery = !normalized || [row.customerName, row.phoneNumber, row.requestedScheduleText]
        .some((value) => value?.toLocaleLowerCase('ar').includes(normalized));
      return matchesSchedule && matchesQuery;
    });
  }, [overview, query, scheduleFilter]);

  const allVisibleSelected = visibleRows.length > 0 && visibleRows.every((row) => selected.has(row.customerId));
  const toggleAll = () => setSelected((current) => {
    const next = new Set(current);
    if (allVisibleSelected) visibleRows.forEach((row) => next.delete(row.customerId));
    else visibleRows.forEach((row) => next.add(row.customerId));
    return next;
  });
  const toggleOne = (customerId: string) => setSelected((current) => {
    const next = new Set(current);
    if (next.has(customerId)) next.delete(customerId); else next.add(customerId);
    return next;
  });

  const sendAvailable = async () => {
    if (!activeProject || selected.size === 0 || sending) return;
    setSending(true);
    setError('');
    setNotice('');
    try {
      const response = await api.post<SendResult>(
        `/api/projects/${activeProject.id}/reports/sales-intelligence/schedule-demand/send-available`,
        { customerIds: [...selected] },
      );
      const result = response.data;
      const skipped = result.skippedDuplicate + result.skippedNoContact + result.skippedNoEligibleAppointments;
      setNotice(result.queued > 0
        ? `تمت إضافة ${result.queued.toLocaleString('ar-EG')} رسالة للإرسال${skipped ? `، وتم تخطي ${skipped.toLocaleString('ar-EG')} بدون تكرار أو لعدم وجود موعد مناسب` : ''}.`
        : 'لم تُضف رسائل جديدة؛ العملاء محدّثون بالفعل أو لا توجد لهم مواعيد مناسبة متاحة.');
      setSelected(new Set());
      setConfirming(false);
    } catch (requestError) {
      setError(errorText(requestError, 'تعذّر إرسال المواعيد المتاحة.'));
    } finally {
      setSending(false);
    }
  };

  if (loading && !overview) return <PageSkeleton />;

  return <main className={styles.page} dir="rtl">
    <header className={styles.pageHeader}>
      <div>
        <span className={styles.eyebrow}>متابعة فرص الحجز</span>
        <h1>طلبات المواعيد</h1>
        <p>كل العملاء الذين ذكروا موعدًا مناسبًا لهم، مع المجموعات المفتوحة التي يمكن إرسالها الآن.</p>
      </div>
      <button type="button" className={styles.refreshButton} disabled={loading} onClick={() => void load()}>
        <RefreshCw size={17} className={loading ? styles.spin : ''} /> تحديث
      </button>
    </header>

    {error && <div className={styles.errorBanner} role="alert">{error}</div>}
    {notice && <div className={styles.noticeBanner} role="status">{notice}</div>}

    <section className={styles.metrics} aria-label="ملخص طلبات المواعيد">
      <Metric icon={UsersRound} label="أشخاص طلبوا مواعيد" value={overview?.totalPeople ?? 0} />
      <Metric icon={CalendarClock} label="مواعيد مطلوبة مختلفة" value={overview?.distinctSchedules ?? 0} />
      <Metric icon={CalendarCheck2} label="مجموعات مفتوحة الآن" value={overview?.openAppointments.length ?? 0} />
    </section>

    <OpenAppointments appointments={overview?.openAppointments ?? []} timezone={timezone} />

    <section className={styles.demandPanel} aria-labelledby="people-title">
      <div className={styles.sectionHeader}>
        <div><span className={styles.eyebrow}>كل الوقت</span><h2 id="people-title">العملاء والمواعيد المطلوبة</h2></div>
        <span className={styles.countBadge}>{visibleRows.length.toLocaleString('ar-EG')}</span>
      </div>

      <div className={styles.filters}>
        <label className={styles.searchBox}><Search size={16} aria-hidden="true" /><span className="sr-only">بحث</span>
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="ابحث بالاسم أو الرقم أو الموعد…" />
          {query && <button type="button" aria-label="مسح البحث" onClick={() => setQuery('')}><X size={14} /></button>}
        </label>
        <label className={styles.filterSelect}><span>الموعد المطلوب</span>
          <select value={scheduleFilter} onChange={(event) => setScheduleFilter(event.target.value)}>
            <option value="all">كل المواعيد</option>
            {(overview?.groups ?? []).map((group) => <option key={group.label} value={group.label}>{group.label} ({group.peopleCount})</option>)}
          </select>
        </label>
        <button type="button" className={styles.selectAll} onClick={toggleAll} disabled={visibleRows.length === 0}>
          <CheckCheck size={16} />{allVisibleSelected ? 'إلغاء تحديد الظاهر' : 'تحديد كل الظاهر'}
        </button>
      </div>

      {!visibleRows.length ? <div className={styles.empty}>لا يوجد عملاء مطابقون للبحث أو الفلتر الحالي.</div> :
        <div className={styles.peopleList}>{visibleRows.map((row) => <DemandPerson
          key={row.customerId} row={row} timezone={timezone} checked={selected.has(row.customerId)} onToggle={toggleOne}
        />)}</div>}
    </section>

    {canManage && selected.size > 0 && <div className={styles.actionDock} role="region" aria-label="إجراءات العملاء المحددين">
      <div><strong>{selected.size.toLocaleString('ar-EG')} عميل محدد</strong><span>سيصل لكل عميل المواعيد المتاحة المناسبة لمكانه فقط.</span></div>
      <button type="button" onClick={() => setConfirming(true)} disabled={!overview?.openAppointments.length}>
        <Send size={17} /> إرسال المواعيد المفتوحة
      </button>
    </div>}

    {confirming && <section className={styles.confirmPanel} aria-labelledby="confirm-title">
      <div><span className={styles.confirmIcon}><Send size={19} /></span><div><h2 id="confirm-title">تأكيد إرسال المواعيد</h2>
        <p>سيتم إرسال المجموعات المفتوحة الآن إلى {selected.size.toLocaleString('ar-EG')} عميل. لن تتكرر نفس الرسالة، ولن تُرسل مجموعة ممتلئة.</p></div></div>
      <div className={styles.confirmActions}>
        <button type="button" className={styles.cancelButton} onClick={() => setConfirming(false)} disabled={sending}>رجوع</button>
        <button type="button" className={styles.sendButton} onClick={() => void sendAvailable()} disabled={sending}>
          <Send size={16} />{sending ? 'جاري الإضافة للإرسال…' : 'تأكيد الإرسال'}
        </button>
      </div>
    </section>}
  </main>;
}

function Metric({ icon: Icon, label, value }: { icon: typeof UsersRound; label: string; value: number }) {
  return <div className={styles.metric}><span><Icon size={17} />{label}</span><strong>{value.toLocaleString('ar-EG')}</strong></div>;
}

function OpenAppointments({ appointments, timezone }: { appointments: OpenAppointment[]; timezone: string }) {
  return <section className={styles.openPanel} aria-labelledby="open-title">
    <div className={styles.sectionHeader}><div><span className={styles.eyebrow}>جاهزة للإرسال</span><h2 id="open-title">المواعيد المفتوحة الآن</h2></div></div>
    {!appointments.length ? <div className={styles.empty}>لا توجد مجموعات مفتوحة بها أماكن حاليًا؛ الإرسال متوقف لحماية العملاء.</div> :
      <div className={styles.appointmentGrid}>{appointments.map((appointment) => {
        const date = new Date(appointment.dateTimeUtc).toLocaleString('ar-EG', {
          weekday: 'long', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit', timeZone: timezone,
        });
        return <article key={appointment.groupId} className={styles.appointmentCard}>
          <div><span className={styles.modeBadge} data-online={appointment.mode.toLowerCase() === 'online'}>
            <MapPin size={12} />{appointment.mode.toLowerCase() === 'online' ? 'أونلاين' : 'في السنتر'}
          </span><strong>{appointment.name}</strong></div>
          <time dateTime={appointment.dateTimeUtc}>{date}</time>
          <small>{appointment.availableSlots.toLocaleString('ar-EG')} مكان متاح{appointment.instructorName ? ` · ${appointment.instructorName}` : ''}</small>
        </article>;
      })}</div>}
  </section>;
}

function DemandPerson({ row, timezone, checked, onToggle }: {
  row: DemandRow; timezone: string; checked: boolean; onToggle: (id: string) => void;
}) {
  const lastMessage = new Date(row.lastMessageAtUtc).toLocaleString('ar-EG', {
    day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit', timeZone: timezone,
  });
  return <label className={styles.personRow} data-selected={checked}>
    <input type="checkbox" checked={checked} onChange={() => onToggle(row.customerId)} />
    <span className={styles.customCheck} aria-hidden="true"><CheckCheck size={13} /></span>
    <div className={styles.personIdentity}><strong>{row.customerName || 'عميل بدون اسم'}</strong><span dir="ltr">{row.phoneNumber || row.channel}</span></div>
    <div className={styles.requestText}><b>{row.requestedScheduleLabel}</b><p>«{row.requestedScheduleText}»</p></div>
    <time dateTime={row.lastMessageAtUtc}>{lastMessage}</time>
  </label>;
}

function PageSkeleton() {
  return <main className={styles.page} dir="rtl" aria-busy="true">
    <div className={styles.skeletonHeader} /><div className={styles.skeletonMetrics} />
    <div className={styles.skeletonPanel} /><div className={styles.skeletonPanel} />
  </main>;
}
